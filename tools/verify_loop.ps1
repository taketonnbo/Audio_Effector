<#
.SYNOPSIS
    Audio Effector ループエンジニアリング用 統合検証ハーネス (Verifier)

.DESCRIPTION
    AI エージェントが自律開発ループの各イテレーション完了判定として単一コマンドで実行できる品質ゲートスクリプトです。
    以下の 5 ゲートを順次実行し、全ゲート合格時に終了コード 0、いずれか失敗時に終了コード 1 を返します。
      1. 隔離ビルド検証 (build_rules.md 準拠、警告 0 必須、終了時 .agent_build 自動削除)
      2. 単体テスト検証 (ソリューション単位 dotnet test、フィルタおよびタイムアウト保護対応)
      3. 仕様書同期検証 (check_spec_sync.py --strict、最終ゲート必須、途中反復時のみ -SkipSpecSync 許可)
      4. XAML バインディング・インデクサ等価性検証 (XAML 変更時または -Full 指定時に check_equivalence.py を実行)
      5. Python ツール単体テスト検証 (tools/ 変更時または -Full 指定時に tools/tests の unittest を実行)

.PARAMETER BaseBranch
    仕様書同期チェックおよび XAML 変更検出の比較ベースブランチ (既定: develop)

.PARAMETER PreferOriginBase
    比較元コミット解決時にローカルブランチより origin/<BaseBranch> を優先する。
    git worktree 内での実行時は指定の有無に関わらず自動で有効になる。

.PARAMETER FetchBase
    比較元コミット解決前に git fetch origin <BaseBranch> を実行してリモート追跡ブランチを最新化する。

.PARAMETER TestFilter
    dotnet test に渡す --filter 式 (途中イテレーションの絞り込み用)

.PARAMETER TestTimeoutSeconds
    dotnet test のタイムアウト秒数 (既定: 180)

.PARAMETER BuildTimeoutSeconds
    dotnet build のタイムアウト秒数 (既定: 120)

.PARAMETER BuildLockTimeoutSeconds
    並列ワークツリー実行時のビルド競合保護用名前付き Mutex の待機タイムアウト秒数 (既定: 300)

.PARAMETER NoBuildLock
    ビルド競合保護用 Mutex を無効化する (単体デバッグ専用。通常の並列運用では使用しない)

.PARAMETER SkipSpecSync
    実装途中の高速イテレーション時のみ仕様書同期検証をスキップする (最終ゲート・PR 作成前は使用禁止、-Full 指定時は無視される)

.PARAMETER Full
    最終完了ゲートモード。-SkipSpecSync を無効化し、XAML 変更の有無に関わらず XAML 等価性検証を含めた全ゲートを実行する

.PARAMETER StrictWarnings
    ビルド警告が 1 件以上ある場合に失敗とする厳格モード (本ソリューションは現状警告 0 のため既定で有効)

.PARAMETER JsonOut
    構造化された検証結果 JSON の出力先ファイルパス (例: .agent_logs/verify_latest.json)。
    相対パスは PowerShell カレントディレクトリ基準で解決される。

.EXAMPLE
    .\tools\verify_loop.ps1 -Full
    最終完了ゲートとして全検証を実行します。

.EXAMPLE
    .\tools\verify_loop.ps1 -SkipSpecSync -TestFilter "FullyQualifiedName~TrackTests" -JsonOut ".agent_logs\iter1.json"
    実装途中の高速イテレーションとして対象テストを絞り込み、結果を JSON に出力します。
#>
[CmdletBinding()]
param(
    [string]$BaseBranch = "develop",
    [switch]$PreferOriginBase,
    [switch]$FetchBase,
    [string]$TestFilter = "",
    [int]$TestTimeoutSeconds = 180,
    [int]$BuildTimeoutSeconds = 120,
    [int]$BuildLockTimeoutSeconds = 300,
    [switch]$NoBuildLock,
    [switch]$SkipSpecSync,
    [switch]$Full,
    [switch]$StrictWarnings,
    [string]$JsonOut = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Windows PowerShell での文字化け防止
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
    # 非対話環境で Console が利用できない場合は無視
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "AudioEffector.sln"
$agentBuildRoot = Join-Path $repoRoot ".agent_build"
$runId = "run_{0}_{1}" -f $PID, ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$agentBuildDir = Join-Path $agentBuildRoot $runId
# プロジェクトごとの出力先 (bin/<ProjectName>, obj/<ProjectName>) を分離し、
# ソリューション内の依存パッケージバージョン差による DLL 上書き競合を防止する
$artifactsOutputArgs = @("-p:UseArtifactsOutput=true", "-p:ArtifactsPath=$agentBuildDir")
$overallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Stop-ProcessTree {
    param([int]$ProcessId)
    try {
        & taskkill /PID $ProcessId /T /F 2>$null | Out-Null
    }
    catch {
        try {
            Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        }
        catch {
        }
    }
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [int]$TimeoutSeconds = 180
    )

    $escapedArgs = foreach ($arg in $ArgumentList) {
        if ($arg -match '[\s"]') {
            '"' + ($arg -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
        }
        else {
            $arg
        }
    }
    $argString = $escapedArgs -join " "

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $argString
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi

    $timedOut = $false
    $startFailed = $false
    $startError = ""
    $exitCode = -1
    $stdoutText = ""
    $stderrText = ""

    try {
        try {
            $null = $proc.Start()
        }
        catch {
            $startFailed = $true
            $startError = $_.Exception.Message
        }

        if (-not $startFailed) {
            $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
            $stderrTask = $proc.StandardError.ReadToEndAsync()

            $exited = $proc.WaitForExit($TimeoutSeconds * 1000)
            if (-not $exited) {
                $timedOut = $true
                Stop-ProcessTree -ProcessId $proc.Id
                try { $proc.WaitForExit(5000) | Out-Null } catch {}
            }
            else {
                # リダイレクトストリームのフラッシュ完了を保証
                $proc.WaitForExit()
                $exitCode = $proc.ExitCode
            }

            try {
                if ($stdoutTask.Wait(3000)) { $stdoutText = $stdoutTask.Result }
            }
            catch {}
            try {
                if ($stderrTask.Wait(3000)) { $stderrText = $stderrTask.Result }
            }
            catch {}
        }
    }
    finally {
        $sw.Stop()
        $proc.Dispose()
    }

    return [PSCustomObject]@{
        ExitCode        = $exitCode
        TimedOut        = $timedOut
        StartFailed     = $startFailed
        StartError      = $startError
        FilePath        = $FilePath
        DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        StdOut          = $stdoutText
        StdErr          = $stderrText
        Command         = "$FilePath $argString"
    }
}

function Test-IsGitWorktree {
    param([string]$RepoPath)

    if ([string]::IsNullOrWhiteSpace($RepoPath)) {
        return $false
    }
    $gitEntry = Join-Path $RepoPath ".git"
    return (Test-Path -LiteralPath $gitEntry -PathType Leaf)
}

function Get-CurrentBranchName {
    param([string]$WorkingDirectory = "")

    $workDir = if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory } else { $repoRoot }
    $branchRes = Invoke-ExternalCommand -FilePath "git" -ArgumentList @("rev-parse", "--abbrev-ref", "HEAD") -WorkingDirectory $workDir -TimeoutSeconds 15
    if (-not $branchRes.StartFailed -and -not $branchRes.TimedOut -and $branchRes.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($branchRes.StdOut)) {
        return ($branchRes.StdOut -split "`r?`n")[0].Trim()
    }
    return ""
}

function Invoke-WithBuildLock {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [string]$MutexName = "Local\AudioEffector_VerifyLoop_BuildLock",
        [int]$TimeoutSeconds = 300,
        [switch]$Bypass
    )

    if ($Bypass) {
        $lockInfo = [PSCustomObject]@{
            WasAbandoned = $false
            WaitSeconds  = 0.0
        }
        $actionResult = & $Action $lockInfo
        return [PSCustomObject]@{
            Acquired     = $true
            TimedOut     = $false
            WasAbandoned = $false
            WaitSeconds  = 0.0
            Error        = ""
            Result       = $actionResult
        }
    }

    $mutex = $null
    $acquired = $false
    $wasAbandoned = $false
    $timedOut = $false
    $waitSeconds = 0.0
    $actionResult = $null
    $lockError = ""

    $lockSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        try {
            $mutex = New-Object System.Threading.Mutex($false, $MutexName)
            $timeoutMs = [int]([Math]::Min([double]$TimeoutSeconds * 1000.0, [double][int]::MaxValue))
            try {
                $acquired = $mutex.WaitOne($timeoutMs, $false)
            }
            catch [System.Threading.AbandonedMutexException] {
                $acquired = $true
                $wasAbandoned = $true
            }
            catch {
                $ex = $_.Exception
                if ($ex -is [System.Threading.AbandonedMutexException] -or ($null -ne $ex.InnerException -and $ex.InnerException -is [System.Threading.AbandonedMutexException])) {
                    $acquired = $true
                    $wasAbandoned = $true
                }
                else {
                    throw
                }
            }
        }
        catch {
            $lockSw.Stop()
            $waitSeconds = [Math]::Round($lockSw.Elapsed.TotalSeconds, 2)
            $lockError = "ビルドロック ('$MutexName') の取得中にエラーが発生しました: $($_.Exception.Message)"
            return [PSCustomObject]@{
                Acquired     = $false
                TimedOut     = $false
                WasAbandoned = $false
                WaitSeconds  = $waitSeconds
                Error        = $lockError
                Result       = $null
            }
        }

        $lockSw.Stop()
        $waitSeconds = [Math]::Round($lockSw.Elapsed.TotalSeconds, 2)

        if (-not $acquired) {
            $timedOut = $true
            $lockError = "ビルドロック ('$MutexName') の取得がタイムアウトしました (${TimeoutSeconds}秒超過)。他の並列ビルドが完了していないか確認してください。"
            return [PSCustomObject]@{
                Acquired     = $false
                TimedOut     = $true
                WasAbandoned = $false
                WaitSeconds  = $waitSeconds
                Error        = $lockError
                Result       = $null
            }
        }

        $lockInfo = [PSCustomObject]@{
            WasAbandoned = $wasAbandoned
            WaitSeconds  = $waitSeconds
        }
        $actionResult = & $Action $lockInfo
        return [PSCustomObject]@{
            Acquired     = $true
            TimedOut     = $false
            WasAbandoned = $wasAbandoned
            WaitSeconds  = $waitSeconds
            Error        = ""
            Result       = $actionResult
        }
    }
    finally {
        if ($acquired -and $null -ne $mutex) {
            try {
                $mutex.ReleaseMutex()
            }
            catch {
                # すでに解放済み等の例外は無視
            }
        }
        if ($null -ne $mutex) {
            try {
                $mutex.Dispose()
            }
            catch {
            }
        }
    }
}

function Resolve-BaseMergeCommit {
    param(
        [Parameter(Mandatory = $true)][string]$BaseRef,
        [switch]$PreferOriginBase,
        [switch]$FetchBase,
        [string]$WorkingDirectory = ""
    )

    $workDir = if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory } else { $repoRoot }
    $isWorktree = Test-IsGitWorktree -RepoPath $workDir
    $useOriginPriority = ([bool]$PreferOriginBase) -or $isWorktree

    if ($FetchBase) {
        $fetchBranch = if ($BaseRef -match '^origin/(.+)$') { $Matches[1] } else { $BaseRef }
        $fetchRes = Invoke-ExternalCommand -FilePath "git" -ArgumentList @("fetch", "origin", $fetchBranch) -WorkingDirectory $workDir -TimeoutSeconds 60
        if ($fetchRes.StartFailed) {
            return [PSCustomObject]@{
                Success     = $false
                MergeBase   = ""
                ResolvedRef = ""
                Error       = "コマンド '$($fetchRes.FilePath)' の起動に失敗しました: $($fetchRes.StartError)"
            }
        }
        if ($fetchRes.TimedOut) {
            return [PSCustomObject]@{
                Success     = $false
                MergeBase   = ""
                ResolvedRef = ""
                Error       = "git fetch origin $fetchBranch がタイムアウトしました (60秒超過)。"
            }
        }
        if ($fetchRes.ExitCode -ne 0) {
            $errText = ($fetchRes.StdErr + " " + $fetchRes.StdOut).Trim()
            return [PSCustomObject]@{
                Success     = $false
                MergeBase   = ""
                ResolvedRef = ""
                Error       = "git fetch origin $fetchBranch に失敗しました: $errText"
            }
        }
    }

    $candidates = @()
    if ($BaseRef -notmatch '^origin/') {
        if ($useOriginPriority) {
            $candidates += [PSCustomObject]@{ Name = "origin/$BaseRef"; Spec = "origin/$BaseRef^{commit}" }
            $candidates += [PSCustomObject]@{ Name = $BaseRef; Spec = "$BaseRef^{commit}" }
        }
        else {
            $candidates += [PSCustomObject]@{ Name = $BaseRef; Spec = "$BaseRef^{commit}" }
            $candidates += [PSCustomObject]@{ Name = "origin/$BaseRef"; Spec = "origin/$BaseRef^{commit}" }
        }
    }
    else {
        $candidates += [PSCustomObject]@{ Name = $BaseRef; Spec = "$BaseRef^{commit}" }
    }

    $resolvedTarget = $null
    $resolvedRefName = ""
    $lastError = ""
    foreach ($candidate in $candidates) {
        $revRes = Invoke-ExternalCommand -FilePath "git" -ArgumentList @("rev-parse", "--verify", "--quiet", $candidate.Spec) -WorkingDirectory $workDir -TimeoutSeconds 30
        if ($revRes.StartFailed) {
            return [PSCustomObject]@{
                Success     = $false
                MergeBase   = ""
                ResolvedRef = ""
                Error       = "コマンド '$($revRes.FilePath)' の起動に失敗しました: $($revRes.StartError)"
            }
        }
        if ($revRes.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($revRes.StdOut)) {
            $resolvedTarget = ($revRes.StdOut -split "`r?`n")[0].Trim()
            $resolvedRefName = $candidate.Name
            break
        }
        $errText = ($revRes.StdErr + " " + $revRes.StdOut).Trim()
        if (-not [string]::IsNullOrWhiteSpace($errText)) {
            $lastError = $errText
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedTarget)) {
        $detail = if ($lastError) { " ($lastError)" } else { "" }
        return [PSCustomObject]@{
            Success     = $false
            MergeBase   = ""
            ResolvedRef = ""
            Error       = "比較元 '$BaseRef'（および 'origin/$BaseRef'）を解決できませんでした。ブランチ名またはコミット指定を確認してください。$detail"
        }
    }

    $mbRes = Invoke-ExternalCommand -FilePath "git" -ArgumentList @("merge-base", $resolvedTarget, "HEAD") -WorkingDirectory $workDir -TimeoutSeconds 30
    if ($mbRes.StartFailed) {
        return [PSCustomObject]@{
            Success     = $false
            MergeBase   = ""
            ResolvedRef = $resolvedRefName
            Error       = "コマンド '$($mbRes.FilePath)' の起動に失敗しました: $($mbRes.StartError)"
        }
    }
    if ($mbRes.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($mbRes.StdOut)) {
        $errText = ($mbRes.StdErr + " " + $mbRes.StdOut).Trim()
        return [PSCustomObject]@{
            Success     = $false
            MergeBase   = ""
            ResolvedRef = $resolvedRefName
            Error       = "比較元 '$resolvedRefName' ($resolvedTarget) と HEAD のマージベースを解決できませんでした: $errText"
        }
    }

    $mergeBase = ($mbRes.StdOut -split "`r?`n")[0].Trim()
    return [PSCustomObject]@{
        Success     = $true
        MergeBase   = $mergeBase
        ResolvedRef = $resolvedRefName
        Error       = ""
    }
}

function Get-ChangedFiles {
    param([string]$BaseCommit)

    $changed = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)

    $diffRes = Invoke-ExternalCommand -FilePath "git" -ArgumentList @("-c", "core.quotepath=false", "diff", "--name-only", "$BaseCommit..HEAD") -WorkingDirectory $repoRoot -TimeoutSeconds 30
    if ($diffRes.StartFailed) {
        return [PSCustomObject]@{
            Success = $false
            Files   = @()
            Error   = "コマンド '$($diffRes.FilePath)' の起動に失敗しました: $($diffRes.StartError)"
        }
    }
    if ($diffRes.ExitCode -ne 0) {
        return [PSCustomObject]@{
            Success = $false
            Files   = @()
            Error   = "ベースコミット '$BaseCommit' からの差分取得に失敗しました: $(($diffRes.StdErr + ' ' + $diffRes.StdOut).Trim())"
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($diffRes.StdOut)) {
        foreach ($line in ($diffRes.StdOut -split "`r?`n")) {
            $norm = $line.Trim().Replace("\", "/")
            if ($norm) { [void]$changed.Add($norm) }
        }
    }

    $workingCommands = @(
        @("-c", "core.quotepath=false", "diff", "--name-only", "--cached"),
        @("-c", "core.quotepath=false", "diff", "--name-only"),
        @("-c", "core.quotepath=false", "ls-files", "--others", "--exclude-standard")
    )

    foreach ($cmdArgs in $workingCommands) {
        $res = Invoke-ExternalCommand -FilePath "git" -ArgumentList $cmdArgs -WorkingDirectory $repoRoot -TimeoutSeconds 30
        if ($res.StartFailed) {
            return [PSCustomObject]@{
                Success = $false
                Files   = @()
                Error   = "コマンド '$($res.FilePath)' の起動に失敗しました: $($res.StartError)"
            }
        }
        if ($res.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($res.StdOut)) {
            foreach ($line in ($res.StdOut -split "`r?`n")) {
                $norm = $line.Trim().Replace("\", "/")
                if ($norm) { [void]$changed.Add($norm) }
            }
        }
    }

    return [PSCustomObject]@{
        Success = $true
        Files   = @($changed | Sort-Object)
        Error   = ""
    }
}

function Get-BaseXamlIndexerKeyCounts {
    param(
        [Parameter(Mandatory = $true)][string]$BaseCommit,
        [Parameter(Mandatory = $true)][string[]]$RelativePaths
    )

    $counts = @{}
    if ($RelativePaths.Count -eq 0) {
        return [PSCustomObject]@{ Success = $true; Counts = $counts; Error = "" }
    }

    $tempBaseDir = Join-Path ([System.IO.Path]::GetTempPath()) ("verify_base_xaml_{0}_{1}" -f $PID, ([Guid]::NewGuid().ToString("N").Substring(0, 8)))
    try {
        New-Item -ItemType Directory -Path $tempBaseDir -Force | Out-Null
        $manifest = @()

        foreach ($rel in $RelativePaths) {
            $normRel = $rel.Replace("\", "/")
            $showRes = Invoke-ExternalCommand -FilePath "git" -ArgumentList @("show", "${BaseCommit}:${normRel}") -WorkingDirectory $repoRoot -TimeoutSeconds 30
            if ($showRes.StartFailed) {
                return [PSCustomObject]@{
                    Success = $false
                    Counts  = $counts
                    Error   = "コマンド '$($showRes.FilePath)' の起動に失敗しました: $($showRes.StartError)"
                }
            }
            if ($showRes.ExitCode -ne 0) {
                # 比較元に存在しない新規 XAML ファイルはベース側 0 件として扱う
                continue
            }

            $outFile = Join-Path $tempBaseDir (([Guid]::NewGuid().ToString("N")) + ".xaml")
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($outFile, $showRes.StdOut, $utf8NoBom)
            $manifest += [PSCustomObject]@{
                rel_path  = $normRel
                temp_path = $outFile
            }
        }

        if ($manifest.Count -eq 0) {
            return [PSCustomObject]@{ Success = $true; Counts = $counts; Error = "" }
        }

        $manifestPath = Join-Path $tempBaseDir "manifest.json"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject @($manifest) -Depth 4), $utf8NoBom)

        $pyCode = @"
import json, re, sys
from pathlib import Path
sys.path.insert(0, str(Path(sys.argv[1]) / "tools"))
from check_equivalence import XamlParser
manifest = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
if isinstance(manifest, dict):
    manifest = [manifest]
pat = re.compile(r"\[(\d+)\]")
counts = {}
for entry in manifest:
    rel = entry["rel_path"].replace("\\", "/").lower()
    for b in XamlParser.parse_file(Path(entry["temp_path"])):
        if pat.search(b.path):
            key = f"{rel}|{b.path} ({b.target_property})|{b.raw_binding}"
            counts[key] = counts.get(key, 0) + 1
print(json.dumps(counts, ensure_ascii=False))
"@

        $pyRes = Invoke-ExternalCommand -FilePath "python" -ArgumentList @("-B", "-c", $pyCode, $repoRoot, $manifestPath) -WorkingDirectory $repoRoot -TimeoutSeconds 30
        if ($pyRes.StartFailed) {
            return [PSCustomObject]@{
                Success = $false
                Counts  = $counts
                Error   = "コマンド '$($pyRes.FilePath)' の起動に失敗しました: $($pyRes.StartError)"
            }
        }
        if ($pyRes.ExitCode -ne 0) {
            return [PSCustomObject]@{
                Success = $false
                Counts  = $counts
                Error   = "比較元 XAML のインデクサ解析に失敗しました: $(($pyRes.StdErr + ' ' + $pyRes.StdOut).Trim())"
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($pyRes.StdOut)) {
            $parsed = $pyRes.StdOut | ConvertFrom-Json
            if ($null -ne $parsed) {
                foreach ($prop in $parsed.PSObject.Properties) {
                    $counts[$prop.Name] = [int]$prop.Value
                }
            }
        }

        return [PSCustomObject]@{ Success = $true; Counts = $counts; Error = "" }
    }
    finally {
        if (Test-Path -LiteralPath $tempBaseDir) {
            Remove-Item -Recurse -Force -LiteralPath $tempBaseDir -ErrorAction SilentlyContinue
        }
    }
}

function Get-TrxTestCounts {
    param([string]$ResultsDir)

    $total = 0
    $executed = 0
    $passed = 0
    $failed = 0
    $fileCount = 0

    if (-not (Test-Path -LiteralPath $ResultsDir)) {
        return [PSCustomObject]@{
            FileCount = 0
            Total     = 0
            Executed  = 0
            Passed    = 0
            Failed    = 0
            ParseError = ""
        }
    }

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDir -Filter "*.trx" -Recurse -File -ErrorAction SilentlyContinue)
    foreach ($file in $trxFiles) {
        $fileCount++
        try {
            [xml]$doc = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
            $counters = $doc.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
            if ($null -ne $counters) {
                $tAttr = $counters.GetAttribute("total")
                $eAttr = $counters.GetAttribute("executed")
                $pAttr = $counters.GetAttribute("passed")
                $fAttr = $counters.GetAttribute("failed")

                $tVal = if (-not [string]::IsNullOrWhiteSpace($tAttr)) { [int]$tAttr } else { 0 }
                $eVal = if (-not [string]::IsNullOrWhiteSpace($eAttr)) { [int]$eAttr } else { $tVal }
                $pVal = if (-not [string]::IsNullOrWhiteSpace($pAttr)) { [int]$pAttr } else { 0 }
                $fVal = if (-not [string]::IsNullOrWhiteSpace($fAttr)) { [int]$fAttr } else { 0 }

                $total += $tVal
                $executed += $eVal
                $passed += $pVal
                $failed += $fVal
            }
        }
        catch {
            return [PSCustomObject]@{
                FileCount  = $fileCount
                Total      = $total
                Executed   = $executed
                Passed     = $passed
                Failed     = $failed
                ParseError = "TRX ファイル '$($file.Name)' の解析に失敗しました: $($_.Exception.Message)"
            }
        }
    }

    return [PSCustomObject]@{
        FileCount  = $fileCount
        Total      = $total
        Executed   = $executed
        Passed     = $passed
        Failed     = $failed
        ParseError = ""
    }
}

function Remove-AgentBuildDir {
    param(
        [string]$RunDirToRemove,
        [string]$RootDirToPrune
    )

    $removedRunDir = $true
    if (Test-Path -LiteralPath $RunDirToRemove) {
        $removedRunDir = $false
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Remove-Item -Recurse -Force -LiteralPath $RunDirToRemove -ErrorAction Stop
                if (-not (Test-Path -LiteralPath $RunDirToRemove)) {
                    $removedRunDir = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 300
            }
        }
    }

    # 他の並行実行がなければ親の .agent_build ディレクトリも空削除する
    if ($removedRunDir -and -not [string]::IsNullOrWhiteSpace($RootDirToPrune) -and (Test-Path -LiteralPath $RootDirToPrune)) {
        try {
            $remaining = @(Get-ChildItem -LiteralPath $RootDirToPrune -Force -ErrorAction SilentlyContinue)
            if ($remaining.Count -eq 0) {
                Remove-Item -Force -LiteralPath $RootDirToPrune -ErrorAction SilentlyContinue
            }
        }
        catch {
            # 他プロセスが同時に書き込んでいる場合は無視
        }
    }

    return $removedRunDir
}

function Write-GateHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------------------" -ForegroundColor DarkGray
}

function New-GateResult {
    param(
        [string]$Name,
        [string]$Status,
        [double]$DurationSeconds,
        [int]$WarningCount = 0,
        [int]$ErrorCount = 0,
        [string]$Summary = "",
        [string[]]$Details = @()
    )
    return [PSCustomObject][ordered]@{
        name             = $Name
        status           = $Status
        duration_seconds = $DurationSeconds
        warning_count    = $WarningCount
        error_count      = $ErrorCount
        summary          = $Summary
        details          = @($Details)
    }
}

$gateResults = New-Object System.Collections.Generic.List[object]
$overallPassed = $true
$cleanupSucceeded = $true
$resolvedBaseRef = ""
$resolvedBaseCommit = ""
$buildLockWaitSeconds = 0.0
$branchName = Get-CurrentBranchName -WorkingDirectory $repoRoot
$isWorktree = Test-IsGitWorktree -RepoPath $repoRoot
$buildLockMutexName = "Local\AudioEffector_VerifyLoop_BuildLock"

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  Audio Effector - 統合検証ハーネス (verify_loop.ps1)" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host ("  リポジトリ : {0}{1}" -f $repoRoot, $(if ($isWorktree) { " (worktree)" } else { "" }))
if (-not [string]::IsNullOrWhiteSpace($branchName)) {
    Write-Host ("  ブランチ   : {0}" -f $branchName)
}
Write-Host ("  比較ベース : {0}" -f $BaseBranch)
Write-Host ("  モード     : {0}" -f $(if ($Full) { "Full (最終ゲート)" } elseif ($SkipSpecSync) { "Fast Iteration (SkipSpecSync)" } else { "Standard" }))
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    Write-Host ("  テスト絞り : {0}" -f $TestFilter)
}

try {
    # ==========================================================================
    # Preflight: 引数整合性・比較元コミットの事前解決
    # ==========================================================================
    $preflightOk = $true
    $preflightErrors = New-Object System.Collections.Generic.List[string]

    if ($Full -and -not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $preflightOk = $false
        $preflightErrors.Add("-Full（最終ゲート）と -TestFilter の併用は禁止されています。最終ゲートでは全テストを実行してください。")
    }

    if ($BuildLockTimeoutSeconds -le 0) {
        $preflightOk = $false
        $preflightErrors.Add("-BuildLockTimeoutSeconds には 1 以上の整数を指定してください (指定値: $BuildLockTimeoutSeconds)。")
    }

    $baseRes = Resolve-BaseMergeCommit -BaseRef $BaseBranch -PreferOriginBase:$PreferOriginBase -FetchBase:$FetchBase -WorkingDirectory $repoRoot
    if (-not $baseRes.Success) {
        $preflightOk = $false
        $preflightErrors.Add($baseRes.Error)
    }
    else {
        $resolvedBaseRef = $baseRes.ResolvedRef
        $resolvedBaseCommit = $baseRes.MergeBase
        Write-Host ("  比較元Ref  : {0}" -f $resolvedBaseRef)
        Write-Host ("  解決済Base : {0}" -f $resolvedBaseCommit)
    }

    if (-not $preflightOk) {
        $overallPassed = $false
        Write-GateHeader -Title "Preflight: 事前検証エラー"
        foreach ($err in $preflightErrors) {
            Write-Host "  [FAIL] $err" -ForegroundColor Red
        }
        $gateResults.Add((New-GateResult -Name "preflight" -Status "FAILED" -DurationSeconds 0 -ErrorCount $preflightErrors.Count -Summary ($preflightErrors -join " / ") -Details @($preflightErrors.ToArray())))
    }

    if ($preflightOk) {
    # ==========================================================================
    # Gate 1: 隔離ビルド検証 (build_rules.md 準拠・名前付き Mutex 直列化)
    # ==========================================================================
    Write-GateHeader -Title "Gate 1/5: 隔離ビルド検証 (dotnet build)"

    $lockRes = Invoke-WithBuildLock -MutexName $buildLockMutexName -TimeoutSeconds $BuildLockTimeoutSeconds -Bypass:$NoBuildLock -Action {
        param($LockInfo)

        if ($null -ne $LockInfo) {
            if ($LockInfo.WasAbandoned) {
                Write-Host "  [WARN] 前プロセスの異常終了により放棄されたビルドロック (AbandonedMutex) を検出し、所有権を引き継ぎました。" -ForegroundColor Yellow
            }
            elseif ($LockInfo.WaitSeconds -ge 0.1) {
                Write-Host ("  [INFO] 他の並列ビルド完了を待機し、ビルドロックを取得しました (待機: {0}秒)。" -f $LockInfo.WaitSeconds) -ForegroundColor DarkGray
            }
        }

        $buildArgs = @("build", $solutionPath) + $artifactsOutputArgs + @("--nologo")
        $exec = Invoke-ExternalCommand -FilePath "dotnet" -ArgumentList $buildArgs -WorkingDirectory $repoRoot -TimeoutSeconds $BuildTimeoutSeconds

        $combined = ($exec.StdOut + "`n" + $exec.StdErr)
        # ソリューション構成変更直後などに WPF の .g.resources 生成で並列ノード間ファイルロック競合 (RG1000) が発生した場合、
        # 単一ノード (-m:1) で 1 回のみ安全リトライする（警告 0・エラー 0 の判定基準は一切緩和しない）
        if ($exec.ExitCode -ne 0 -and -not $exec.StartFailed -and -not $exec.TimedOut -and $combined -match 'error\s+RG1000:.*used by another process') {
            Write-Host "  [WARN] 並列ビルドで WPF リソースロック (RG1000) を検出しました。単一ノード (-m:1) でリトライします..." -ForegroundColor Yellow
            $retryArgs = @("build", $solutionPath) + $artifactsOutputArgs + @("-m:1", "--nologo")
            $exec = Invoke-ExternalCommand -FilePath "dotnet" -ArgumentList $retryArgs -WorkingDirectory $repoRoot -TimeoutSeconds $BuildTimeoutSeconds
            $combined = ($exec.StdOut + "`n" + $exec.StdErr)
        }

        return [PSCustomObject]@{
            Exec     = $exec
            Combined = $combined
        }
    }

    $buildLockWaitSeconds = $lockRes.WaitSeconds

    if (-not $lockRes.Acquired) {
        $overallPassed = $false
        $msg = $lockRes.Error
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $gateResults.Add((New-GateResult -Name "build" -Status "FAILED" -DurationSeconds $buildLockWaitSeconds -ErrorCount 1 -Summary $msg))
    }
    else {
    $buildExec = $lockRes.Result.Exec
    $buildCombined = $lockRes.Result.Combined
    $buildLines = $buildCombined -split "`r?`n"

    $warningLines = @($buildLines | Where-Object { $_ -match ':\s*warning\s+[A-Za-z]+\d+:' } | Select-Object -Unique)
    $errorLines = @($buildLines | Where-Object { $_ -match ':\s*error\s+[A-Za-z]+\d+:' } | Select-Object -Unique)

    $warningCount = $warningLines.Count
    $errorCount = $errorLines.Count

    # MSBuild サマリ行からも警告・エラー数を補完チェック
    if ($warningCount -eq 0 -and $buildCombined -match '(\d+)\s*(個の警告|Warning\(s\))') {
        $warningCount = [int]$Matches[1]
    }
    if ($errorCount -eq 0 -and $buildCombined -match '(\d+)\s*(エラー|Error\(s\))') {
        $errorCount = [int]$Matches[1]
    }

    # 現状ソリューションは既存警告 0 のため、警告 > 0 はデフォルトで失敗扱い (-StrictWarnings 指定時も同様)
    $enforceZeroWarnings = $true
    if ($StrictWarnings) {
        $enforceZeroWarnings = $true
    }

    if ($buildExec.StartFailed) {
        $overallPassed = $false
        $msg = "コマンド '$($buildExec.FilePath)' の起動に失敗しました: $($buildExec.StartError)"
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $gateResults.Add((New-GateResult -Name "build" -Status "FAILED" -DurationSeconds $buildExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details @($buildExec.Command, $buildExec.StartError)))
    }
    elseif ($buildExec.TimedOut) {
        $overallPassed = $false
        $msg = "ビルドがタイムアウトしました (${BuildTimeoutSeconds}秒超過)。"
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $gateResults.Add((New-GateResult -Name "build" -Status "FAILED" -DurationSeconds $buildExec.DurationSeconds -WarningCount $warningCount -ErrorCount 1 -Summary $msg))
    }
    elseif ($buildExec.ExitCode -ne 0 -or $errorCount -gt 0) {
        $overallPassed = $false
        $msg = "ビルドに失敗しました (ExitCode=$($buildExec.ExitCode), Errors=$errorCount, Warnings=$warningCount)。"
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $detailLines = @($errorLines | Select-Object -First 20)
        if ($detailLines.Count -eq 0) {
            $detailLines = @($buildLines | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 20)
        }
        foreach ($d in $detailLines) { Write-Host "    $d" -ForegroundColor Red }
        $gateResults.Add((New-GateResult -Name "build" -Status "FAILED" -DurationSeconds $buildExec.DurationSeconds -WarningCount $warningCount -ErrorCount ([Math]::Max($errorCount, 1)) -Summary $msg -Details $detailLines))
    }
    elseif ($enforceZeroWarnings -and $warningCount -gt 0) {
        $overallPassed = $false
        $msg = "ビルド警告が $warningCount 件検出されました (警告 0 必須基準に違反)。"
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $detailLines = @($warningLines | Select-Object -First 20)
        foreach ($d in $detailLines) { Write-Host "    $d" -ForegroundColor Yellow }
        $gateResults.Add((New-GateResult -Name "build" -Status "FAILED" -DurationSeconds $buildExec.DurationSeconds -WarningCount $warningCount -ErrorCount 0 -Summary $msg -Details $detailLines))
    }
    else {
        $msg = "隔離ビルド成功 (エラー: 0, 警告: $warningCount, 所要: $($buildExec.DurationSeconds)秒, ロック待機: ${buildLockWaitSeconds}秒)。"
        Write-Host "  [PASS] $msg" -ForegroundColor Green
        $gateResults.Add((New-GateResult -Name "build" -Status "PASSED" -DurationSeconds $buildExec.DurationSeconds -WarningCount $warningCount -ErrorCount 0 -Summary $msg))
    }
    }

    # ==========================================================================
    # Gate 2: 単体テスト検証 (ソリューション単位 dotnet test)
    # ==========================================================================
    Write-GateHeader -Title "Gate 2/5: 単体テスト検証 (dotnet test)"

    $buildPassed = ($gateResults[0].status -eq "PASSED")
    if (-not $buildPassed) {
        $msg = "ビルド失敗のため単体テストをスキップしました。"
        Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
        $gateResults.Add((New-GateResult -Name "test" -Status "SKIPPED" -DurationSeconds 0 -Summary $msg))
    }
    else {
        $testResultsDir = Join-Path $agentBuildDir "TestResults"
        New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null

        $testArgs = @("test", $solutionPath) + $artifactsOutputArgs + @("--no-build", "--nologo", "--logger", "trx", "--results-directory", $testResultsDir)
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $testArgs += @("--filter", $TestFilter)
        }

        $testExec = Invoke-ExternalCommand -FilePath "dotnet" -ArgumentList $testArgs -WorkingDirectory $repoRoot -TimeoutSeconds $TestTimeoutSeconds
        $testCombined = ($testExec.StdOut + "`n" + $testExec.StdErr)
        $testLines = $testCombined -split "`r?`n"

        $failedTests = @($testLines | Where-Object { $_ -match '^\s*(失敗|Failed)\s+' } | Select-Object -First 20)
        $trxStats = Get-TrxTestCounts -ResultsDir $testResultsDir

        if ($testExec.StartFailed) {
            $overallPassed = $false
            $msg = "コマンド '$($testExec.FilePath)' の起動に失敗しました: $($testExec.StartError)"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $gateResults.Add((New-GateResult -Name "test" -Status "FAILED" -DurationSeconds $testExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details @($testExec.Command, $testExec.StartError)))
        }
        elseif ($testExec.TimedOut) {
            $overallPassed = $false
            $msg = "単体テストがタイムアウトしました (${TestTimeoutSeconds}秒超過)。プロセスツリーを強制終了しました。"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $tailLines = @($testLines | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 15)
            $gateResults.Add((New-GateResult -Name "test" -Status "FAILED" -DurationSeconds $testExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details $tailLines))
        }
        elseif (-not [string]::IsNullOrWhiteSpace($trxStats.ParseError)) {
            $overallPassed = $false
            $msg = "テスト結果 (TRX) の集計に失敗しました: $($trxStats.ParseError)"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $gateResults.Add((New-GateResult -Name "test" -Status "FAILED" -DurationSeconds $testExec.DurationSeconds -ErrorCount 1 -Summary $msg))
        }
        elseif ($testExec.ExitCode -ne 0 -or $trxStats.Failed -gt 0) {
            $overallPassed = $false
            $msg = "単体テストに失敗しました (ExitCode=$($testExec.ExitCode), 実行: $($trxStats.Executed)件, 合格: $($trxStats.Passed)件, 失敗: $($trxStats.Failed)件, 所要: $($testExec.DurationSeconds)秒)。"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $detailLines = @()
            if ($failedTests.Count -gt 0) { $detailLines += $failedTests }
            if ($detailLines.Count -eq 0) {
                $detailLines = @($testLines | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 20)
            }
            foreach ($d in $detailLines) { Write-Host "    $d" -ForegroundColor Red }
            $errCount = [Math]::Max($trxStats.Failed, [Math]::Max($failedTests.Count, 1))
            $gateResults.Add((New-GateResult -Name "test" -Status "FAILED" -DurationSeconds $testExec.DurationSeconds -ErrorCount $errCount -Summary $msg -Details $detailLines))
        }
        elseif ($trxStats.Executed -eq 0) {
            $overallPassed = $false
            $filterNote = if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { " (フィルタ: $TestFilter)" } else { "" }
            $msg = "実行された単体テストが 0 件でした${filterNote} (TRX: $($trxStats.FileCount)件)。テストフィルタの誤記やテスト未検出を確認してください。"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $detailLines = @($testLines | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 15)
            foreach ($d in $detailLines) { Write-Host "    $d" -ForegroundColor Red }
            $gateResults.Add((New-GateResult -Name "test" -Status "FAILED" -DurationSeconds $testExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details $detailLines))
        }
        else {
            $skippedCount = [Math]::Max(0, $trxStats.Total - $trxStats.Executed)
            $msg = "単体テスト合格 (実行: $($trxStats.Executed)件, 合格: $($trxStats.Passed)件, 失敗: 0件, スキップ: ${skippedCount}件, TRX: $($trxStats.FileCount)件, 所要: $($testExec.DurationSeconds)秒)。"
            Write-Host "  [PASS] $msg" -ForegroundColor Green
            $gateResults.Add((New-GateResult -Name "test" -Status "PASSED" -DurationSeconds $testExec.DurationSeconds -Summary $msg))
        }
    }

    # ==========================================================================
    # Gate 3: 仕様書同期検証 (check_spec_sync.py)
    # ==========================================================================
    Write-GateHeader -Title "Gate 3/5: 仕様書同期検証 (check_spec_sync.py)"

    if ($SkipSpecSync -and -not $Full) {
        $msg = "-SkipSpecSync 指定によりスキップしました (最終ゲート・PR 作成前はスキップ禁止)。"
        Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
        $gateResults.Add((New-GateResult -Name "spec_sync" -Status "SKIPPED" -DurationSeconds 0 -Summary $msg))
    }
    else {
        $specScript = Join-Path $repoRoot "tools\check_spec_sync.py"
        $specArgs = @("-B", $specScript, "--base", $resolvedBaseCommit, "--strict", "--json")
        $specExec = Invoke-ExternalCommand -FilePath "python" -ArgumentList $specArgs -WorkingDirectory $repoRoot -TimeoutSeconds 60

        if ($specExec.StartFailed) {
            $overallPassed = $false
            $msg = "コマンド '$($specExec.FilePath)' の起動に失敗しました: $($specExec.StartError)"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $gateResults.Add((New-GateResult -Name "spec_sync" -Status "FAILED" -DurationSeconds $specExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details @($specExec.Command, $specExec.StartError)))
        }
        elseif ($specExec.TimedOut) {
            $overallPassed = $false
            $msg = "仕様書同期チェックがタイムアウトしました。"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $gateResults.Add((New-GateResult -Name "spec_sync" -Status "FAILED" -DurationSeconds $specExec.DurationSeconds -ErrorCount 1 -Summary $msg))
        }
        else {
            $specJson = $null
            try {
                if (-not [string]::IsNullOrWhiteSpace($specExec.StdOut)) {
                    $specJson = $specExec.StdOut | ConvertFrom-Json
                }
            }
            catch {
                $specJson = $null
            }

            if ($null -eq $specJson) {
                $overallPassed = $false
                $msg = "check_spec_sync.py の出力解析に失敗しました (ExitCode=$($specExec.ExitCode))。"
                Write-Host "  [FAIL] $msg" -ForegroundColor Red
                $detailLines = @(($specExec.StdOut + "`n" + $specExec.StdErr) -split "`r?`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 15)
                foreach ($d in $detailLines) { Write-Host "    $d" -ForegroundColor Red }
                $gateResults.Add((New-GateResult -Name "spec_sync" -Status "FAILED" -DurationSeconds $specExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details $detailLines))
            }
            else {
                $primaryMissing = @($specJson.primary_missing)
                $relatedMissing = @($specJson.related_missing)

                if ($specExec.ExitCode -ne 0 -or $primaryMissing.Count -gt 0) {
                    $overallPassed = $false
                    $msg = "未更新の主要仕様書 (Primary) が $($primaryMissing.Count) 件あります。"
                    Write-Host "  [FAIL] $msg" -ForegroundColor Red
                    $detailLines = @()
                    foreach ($item in $primaryMissing) {
                        $triggers = ($item.triggered_by -join ", ")
                        $line = "Primary 未更新: $($item.spec) (トリガー: $triggers)"
                        $detailLines += $line
                        Write-Host "    - $line" -ForegroundColor Red
                    }
                    $gateResults.Add((New-GateResult -Name "spec_sync" -Status "FAILED" -DurationSeconds $specExec.DurationSeconds -ErrorCount $primaryMissing.Count -WarningCount $relatedMissing.Count -Summary $msg -Details $detailLines))
                }
                else {
                    $codeCount = @($specJson.code_files).Count
                    $updatedCount = @($specJson.updated_specs).Count
                    $msg = "仕様書同期 OK (変更コード: ${codeCount}件, 更新済み仕様書: ${updatedCount}件, 未更新 Primary: 0件, 参考 Related 未更新: $($relatedMissing.Count)件)。"
                    Write-Host "  [PASS] $msg" -ForegroundColor Green
                    $gateResults.Add((New-GateResult -Name "spec_sync" -Status "PASSED" -DurationSeconds $specExec.DurationSeconds -WarningCount 0 -ErrorCount 0 -Summary $msg))
                }
            }
        }
    }

    # ==========================================================================
    # Gate 4: XAML バインディング・インデクサ等価性検証 (check_equivalence.py)
    # ==========================================================================
    Write-GateHeader -Title "Gate 4/5: XAML バインディング等価性検証 (check_equivalence.py)"

    $changedFilesRes = Get-ChangedFiles -BaseCommit $resolvedBaseCommit
    if (-not $changedFilesRes.Success) {
        $overallPassed = $false
        $msg = "変更ファイルの取得に失敗しました: $($changedFilesRes.Error)"
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "FAILED" -DurationSeconds 0 -ErrorCount 1 -Summary $msg))
    }
    else {
    $changedFiles = @($changedFilesRes.Files)
    $changedXamlFiles = @($changedFiles | Where-Object { $_ -match '\.xaml$' })

    if (-not $Full -and $changedXamlFiles.Count -eq 0) {
        $msg = "XAML ファイルの変更がないためスキップしました (-Full 指定で強制実行可能)。"
        Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
        $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "SKIPPED" -DurationSeconds 0 -Summary $msg))
    }
    else {
        $equivScript = Join-Path $repoRoot "tools\check_equivalence.py"
        $equivArgs = @("-B", $equivScript, "--xaml", "--indexer", "--json")
        $equivExec = Invoke-ExternalCommand -FilePath "python" -ArgumentList $equivArgs -WorkingDirectory $repoRoot -TimeoutSeconds 60

        if ($equivExec.StartFailed) {
            $overallPassed = $false
            $msg = "コマンド '$($equivExec.FilePath)' の起動に失敗しました: $($equivExec.StartError)"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "FAILED" -DurationSeconds $equivExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details @($equivExec.Command, $equivExec.StartError)))
        }
        elseif ($equivExec.TimedOut) {
            $overallPassed = $false
            $msg = "XAML 等価性チェックがタイムアウトしました。"
            Write-Host "  [FAIL] $msg" -ForegroundColor Red
            $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "FAILED" -DurationSeconds $equivExec.DurationSeconds -ErrorCount 1 -Summary $msg))
        }
        else {
            $equivItems = $null
            try {
                if (-not [string]::IsNullOrWhiteSpace($equivExec.StdOut)) {
                    $parsed = $equivExec.StdOut | ConvertFrom-Json
                    $equivItems = @($parsed)
                }
            }
            catch {
                $equivItems = $null
            }

            if ($null -eq $equivItems) {
                $overallPassed = $false
                $msg = "check_equivalence.py の出力解析に失敗しました (ExitCode=$($equivExec.ExitCode))。"
                Write-Host "  [FAIL] $msg" -ForegroundColor Red
                $detailLines = @(($equivExec.StdOut + "`n" + $equivExec.StdErr) -split "`r?`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 15)
                $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "FAILED" -DurationSeconds $equivExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details $detailLines))
            }
            else {
                $failItems = @($equivItems | Where-Object { $_.status -eq "FAIL" })
                $bindingWarns = @($equivItems | Where-Object { $_.status -eq "WARN" -and $_.category -ne "XAML Indexer Boundary" })
                $indexerWarns = @($equivItems | Where-Object { $_.status -eq "WARN" -and $_.category -eq "XAML Indexer Boundary" })

                # 変更された XAML ファイル内のインデクサ警告は、比較元と (ファイル, バインディング式, 出現件数) で照合し
                # 既存の式の行移動は許容しつつ、新たに増加した警告のみ失敗扱いとする
                $newIndexerWarns = @()
                $baseCompareError = ""
                if ($changedXamlFiles.Count -gt 0 -and $indexerWarns.Count -gt 0) {
                    $baseCountsRes = Get-BaseXamlIndexerKeyCounts -BaseCommit $resolvedBaseCommit -RelativePaths $changedXamlFiles
                    if (-not $baseCountsRes.Success) {
                        $baseCompareError = $baseCountsRes.Error
                    }
                    else {
                        $remainingBaseCounts = @{}
                        foreach ($k in $baseCountsRes.Counts.Keys) {
                            $remainingBaseCounts[$k] = [int]$baseCountsRes.Counts[$k]
                        }

                        foreach ($iw in $indexerWarns) {
                            $relPath = ""
                            if (-not [string]::IsNullOrWhiteSpace($iw.file_path)) {
                                $normFull = $iw.file_path.Replace("\", "/")
                                $normRoot = $repoRoot.Replace("\", "/").TrimEnd("/") + "/"
                                if ($normFull.StartsWith($normRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                                    $relPath = $normFull.Substring($normRoot.Length)
                                }
                                else {
                                    $relPath = $normFull
                                }
                            }
                            if ($changedXamlFiles -contains $relPath) {
                                $rawBinding = ""
                                foreach ($d in @($iw.details)) {
                                    if ($d -match '^バインディング式:\s*(.+)$') {
                                        $rawBinding = $Matches[1].Trim()
                                        break
                                    }
                                }
                                $key = "{0}|{1}|{2}" -f $relPath.ToLowerInvariant(), $iw.item_name, $rawBinding
                                if ($remainingBaseCounts.ContainsKey($key) -and $remainingBaseCounts[$key] -gt 0) {
                                    $remainingBaseCounts[$key] = $remainingBaseCounts[$key] - 1
                                }
                                else {
                                    $newIndexerWarns += $iw
                                }
                            }
                        }
                    }
                }

                $blockingCount = $failItems.Count + $bindingWarns.Count + $newIndexerWarns.Count
                if (-not [string]::IsNullOrWhiteSpace($baseCompareError)) {
                    $overallPassed = $false
                    $msg = "比較元 XAML 警告の照合に失敗しました: $baseCompareError"
                    Write-Host "  [FAIL] $msg" -ForegroundColor Red
                    $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "FAILED" -DurationSeconds $equivExec.DurationSeconds -ErrorCount 1 -Summary $msg))
                }
                elseif ($equivExec.ExitCode -ne 0 -or $blockingCount -gt 0) {
                    $overallPassed = $false
                    $msg = "XAML バインディング検証で問題が検出されました (FAIL: $($failItems.Count), Binding WARN: $($bindingWarns.Count), 新規 Indexer WARN: $($newIndexerWarns.Count))。"
                    Write-Host "  [FAIL] $msg" -ForegroundColor Red
                    $detailLines = @()
                    foreach ($item in ($failItems + $bindingWarns + $newIndexerWarns)) {
                        $line = "[$($item.status)] $($item.category): $($item.message)"
                        $detailLines += $line
                        Write-Host "    - $line" -ForegroundColor Red
                    }
                    $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "FAILED" -DurationSeconds $equivExec.DurationSeconds -WarningCount ($bindingWarns.Count + $indexerWarns.Count) -ErrorCount $blockingCount -Summary $msg -Details $detailLines))
                }
                else {
                    $existingIndexerCount = $indexerWarns.Count
                    $msg = "XAML バインディング検証合格 (FAIL: 0, Binding WARN: 0, 既存 Indexer WARN: ${existingIndexerCount}件, 所要: $($equivExec.DurationSeconds)秒)。"
                    Write-Host "  [PASS] $msg" -ForegroundColor Green
                    $gateResults.Add((New-GateResult -Name "xaml_equivalence" -Status "PASSED" -DurationSeconds $equivExec.DurationSeconds -WarningCount $existingIndexerCount -ErrorCount 0 -Summary $msg))
                }
            }
        }
    }
    }

    # ==========================================================================
    # Gate 5: Python ツール単体テスト検証 (tools/tests)
    # ==========================================================================
    Write-GateHeader -Title "Gate 5/5: Python ツール単体テスト検証 (unittest)"

    $pyTestsDir = Join-Path $repoRoot "tools\tests"
    if (-not (Test-Path -LiteralPath $pyTestsDir)) {
        $msg = "tools/tests ディレクトリが存在しないためスキップしました。"
        Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
        $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "SKIPPED" -DurationSeconds 0 -Summary $msg))
    }
    elseif (-not $changedFilesRes.Success) {
        $overallPassed = $false
        $msg = "変更ファイルの取得に失敗したため Python ツールテスト対象を判定できません: $($changedFilesRes.Error)"
        Write-Host "  [FAIL] $msg" -ForegroundColor Red
        $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "FAILED" -DurationSeconds 0 -ErrorCount 1 -Summary $msg))
    }
    else {
        $changedToolsFiles = @(@($changedFilesRes.Files) | Where-Object { $_ -match '^tools/' })
        if (-not $Full -and $changedToolsFiles.Count -eq 0) {
            $msg = "tools/ 配下の変更がないためスキップしました (-Full 指定で強制実行可能)。"
            Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
            $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "SKIPPED" -DurationSeconds 0 -Summary $msg))
        }
        else {
            $pyArgs = @("-B", "-m", "unittest", "discover", "-s", "tools/tests", "-v")
            $pyExec = Invoke-ExternalCommand -FilePath "python" -ArgumentList $pyArgs -WorkingDirectory $repoRoot -TimeoutSeconds 120

            if ($pyExec.StartFailed) {
                $overallPassed = $false
                $msg = "コマンド '$($pyExec.FilePath)' の起動に失敗しました: $($pyExec.StartError)"
                Write-Host "  [FAIL] $msg" -ForegroundColor Red
                $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "FAILED" -DurationSeconds $pyExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details @($pyExec.Command, $pyExec.StartError)))
            }
            elseif ($pyExec.TimedOut) {
                $overallPassed = $false
                $msg = "Python ツール単体テストがタイムアウトしました (120秒)。"
                Write-Host "  [FAIL] $msg" -ForegroundColor Red
                $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "FAILED" -DurationSeconds $pyExec.DurationSeconds -ErrorCount 1 -Summary $msg))
            }
            else {
                $combinedPyOut = ($pyExec.StdOut + "`n" + $pyExec.StdErr)
                $ranCount = 0
                if ($combinedPyOut -match 'Ran\s+(\d+)\s+tests?') {
                    $ranCount = [int]$Matches[1]
                }

                if ($pyExec.ExitCode -ne 0) {
                    $overallPassed = $false
                    $msg = "Python ツール単体テストに失敗しました (ExitCode=$($pyExec.ExitCode), 実行: ${ranCount}件, 所要: $($pyExec.DurationSeconds)秒)。"
                    Write-Host "  [FAIL] $msg" -ForegroundColor Red
                    $detailLines = @($combinedPyOut -split "`r?`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -Last 20)
                    foreach ($line in $detailLines) {
                        Write-Host "    $line" -ForegroundColor Red
                    }
                    $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "FAILED" -DurationSeconds $pyExec.DurationSeconds -ErrorCount 1 -Summary $msg -Details $detailLines))
                }
                else {
                    $msg = "Python ツール単体テスト合格 (実行: ${ranCount}件, 所要: $($pyExec.DurationSeconds)秒)。"
                    Write-Host "  [PASS] $msg" -ForegroundColor Green
                    $gateResults.Add((New-GateResult -Name "python_tools_test" -Status "PASSED" -DurationSeconds $pyExec.DurationSeconds -WarningCount 0 -ErrorCount 0 -Summary $msg))
                }
            }
        }
    }
    }
}
finally {
    # ==========================================================================
    # 後処理: 実行単位の隔離ビルド出力の確実なクリーンアップ
    # ==========================================================================
    $cleanupSucceeded = Remove-AgentBuildDir -RunDirToRemove $agentBuildDir -RootDirToPrune $agentBuildRoot
    $overallStopwatch.Stop()
}

if (-not $cleanupSucceeded) {
    $overallPassed = $false
    Write-Host ""
    Write-Host "  [FAIL] 一時ディレクトリ .agent_build の削除に失敗しました。" -ForegroundColor Red
}

$totalDuration = [Math]::Round($overallStopwatch.Elapsed.TotalSeconds, 2)
$exitCode = if ($overallPassed) { 0 } else { 1 }

# ==============================================================================
# 最終サマリ表示 & JSON 出力
# ==============================================================================
Write-Host ""
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "  検証サマリ (合計所要時間: ${totalDuration}秒)" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan
foreach ($g in $gateResults) {
    $color = switch ($g.status) {
        "PASSED"  { "Green" }
        "SKIPPED" { "Yellow" }
        default   { "Red" }
    }
    Write-Host ("  [{0,-7}] {1,-18} ({2,6}s) | Warn:{3} Err:{4} | {5}" -f $g.status, $g.name, $g.duration_seconds, $g.warning_count, $g.error_count, $g.summary) -ForegroundColor $color
}
Write-Host "------------------------------------------------------------------------" -ForegroundColor DarkGray
if ($overallPassed) {
    Write-Host "  最終判定: PASS (終了コード: 0) / .agent_build クリーンアップ済み" -ForegroundColor Green
}
else {
    Write-Host "  最終判定: FAIL (終了コード: 1)" -ForegroundColor Red
}
Write-Host "========================================================================" -ForegroundColor Cyan

if (-not [string]::IsNullOrWhiteSpace($JsonOut)) {
    $resolvedJsonOut = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($JsonOut)
    $jsonParent = Split-Path -Parent $resolvedJsonOut
    if (-not [string]::IsNullOrWhiteSpace($jsonParent) -and -not (Test-Path -LiteralPath $jsonParent)) {
        New-Item -ItemType Directory -Path $jsonParent -Force | Out-Null
    }

    $reportObj = [PSCustomObject][ordered]@{
        timestamp               = (Get-Date).ToString("o")
        overall_passed          = $overallPassed
        exit_code               = $exitCode
        worktree_path           = $repoRoot
        branch_name             = $branchName
        base_branch             = $BaseBranch
        resolved_base_ref       = $resolvedBaseRef
        resolved_base_commit    = $resolvedBaseCommit
        build_lock_wait_seconds = $buildLockWaitSeconds
        full_mode               = [bool]$Full
        skip_spec_sync          = [bool]($SkipSpecSync -and -not $Full)
        test_filter             = $TestFilter
        cleanup_succeeded       = $cleanupSucceeded
        total_duration_seconds  = $totalDuration
        gates                   = @($gateResults.ToArray())
    }

    $jsonText = $reportObj | ConvertTo-Json -Depth 6
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($resolvedJsonOut, $jsonText, $utf8NoBom)
    Write-Host ("  JSON レポート出力先: {0}" -f $resolvedJsonOut) -ForegroundColor DarkGray
}

exit $exitCode
