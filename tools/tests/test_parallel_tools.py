#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/worktree_manager.ps1 および tools/sync_branch.ps1 の結合テスト。"""

from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
WORKTREE_MANAGER = REPO_ROOT / "tools" / "worktree_manager.ps1"
SYNC_BRANCH = REPO_ROOT / "tools" / "sync_branch.ps1"


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


def _run_ps1(script_path: Path, args: list[str], cwd: Path) -> subprocess.CompletedProcess[str]:
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
        check=False,
    )


def _create_sandbox_repo(base_dir: Path) -> tuple[Path, Path]:
    """bare リモート (origin) と develop ブランチを持つローカル作業リポジトリを構築する。"""
    origin_dir = base_dir / "origin.git"
    work_dir = base_dir / "repo"

    _run_git(["init", "--bare", str(origin_dir)], cwd=base_dir)
    _run_git(["clone", str(origin_dir), str(work_dir)], cwd=base_dir)
    _run_git(["config", "user.name", "Test User"], cwd=work_dir)
    _run_git(["config", "user.email", "test@example.com"], cwd=work_dir)
    _run_git(["checkout", "-b", "develop"], cwd=work_dir)

    (work_dir / ".gitignore").write_text(".agent_worktrees/\n.agent_build/\n.agent_logs/\n", encoding="utf-8")
    (work_dir / "app.txt").write_text("line1\nline2\n", encoding="utf-8")
    _run_git(["add", "."], cwd=work_dir)
    _run_git(["commit", "-m", "chore: initial commit"], cwd=work_dir)
    _run_git(["push", "-u", "origin", "develop"], cwd=work_dir)

    return origin_dir, work_dir


class WorktreeManagerTests(unittest.TestCase):
    """tools/worktree_manager.ps1 の作成・上限ガード・一覧・削除・整理テスト。"""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory(prefix="ae_wt_test_")
        self.base_dir = Path(self._tmp.name)
        self.origin_dir, self.repo_dir = _create_sandbox_repo(self.base_dir)

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def test_ワークツリー作成と最大3並列上限ガードおよび削除が正常動作すること(self) -> None:
        # Arrange & Act: 3 件のワークツリーを順次作成
        for idx, issue_no in enumerate((101, 102, 103), start=1):
            res = _run_ps1(
                WORKTREE_MANAGER,
                [
                    "-Action", "create",
                    "-IssueNumber", str(issue_no),
                    "-BranchName", f"feat/{issue_no}-task-{idx}",
                    "-Json",
                ],
                cwd=self.repo_dir,
            )
            self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
            payload = json.loads(res.stdout)
            self.assertEqual(payload["status"], "CREATED")
            self.assertEqual(payload["active_count"], idx)
            self.assertTrue(Path(payload["worktree_path"]).is_dir())

        # Assert: 親リポジトリの git status がクリーンであること
        status_res = _run_git(["status", "--porcelain"], cwd=self.repo_dir)
        self.assertEqual(status_res.stdout.strip(), "")

        # Act: 4 件目の作成は最大 3 並列上限で拒否されること
        res_4th = _run_ps1(
            WORKTREE_MANAGER,
            [
                "-Action", "create",
                "-IssueNumber", "104",
                "-BranchName", "feat/104-overflow",
                "-Json",
            ],
            cwd=self.repo_dir,
        )
        self.assertEqual(res_4th.returncode, 1)
        payload_4th = json.loads(res_4th.stdout)
        self.assertEqual(payload_4th["status"], "ERROR")

        # Arrange: issue-102 配下に .agent_build と未追跡ファイルを作成し list で検知できることを確認
        wt102 = self.repo_dir / ".agent_worktrees" / "issue-102"
        (wt102 / ".agent_build").mkdir(parents=True, exist_ok=True)
        (wt102 / ".agent_build" / "dummy.dll").write_text("bin", encoding="utf-8")
        (wt102 / "dirty.txt").write_text("modified", encoding="utf-8")

        list_res = _run_ps1(WORKTREE_MANAGER, ["-Action", "list", "-Json"], cwd=self.repo_dir)
        self.assertEqual(list_res.returncode, 0, msg=list_res.stdout + "\n" + list_res.stderr)
        list_payload = json.loads(list_res.stdout)
        self.assertEqual(list_payload["active_count"], 3)
        self.assertEqual(list_payload["available_slots"], 0)
        wt_by_issue = {w["issue_number"]: w for w in list_payload["worktrees"]}
        self.assertTrue(wt_by_issue[102]["has_agent_build"])
        self.assertTrue(wt_by_issue[102]["is_dirty"])
        self.assertFalse(wt_by_issue[101]["is_dirty"])

        # Act: remove (-Force) で issue-102 と .agent_build が確実に削除されること
        rm_res = _run_ps1(
            WORKTREE_MANAGER,
            ["-Action", "remove", "-IssueNumber", "102", "-Force", "-Json"],
            cwd=self.repo_dir,
        )
        self.assertEqual(rm_res.returncode, 0, msg=rm_res.stdout + "\n" + rm_res.stderr)
        rm_payload = json.loads(rm_res.stdout)
        self.assertEqual(rm_payload["status"], "REMOVED")
        self.assertTrue(rm_payload["removed_agent_build"])
        self.assertEqual(rm_payload["active_count"], 2)
        self.assertFalse(wt102.exists())

    def test_ブランチ名規約違反とprune孤立ディレクトリ整理が正しく動作すること(self) -> None:
        # Arrange & Act: 不正なブランチ名は拒否されること
        bad_res = _run_ps1(
            WORKTREE_MANAGER,
            [
                "-Action", "create",
                "-IssueNumber", "201",
                "-BranchName", "invalid-branch-name",
                "-Json",
            ],
            cwd=self.repo_dir,
        )
        self.assertEqual(bad_res.returncode, 1)
        self.assertEqual(json.loads(bad_res.stdout)["status"], "ERROR")

        # Arrange: 孤立ディレクトリを作成
        orphan_dir = self.repo_dir / ".agent_worktrees" / "issue-999"
        orphan_dir.mkdir(parents=True, exist_ok=True)
        (orphan_dir / "orphan.txt").write_text("orphan", encoding="utf-8")

        # Act: prune で孤立ディレクトリが削除されること
        prune_res = _run_ps1(WORKTREE_MANAGER, ["-Action", "prune", "-Json"], cwd=self.repo_dir)
        self.assertEqual(prune_res.returncode, 0, msg=prune_res.stdout + "\n" + prune_res.stderr)
        prune_payload = json.loads(prune_res.stdout)
        self.assertEqual(prune_payload["status"], "PRUNED")
        self.assertFalse(orphan_dir.exists())

    def test_UseExistingBranchとdirty時のForceなしremove拒否およびpruneの稼働中build保護が動作すること(self) -> None:
        # Arrange: 既存ブランチをローカル作成
        _run_git(["branch", "feat/205-existing", "develop"], cwd=self.repo_dir)

        # Act: -UseExistingBranch でワークツリー作成（base_ref は null）
        create_res = _run_ps1(
            WORKTREE_MANAGER,
            [
                "-Action", "create",
                "-IssueNumber", "205",
                "-BranchName", "feat/205-existing",
                "-UseExistingBranch",
                "-Json",
            ],
            cwd=self.repo_dir,
        )
        self.assertEqual(create_res.returncode, 0, msg=create_res.stdout + "\n" + create_res.stderr)
        create_payload = json.loads(create_res.stdout)
        self.assertEqual(create_payload["status"], "CREATED")
        self.assertIsNone(create_payload["base_ref"])

        wt_path = Path(create_payload["worktree_path"])
        (wt_path / "uncommitted.txt").write_text("dirty", encoding="utf-8")
        (wt_path / ".agent_build").mkdir(parents=True, exist_ok=True)
        (wt_path / ".agent_build" / "in_use.dll").write_text("bin", encoding="utf-8")

        # Assert: 既定の prune は稼働中ワークツリーの .agent_build を削除しない
        prune_default = _run_ps1(WORKTREE_MANAGER, ["-Action", "prune", "-Json"], cwd=self.repo_dir)
        self.assertEqual(prune_default.returncode, 0, msg=prune_default.stdout + "\n" + prune_default.stderr)
        self.assertTrue((wt_path / ".agent_build").exists())

        # Assert: -CleanActiveBuilds 指定時は .agent_build を削除する
        prune_clean = _run_ps1(WORKTREE_MANAGER, ["-Action", "prune", "-CleanActiveBuilds", "-Json"], cwd=self.repo_dir)
        self.assertEqual(prune_clean.returncode, 0, msg=prune_clean.stdout + "\n" + prune_clean.stderr)
        self.assertFalse((wt_path / ".agent_build").exists())

        # Assert: dirty なワークツリーに対する -Force なし remove は拒否される
        rm_no_force = _run_ps1(
            WORKTREE_MANAGER,
            ["-Action", "remove", "-IssueNumber", "205", "-Json"],
            cwd=self.repo_dir,
        )
        self.assertEqual(rm_no_force.returncode, 1)
        self.assertEqual(json.loads(rm_no_force.stdout)["status"], "ERROR")
        self.assertTrue(wt_path.exists())

    def test_SetLocation後の相対JsonOutが正常に書き出されること(self) -> None:
        # Arrange & Act: PowerShell セッション内で Set-Location してから相対 -JsonOut で呼び出す
        cmd = [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            f"Set-Location -LiteralPath '{self.repo_dir}'; & '{WORKTREE_MANAGER}' -Action list -JsonOut 'sub/wt_out.json' -Json",
        ]
        res = subprocess.run(cmd, cwd=str(REPO_ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace")

        # Assert
        self.assertEqual(res.returncode, 0, msg=res.stdout + "\n" + res.stderr)
        out_file = self.repo_dir / "sub" / "wt_out.json"
        self.assertTrue(out_file.is_file())
        self.assertEqual(json.loads(out_file.read_text(encoding="utf-8"))["status"], "OK")


class SyncBranchTests(unittest.TestCase):
    """tools/sync_branch.ps1 の UP_TO_DATE / DRY_RUN / MERGED / CONFLICT / ABORTED テスト。"""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory(prefix="ae_sync_test_")
        self.base_dir = Path(self._tmp.name)
        self.origin_dir, self.repo_dir = _create_sandbox_repo(self.base_dir)

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def _push_develop_change(self, filename: str, content: str, message: str) -> None:
        """別クローン経由で origin/develop を前進させる。"""
        pusher = self.base_dir / "pusher"
        if not pusher.exists():
            _run_git(["clone", "-b", "develop", str(self.origin_dir), str(pusher)], cwd=self.base_dir)
            _run_git(["config", "user.name", "Other Dev"], cwd=pusher)
            _run_git(["config", "user.email", "other@example.com"], cwd=pusher)
        else:
            _run_git(["pull", "origin", "develop"], cwd=pusher)

        (pusher / filename).write_text(content, encoding="utf-8")
        _run_git(["add", filename], cwd=pusher)
        _run_git(["commit", "-m", message], cwd=pusher)
        _run_git(["push", "origin", "develop"], cwd=pusher)

    def test_UP_TO_DATEおよび非破壊マージMERGEDとDryRunが正常動作すること(self) -> None:
        # Arrange: ワークツリーを作成し独立ファイルを追加
        create_res = _run_ps1(
            WORKTREE_MANAGER,
            ["-Action", "create", "-IssueNumber", "301", "-BranchName", "feat/301-feature", "-Json"],
            cwd=self.repo_dir,
        )
        self.assertEqual(create_res.returncode, 0, msg=create_res.stdout + "\n" + create_res.stderr)
        wt_path = Path(json.loads(create_res.stdout)["worktree_path"])

        (wt_path / "feature.txt").write_text("feature content\n", encoding="utf-8")
        _run_git(["add", "feature.txt"], cwd=wt_path)
        _run_git(["commit", "-m", "feat: #301 add feature"], cwd=wt_path)

        # Act 1: origin/develop 未更新なら UP_TO_DATE (exit 0)
        res_up = _run_ps1(SYNC_BRANCH, ["-Json"], cwd=wt_path)
        self.assertEqual(res_up.returncode, 0, msg=res_up.stdout + "\n" + res_up.stderr)
        up_payload = json.loads(res_up.stdout)
        self.assertEqual(up_payload["status"], "UP_TO_DATE")
        self.assertEqual(up_payload["behind_count"], 0)
        self.assertEqual(up_payload["ahead_count"], 1)

        # Arrange 2: origin/develop に別ファイル変更を push
        self._push_develop_change("other.txt", "from develop\n", "feat: update develop")

        # Act 2: -DryRun はマージせず behind_count=1 を返す
        res_dry = _run_ps1(SYNC_BRANCH, ["-DryRun", "-Json"], cwd=wt_path)
        self.assertEqual(res_dry.returncode, 0, msg=res_dry.stdout + "\n" + res_dry.stderr)
        dry_payload = json.loads(res_dry.stdout)
        self.assertEqual(dry_payload["status"], "DRY_RUN")
        self.assertEqual(dry_payload["behind_count"], 1)
        self.assertEqual(dry_payload["overlap_files"], [])
        self.assertFalse((wt_path / "other.txt").exists())

        # Act 3: 通常実行で非破壊マージ MERGED (exit 0)
        res_merge = _run_ps1(SYNC_BRANCH, ["-Json"], cwd=wt_path)
        self.assertEqual(res_merge.returncode, 0, msg=res_merge.stdout + "\n" + res_merge.stderr)
        merge_payload = json.loads(res_merge.stdout)
        self.assertEqual(merge_payload["status"], "MERGED")
        self.assertTrue(merge_payload["merge_commit"])
        self.assertTrue((wt_path / "other.txt").exists())

    def test_コンフリクト検知exit2とAbortロールバックおよびDirty拒否が正常動作すること(self) -> None:
        # Arrange: ワークツリーを作成し app.txt を変更
        create_res = _run_ps1(
            WORKTREE_MANAGER,
            ["-Action", "create", "-IssueNumber", "302", "-BranchName", "feat/302-conflict", "-Json"],
            cwd=self.repo_dir,
        )
        self.assertEqual(create_res.returncode, 0, msg=create_res.stdout + "\n" + create_res.stderr)
        wt_path = Path(json.loads(create_res.stdout)["worktree_path"])

        # 未コミット変更時は DIRTY_WORKTREE (exit 1) で拒否されること
        (wt_path / "app.txt").write_text("branch-change\nline2\n", encoding="utf-8")
        res_dirty = _run_ps1(SYNC_BRANCH, ["-Json"], cwd=wt_path)
        self.assertEqual(res_dirty.returncode, 1)
        self.assertEqual(json.loads(res_dirty.stdout)["status"], "DIRTY_WORKTREE")

        _run_git(["add", "app.txt"], cwd=wt_path)
        _run_git(["commit", "-m", "feat: #302 edit app.txt"], cwd=wt_path)

        # origin/develop で同一行を変更
        self._push_develop_change("app.txt", "develop-change\nline2\n", "feat: develop edit app.txt")

        # Act 1: マージ実行で CONFLICT (exit 2) かつ conflict_files に app.txt が含まれること
        res_conflict = _run_ps1(SYNC_BRANCH, ["-Json"], cwd=wt_path)
        self.assertEqual(res_conflict.returncode, 2, msg=res_conflict.stdout + "\n" + res_conflict.stderr)
        conflict_payload = json.loads(res_conflict.stdout)
        self.assertEqual(conflict_payload["status"], "CONFLICT")
        self.assertIn("app.txt", conflict_payload["conflict_files"])

        # Act 2: -Abort でロールバックされ ABORTED (exit 0) かつクリーンに戻ること
        res_abort = _run_ps1(SYNC_BRANCH, ["-Abort", "-Json"], cwd=wt_path)
        self.assertEqual(res_abort.returncode, 0, msg=res_abort.stdout + "\n" + res_abort.stderr)
        abort_payload = json.loads(res_abort.stdout)
        self.assertEqual(abort_payload["status"], "ABORTED")
        self.assertEqual(_run_git(["status", "--porcelain"], cwd=wt_path).stdout.strip(), "")

    def test_日本語ファイルパスのDryRun重複とコンフリクトおよびSetLocation後の相対JsonOutが正常動作すること(self) -> None:
        # Arrange: ワークツリーを作成し日本語パスファイルを作成・競合させる
        create_res = _run_ps1(
            WORKTREE_MANAGER,
            ["-Action", "create", "-IssueNumber", "303", "-BranchName", "docs/303-jp-path", "-Json"],
            cwd=self.repo_dir,
        )
        self.assertEqual(create_res.returncode, 0, msg=create_res.stdout + "\n" + create_res.stderr)
        wt_path = Path(json.loads(create_res.stdout)["worktree_path"])

        jp_rel = "設計/仕様書.md"
        (wt_path / "設計").mkdir(parents=True, exist_ok=True)
        (wt_path / jp_rel).write_text("branch version\n", encoding="utf-8")
        _run_git(["add", jp_rel], cwd=wt_path)
        _run_git(["commit", "-m", "docs: #303 add jp spec"], cwd=wt_path)

        # origin/develop でも同一の日本語パスファイルを作成して競合させる
        pusher = self.base_dir / "pusher_jp"
        _run_git(["clone", "-b", "develop", str(self.origin_dir), str(pusher)], cwd=self.base_dir)
        _run_git(["config", "user.name", "Other Dev"], cwd=pusher)
        _run_git(["config", "user.email", "other@example.com"], cwd=pusher)
        (pusher / "設計").mkdir(parents=True, exist_ok=True)
        (pusher / jp_rel).write_text("develop version\n", encoding="utf-8")
        _run_git(["add", jp_rel], cwd=pusher)
        _run_git(["commit", "-m", "docs: develop add jp spec"], cwd=pusher)
        _run_git(["push", "origin", "develop"], cwd=pusher)

        # Act 1: Set-Location 後の相対 -JsonOut + -DryRun で日本語パスがエスケープされず出力されること
        cmd_dry = [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            f"Set-Location -LiteralPath '{wt_path}'; & '{SYNC_BRANCH}' -DryRun -JsonOut 'sub/sync_dry.json' -Json",
        ]
        res_dry = subprocess.run(cmd_dry, cwd=str(REPO_ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace")
        self.assertEqual(res_dry.returncode, 0, msg=res_dry.stdout + "\n" + res_dry.stderr)
        dry_file = wt_path / "sub" / "sync_dry.json"
        self.assertTrue(dry_file.is_file())
        dry_payload = json.loads(dry_file.read_text(encoding="utf-8"))
        self.assertIn(jp_rel, dry_payload["overlap_files"])

        # 未追跡ファイルを消してクリーンに戻す
        (wt_path / "sub" / "sync_dry.json").unlink()
        (wt_path / "sub").rmdir()

        # Act 2: 通常マージで CONFLICT (exit 2) かつ conflict_files に日本語パスがそのまま入ること
        res_conflict = _run_ps1(SYNC_BRANCH, ["-Json"], cwd=wt_path)
        self.assertEqual(res_conflict.returncode, 2, msg=res_conflict.stdout + "\n" + res_conflict.stderr)
        conflict_payload = json.loads(res_conflict.stdout)
        self.assertEqual(conflict_payload["status"], "CONFLICT")
        self.assertIn(jp_rel, conflict_payload["conflict_files"])

        # 後片付け: -Abort
        res_abort = _run_ps1(SYNC_BRANCH, ["-Abort", "-Json"], cwd=wt_path)
        self.assertEqual(res_abort.returncode, 0)


if __name__ == "__main__":
    unittest.main()
