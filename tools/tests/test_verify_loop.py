#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/verify_loop.ps1 の並列ワークツリー対応・比較ベース解決・ビルド Mutex 保護の検証テスト。"""

from __future__ import annotations

import codecs
import json
import shutil
import subprocess
import tempfile
import time
import unittest
import uuid
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
VERIFY_LOOP = REPO_ROOT / "tools" / "verify_loop.ps1"


def _run_git(args: list[str], cwd: Path) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        ["git", *args],
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    if result.returncode != 0:
        raise AssertionError(
            f"git {' '.join(args)} failed (code={result.returncode}):\nSTDOUT:\n{result.stdout}\nSTDERR:\n{result.stderr}"
        )
    return result


def _run_ps_command(script: str, cwd: Path, timeout: int = 30) -> subprocess.CompletedProcess[str]:
    cmd = [
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        script,
    ]
    return subprocess.run(
        cmd,
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        check=False,
    )


def _run_ps1_file(script_path: Path, args: list[str], cwd: Path, timeout: int = 30) -> subprocess.CompletedProcess[str]:
    cmd = [
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(script_path),
        *args,
    ]
    return subprocess.run(
        cmd,
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        check=False,
    )


def _ps_loader_snippet(ps1_path: Path, function_names: list[str]) -> str:
    """verify_loop.ps1 から指定関数群のみを AST 抽出してロードする PowerShell スニペットを返す。"""
    names_literal = ", ".join(f"'{name}'" for name in function_names)
    escaped_path = str(ps1_path).replace("'", "''")
    return f"""
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile('{escaped_path}', [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) {{ throw "Parse errors: $($errors | Out-String)" }}
$targetNames = @({names_literal})
$funcs = $ast.FindAll({{ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and ($targetNames -contains $n.Name) }}, $false)
foreach ($f in $funcs) {{ Invoke-Expression $f.Extent.Text }}
"""


def _wait_for_file(path: Path, timeout_sec: float = 15.0) -> bool:
    deadline = time.monotonic() + timeout_sec
    while time.monotonic() < deadline:
        if path.exists():
            return True
        time.sleep(0.025)
    return False


class VerifyLoopParallelWorktreeTests(unittest.TestCase):
    """tools/verify_loop.ps1 の構文・比較ベース解決・JSON スキーマ・Mutex 保護テスト。"""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory(prefix="ae_vloop_test_")
        self.base_dir = Path(self._tmp.name)

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def _create_diverged_origin_sandbox(self) -> dict[str, Path | str]:
        """ローカル develop が古く、origin/develop が 1 コミット進んだサンドボックスとワークツリーを構築する。"""
        origin_dir = self.base_dir / "origin.git"
        repo_dir = self.base_dir / "repo"
        pusher_dir = self.base_dir / "pusher"

        _run_git(["init", "--bare", str(origin_dir)], cwd=self.base_dir)
        _run_git(["clone", str(origin_dir), str(repo_dir)], cwd=self.base_dir)
        _run_git(["config", "user.name", "Test User"], cwd=repo_dir)
        _run_git(["config", "user.email", "test@example.com"], cwd=repo_dir)
        _run_git(["checkout", "-b", "develop"], cwd=repo_dir)

        (repo_dir / "file.txt").write_text("v1\n", encoding="utf-8")
        _run_git(["add", "."], cwd=repo_dir)
        _run_git(["commit", "-m", "chore: c1"], cwd=repo_dir)
        _run_git(["push", "-u", "origin", "develop"], cwd=repo_dir)
        c1_hash = _run_git(["rev-parse", "HEAD"], cwd=repo_dir).stdout.strip()

        # 別クローンから origin/develop に新しいコミット C2 を push する
        _run_git(["clone", str(origin_dir), str(pusher_dir)], cwd=self.base_dir)
        _run_git(["config", "user.name", "Test User"], cwd=pusher_dir)
        _run_git(["config", "user.email", "test@example.com"], cwd=pusher_dir)
        _run_git(["checkout", "develop"], cwd=pusher_dir)
        (pusher_dir / "file.txt").write_text("v2\n", encoding="utf-8")
        _run_git(["add", "."], cwd=pusher_dir)
        _run_git(["commit", "-m", "chore: c2"], cwd=pusher_dir)
        _run_git(["push", "origin", "develop"], cwd=pusher_dir)
        c2_hash = _run_git(["rev-parse", "HEAD"], cwd=pusher_dir).stdout.strip()

        # repo_dir で fetch する（origin/develop は C2、ローカル develop は古い C1 のまま）
        _run_git(["fetch", "origin", "develop"], cwd=repo_dir)

        # repo_dir 側の作業ブランチは origin/develop (C2) から分岐させる
        _run_git(["checkout", "-b", "feat/main-work", "origin/develop"], cwd=repo_dir)
        (repo_dir / "work.txt").write_text("main work\n", encoding="utf-8")
        _run_git(["add", "."], cwd=repo_dir)
        _run_git(["commit", "-m", "feat: main work"], cwd=repo_dir)

        # linked worktree を origin/develop (C2) から作成
        wt_dir = repo_dir / ".agent_worktrees" / "issue-999"
        wt_dir.parent.mkdir(parents=True, exist_ok=True)
        _run_git(["worktree", "add", "-b", "feat/999-wt-test", str(wt_dir), "origin/develop"], cwd=repo_dir)
        (wt_dir / "wt.txt").write_text("wt work\n", encoding="utf-8")
        _run_git(["add", "."], cwd=wt_dir)
        _run_git(["commit", "-m", "feat: wt work"], cwd=wt_dir)

        return {
            "origin_dir": origin_dir,
            "repo_dir": repo_dir,
            "pusher_dir": pusher_dir,
            "wt_dir": wt_dir,
            "c1": c1_hash,
            "c2": c2_hash,
        }

    def test_エンコーディングとPowerShell51構文および新規パラメータ定義が正しいこと(self) -> None:
        # Arrange & Act: UTF-8 with BOM & CRLF 検証
        raw = VERIFY_LOOP.read_bytes()
        self.assertTrue(raw.startswith(codecs.BOM_UTF8), "verify_loop.ps1 に UTF-8 BOM がありません。")
        decoded = raw[len(codecs.BOM_UTF8):].decode("utf-8")
        self.assertIn("\r\n", decoded, "verify_loop.ps1 は CRLF で保存されている必要があります。")
        self.assertNotIn("\n", decoded.replace("\r\n", ""), "verify_loop.ps1 に LF のみの行が混在しています。")

        # Act: PowerShell 5.1 AST パーサーで構文エラーとパラメータ定義を検証
        escaped_path = str(VERIFY_LOOP).replace("'", "''")
        ps_script = f"""
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile('{escaped_path}', [ref]$tokens, [ref]$errors)
$params = @{{}}
foreach ($p in $ast.ParamBlock.Parameters) {{
    $params[$p.Name.VariablePath.UserPath] = if ($null -ne $p.DefaultValue) {{ $p.DefaultValue.Extent.Text }} else {{ $null }}
}}
[PSCustomObject]@{{
    error_count = $errors.Count
    errors      = @($errors | ForEach-Object {{ $_.Message }})
    params      = $params
}} | ConvertTo-Json -Depth 4
"""
        res = _run_ps_command(ps_script, cwd=REPO_ROOT)
        self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
        data = json.loads(res.stdout)

        # Assert
        self.assertEqual(data["error_count"], 0, msg=f"構文エラー: {data['errors']}")
        params = data["params"]
        for expected in (
            "PreferOriginBase",
            "FetchBase",
            "BuildLockTimeoutSeconds",
            "NoBuildLock",
            "PythonTestTimeoutSeconds",
            "SkipLicenseSync",
            "StrictAllLicenses",
        ):
            self.assertIn(expected, params, msg=f"パラメータ -{expected} が未定義です。")
        self.assertEqual(params["BuildLockTimeoutSeconds"], "300")
        self.assertEqual(params["PythonTestTimeoutSeconds"], "240")

    def test_比較ベース解決が通常リポジトリとワークツリーおよびフォールバックで正しく動作すること(self) -> None:
        # Arrange
        sb = self._create_diverged_origin_sandbox()
        repo_dir = Path(sb["repo_dir"])
        wt_dir = Path(sb["wt_dir"])

        # origin リモートを持たないローカル専用リポジトリも作成
        local_only = self.base_dir / "local_only"
        local_only.mkdir()
        _run_git(["init"], cwd=local_only)
        _run_git(["config", "user.name", "Test User"], cwd=local_only)
        _run_git(["config", "user.email", "test@example.com"], cwd=local_only)
        _run_git(["checkout", "-b", "develop"], cwd=local_only)
        (local_only / "a.txt").write_text("local\n", encoding="utf-8")
        _run_git(["add", "."], cwd=local_only)
        _run_git(["commit", "-m", "chore: local"], cwd=local_only)
        local_hash = _run_git(["rev-parse", "HEAD"], cwd=local_only).stdout.strip()
        _run_git(["checkout", "-b", "feat/local-only"], cwd=local_only)

        loader = _ps_loader_snippet(
            VERIFY_LOOP,
            ["Stop-ProcessTree", "Invoke-ExternalCommand", "Test-IsGitWorktree", "Resolve-BaseMergeCommit"],
        )
        ps_script = loader + f"""
$rNormalDefault = Resolve-BaseMergeCommit -BaseRef 'develop' -WorkingDirectory '{str(repo_dir).replace("'", "''")}'
$rNormalPrefer  = Resolve-BaseMergeCommit -BaseRef 'develop' -PreferOriginBase -WorkingDirectory '{str(repo_dir).replace("'", "''")}'
$rWorktreeAuto  = Resolve-BaseMergeCommit -BaseRef 'develop' -WorkingDirectory '{str(wt_dir).replace("'", "''")}'
$rFallback      = Resolve-BaseMergeCommit -BaseRef 'develop' -PreferOriginBase -WorkingDirectory '{str(local_only).replace("'", "''")}'
[PSCustomObject]@{{
    normal_default = $rNormalDefault
    normal_prefer  = $rNormalPrefer
    worktree_auto  = $rWorktreeAuto
    fallback       = $rFallback
}} | ConvertTo-Json -Depth 4
"""
        # Act
        res = _run_ps_command(ps_script, cwd=REPO_ROOT)
        self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
        out = json.loads(res.stdout)

        # Assert: 通常リポジトリで -PreferOriginBase なし → ローカル develop (C1)
        self.assertTrue(out["normal_default"]["Success"])
        self.assertEqual(out["normal_default"]["ResolvedRef"], "develop")
        self.assertEqual(out["normal_default"]["MergeBase"], sb["c1"])

        # Assert: 通常リポジトリで -PreferOriginBase あり → origin/develop (C2)
        self.assertTrue(out["normal_prefer"]["Success"])
        self.assertEqual(out["normal_prefer"]["ResolvedRef"], "origin/develop")
        self.assertEqual(out["normal_prefer"]["MergeBase"], sb["c2"])

        # Assert: ワークツリー内実行（-PreferOriginBase 未指定） → 自動で origin/develop (C2) を優先
        self.assertTrue(out["worktree_auto"]["Success"])
        self.assertEqual(out["worktree_auto"]["ResolvedRef"], "origin/develop")
        self.assertEqual(out["worktree_auto"]["MergeBase"], sb["c2"])

        # Assert: origin/develop 不在時はローカル develop へフォールバック
        self.assertTrue(out["fallback"]["Success"])
        self.assertEqual(out["fallback"]["ResolvedRef"], "develop")
        self.assertEqual(out["fallback"]["MergeBase"], local_hash)

    def test_FetchBase指定時にリモート最新コミットを取得して比較ベースを解決できること(self) -> None:
        # Arrange
        sb = self._create_diverged_origin_sandbox()
        pusher_dir = Path(sb["pusher_dir"])
        wt_dir = Path(sb["wt_dir"])

        # リモート (origin/develop) にさらに C3 を push する（wt_dir からはまだ未 fetch）
        (pusher_dir / "file.txt").write_text("v3\n", encoding="utf-8")
        _run_git(["add", "."], cwd=pusher_dir)
        _run_git(["commit", "-m", "chore: c3"], cwd=pusher_dir)
        _run_git(["push", "origin", "develop"], cwd=pusher_dir)
        c3_hash = _run_git(["rev-parse", "HEAD"], cwd=pusher_dir).stdout.strip()

        before_ref = _run_git(["rev-parse", "origin/develop"], cwd=wt_dir).stdout.strip()
        self.assertEqual(before_ref, sb["c2"])

        loader = _ps_loader_snippet(
            VERIFY_LOOP,
            ["Stop-ProcessTree", "Invoke-ExternalCommand", "Test-IsGitWorktree", "Resolve-BaseMergeCommit"],
        )
        ps_script = loader + f"""
$res = Resolve-BaseMergeCommit -BaseRef 'develop' -PreferOriginBase -FetchBase -WorkingDirectory '{str(wt_dir).replace("'", "''")}'
$res | ConvertTo-Json -Depth 4
"""
        # Act
        res = _run_ps_command(ps_script, cwd=REPO_ROOT)
        self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
        data = json.loads(res.stdout)

        # Assert: fetch によって origin/develop が C3 へ更新され、解決も成功していること
        self.assertTrue(data["Success"], msg=data.get("Error"))
        self.assertEqual(data["ResolvedRef"], "origin/develop")
        after_ref = _run_git(["rev-parse", "origin/develop"], cwd=wt_dir).stdout.strip()
        self.assertEqual(after_ref, c3_hash)

    def test_JsonOut相対パス解決とワークツリー拡張JSONスキーマが正しく出力されること(self) -> None:
        # Arrange: サンドボックスのワークツリーに verify_loop.ps1 を配置
        sb = self._create_diverged_origin_sandbox()
        wt_dir = Path(sb["wt_dir"])
        wt_tools = wt_dir / "tools"
        wt_tools.mkdir(parents=True, exist_ok=True)
        wt_script = wt_tools / "verify_loop.ps1"
        shutil.copy2(VERIFY_LOOP, wt_script)

        # カレントディレクトリを別ディレクトリにして相対 -JsonOut の解決を確認
        caller_cwd = self.base_dir / "caller_cwd"
        caller_cwd.mkdir(parents=True, exist_ok=True)
        rel_json = Path("nested") / "verify_report.json"

        # Act: -Full と -TestFilter の併用で Preflight を即終了させつつ JSON 出力をエンドツーエンド検証
        res = _run_ps1_file(
            wt_script,
            ["-Full", "-TestFilter", "DummyFilter", "-JsonOut", str(rel_json)],
            cwd=caller_cwd,
        )

        # Assert: exit code 1 (Preflight 失敗) だが JSON はカレントディレクトリ基準で生成される
        self.assertEqual(res.returncode, 1, msg=res.stdout + "\n" + res.stderr)
        expected_json_path = caller_cwd / rel_json
        self.assertTrue(expected_json_path.is_file(), msg=f"JSON が期待パスに生成されていません: {expected_json_path}")

        report = json.loads(expected_json_path.read_text(encoding="utf-8"))
        for key in (
            "worktree_path",
            "branch_name",
            "resolved_base_ref",
            "resolved_base_commit",
            "build_lock_wait_seconds",
            "overall_passed",
            "exit_code",
            "gates",
        ):
            self.assertIn(key, report, msg=f"JSON レポートに必須キー '{key}' がありません。")

        self.assertEqual(Path(report["worktree_path"]).resolve(), wt_dir.resolve())
        self.assertEqual(report["branch_name"], "feat/999-wt-test")
        self.assertEqual(report["resolved_base_ref"], "origin/develop")
        self.assertEqual(report["resolved_base_commit"], sb["c2"])
        self.assertIsInstance(report["build_lock_wait_seconds"], (int, float))
        self.assertFalse(report["overall_passed"])
        self.assertEqual(report["exit_code"], 1)

    def test_ビルドロックMutexが並列実行を直列化しタイムアウトとAbandonedMutexを安全に処理すること(self) -> None:
        loader = _ps_loader_snippet(VERIFY_LOOP, ["Invoke-WithBuildLock"])

        # 1) 並列直列化と待機秒数計測の検証（2 段階ハンドシェイクで起動遅延依存を排除）
        with self.subTest(case="serialization_and_wait_seconds"):
            mutex_name = f"Local\\AudioEffector_TestLock_{uuid.uuid4().hex}"
            holder_ready_file = self.base_dir / "holder_ready.flag"
            waiter_ready_file = self.base_dir / "waiter_ready.flag"
            escaped_holder_ready = str(holder_ready_file).replace("'", "''")
            escaped_waiter_ready = str(waiter_ready_file).replace("'", "''")
            holder_script = f"""
$m = New-Object System.Threading.Mutex($false, '{mutex_name}')
$acquired = $m.WaitOne(5000, $false)
if (-not $acquired) {{ exit 2 }}
try {{
    [System.IO.File]::WriteAllText('{escaped_holder_ready}', 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not [System.IO.File]::Exists('{escaped_waiter_ready}') -and [DateTime]::UtcNow -lt $deadline) {{
        Start-Sleep -Milliseconds 25
    }}
    if (-not [System.IO.File]::Exists('{escaped_waiter_ready}')) {{
        exit 3
    }}
    Start-Sleep -Milliseconds 900
}} finally {{
    $m.ReleaseMutex()
    $m.Dispose()
}}
"""
            holder_proc = subprocess.Popen(
                ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", holder_script],
                cwd=str(REPO_ROOT),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            try:
                self.assertTrue(_wait_for_file(holder_ready_file), "ロック保持プロセスの起動待機に失敗しました。")
                waiter_script = loader + f"""
[System.IO.File]::WriteAllText('{escaped_waiter_ready}', 'ready')
$res = Invoke-WithBuildLock -MutexName '{mutex_name}' -TimeoutSeconds 10 -Action {{ 'build_ok' }}
$res | ConvertTo-Json -Depth 4
"""
                waiter_res = _run_ps_command(waiter_script, cwd=REPO_ROOT)
                self.assertEqual(waiter_res.returncode, 0, msg=waiter_res.stdout + "\n" + waiter_res.stderr)
                payload = json.loads(waiter_res.stdout)
                self.assertTrue(payload["Acquired"])
                self.assertFalse(payload["TimedOut"])
                self.assertFalse(payload["WasAbandoned"])
                self.assertEqual(payload["Result"], "build_ok")
                self.assertGreaterEqual(payload["WaitSeconds"], 0.4)
                self.assertEqual(holder_proc.wait(timeout=15), 0, "ロック保持プロセスが異常終了しました。")
            finally:
                if holder_proc.poll() is None:
                    try:
                        holder_proc.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        holder_proc.kill()
                        holder_proc.wait(timeout=5)

        # 2) ロック取得タイムアウトの検証
        with self.subTest(case="timeout"):
            mutex_name = f"Local\\AudioEffector_TestLock_{uuid.uuid4().hex}"
            ready_file = self.base_dir / "timeout_holder_ready.flag"
            holder_script = f"""
$m = New-Object System.Threading.Mutex($false, '{mutex_name}')
$acquired = $m.WaitOne(5000, $false)
if (-not $acquired) {{ exit 2 }}
try {{
    [System.IO.File]::WriteAllText('{str(ready_file).replace("'", "''")}', 'ready')
    Start-Sleep -Seconds 30
}} finally {{
    $m.ReleaseMutex()
    $m.Dispose()
}}
"""
            holder_proc = subprocess.Popen(
                ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", holder_script],
                cwd=str(REPO_ROOT),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            try:
                self.assertTrue(_wait_for_file(ready_file), "タイムアウト用ロック保持プロセスの起動待機に失敗しました。")
                waiter_script = loader + f"""
$res = Invoke-WithBuildLock -MutexName '{mutex_name}' -TimeoutSeconds 1 -Action {{ 'should_not_run' }}
$res | ConvertTo-Json -Depth 4
"""
                waiter_res = _run_ps_command(waiter_script, cwd=REPO_ROOT)
                self.assertEqual(waiter_res.returncode, 0, msg=waiter_res.stdout + "\n" + waiter_res.stderr)
                payload = json.loads(waiter_res.stdout)
                self.assertFalse(payload["Acquired"])
                self.assertTrue(payload["TimedOut"])
                self.assertIsNone(payload["Result"])
            finally:
                if holder_proc.poll() is None:
                    holder_proc.kill()
                holder_proc.wait(timeout=10)

        # 3) 前プロセス強制終了時の AbandonedMutexException 回復と解放の検証
        with self.subTest(case="abandoned_mutex_recovery"):
            mutex_name = f"Local\\AudioEffector_TestLock_{uuid.uuid4().hex}"
            ready_file = self.base_dir / "abandoned_holder_ready.flag"
            waiter_started_file = self.base_dir / "abandoned_waiter_started.flag"
            holder_script = f"""
$m = New-Object System.Threading.Mutex($false, '{mutex_name}')
$acquired = $m.WaitOne(5000, $false)
if (-not $acquired) {{ exit 2 }}
[System.IO.File]::WriteAllText('{str(ready_file).replace("'", "''")}', 'ready')
Start-Sleep -Seconds 30
"""
            holder_proc = subprocess.Popen(
                ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", holder_script],
                cwd=str(REPO_ROOT),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            try:
                self.assertTrue(_wait_for_file(ready_file), "異常終了用ロック保持プロセスの起動待機に失敗しました。")

                # 待機プロセス側で先に同一 Mutex の参照ハンドルを保持してから通知することで、
                # 高負荷で WaitOne 到達が遅延しても保持プロセス強制終了時に Mutex カーネルオブジェクトが破棄されず
                # 確実に AbandonedMutexException を再現できる
                recover_script = loader + f"""
$keepAliveMutex = New-Object System.Threading.Mutex($false, '{mutex_name}')
try {{
    [System.IO.File]::WriteAllText('{str(waiter_started_file).replace("'", "''")}', 'waiting')
    $res = Invoke-WithBuildLock -MutexName '{mutex_name}' -TimeoutSeconds 10 -Action {{ 'recovered' }}
    $res | ConvertTo-Json -Depth 4
}} finally {{
    $keepAliveMutex.Dispose()
}}
"""
                waiter_proc = subprocess.Popen(
                    ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", recover_script],
                    cwd=str(REPO_ROOT),
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                )
                try:
                    self.assertTrue(_wait_for_file(waiter_started_file), "待機プロセスの起動確認に失敗しました。")
                    time.sleep(0.2)
                    # Mutex を解放せず強制終了して AbandonedMutexException を発生させる
                    holder_proc.kill()
                    holder_proc.wait(timeout=10)

                    stdout_text, stderr_text = waiter_proc.communicate(timeout=15)
                    self.assertEqual(waiter_proc.returncode, 0, msg=stdout_text + "\n" + stderr_text)
                    payload = json.loads(stdout_text)
                    self.assertTrue(payload["Acquired"])
                    self.assertTrue(payload["WasAbandoned"])
                    self.assertEqual(payload["Result"], "recovered")
                finally:
                    if waiter_proc.poll() is None:
                        waiter_proc.kill()
                        waiter_proc.wait(timeout=5)
            finally:
                if holder_proc.poll() is None:
                    holder_proc.kill()
                    holder_proc.wait(timeout=5)

            # 直後の再取得が通常通り（Abandoned ではなく）即時成功することを確認
            next_script = loader + f"""
$res = Invoke-WithBuildLock -MutexName '{mutex_name}' -TimeoutSeconds 5 -Action {{ 'next_ok' }}
$res | ConvertTo-Json -Depth 4
"""
            next_res = _run_ps_command(next_script, cwd=REPO_ROOT)
            self.assertEqual(next_res.returncode, 0, msg=next_res.stdout + "\n" + next_res.stderr)
            next_payload = json.loads(next_res.stdout)
            self.assertTrue(next_payload["Acquired"])
            self.assertFalse(next_payload["WasAbandoned"])
            self.assertEqual(next_payload["Result"], "next_ok")

    def test_スラッシュを含む統合ブランチ名でFetchBaseとPreflight比較ベース解決が正常動作すること(self) -> None:
        # Arrange: origin に epic/401-lyrics ブランチを作成し、子 Issue ワークツリーを分岐させる
        sb = self._create_diverged_origin_sandbox()
        repo_dir = Path(sb["repo_dir"])
        pusher_dir = Path(sb["pusher_dir"])

        _run_git(["checkout", "-b", "epic/401-lyrics", "develop"], cwd=pusher_dir)
        (pusher_dir / "epic_file.txt").write_text("epic v1\n", encoding="utf-8")
        _run_git(["add", "epic_file.txt"], cwd=pusher_dir)
        _run_git(["commit", "-m", "chore: #401 epic base"], cwd=pusher_dir)
        _run_git(["push", "-u", "origin", "epic/401-lyrics"], cwd=pusher_dir)
        epic_c1 = _run_git(["rev-parse", "HEAD"], cwd=pusher_dir).stdout.strip()

        # repo_dir 側で一度 fetch してワークツリーを origin/epic/401-lyrics から分岐
        _run_git(["fetch", "origin", "+refs/heads/epic/401-lyrics:refs/remotes/origin/epic/401-lyrics"], cwd=repo_dir)
        wt_epic = repo_dir / ".agent_worktrees" / "issue-402"
        _run_git(["worktree", "add", "-b", "feat/402-child", str(wt_epic), "origin/epic/401-lyrics"], cwd=repo_dir)
        (wt_epic / "child.txt").write_text("child\n", encoding="utf-8")
        _run_git(["add", "child.txt"], cwd=wt_epic)
        _run_git(["commit", "-m", "feat: #402 child"], cwd=wt_epic)

        # リモート側の epic/401-lyrics をさらに 1 コミット前進させる
        (pusher_dir / "epic_file.txt").write_text("epic v2\n", encoding="utf-8")
        _run_git(["add", "epic_file.txt"], cwd=pusher_dir)
        _run_git(["commit", "-m", "chore: #401 epic v2"], cwd=pusher_dir)
        _run_git(["push", "origin", "epic/401-lyrics"], cwd=pusher_dir)
        epic_c2 = _run_git(["rev-parse", "HEAD"], cwd=pusher_dir).stdout.strip()

        loader = _ps_loader_snippet(
            VERIFY_LOOP,
            ["Stop-ProcessTree", "Invoke-ExternalCommand", "Test-IsGitWorktree", "Resolve-BaseMergeCommit"],
        )
        ps_script = loader + f"""
$res = Resolve-BaseMergeCommit -BaseRef 'epic/401-lyrics' -PreferOriginBase -FetchBase -WorkingDirectory '{str(wt_epic).replace("'", "''")}'
$res | ConvertTo-Json -Depth 4
"""
        # Act 1: -FetchBase 付きで origin/epic/401-lyrics が最新化され、マージベースが epic_c1 に解決されること
        res = _run_ps_command(ps_script, cwd=REPO_ROOT)
        self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
        data = json.loads(res.stdout)
        self.assertTrue(data["Success"], msg=data.get("Error"))
        self.assertEqual(data["ResolvedRef"], "origin/epic/401-lyrics")
        self.assertEqual(data["MergeBase"], epic_c1)
        self.assertEqual(_run_git(["rev-parse", "origin/epic/401-lyrics"], cwd=wt_epic).stdout.strip(), epic_c2)

        # Act 2: ワークツリー内の verify_loop.ps1 エンドツーエンド実行で -BaseBranch "epic/401-lyrics" がレポートに記録されること
        wt_tools = wt_epic / "tools"
        wt_tools.mkdir(parents=True, exist_ok=True)
        wt_script = wt_tools / "verify_loop.ps1"
        shutil.copy2(VERIFY_LOOP, wt_script)
        json_out = self.base_dir / "epic_verify_report.json"

        run_res = _run_ps1_file(
            wt_script,
            ["-Full", "-TestFilter", "Dummy", "-BaseBranch", "epic/401-lyrics", "-PreferOriginBase", "-JsonOut", str(json_out)],
            cwd=wt_epic,
        )
        self.assertEqual(run_res.returncode, 1, msg=run_res.stdout + "\n" + run_res.stderr)
        report = json.loads(json_out.read_text(encoding="utf-8"))
        self.assertEqual(report["base_branch"], "epic/401-lyrics")
        self.assertEqual(report["resolved_base_ref"], "origin/epic/401-lyrics")
        self.assertEqual(report["resolved_base_commit"], epic_c1)

        # Act 3: refs/heads/ プレフィックス・前後空白付きの指定でも origin/epic/401-lyrics へ一貫正規化され、空白のみは失敗すること
        ps_norm_script = loader + f"""
$rPrefixed = Resolve-BaseMergeCommit -BaseRef '  refs/heads/epic/401-lyrics  ' -PreferOriginBase -FetchBase -WorkingDirectory '{str(wt_epic).replace("'", "''")}'
$rBlank    = Resolve-BaseMergeCommit -BaseRef '   ' -PreferOriginBase -FetchBase -WorkingDirectory '{str(wt_epic).replace("'", "''")}'
[PSCustomObject]@{{
    prefixed = $rPrefixed
    blank    = $rBlank
}} | ConvertTo-Json -Depth 4
"""
        norm_res = _run_ps_command(ps_norm_script, cwd=REPO_ROOT)
        self.assertEqual(norm_res.returncode, 0, msg=norm_res.stdout + "\n" + norm_res.stderr)
        norm_data = json.loads(norm_res.stdout)
        self.assertTrue(norm_data["prefixed"]["Success"], msg=norm_data["prefixed"].get("Error"))
        self.assertEqual(norm_data["prefixed"]["ResolvedRef"], "origin/epic/401-lyrics")
        self.assertEqual(norm_data["prefixed"]["MergeBase"], epic_c1)
        self.assertFalse(norm_data["blank"]["Success"])

    def test_ライセンス同期ゲートが差分違反検出と正常合格およびJSON出力を行えること(self) -> None:
        # Arrange: サンドボックスリポジトリへ check_license_sync.py と製品 .csproj・THIRD-PARTY-NOTICES.md を配置
        lic_repo = self.base_dir / "lic_repo"
        lic_repo.mkdir(parents=True, exist_ok=True)
        _run_git(["init"], cwd=lic_repo)
        _run_git(["config", "user.name", "Test User"], cwd=lic_repo)
        _run_git(["config", "user.email", "test@example.com"], cwd=lic_repo)
        _run_git(["checkout", "-b", "develop"], cwd=lic_repo)

        tools_dst = lic_repo / "tools"
        tools_dst.mkdir(parents=True, exist_ok=True)
        shutil.copy2(REPO_ROOT / "tools" / "check_license_sync.py", tools_dst / "check_license_sync.py")

        infra_csproj = lic_repo / "AudioEffector.Infrastructure" / "AudioEffector.Infrastructure.csproj"
        infra_csproj.parent.mkdir(parents=True, exist_ok=True)
        infra_csproj.write_text(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            "  <ItemGroup>\n"
            "    <PackageReference Include=\"NAudio\" Version=\"2.2.1\" />\n"
            "  </ItemGroup>\n"
            "</Project>\n",
            encoding="utf-8",
        )
        notices_file = lic_repo / "THIRD-PARTY-NOTICES.md"
        notices_file.write_text("# Third-Party Notices\n\n## NAudio\n- **Version:** 2.2.1\n", encoding="utf-8")
        _run_git(["add", "."], cwd=lic_repo)
        _run_git(["commit", "-m", "chore: base"], cwd=lic_repo)
        base_commit = _run_git(["rev-parse", "HEAD"], cwd=lic_repo).stdout.strip()
        _run_git(["checkout", "-b", "feat/437-lic-gate"], cwd=lic_repo)

        loader = _ps_loader_snippet(
            VERIFY_LOOP,
            ["Stop-ProcessTree", "Invoke-ExternalCommand", "New-GateResult", "Invoke-LicenseSyncGate"],
        )
        escaped_repo = str(lic_repo).replace("'", "''")

        # Act 1: 変更なし -Full 実行で PASSED ＆ Report が返ること
        ps_pass = loader + f"""
$res = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_commit}' -ChangedFiles @() -Full
$res | ConvertTo-Json -Depth 6
"""
        res_pass = _run_ps_command(ps_pass, cwd=REPO_ROOT)
        self.assertEqual(res_pass.returncode, 0, msg=res_pass.stdout + "\n" + res_pass.stderr)
        pass_data = json.loads(res_pass.stdout)
        self.assertTrue(pass_data["Passed"])
        self.assertEqual(pass_data["GateResult"]["name"], "license_sync")
        self.assertEqual(pass_data["GateResult"]["status"], "PASSED")
        self.assertIsNotNone(pass_data["Report"])
        self.assertFalse(pass_data["Report"]["needs_update"])

        # Act 2: 新規パッケージ追加（THIRD-PARTY-NOTICES.md 未更新）で FAILED になること
        infra_csproj.write_text(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            "  <ItemGroup>\n"
            "    <PackageReference Include=\"NAudio\" Version=\"2.2.1\" />\n"
            "    <PackageReference Include=\"Whisper.net\" Version=\"1.9.0\" />\n"
            "  </ItemGroup>\n"
            "</Project>\n",
            encoding="utf-8",
        )
        ps_fail = loader + f"""
$res = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_commit}' -ChangedFiles @('AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj') -Full
$res | ConvertTo-Json -Depth 6
"""
        res_fail = _run_ps_command(ps_fail, cwd=REPO_ROOT)
        self.assertEqual(res_fail.returncode, 0, msg=res_fail.stdout + "\n" + res_fail.stderr)
        fail_data = json.loads(res_fail.stdout)
        self.assertFalse(fail_data["Passed"])
        self.assertEqual(fail_data["GateResult"]["status"], "FAILED")
        self.assertGreaterEqual(fail_data["GateResult"]["error_count"], 1)
        self.assertTrue(fail_data["Report"]["needs_update"])
        self.assertIn("Whisper.net", fail_data["Report"]["missing_packages"])

        # Act 3: スキップ分岐・非-Full 対象外ファイル・-Full -SkipLicenseSync 併用・-StrictAllLicenses の検証
        # ベースコミットへ既存未記載パッケージ (NLog) を含めたブランチを作成して -StrictAllLicenses も同時検証
        _run_git(["checkout", "develop"], cwd=lic_repo)
        infra_csproj.write_text(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            "  <ItemGroup>\n"
            "    <PackageReference Include=\"NAudio\" Version=\"2.2.1\" />\n"
            "    <PackageReference Include=\"NLog\" Version=\"6.2.0\" />\n"
            "  </ItemGroup>\n"
            "</Project>\n",
            encoding="utf-8",
        )
        _run_git(["add", "."], cwd=lic_repo)
        _run_git(["commit", "-m", "chore: base with existing missing"], cwd=lic_repo)
        base_with_debt = _run_git(["rev-parse", "HEAD"], cwd=lic_repo).stdout.strip()
        _run_git(["checkout", "-b", "feat/437-branches"], cwd=lic_repo)

        ps_branches = loader + f"""
$rSkipOnly      = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_with_debt}' -ChangedFiles @('AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj') -SkipLicenseSync
$rFullOverrides = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_with_debt}' -ChangedFiles @() -Full -SkipLicenseSync
$rUnrelatedSkip = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_with_debt}' -ChangedFiles @('README.md')
$rTargetExec    = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_with_debt}' -ChangedFiles @('AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj')
$rStrictAll     = Invoke-LicenseSyncGate -RepoRootPath '{escaped_repo}' -BaseCommit '{base_with_debt}' -ChangedFiles @() -Full -StrictAllLicenses
[PSCustomObject]@{{
    skip_only      = $rSkipOnly
    full_overrides = $rFullOverrides
    unrelated_skip = $rUnrelatedSkip
    target_exec    = $rTargetExec
    strict_all     = $rStrictAll
}} | ConvertTo-Json -Depth 6
"""
        res_branches = _run_ps_command(ps_branches, cwd=REPO_ROOT)
        self.assertEqual(res_branches.returncode, 0, msg=res_branches.stdout + "\n" + res_branches.stderr)
        b_data = json.loads(res_branches.stdout)

        self.assertTrue(b_data["skip_only"]["Passed"])
        self.assertEqual(b_data["skip_only"]["GateResult"]["status"], "SKIPPED")
        self.assertTrue(b_data["full_overrides"]["Passed"])
        self.assertEqual(b_data["full_overrides"]["GateResult"]["status"], "PASSED")
        self.assertTrue(b_data["unrelated_skip"]["Passed"])
        self.assertEqual(b_data["unrelated_skip"]["GateResult"]["status"], "SKIPPED")
        self.assertTrue(b_data["target_exec"]["Passed"])
        self.assertEqual(b_data["target_exec"]["GateResult"]["status"], "PASSED")
        self.assertFalse(b_data["strict_all"]["Passed"])
        self.assertEqual(b_data["strict_all"]["GateResult"]["status"], "FAILED")
        self.assertTrue(b_data["strict_all"]["Report"]["needs_update"])

    def test_BuildTimeoutSecondsの既定値が180であり0以下指定でPreflightエラーとなること(self) -> None:
        # Arrange & Act 1: PowerShell AST から既定値を検証
        escaped_path = str(VERIFY_LOOP).replace("'", "''")
        ps_script = f"""
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile('{escaped_path}', [ref]$tokens, [ref]$errors)
$params = @{{}}
foreach ($p in $ast.ParamBlock.Parameters) {{
    $params[$p.Name.VariablePath.UserPath] = if ($null -ne $p.DefaultValue) {{ $p.DefaultValue.Extent.Text }} else {{ $null }}
}}
$params | ConvertTo-Json -Depth 4
"""
        ast_res = _run_ps_command(ps_script, cwd=REPO_ROOT)
        self.assertEqual(ast_res.returncode, 0, msg=ast_res.stdout + "\n" + ast_res.stderr)
        params = json.loads(ast_res.stdout)
        self.assertEqual(params["BuildTimeoutSeconds"], "180")
        self.assertEqual(params["TestTimeoutSeconds"], "180")

        # Arrange 2: サンドボックスのワークツリーに verify_loop.ps1 を配置して Preflight 検証
        sb = self._create_diverged_origin_sandbox()
        wt_dir = Path(sb["wt_dir"])
        wt_tools = wt_dir / "tools"
        wt_tools.mkdir(parents=True, exist_ok=True)
        wt_script = wt_tools / "verify_loop.ps1"
        shutil.copy2(VERIFY_LOOP, wt_script)

        invalid_cases = [
            ("build_zero", ["-BuildTimeoutSeconds", "0"], "-BuildTimeoutSeconds"),
            ("build_negative", ["-BuildTimeoutSeconds", "-5"], "-BuildTimeoutSeconds"),
            ("test_zero", ["-TestTimeoutSeconds", "0"], "-TestTimeoutSeconds"),
            ("test_negative", ["-TestTimeoutSeconds", "-1"], "-TestTimeoutSeconds"),
        ]

        for case_name, extra_args, expected_param in invalid_cases:
            with self.subTest(case=case_name):
                json_out = self.base_dir / f"preflight_{case_name}.json"
                run_res = _run_ps1_file(
                    wt_script,
                    [*extra_args, "-JsonOut", str(json_out)],
                    cwd=wt_dir,
                )
                self.assertEqual(run_res.returncode, 1, msg=run_res.stdout + "\n" + run_res.stderr)
                self.assertIn(expected_param, run_res.stdout)
                self.assertTrue(json_out.is_file(), msg=f"JSON レポートが生成されていません: {json_out}")
                report = json.loads(json_out.read_text(encoding="utf-8"))
                self.assertFalse(report["overall_passed"])
                self.assertEqual(report["exit_code"], 1)
                preflight_gates = [g for g in report["gates"] if g["name"] == "preflight"]
                self.assertEqual(len(preflight_gates), 1)
                self.assertEqual(preflight_gates[0]["status"], "FAILED")
                self.assertTrue(
                    any(expected_param in detail for detail in preflight_gates[0]["details"]),
                    msg=f"preflight details に {expected_param} のエラーが含まれていません: {preflight_gates[0]['details']}",
                )

    def test_InvokeExternalCommandがタイムアウト強制終了直前までの標準出力と標準エラーを取りこぼさず回収すること(self) -> None:
        # Arrange: 複数行を stdout/stderr へ即時フラッシュしてから長時間スリープするコマンドを 2 秒でタイムアウトさせる
        loader = _ps_loader_snippet(VERIFY_LOOP, ["Stop-ProcessTree", "Invoke-ExternalCommand"])
        py_payload = (
            "import sys, time; "
            "[print(f'OUT_LINE_{i:02d}', flush=True) for i in range(1, 6)]; "
            "[print(f'ERR_LINE_{i:02d}', file=sys.stderr, flush=True) for i in range(1, 4)]; "
            "time.sleep(30)"
        )
        escaped_repo = str(REPO_ROOT).replace("'", "''")
        ps_script = loader + f"""
$res = Invoke-ExternalCommand -FilePath 'python' -ArgumentList @('-u', '-c', "{py_payload}") -WorkingDirectory '{escaped_repo}' -TimeoutSeconds 2
$res | ConvertTo-Json -Depth 4
"""
        # Act
        res = _run_ps_command(ps_script, cwd=REPO_ROOT, timeout=30)
        self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
        data = json.loads(res.stdout)

        # Assert
        self.assertTrue(data["TimedOut"])
        self.assertFalse(data["StartFailed"])
        for i in range(1, 6):
            self.assertIn(f"OUT_LINE_{i:02d}", data["StdOut"])
        for i in range(1, 4):
            self.assertIn(f"ERR_LINE_{i:02d}", data["StdErr"])

    def test_Gate1ビルドタイムアウト時に標準出力とJSONレポートdetailsへ直前ビルド出力末尾が記録されること(self) -> None:
        # Arrange: サンドボックスワークツリーに verify_loop.ps1 とタイムアウト再現用スタブソリューションを配置
        sb = self._create_diverged_origin_sandbox()
        wt_dir = Path(sb["wt_dir"])
        wt_tools = wt_dir / "tools"
        wt_tools.mkdir(parents=True, exist_ok=True)
        wt_script = wt_tools / "verify_loop.ps1"
        shutil.copy2(VERIFY_LOOP, wt_script)

        # SDK 解決や NuGet 復元を伴わない最小 MSBuild プロジェクトで 20 行出力後にハングさせる
        project_guid = "{11111111-1111-1111-1111-111111111111}"
        sln_content = (
            "Microsoft Visual Studio Solution File, Format Version 12.00\r\n"
            "# Visual Studio Version 17\r\n"
            f'Project("{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}") = "StubBuild", "StubBuild.csproj", "{project_guid}"\r\n'
            "EndProject\r\n"
            "Global\r\n"
            "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n"
            "\t\tDebug|Any CPU = Debug|Any CPU\r\n"
            "\tEndGlobalSection\r\n"
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n"
            f"\t\t{project_guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n"
            f"\t\t{project_guid}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n"
            "\tEndGlobalSection\r\n"
            "EndGlobal\r\n"
        )
        (wt_dir / "AudioEffector.sln").write_text(sln_content, encoding="utf-8")

        message_lines = "\n".join(
            f'    <Message Importance="high" Text="BUILD_PROGRESS_LINE_{i:02d}" />'
            for i in range(1, 21)
        )
        csproj_content = (
            '<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">\n'
            '  <Target Name="Restore" />\n'
            '  <Target Name="Build">\n'
            f"{message_lines}\n"
            '    <Exec Command="powershell -NoProfile -Command Start-Sleep -Seconds 30" />\n'
            "  </Target>\n"
            "</Project>\n"
        )
        (wt_dir / "StubBuild.csproj").write_text(csproj_content, encoding="utf-8")

        json_out = self.base_dir / "build_timeout_report.json"

        # Act: Gate 1 のタイムアウトを発生させ、標準出力と JSON レポートを検証
        run_res = _run_ps1_file(
            wt_script,
            [
                "-NoBuildLock",
                "-SkipSpecSync",
                "-SkipLicenseSync",
                "-BuildTimeoutSeconds",
                "6",
                "-JsonOut",
                str(json_out),
            ],
            cwd=wt_dir,
            timeout=60,
        )

        # Assert
        self.assertEqual(run_res.returncode, 1, msg=run_res.stdout + "\n" + run_res.stderr)
        self.assertIn("ビルドがタイムアウトしました (6秒超過)", run_res.stdout)
        self.assertIn("BUILD_PROGRESS_LINE_20", run_res.stdout)

        self.assertTrue(json_out.is_file(), msg=f"JSON レポートが生成されていません: {json_out}")
        report = json.loads(json_out.read_text(encoding="utf-8"))
        self.assertFalse(report["overall_passed"])
        self.assertEqual(report["exit_code"], 1)
        self.assertTrue(report["cleanup_succeeded"])

        build_gates = [g for g in report["gates"] if g["name"] == "build"]
        self.assertEqual(len(build_gates), 1)
        build_gate = build_gates[0]
        self.assertEqual(build_gate["status"], "FAILED")
        self.assertEqual(build_gate["error_count"], 1)

        details = build_gate["details"]
        self.assertGreater(len(details), 0, msg="タイムアウト時の build.details が空です。")
        self.assertLessEqual(len(details), 15, msg=f"build.details が末尾 15 行制限を超えています: {len(details)}")
        joined_details = "\n".join(details)
        self.assertIn("BUILD_PROGRESS_LINE_20", joined_details)
        self.assertIn("BUILD_PROGRESS_LINE_07", joined_details)
        self.assertNotIn("BUILD_PROGRESS_LINE_01", joined_details)
        self.assertFalse((wt_dir / ".agent_build").exists(), ".agent_build ディレクトリが残留しています。")


if __name__ == "__main__":
    unittest.main()
