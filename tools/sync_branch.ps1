<#
.SYNOPSIS
    並列ループエンジニアリング用 ブランチ最新化・コンフリクト検知ツール (sync_branch.ps1)

.DESCRIPTION
    作業ブランチ（ワークツリー内実行にも対応）を非破壊マージ（git merge --no-edit origin/<BaseBranch>）で
    最新のリモートベースブランチへ追従させ、最新・マージ成功・コンフリクト発生・ロールバックの状態を
    構造化 JSON と終了コードで返します。

    終了コード:
      0: UP_TO_DATE / MERGED / DRY_RUN / ABORTED
      1: 通常エラー（未コミット変更あり、fetch 失敗、マージ対象外エラー等）
      2: CONFLICT（マージコンフリクト発生、conflict_files を構造化出力）

.PARAMETER BaseBranch
    追従元のリモートベースブランチ名（既定: develop）

.PARAMETER DryRun
    マージを実行せず、behind/ahead 件数および変更ファイル重複候補のみを出力する

.PARAMETER Abort
    進行中のマージを git merge --abort でロールバックし、ABORTED を返す

.PARAMETER SkipFetch
    git fetch origin <BaseBranch> をスキップする

.PARAMETER WorkingDirectory
    対象リポジトリまたはワークツリーの作業ディレクトリ（省略時はカレントディレクトリ）

.PARAMETER Json
    構造化 JSON を標準出力へ出力する

.PARAMETER JsonOut
    構造化 JSON の出力先ファイルパス
#>
[CmdletBinding()]
param(
    [string]$BaseBranch = "develop",
    [switch]$DryRun,
    [switch]$Abort,
    [switch]$SkipFetch,
    [string]$WorkingDirectory = "",
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
        [Parameter(Mandatory = $true)][string]$WorkDir
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
    $psi.WorkingDirectory = $WorkDir
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

function Split-NonEmptyLines {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }
    $list = New-Object System.Collections.ArrayList
    foreach ($line in ($Text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -gt 0) {
            [void]$list.Add($trimmed)
        }
    }
    return @($list.ToArray())
}

function Resolve-PSPath {
    param([Parameter(Mandatory = $true)][string]$PathText)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PathText)
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
        if ($Payload.Contains("conflict_files") -and @($Payload.conflict_files).Count -gt 0) {
            Write-Output "  コンフリクトファイル:"
            foreach ($f in @($Payload.conflict_files)) {
                Write-Output ("    - {0}" -f $f)
            }
        }
    }

    exit $ExitCode
}

$workDir = if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    Resolve-PSPath -PathText $WorkingDirectory
}
else {
    (Get-Location).Path
}

$baseRef = "origin/$BaseBranch"
$currentBranch = ""

try {
    if (-not (Test-Path -LiteralPath $workDir)) {
        throw "作業ディレクトリが存在しません: $workDir"
    }

    $topRes = Invoke-Git -Arguments @("rev-parse", "--show-toplevel") -WorkDir $workDir
    if ($topRes.ExitCode -ne 0) {
        throw "Git リポジトリではありません: $workDir"
    }

    $branchRes = Invoke-Git -Arguments @("rev-parse", "--abbrev-ref", "HEAD") -WorkDir $workDir
    if ($branchRes.ExitCode -eq 0) {
        $currentBranch = $branchRes.StdOut.Trim()
    }

    # -Abort 指定時は進行中のマージをロールバック
    if ($Abort) {
        $mergeHeadRes = Invoke-Git -Arguments @("rev-parse", "-q", "--verify", "MERGE_HEAD") -WorkDir $workDir
        if ($mergeHeadRes.ExitCode -ne 0) {
            throw "進行中のマージが存在しないため、マージ中止を実行できません。"
        }

        $abortRes = Invoke-Git -Arguments @("merge", "--abort") -WorkDir $workDir
        if ($abortRes.ExitCode -ne 0) {
            throw "git merge --abort に失敗しました: $($abortRes.StdErr.Trim())"
        }

        $payload = [ordered]@{
            status         = "ABORTED"
            branch         = $currentBranch
            base_ref       = $baseRef
            behind_count   = 0
            ahead_count    = 0
            conflict_files = @()
            overlap_files  = @()
            merge_commit   = $null
            message        = "進行中のマージを安全に中止（ロールバック）しました。"
        }
        Write-ResultAndExit -Payload $payload -ExitCode 0
    }

    # 未コミット変更がある場合は安全のため即エラー終了（-AllowDirty 非提供）
    $statusRes = Invoke-Git -Arguments @("status", "--porcelain") -WorkDir $workDir
    if ($statusRes.ExitCode -ne 0) {
        throw "git status の取得に失敗しました: $($statusRes.StdErr.Trim())"
    }
    if (-not [string]::IsNullOrWhiteSpace($statusRes.StdOut)) {
        $dirtyPayload = [ordered]@{
            status         = "DIRTY_WORKTREE"
            branch         = $currentBranch
            base_ref       = $baseRef
            behind_count   = 0
            ahead_count    = 0
            conflict_files = @()
            overlap_files  = @()
            merge_commit   = $null
            message        = "未コミットの変更が存在するためブランチ最新化を中止しました。先にコミットまたは退避してください。"
        }
        Write-ResultAndExit -Payload $dirtyPayload -ExitCode 1
    }

    if (-not $SkipFetch) {
        $fetchRes = Invoke-Git -Arguments @("fetch", "origin", $BaseBranch) -WorkDir $workDir
        if ($fetchRes.ExitCode -ne 0) {
            throw "git fetch origin $BaseBranch に失敗しました: $($fetchRes.StdErr.Trim())"
        }
    }

    $verifyBaseRes = Invoke-Git -Arguments @("rev-parse", "-q", "--verify", $baseRef) -WorkDir $workDir
    if ($verifyBaseRes.ExitCode -ne 0) {
        throw "ベースリファレンス '$baseRef' が見つかりません。"
    }

    $countRes = Invoke-Git -Arguments @("rev-list", "--left-right", "--count", "HEAD...$baseRef") -WorkDir $workDir
    if ($countRes.ExitCode -ne 0) {
        throw "コミット差分件数の算出に失敗しました: $($countRes.StdErr.Trim())"
    }

    $countParts = ($countRes.StdOut.Trim() -split '\s+')
    if ($countParts.Count -lt 2) {
        throw "git rev-list の出力形式が不正です: $($countRes.StdOut.Trim())"
    }
    $aheadCount = [int]$countParts[0]
    $behindCount = [int]$countParts[1]

    if ($behindCount -eq 0) {
        $upToDatePayload = [ordered]@{
            status         = "UP_TO_DATE"
            branch         = $currentBranch
            base_ref       = $baseRef
            behind_count   = $behindCount
            ahead_count    = $aheadCount
            conflict_files = @()
            overlap_files  = @()
            merge_commit   = $null
            message        = ("ブランチ '{0}' は '{1}' に対して最新です（ahead={2}, behind=0）。" -f $currentBranch, $baseRef, $aheadCount)
        }
        Write-ResultAndExit -Payload $upToDatePayload -ExitCode 0
    }

    # -DryRun 指定時はマージを行わず差分サマリ（変更ファイル重複候補含む）を出力
    if ($DryRun) {
        $headChangedRes = Invoke-Git -Arguments @("-c", "core.quotepath=false", "diff", "--name-only", "$baseRef...HEAD") -WorkDir $workDir
        if ($headChangedRes.ExitCode -ne 0) {
            throw "HEAD 側の差分ファイル取得に失敗しました: $($headChangedRes.StdErr.Trim())"
        }
        $baseChangedRes = Invoke-Git -Arguments @("-c", "core.quotepath=false", "diff", "--name-only", "HEAD...$baseRef") -WorkDir $workDir
        if ($baseChangedRes.ExitCode -ne 0) {
            throw "ベース側の差分ファイル取得に失敗しました: $($baseChangedRes.StdErr.Trim())"
        }

        $headFiles = @(Split-NonEmptyLines -Text $headChangedRes.StdOut)
        $baseFiles = @(Split-NonEmptyLines -Text $baseChangedRes.StdOut)

        $baseSet = @{}
        foreach ($bf in $baseFiles) {
            $baseSet[$bf] = $true
        }
        $overlapList = New-Object System.Collections.ArrayList
        foreach ($hf in $headFiles) {
            if ($baseSet.ContainsKey($hf)) {
                [void]$overlapList.Add($hf)
            }
        }
        $overlapFiles = @($overlapList.ToArray() | Sort-Object -Unique)

        $dryRunPayload = [ordered]@{
            status         = "DRY_RUN"
            branch         = $currentBranch
            base_ref       = $baseRef
            behind_count   = $behindCount
            ahead_count    = $aheadCount
            conflict_files = @()
            overlap_files  = $overlapFiles
            head_files     = $headFiles
            base_files     = $baseFiles
            merge_commit   = $null
            message        = ("DryRun: '{0}' は '{1}' より {2} コミット遅れています（ahead={3}, 重複変更ファイル候補={4} 件）。" -f $currentBranch, $baseRef, $behindCount, $aheadCount, $overlapFiles.Count)
        }
        Write-ResultAndExit -Payload $dryRunPayload -ExitCode 0
    }

    # 非破壊マージを実行
    $mergeRes = Invoke-Git -Arguments @("merge", "--no-edit", $baseRef) -WorkDir $workDir
    if ($mergeRes.ExitCode -eq 0) {
        $headCommitRes = Invoke-Git -Arguments @("rev-parse", "HEAD") -WorkDir $workDir
        $mergeCommit = if ($headCommitRes.ExitCode -eq 0) { $headCommitRes.StdOut.Trim() } else { $null }

        $mergedPayload = [ordered]@{
            status         = "MERGED"
            branch         = $currentBranch
            base_ref       = $baseRef
            behind_count   = $behindCount
            ahead_count    = $aheadCount
            conflict_files = @()
            overlap_files  = @()
            merge_commit   = $mergeCommit
            message        = ("'{0}' を '{1}' へ非破壊マージしました（commit: {2}）。" -f $baseRef, $currentBranch, $mergeCommit)
        }
        Write-ResultAndExit -Payload $mergedPayload -ExitCode 0
    }

    # コンフリクト発生判定
    $conflictRes = Invoke-Git -Arguments @("-c", "core.quotepath=false", "diff", "--name-only", "--diff-filter=U") -WorkDir $workDir
    $conflictFiles = @(Split-NonEmptyLines -Text $conflictRes.StdOut)

    if ($conflictFiles.Count -gt 0) {
        $conflictPayload = [ordered]@{
            status         = "CONFLICT"
            branch         = $currentBranch
            base_ref       = $baseRef
            behind_count   = $behindCount
            ahead_count    = $aheadCount
            conflict_files = $conflictFiles
            overlap_files  = @()
            merge_commit   = $null
            message        = ("'{0}' のマージで {1} 件のコンフリクトが発生しました。解消するか -Abort でロールバックしてください。" -f $baseRef, $conflictFiles.Count)
        }
        Write-ResultAndExit -Payload $conflictPayload -ExitCode 2
    }

    throw ("git merge --no-edit {0} に失敗しました: {1}" -f $baseRef, ($mergeRes.StdErr.Trim() + " " + $mergeRes.StdOut.Trim()).Trim())
}
catch {
    $errorMsg = $_.Exception.Message
    $errPayload = [ordered]@{
        status         = "ERROR"
        branch         = $currentBranch
        base_ref       = $baseRef
        behind_count   = 0
        ahead_count    = 0
        conflict_files = @()
        overlap_files  = @()
        merge_commit   = $null
        error          = $errorMsg
        message        = $errorMsg
    }
    Write-ResultAndExit -Payload $errPayload -ExitCode 1
}
