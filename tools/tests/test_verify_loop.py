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


def _wait_for_file(path: Path, timeout_sec: float = 10.0) -> bool:
    deadline = time.monotonic() + timeout_sec
    while time.monotonic() < deadline:
        if path.exists():
            return True
        time.sleep(0.05)
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
        for expected in ("PreferOriginBase", "FetchBase", "BuildLockTimeoutSeconds", "NoBuildLock"):
            self.assertIn(expected, params, msg=f"パラメータ -{expected} が未定義です。")
        self.assertEqual(params["BuildLockTimeoutSeconds"], "300")

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

        # 1) 並列直列化と待機秒数計測の検証
        with self.subTest(case="serialization_and_wait_seconds"):
            mutex_name = f"Local\\AudioEffector_TestLock_{uuid.uuid4().hex}"
            ready_file = self.base_dir / "holder_ready.flag"
            holder_script = f"""
$m = New-Object System.Threading.Mutex($false, '{mutex_name}')
$acquired = $m.WaitOne(5000, $false)
if (-not $acquired) {{ exit 2 }}
try {{
    [System.IO.File]::WriteAllText('{str(ready_file).replace("'", "''")}', 'ready')
    Start-Sleep -Milliseconds 1200
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
                self.assertTrue(_wait_for_file(ready_file), "ロック保持プロセスの起動待機に失敗しました。")
                waiter_script = loader + f"""
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
            finally:
                holder_proc.wait(timeout=10)

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
    Start-Sleep -Seconds 15
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

                # 待機プロセスを先に起動し、Mutex ハンドルを開いて WaitOne 待機させた状態で保持プロセスを強制終了する
                recover_script = loader + f"""
[System.IO.File]::WriteAllText('{str(waiter_started_file).replace("'", "''")}', 'waiting')
$res = Invoke-WithBuildLock -MutexName '{mutex_name}' -TimeoutSeconds 10 -Action {{ 'recovered' }}
$res | ConvertTo-Json -Depth 4
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
                    time.sleep(0.4)
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


if __name__ == "__main__":
    unittest.main()
