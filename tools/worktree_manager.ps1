<#
.SYNOPSIS
    並列ループエンジニアリング用 ワークツリー管理ツール (worktree_manager.ps1)

.DESCRIPTION
    リポジトリ配下の .agent_worktrees/issue-<Issue番号> に対して、
    最大並列数（既定: 3）の上限ガード付きで git worktree の作成・一覧・削除・整理を一元管理します。
    Windows PowerShell 5.1 準拠で実装されています。

.PARAMETER Action
    実行するサブコマンド: create | list | remove | prune

.PARAMETER IssueNumber
    対象 Issue 番号（create / remove で必須）

.PARAMETER BranchName
    作業ブランチ名（create で必須。<Type>/<Issue番号>-<slug> 形式を検証）

.PARAMETER BaseBranch
    派生元のリモートベースブランチ名（既定: develop）

.PARAMETER MaxParallel
    同時に稼働可能なワークツリーの最大並列数（既定: 3）

.PARAMETER UseExistingBranch
    create 時に新規ブランチ作成ではなく既存ブランチをチェックアウトする

.PARAMETER Force
    remove 時に未コミット変更がある場合でも強制削除する

.PARAMETER SkipFetch
    create 時の git fetch origin <BaseBranch> をスキップする

.PARAMETER CleanActiveBuilds
    prune 時に稼働中ワークツリー配下の .agent_build/ も対象として安全削除を試行する（既定では並列ビルド保護のため稼働中ワークツリーの .agent_build/ は削除しない）

.PARAMETER RepoRoot
    対象リポジトリのルートパス（省略時は現在の Git リポジトリから自動解決）

.PARAMETER Json
    構造化 JSON を標準出力へ出力する

.PARAMETER JsonOut
    構造化 JSON の出力先ファイルパス
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("create", "list", "remove", "prune")]
    [string]$Action,

    [int]$IssueNumber = 0,
    [string]$BranchName = "",
    [string]$BaseBranch = "develop",
    [int]$MaxParallel = 3,
    [switch]$UseExistingBranch,
    [switch]$Force,
    [switch]$SkipFetch,
    [switch]$CleanActiveBuilds,
    [string]$RepoRoot = "",
    [switch]$Json,
    [string]$JsonOut = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
    # 非対話環境で Console が利用できない場合は無視
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $escapedArgs = foreach ($arg in $Arguments) {
        if ($arg -eq "") {
            '""'
        }
        elseif ($arg -match '[\s"]') {
            '"' + ($arg -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
        }
        else {
            $arg
        }
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.Arguments = ($escapedArgs -join " ")
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()

    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $proc.ExitCode
    $proc.Dispose()

    return [ordered]@{
        ExitCode = $exitCode
        StdOut   = $stdout
        StdErr   = $stderr
    }
}

function Resolve-PSPath {
    param([Parameter(Mandatory = $true)][string]$PathText)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PathText)
}

function Resolve-MainRepoRoot {
    param([string]$SpecifiedRoot)

    $baseDir = if (-not [string]::IsNullOrWhiteSpace($SpecifiedRoot)) {
        Resolve-PSPath -PathText $SpecifiedRoot
    }
    else {
        (Get-Location).Path
    }

    if (-not (Test-Path -LiteralPath $baseDir)) {
        throw "指定されたリポジトリパスが存在しません: $baseDir"
    }

    $commonRes = Invoke-Git -Arguments @("rev-parse", "--git-common-dir") -WorkingDirectory $baseDir
    if ($commonRes.ExitCode -eq 0) {
        $commonDirRaw = $commonRes.StdOut.Trim()
        if (-not [string]::IsNullOrWhiteSpace($commonDirRaw)) {
            $commonDirFull = if ([System.IO.Path]::IsPathRooted($commonDirRaw)) {
                [System.IO.Path]::GetFullPath($commonDirRaw)
            }
            else {
                [System.IO.Path]::GetFullPath((Join-Path $baseDir $commonDirRaw))
            }

            if ((Split-Path -Leaf $commonDirFull) -ieq ".git") {
                return (Split-Path -Parent $commonDirFull)
            }
        }
    }

    $topRes = Invoke-Git -Arguments @("rev-parse", "--show-toplevel") -WorkingDirectory $baseDir
    if ($topRes.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($topRes.StdOut)) {
        return [System.IO.Path]::GetFullPath($topRes.StdOut.Trim())
    }

    if ([string]::IsNullOrWhiteSpace($SpecifiedRoot)) {
        return (Split-Path -Parent $PSScriptRoot)
    }

    throw "Git リポジトリを解決できませんでした: $baseDir"
}

function Normalize-PathKey {
    param([string]$PathText)
    return ([System.IO.Path]::GetFullPath($PathText).TrimEnd('\', '/')).ToLowerInvariant()
}

function Get-ManagedWorktrees {
    param(
        [Parameter(Mandatory = $true)][string]$MainRepoRoot
    )

    $worktreesRoot = Join-Path $MainRepoRoot ".agent_worktrees"
    $worktreesRootNorm = Normalize-PathKey -PathText $worktreesRoot
    $prefix = $worktreesRootNorm + "\"

    $listRes = Invoke-Git -Arguments @("worktree", "list", "--porcelain") -WorkingDirectory $MainRepoRoot
    if ($listRes.ExitCode -ne 0) {
        throw "git worktree list に失敗しました: $($listRes.StdErr.Trim())"
    }

    $entries = New-Object System.Collections.ArrayList
    $current = $null
    $lines = $listRes.StdOut -split "`r?`n"

    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            if ($null -ne $current) {
                [void]$entries.Add($current)
                $current = $null
            }
            continue
        }

        if ($line -match '^worktree\s+(.+)$') {
            $current = [ordered]@{
                path     = [System.IO.Path]::GetFullPath($Matches[1].Trim())
                head     = ""
                branch   = ""
                detached = $false
                prunable = $false
            }
        }
        elseif ($null -ne $current) {
            if ($line -match '^HEAD\s+(.+)$') {
                $current.head = $Matches[1].Trim()
            }
            elseif ($line -match '^branch\s+refs/heads/(.+)$') {
                $current.branch = $Matches[1].Trim()
            }
            elseif ($line -match '^branch\s+(.+)$') {
                $current.branch = $Matches[1].Trim()
            }
            elseif ($line -eq 'detached') {
                $current.detached = $true
            }
            elseif ($line -match '^prunable') {
                $current.prunable = $true
            }
        }
    }

    if ($null -ne $current) {
        [void]$entries.Add($current)
    }

    $managed = New-Object System.Collections.ArrayList
    foreach ($entry in $entries) {
        $entryNorm = Normalize-PathKey -PathText $entry.path
        if (-not $entryNorm.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $leaf = Split-Path -Leaf $entry.path
        $parsedIssue = $null
        if ($leaf -match '^issue-(\d+)$') {
            $parsedIssue = [int]$Matches[1]
        }

        $exists = Test-Path -LiteralPath $entry.path
        $isDirty = $false
        $hasAgentBuild = $false

        if ($exists) {
            $statusRes = Invoke-Git -Arguments @("status", "--porcelain") -WorkingDirectory $entry.path
            if ($statusRes.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($statusRes.StdOut)) {
                $isDirty = $true
            }
            $buildPath = Join-Path $entry.path ".agent_build"
            $hasAgentBuild = Test-Path -LiteralPath $buildPath
        }

        [void]$managed.Add([ordered]@{
            issue_number    = $parsedIssue
            path            = $entry.path
            branch          = $entry.branch
            head            = $entry.head
            detached        = $entry.detached
            prunable        = $entry.prunable
            exists          = $exists
            is_dirty        = $isDirty
            has_agent_build = $hasAgentBuild
        })
    }

    return @($managed.ToArray())
}

function Remove-DirectorySafe {
    param([Parameter(Mandatory = $true)][string]$TargetPath)
    if (Test-Path -LiteralPath $TargetPath) {
        Remove-Item -LiteralPath $TargetPath -Recurse -Force -ErrorAction Stop
        return $true
    }
    return $false
}

function Write-ResultAndExit {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Payload,
        [int]$ExitCode = 0
    )

    $jsonText = $Payload | ConvertTo-Json -Depth 8

    if (-not [string]::IsNullOrWhiteSpace($JsonOut)) {
        $resolvedJsonOut = Resolve-PSPath -PathText $JsonOut
        $outDir = Split-Path -Parent $resolvedJsonOut
        if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path -LiteralPath $outDir)) {
            New-Item -ItemType Directory -Path $outDir -Force | Out-Null
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($resolvedJsonOut, $jsonText, $utf8NoBom)
    }

    if ($Json) {
        Write-Output $jsonText
    }
    else {
        Write-Output ("[{0}] {1}" -f $Payload.status, $Payload.message)
        if ($Payload.Contains("worktrees") -and @($Payload.worktrees).Count -gt 0) {
            foreach ($wt in @($Payload.worktrees)) {
                $dirtyLabel = if ($wt.is_dirty) { "dirty" } else { "clean" }
                $buildLabel = if ($wt.has_agent_build) { ".agent_build:残存" } else { ".agent_build:なし" }
                Write-Output ("  - issue-{0} | {1} | {2} | {3} | {4}" -f $wt.issue_number, $wt.branch, $dirtyLabel, $buildLabel, $wt.path)
            }
        }
    }

    exit $ExitCode
}

$resolvedRepoRoot = ""
try {
    if ($MaxParallel -lt 1) {
        throw "MaxParallel は 1 以上を指定してください: $MaxParallel"
    }

    $resolvedRepoRoot = Resolve-MainRepoRoot -SpecifiedRoot $RepoRoot
    $worktreesRoot = Join-Path $resolvedRepoRoot ".agent_worktrees"

    switch ($Action) {
        "list" {
            $worktrees = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
            $activeCount = $worktrees.Count
            $availableSlots = [Math]::Max(0, $MaxParallel - $activeCount)
            $payload = [ordered]@{
                action          = "list"
                status          = "OK"
                repo_root       = $resolvedRepoRoot
                worktrees_root  = $worktreesRoot
                max_parallel    = $MaxParallel
                active_count    = $activeCount
                available_slots = $availableSlots
                worktrees       = $worktrees
                message         = ("稼働中ワークツリー: {0}/{1} (空きスロット: {2})" -f $activeCount, $MaxParallel, $availableSlots)
            }
            Write-ResultAndExit -Payload $payload -ExitCode 0
        }

        "create" {
            if ($IssueNumber -le 0) {
                throw "create 実行時は 1 以上の -IssueNumber が必須です。"
            }
            if ([string]::IsNullOrWhiteSpace($BranchName)) {
                throw "create 実行時は -BranchName が必須です（例: feat/${IssueNumber}-description）。"
            }

            if (-not $UseExistingBranch) {
                $branchPattern = "^(feat|fix|docs|style|refactor|test|chore|feature)/$IssueNumber-[A-Za-z0-9._-]+$"
                if ($BranchName -notmatch $branchPattern) {
                    throw "ブランチ名 '$BranchName' は規約 '<Type>/$IssueNumber-<slug>' に一致しません。"
                }
            }

            $existingWorktrees = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
            $activeCount = $existingWorktrees.Count
            if ($activeCount -ge $MaxParallel) {
                throw ("最大並列数上限（{0}）に達しているためワークツリーを作成できません（現在稼働中: {1} 件）。既存ワークツリーを削除してから再実行してください。" -f $MaxParallel, $activeCount)
            }

            $targetPath = Join-Path $worktreesRoot ("issue-{0}" -f $IssueNumber)
            if (Test-Path -LiteralPath $targetPath) {
                throw "対象ワークツリーパスが既に存在します: $targetPath"
            }

            if (-not (Test-Path -LiteralPath $worktreesRoot)) {
                New-Item -ItemType Directory -Path $worktreesRoot -Force | Out-Null
            }

            if (-not $UseExistingBranch -and -not $SkipFetch) {
                $fetchRes = Invoke-Git -Arguments @("fetch", "origin", $BaseBranch) -WorkingDirectory $resolvedRepoRoot
                if ($fetchRes.ExitCode -ne 0) {
                    throw "git fetch origin $BaseBranch に失敗しました: $($fetchRes.StdErr.Trim())"
                }
            }

            $baseRef = if ($UseExistingBranch) { $null } else { "origin/$BaseBranch" }
            if ($UseExistingBranch) {
                $addRes = Invoke-Git -Arguments @("worktree", "add", $targetPath, $BranchName) -WorkingDirectory $resolvedRepoRoot
            }
            else {
                $verifyBase = Invoke-Git -Arguments @("rev-parse", "--verify", "--quiet", $baseRef) -WorkingDirectory $resolvedRepoRoot
                if ($verifyBase.ExitCode -ne 0) {
                    throw "派生元リファレンス '$baseRef' が見つかりません。"
                }
                $addRes = Invoke-Git -Arguments @("worktree", "add", "-b", $BranchName, $targetPath, $baseRef) -WorkingDirectory $resolvedRepoRoot
            }

            if ($addRes.ExitCode -ne 0) {
                throw "git worktree add に失敗しました: $($addRes.StdErr.Trim())"
            }

            $afterWorktrees = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
            $payload = [ordered]@{
                action          = "create"
                status          = "CREATED"
                repo_root       = $resolvedRepoRoot
                issue_number    = $IssueNumber
                branch          = $BranchName
                base_ref        = $baseRef
                worktree_path   = $targetPath
                max_parallel    = $MaxParallel
                active_count    = $afterWorktrees.Count
                available_slots = [Math]::Max(0, $MaxParallel - $afterWorktrees.Count)
                message         = ("ワークツリーを作成しました: {0} (branch: {1}, 稼働数: {2}/{3})" -f $targetPath, $BranchName, $afterWorktrees.Count, $MaxParallel)
            }
            Write-ResultAndExit -Payload $payload -ExitCode 0
        }

        "remove" {
            if ($IssueNumber -le 0) {
                throw "remove 実行時は 1 以上の -IssueNumber が必須です。"
            }

            $targetPath = Join-Path $worktreesRoot ("issue-{0}" -f $IssueNumber)
            $removedAgentBuild = $false

            $agentBuildPath = Join-Path $targetPath ".agent_build"
            if (Test-Path -LiteralPath $agentBuildPath) {
                $removedAgentBuild = Remove-DirectorySafe -TargetPath $agentBuildPath
            }

            $removeArgs = @("worktree", "remove")
            if ($Force) {
                $removeArgs += "--force"
            }
            $removeArgs += $targetPath

            $rmRes = Invoke-Git -Arguments $removeArgs -WorkingDirectory $resolvedRepoRoot
            if ($rmRes.ExitCode -ne 0) {
                # 未登録だがディレクトリだけ残っている場合は -Force 時に安全削除
                $managed = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
                $targetNorm = Normalize-PathKey -PathText $targetPath
                $isRegistered = $false
                foreach ($wt in $managed) {
                    if ((Normalize-PathKey -PathText $wt.path) -eq $targetNorm) {
                        $isRegistered = $true
                        break
                    }
                }

                if (-not $isRegistered -and (Test-Path -LiteralPath $targetPath) -and $Force) {
                    [void](Remove-DirectorySafe -TargetPath $targetPath)
                }
                else {
                    throw "git worktree remove に失敗しました: $($rmRes.StdErr.Trim())"
                }
            }

            $pruneRes = Invoke-Git -Arguments @("worktree", "prune") -WorkingDirectory $resolvedRepoRoot
            if ($pruneRes.ExitCode -ne 0) {
                throw "git worktree prune に失敗しました: $($pruneRes.StdErr.Trim())"
            }

            if ($Force -and (Test-Path -LiteralPath $targetPath)) {
                [void](Remove-DirectorySafe -TargetPath $targetPath)
            }

            $afterWorktrees = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
            $payload = [ordered]@{
                action              = "remove"
                status              = "REMOVED"
                repo_root           = $resolvedRepoRoot
                issue_number        = $IssueNumber
                worktree_path       = $targetPath
                removed_agent_build = $removedAgentBuild
                max_parallel        = $MaxParallel
                active_count        = $afterWorktrees.Count
                available_slots     = [Math]::Max(0, $MaxParallel - $afterWorktrees.Count)
                message             = ("ワークツリーを削除しました: {0} (残稼働数: {1}/{2})" -f $targetPath, $afterWorktrees.Count, $MaxParallel)
            }
            Write-ResultAndExit -Payload $payload -ExitCode 0
        }

        "prune" {
            $pruneRes = Invoke-Git -Arguments @("worktree", "prune") -WorkingDirectory $resolvedRepoRoot
            if ($pruneRes.ExitCode -ne 0) {
                throw "git worktree prune に失敗しました: $($pruneRes.StdErr.Trim())"
            }

            $managed = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
            $registeredKeys = @{}
            $cleanedBuilds = New-Object System.Collections.ArrayList
            $failedCleanups = New-Object System.Collections.ArrayList

            foreach ($wt in $managed) {
                $registeredKeys[(Normalize-PathKey -PathText $wt.path)] = $true
                if ($CleanActiveBuilds -or $wt.prunable) {
                    $wtBuildDir = Join-Path $wt.path ".agent_build"
                    if (Test-Path -LiteralPath $wtBuildDir) {
                        try {
                            if (Remove-DirectorySafe -TargetPath $wtBuildDir) {
                                [void]$cleanedBuilds.Add($wtBuildDir)
                            }
                        }
                        catch {
                            [void]$failedCleanups.Add([ordered]@{
                                path  = $wtBuildDir
                                error = $_.Exception.Message
                            })
                        }
                    }
                }
            }

            $removedOrphans = New-Object System.Collections.ArrayList
            if (Test-Path -LiteralPath $worktreesRoot) {
                $dirs = @(Get-ChildItem -LiteralPath $worktreesRoot -Directory -ErrorAction SilentlyContinue)
                foreach ($dir in $dirs) {
                    $dirKey = Normalize-PathKey -PathText $dir.FullName
                    if (-not $registeredKeys.ContainsKey($dirKey)) {
                        try {
                            if (Remove-DirectorySafe -TargetPath $dir.FullName) {
                                [void]$removedOrphans.Add($dir.FullName)
                            }
                        }
                        catch {
                            [void]$failedCleanups.Add([ordered]@{
                                path  = $dir.FullName
                                error = $_.Exception.Message
                            })
                        }
                    }
                }
            }

            $afterWorktrees = @(Get-ManagedWorktrees -MainRepoRoot $resolvedRepoRoot)
            $payload = [ordered]@{
                action              = "prune"
                status              = "PRUNED"
                repo_root           = $resolvedRepoRoot
                removed_orphans     = @($removedOrphans.ToArray())
                cleaned_agent_build = @($cleanedBuilds.ToArray())
                failed_cleanups     = @($failedCleanups.ToArray())
                max_parallel        = $MaxParallel
                active_count        = $afterWorktrees.Count
                available_slots     = [Math]::Max(0, $MaxParallel - $afterWorktrees.Count)
                message             = ("ワークツリーを整理しました（孤立ディレクトリ削除: {0} 件, .agent_build 削除: {1} 件, 削除スキップ: {2} 件）" -f $removedOrphans.Count, $cleanedBuilds.Count, $failedCleanups.Count)
            }
            Write-ResultAndExit -Payload $payload -ExitCode 0
        }
    }
}
catch {
    $errorMsg = $_.Exception.Message
    $errPayload = [ordered]@{
        action    = $Action
        status    = "ERROR"
        repo_root = $resolvedRepoRoot
        error     = $errorMsg
        message   = $errorMsg
    }
    Write-ResultAndExit -Payload $errPayload -ExitCode 1
}
