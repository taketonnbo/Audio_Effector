#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/epic_tracker.py の単体テスト。"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

TOOLS_DIR = Path(__file__).resolve().parent.parent
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import epic_tracker  # noqa: E402


SAMPLE_EPIC_BODY_SEQUENTIAL = """
### 2. 実装内容

#### 子 Issue 一覧（実施順序）

| 順序 | 状態 | 子 Issue | 主なスコープ |
| :---: | :---: | :--- | :--- |
| 1 | 未着手 | #321 | 共通テスト基盤の整備 |
| 2 | 未着手 | #322 | Domain.Tests 新設 |
| 3 | 未着手 | #323 | Application.Tests 新設 |

### 残タスクチェックリスト
- [x] #321 共通テスト基盤
- [ ] #322 Domain 層テスト移設
- [ ] #323 Application 層テスト移設
"""

SAMPLE_EPIC_BODY_PARALLEL_AND_DEPENDS = """
### タスク一覧
| 順序 | 子 Issue | 依存 | 内容 |
| :---: | :--- | :--- | :--- |
| 1 | #401 | - | 基盤インターフェース追加 |
| 2 | #402 | #401 | 実装A |
| 2 | #403 | #401 | 実装B |
| 3 | #404 | depends: #402, #403 | 統合テスト |

- [ ] #405 追加ドキュメント (depends: #404) [order: 4]
"""


class EpicTrackerParserTests(unittest.TestCase):
    """Epic 本文パーサーのテスト。"""

    def test_順次テーブルとチェックリストを正しくマージして暗黙依存を構築する(self) -> None:
        # Arrange / Act
        specs, warnings = epic_tracker.parse_epic_body(SAMPLE_EPIC_BODY_SEQUENTIAL, epic_number=320)

        # Assert
        self.assertEqual(warnings, [])
        self.assertEqual([s.number for s in specs], [321, 322, 323])
        self.assertEqual([s.order for s in specs], [1, 2, 3])
        self.assertTrue(specs[0].checked_in_epic)
        self.assertFalse(specs[1].checked_in_epic)
        self.assertEqual(specs[0].depends_on, [])
        self.assertEqual(specs[1].depends_on, [321])
        self.assertEqual(specs[2].depends_on, [322])
        self.assertEqual(specs[0].scope, "共通テスト基盤の整備")

    def test_並列グループと明示依存およびチェックリストorderタグを正しく解析する(self) -> None:
        # Arrange / Act
        specs, warnings = epic_tracker.parse_epic_body(SAMPLE_EPIC_BODY_PARALLEL_AND_DEPENDS, epic_number=400)
        by_num = {s.number: s for s in specs}

        # Assert
        self.assertEqual(warnings, [])
        self.assertEqual([s.number for s in specs], [401, 402, 403, 404, 405])
        self.assertEqual(by_num[402].parallel_group, 2)
        self.assertEqual(by_num[403].parallel_group, 2)
        self.assertEqual(by_num[402].depends_on, [401])
        self.assertEqual(by_num[403].depends_on, [401])
        self.assertEqual(by_num[404].depends_on, [402, 403])
        self.assertEqual(by_num[405].order, 4)
        self.assertEqual(by_num[405].depends_on, [404])

    def test_循環依存がある場合に警告を返す(self) -> None:
        # Arrange
        body = """
- [ ] #501 タスクA (depends: #502)
- [ ] #502 タスクB (depends: #501)
"""
        # Act
        _, warnings = epic_tracker.parse_epic_body(body, epic_number=500)

        # Assert
        self.assertTrue(any("循環依存" in w for w in warnings))

    def test_コロンなしのdepends_on指定を正しく抽出する(self) -> None:
        # Arrange
        body = """
- [ ] #601 基盤構築
- [ ] #602 拡張機能 (depends on #601)
"""
        # Act
        specs, warnings = epic_tracker.parse_epic_body(body, epic_number=600)
        by_num = {s.number: s for s in specs}

        # Assert
        self.assertEqual(warnings, [])
        self.assertEqual(by_num[602].depends_on, [601])

    def test_依存Issue列とコードフェンス内の例示を誤判定しない(self) -> None:
        # Arrange
        body = """
| 順序 | 依存 Issue | 子 Issue | 内容 |
| :---: | :--- | :--- | :--- |
| 1 | - | #701 | 基盤 |
| 2 | #701 | #702 | 後続 |

```markdown
| 順序 | 子 Issue | 内容 |
| :---: | :--- | :--- |
| 1 | #999 | 例示テーブル |

- [ ] #998 例示チェックリスト
```
"""
        # Act
        specs, warnings = epic_tracker.parse_epic_body(body, epic_number=700)
        by_num = {s.number: s for s in specs}

        # Assert
        self.assertEqual(warnings, [])
        self.assertEqual([s.number for s in specs], [701, 702])
        self.assertEqual(by_num[702].depends_on, [701])


class EpicTrackerPrAndApiTests(unittest.TestCase):
    """PR 紐付け・GraphQL 変数バインド・API エラーハンドリングのテスト。"""

    def test_PR本文の単なるIssue言及では誤紐付けされずクローズキーワードのみ紐付く(self) -> None:
        # Arrange
        pr = {
            "headRefName": "feat/322-domain-tests",
            "title": "[リファクタ] Domain.Tests 新設 (Issue #322)",
            "body": "前提 Issue #321 の基盤を利用します。\nCloses: #322\nFixed #323\nResolved #324",
        }

        # Act
        matched = epic_tracker.match_pr_to_issue_numbers(pr)

        # Assert
        self.assertNotIn(321, matched)
        self.assertEqual(matched, {322, 323, 324})

    def test_不正なrepo指定はRuntimeErrorを送出する(self) -> None:
        # Arrange / Act / Assert
        with self.assertRaises(RuntimeError):
            epic_tracker.detect_repo_slug('bad"owner/repo', timeout=5)

    def test_fetch_pull_requests失敗時はサイレント空配列にせずRuntimeErrorを送出する(self) -> None:
        # Arrange
        with mock.patch.object(epic_tracker, "run_command", return_value=(1, "", "network error")):
            # Act / Assert
            with self.assertRaises(RuntimeError):
                epic_tracker.fetch_pull_requests(repo="owner/repo", timeout=5)

    def test_fetch_unresolved_review_threadsは変数バインドを使用しGraphQLエラー時に例外を送出する(self) -> None:
        # Arrange: 正常応答で変数バインド引数を検証
        ok_payload = json.dumps({
            "data": {
                "repository": {
                    "p10": {"reviewThreads": {"nodes": [{"isResolved": False}, {"isResolved": True}]}}
                }
            }
        })
        with mock.patch.object(epic_tracker, "run_command", return_value=(0, ok_payload, "")) as mocked_run:
            # Act
            counts = epic_tracker.fetch_unresolved_review_threads([10], repo="owner/repo", timeout=5)

            # Assert
            self.assertEqual(counts, {10: 1})
            called_cmd = mocked_run.call_args[0][0]
            self.assertIn("owner=owner", called_cmd)
            self.assertIn("name=repo", called_cmd)

        # Arrange: GraphQL errors 返却時は RuntimeError
        err_payload = json.dumps({"errors": [{"message": "API rate limit exceeded"}]})
        with mock.patch.object(epic_tracker, "run_command", return_value=(0, err_payload, "")):
            with self.assertRaises(RuntimeError):
                epic_tracker.fetch_unresolved_review_threads([10], repo="owner/repo", timeout=5)


class EpicTrackerClassificationTests(unittest.TestCase):
    """状態分類および次アクション提案のテスト。"""

    def test_7種類の状態分類が仕様通りに行われる(self) -> None:
        # Arrange
        specs = [
            epic_tracker.ChildIssueSpec(number=101, order=1, depends_on=[], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=102, order=2, depends_on=[101], parallel_group=2),
            epic_tracker.ChildIssueSpec(number=103, order=2, depends_on=[101], parallel_group=2),
            epic_tracker.ChildIssueSpec(number=104, order=2, depends_on=[101], parallel_group=2),
            epic_tracker.ChildIssueSpec(number=105, order=2, depends_on=[101], parallel_group=2),
            epic_tracker.ChildIssueSpec(number=106, order=2, depends_on=[101], parallel_group=2),
            epic_tracker.ChildIssueSpec(number=107, order=3, depends_on=[102], parallel_group=3),
        ]
        live_issues = {
            101: {"number": 101, "title": "T101", "state": "CLOSED", "url": "", "labels": []},
            102: {"number": 102, "title": "T102", "state": "OPEN", "url": "", "labels": []},
            103: {"number": 103, "title": "T103", "state": "OPEN", "url": "", "labels": []},
            104: {"number": 104, "title": "T104", "state": "OPEN", "url": "", "labels": []},
            105: {"number": 105, "title": "T105", "state": "OPEN", "url": "", "labels": []},
            106: {"number": 106, "title": "T106", "state": "OPEN", "url": "", "labels": []},
            107: {"number": 107, "title": "T107", "state": "OPEN", "url": "", "labels": []},
        }
        prs_by_issue = {
            101: [{"number": 11, "state": "MERGED", "isDraft": False}],
            102: [{"number": 12, "state": "OPEN", "isDraft": False, "reviewDecision": ""}],
            103: [{"number": 13, "state": "OPEN", "isDraft": False, "reviewDecision": ""}],
            104: [{"number": 14, "state": "OPEN", "isDraft": True}],
            105: [],
            106: [],
            107: [],
        }
        unresolved_threads = {12: 2, 13: 0}
        blocked_overrides = {106: {"reason": "連続3回失敗", "marked_at": "2026-11-26T22:00:00+09:00"}}

        # Act
        classified = epic_tracker.classify_child_statuses(
            specs=specs,
            live_issues=live_issues,
            prs_by_issue=prs_by_issue,
            unresolved_threads_by_pr=unresolved_threads,
            local_branch_issues=set(),
            blocked_overrides=blocked_overrides,
        )
        status_map = {c["number"]: c["status"] for c in classified}

        # Assert
        self.assertEqual(status_map[101], "merged")
        self.assertEqual(status_map[102], "fixing_review")
        self.assertEqual(status_map[103], "in_review")
        self.assertEqual(status_map[104], "implementing")
        self.assertEqual(status_map[105], "pending")
        self.assertEqual(status_map[106], "blocked")
        self.assertEqual(status_map[107], "waiting_deps")

    def test_次アクション提案はfixing_reviewを最優先し並列pendingもまとめて返す(self) -> None:
        # Arrange
        children = [
            {
                "number": 201,
                "order": 1,
                "parallel_group": 1,
                "status": "fixing_review",
                "status_reason": "PR #21: 未解決コメント 1 件",
                "blocked_reason": "",
                "pr": {"number": 21},
            },
            {
                "number": 202,
                "order": 2,
                "parallel_group": 2,
                "status": "pending",
                "status_reason": "着手可能",
                "blocked_reason": "",
                "pr": None,
            },
            {
                "number": 203,
                "order": 2,
                "parallel_group": 2,
                "status": "pending",
                "status_reason": "着手可能",
                "blocked_reason": "",
                "pr": None,
            },
        ]

        # Act
        next_info = epic_tracker.compute_next_actions(children, epic_number=200)

        # Assert
        self.assertFalse(next_info["epic_halt_recommended"])
        self.assertEqual(next_info["primary_action"]["action"], "fix_review")
        start_actions = [a for a in next_info["recommended_actions"] if a["action"] == "start_issue"]
        self.assertEqual(len(start_actions), 1)
        self.assertEqual(start_actions[0]["issues"], [202, 203])
        self.assertTrue(start_actions[0]["parallel"])

    def test_blockedが2件以上の場合はEpic停止推奨フラグが立つ(self) -> None:
        # Arrange
        children = [
            {
                "number": 301,
                "order": 1,
                "parallel_group": 1,
                "status": "blocked",
                "status_reason": "失敗A",
                "blocked_reason": "失敗A",
                "pr": None,
            },
            {
                "number": 302,
                "order": 1,
                "parallel_group": 1,
                "status": "blocked",
                "status_reason": "失敗B",
                "blocked_reason": "失敗B",
                "pr": None,
            },
        ]

        # Act
        next_info = epic_tracker.compute_next_actions(children, epic_number=300)

        # Assert
        self.assertTrue(next_info["epic_halt_recommended"])
        self.assertEqual(next_info["primary_action"]["action"], "halt_epic")

    def test_全子Issueがmergedの場合はcomplete_epicを提案する(self) -> None:
        # Arrange
        children = [
            {
                "number": 311,
                "order": 1,
                "parallel_group": 1,
                "status": "merged",
                "status_reason": "マージ済み",
                "blocked_reason": "",
                "pr": {"number": 51},
            }
        ]

        # Act
        next_info = epic_tracker.compute_next_actions(children, epic_number=310)

        # Assert
        self.assertEqual(next_info["primary_action"]["action"], "complete_epic")

    def test_Epic外の外部依存IssueがCLOSEDならpendingへ遷移する(self) -> None:
        # Arrange
        specs = [
            epic_tracker.ChildIssueSpec(number=341, order=1, depends_on=[340], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=342, order=1, depends_on=[339], parallel_group=1),
        ]
        live_issues = {
            341: {"number": 341, "title": "Child 1", "state": "OPEN", "url": "", "labels": []},
            342: {"number": 342, "title": "Child 2", "state": "OPEN", "url": "", "labels": []},
            340: {"number": 340, "title": "External Closed", "state": "CLOSED", "url": "", "labels": []},
            339: {"number": 339, "title": "External Open", "state": "OPEN", "url": "", "labels": []},
        }

        # Act
        classified = epic_tracker.classify_child_statuses(
            specs=specs,
            live_issues=live_issues,
            prs_by_issue={341: [], 342: []},
            unresolved_threads_by_pr={},
            local_branch_issues=set(),
            blocked_overrides={},
        )
        status_map = {c["number"]: c["status"] for c in classified}

        # Assert
        self.assertEqual(status_map[341], "pending")
        self.assertEqual(status_map[342], "waiting_deps")


class EpicTrackerPersistenceTests(unittest.TestCase):
    """状態スナップショット永続化とオフライン読み込みのテスト。"""

    def test_状態ファイルを保存してofflineモードで再読み込みできる(self) -> None:
        # Arrange
        with tempfile.TemporaryDirectory() as tmpdir:
            state_path = Path(tmpdir) / "epic_999_state.json"
            sample_state = {
                "schema_version": 1,
                "updated_at": "2026-11-26T22:00:00+09:00",
                "state_file": str(state_path),
                "epic": {"number": 999, "title": "テストEpic", "state": "OPEN", "url": ""},
                "summary": {
                    "total": 1,
                    "pending": 1,
                    "waiting_deps": 0,
                    "implementing": 0,
                    "in_review": 0,
                    "fixing_review": 0,
                    "merged": 0,
                    "blocked": 0,
                },
                "children": [],
                "next": {
                    "epic_halt_recommended": False,
                    "epic_halt_reason": "",
                    "recommended_actions": [],
                    "primary_action": None,
                },
                "blocked_overrides": {},
                "warnings": [],
            }
            epic_tracker.save_state(state_path, sample_state)

            # Act
            with mock.patch("sys.stdout") as fake_stdout:
                buf = []
                fake_stdout.write.side_effect = buf.append
                rc = epic_tracker.main(["--epic", "999", "--offline", "--json", "--state-file", str(state_path)])

            # Assert
            self.assertEqual(rc, 0)
            loaded = json.loads("".join(buf))
            self.assertEqual(loaded["epic"]["number"], 999)

    def test_offlineモードでmark_blockedとunblockがローカル反映され同一番号同時指定はエラーになる(self) -> None:
        # Arrange
        with tempfile.TemporaryDirectory() as tmpdir:
            state_path = Path(tmpdir) / "epic_888_state.json"
            sample_state = {
                "schema_version": 1,
                "updated_at": "2026-11-26T22:00:00+09:00",
                "state_file": str(state_path),
                "epic": {"number": 888, "title": "オフラインテストEpic", "state": "OPEN", "url": ""},
                "summary": {
                    "total": 1,
                    "pending": 1,
                    "waiting_deps": 0,
                    "implementing": 0,
                    "in_review": 0,
                    "fixing_review": 0,
                    "merged": 0,
                    "blocked": 0,
                },
                "children": [
                    {
                        "number": 889,
                        "order": 1,
                        "parallel_group": 1,
                        "title": "子タスク",
                        "scope": "",
                        "url": "",
                        "issue_state": "OPEN",
                        "checked_in_epic": False,
                        "depends_on": [],
                        "unmet_depends": [],
                        "status": "pending",
                        "status_reason": "着手可能",
                        "blocked_reason": "",
                        "pr": None,
                    }
                ],
                "next": {
                    "epic_halt_recommended": False,
                    "epic_halt_reason": "",
                    "recommended_actions": [],
                    "primary_action": None,
                },
                "blocked_overrides": {},
                "warnings": [],
            }
            epic_tracker.save_state(state_path, sample_state)

            # Act 1: 同一番号の同時指定はエラー終了
            with mock.patch("sys.stderr"):
                rc_err = epic_tracker.main([
                    "--epic", "888", "--offline", "--mark-blocked", "889", "--unblock", "889",
                    "--state-file", str(state_path),
                ])

            # Act 2: --offline --mark-blocked でローカル反映・保存
            with mock.patch("sys.stdout"):
                rc_blk = epic_tracker.main([
                    "--epic", "888", "--offline", "--mark-blocked", "889", "--reason", "ネットワーク断",
                    "--state-file", str(state_path),
                ])
            after_blk = epic_tracker.load_existing_state(state_path)

            # Act 3: --offline --unblock で pending へ復帰
            with mock.patch("sys.stdout"):
                rc_unblk = epic_tracker.main([
                    "--epic", "888", "--offline", "--unblock", "889",
                    "--state-file", str(state_path),
                ])
            after_unblk = epic_tracker.load_existing_state(state_path)

            # Assert
            self.assertEqual(rc_err, 1)
            self.assertEqual(rc_blk, 0)
            self.assertEqual(after_blk["children"][0]["status"], "blocked")
            self.assertEqual(after_blk["summary"]["blocked"], 1)
            self.assertEqual(rc_unblk, 0)
            self.assertEqual(after_unblk["children"][0]["status"], "pending")
            self.assertEqual(after_unblk["summary"]["blocked"], 0)


class EpicTrackerParallelAndMergeStateTests(unittest.TestCase):
    """PR マージ状態・ワークツリー検出・最大並列数制御のテスト。"""

    def test_fetch_pull_requestsはmergeableとmergeStateStatusを取得フィールドに含める(self) -> None:
        # Arrange
        with mock.patch.object(epic_tracker, "run_command", return_value=(0, "[]", "")) as mocked_run:
            # Act
            prs = epic_tracker.fetch_pull_requests(repo="owner/repo", timeout=5)

            # Assert
            self.assertEqual(prs, [])
            called_cmd = mocked_run.call_args[0][0]
            json_idx = called_cmd.index("--json") + 1
            fields = set(called_cmd[json_idx].split(","))
            self.assertIn("mergeable", fields)
            self.assertIn("mergeStateStatus", fields)

    def test_PRマージ状態のCONFLICTINGとBEHINDとCLEANが正しく分類される(self) -> None:
        # Arrange
        specs = [
            epic_tracker.ChildIssueSpec(number=501, order=1, depends_on=[], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=502, order=1, depends_on=[], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=503, order=1, depends_on=[], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=504, order=1, depends_on=[], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=505, order=1, depends_on=[], parallel_group=1),
        ]
        live_issues = {
            n: {"number": n, "title": f"T{n}", "state": "OPEN", "url": "", "labels": []}
            for n in (501, 502, 503, 504, 505)
        }
        prs_by_issue = {
            501: [{"number": 61, "state": "OPEN", "isDraft": False, "mergeable": "CONFLICTING", "mergeStateStatus": "DIRTY"}],
            502: [{"number": 62, "state": "OPEN", "isDraft": False, "mergeable": "MERGEABLE", "mergeStateStatus": "BEHIND"}],
            503: [{"number": 63, "state": "OPEN", "isDraft": False, "mergeable": "MERGEABLE", "mergeStateStatus": "CLEAN"}],
            504: [{"number": 64, "state": "OPEN", "isDraft": True, "mergeable": "CONFLICTING", "mergeStateStatus": "DIRTY"}],
            505: [{"number": 65, "state": "MERGED", "isDraft": False, "mergeable": "UNKNOWN", "mergeStateStatus": "BEHIND"}],
        }

        # Act
        classified = epic_tracker.classify_child_statuses(
            specs=specs,
            live_issues=live_issues,
            prs_by_issue=prs_by_issue,
            unresolved_threads_by_pr={61: 0, 62: 0, 63: 0, 64: 0},
            local_branch_issues=set(),
            blocked_overrides={},
        )
        by_num = {c["number"]: c for c in classified}

        # Assert
        self.assertTrue(by_num[501]["pr"]["has_conflict"])
        self.assertTrue(by_num[501]["pr"]["needs_sync"])
        self.assertEqual(by_num[501]["pr"]["mergeable"], "CONFLICTING")
        self.assertEqual(by_num[501]["pr"]["merge_state_status"], "DIRTY")

        self.assertFalse(by_num[502]["pr"]["has_conflict"])
        self.assertTrue(by_num[502]["pr"]["needs_sync"])
        self.assertEqual(by_num[502]["pr"]["merge_state_status"], "BEHIND")

        self.assertFalse(by_num[503]["pr"]["has_conflict"])
        self.assertFalse(by_num[503]["pr"]["needs_sync"])
        self.assertEqual(by_num[503]["pr"]["merge_state_status"], "CLEAN")

        # Draft や MERGED では sync/conflict フラグを立てない
        self.assertFalse(by_num[504]["pr"]["has_conflict"])
        self.assertFalse(by_num[504]["pr"]["needs_sync"])
        self.assertFalse(by_num[505]["pr"]["has_conflict"])
        self.assertFalse(by_num[505]["pr"]["needs_sync"])

    def test_sync_branchがコンフリクト最優先で提案されCLEANなPRは直列マージ先頭のみready_to_mergeになる(self) -> None:
        # Arrange
        children = [
            {
                "number": 601,
                "order": 1,
                "parallel_group": 1,
                "status": "in_review",
                "status_reason": "PR #71 CLEAN",
                "blocked_reason": "",
                "pr": {
                    "number": 71,
                    "state": "OPEN",
                    "is_draft": False,
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "CLEAN",
                    "has_conflict": False,
                    "needs_sync": False,
                },
                "worktree": None,
            },
            {
                "number": 602,
                "order": 1,
                "parallel_group": 1,
                "status": "in_review",
                "status_reason": "PR #72 CLEAN",
                "blocked_reason": "",
                "pr": {
                    "number": 72,
                    "state": "OPEN",
                    "is_draft": False,
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "CLEAN",
                    "has_conflict": False,
                    "needs_sync": False,
                },
                "worktree": None,
            },
            {
                "number": 603,
                "order": 2,
                "parallel_group": 2,
                "status": "in_review",
                "status_reason": "PR #73 BEHIND",
                "blocked_reason": "",
                "pr": {
                    "number": 73,
                    "state": "OPEN",
                    "is_draft": False,
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "BEHIND",
                    "has_conflict": False,
                    "needs_sync": True,
                },
                "worktree": {"path": "C:/repo/.agent_worktrees/issue-603", "branch": "feat/603-behind"},
            },
            {
                "number": 604,
                "order": 2,
                "parallel_group": 2,
                "status": "in_review",
                "status_reason": "PR #74 CONFLICT",
                "blocked_reason": "",
                "pr": {
                    "number": 74,
                    "state": "OPEN",
                    "is_draft": False,
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "CONFLICTING",
                    "merge_state_status": "DIRTY",
                    "has_conflict": True,
                    "needs_sync": True,
                },
                "worktree": {"path": "C:/repo/.agent_worktrees/issue-604", "branch": "feat/604-conflict"},
            },
        ]

        # Act
        next_info = epic_tracker.compute_next_actions(children, epic_number=600, max_parallel=3)

        # Assert: コンフリクトありの sync_branch が最優先
        self.assertEqual(next_info["primary_action"]["action"], "sync_branch")
        self.assertEqual(next_info["primary_action"]["pr_number"], 74)
        self.assertTrue(next_info["primary_action"]["has_conflict"])
        self.assertIn("-WorkingDirectory \"C:/repo/.agent_worktrees/issue-604\"", next_info["primary_action"]["command_hint"])

        sync_actions = [a for a in next_info["recommended_actions"] if a["action"] == "sync_branch"]
        self.assertEqual([a["pr_number"] for a in sync_actions], [74, 73])

        # CLEAN な PR (#71, #72) のうち先頭 #71 のみ ready_to_merge、#72 は queued_prs
        merge_actions = [a for a in next_info["recommended_actions"] if a["action"] == "ready_to_merge"]
        self.assertEqual(len(merge_actions), 1)
        self.assertEqual(merge_actions[0]["pr_number"], 71)
        self.assertEqual(merge_actions[0]["queued_prs"], [72])

    def test_max_parallel上限により稼働中2件なら新規着手は最大1件・3件なら新規着手抑制となる(self) -> None:
        # Arrange: 2 件稼働中 + pending 3 件
        children_two_active = [
            {"number": 701, "order": 1, "parallel_group": 1, "status": "implementing", "status_reason": "", "blocked_reason": "", "pr": None, "worktree": {"path": ".agent_worktrees/issue-701", "branch": "feat/701-a"}},
            {"number": 702, "order": 1, "parallel_group": 1, "status": "in_review", "status_reason": "", "blocked_reason": "", "pr": {"number": 82, "state": "OPEN", "is_draft": False}, "worktree": None},
            {"number": 703, "order": 2, "parallel_group": 2, "status": "pending", "status_reason": "", "blocked_reason": "", "pr": None, "worktree": None},
            {"number": 704, "order": 2, "parallel_group": 2, "status": "pending", "status_reason": "", "blocked_reason": "", "pr": None, "worktree": None},
            {"number": 705, "order": 2, "parallel_group": 2, "status": "pending", "status_reason": "", "blocked_reason": "", "pr": None, "worktree": None},
        ]

        # Act 1: max_parallel=3 (2/3 使用中 → 空き 1)
        next_two = epic_tracker.compute_next_actions(children_two_active, epic_number=700, max_parallel=3)
        start_two = [a for a in next_two["recommended_actions"] if a["action"] == "start_issue"]

        # Assert 1
        self.assertEqual(next_two["parallel_slots"]["active_slots"], 2)
        self.assertEqual(next_two["parallel_slots"]["available_slots"], 1)
        self.assertEqual(len(start_two), 1)
        self.assertEqual(start_two[0]["issues"], [703])
        self.assertFalse(start_two[0]["parallel"])

        # Arrange 2: 3 件稼働中 + pending 2 件
        children_three_active = [
            *children_two_active[:2],
            {"number": 703, "order": 2, "parallel_group": 2, "status": "fixing_review", "status_reason": "", "blocked_reason": "", "pr": {"number": 83, "state": "OPEN", "is_draft": False}, "worktree": None},
            *children_two_active[3:],
        ]

        # Act 2: max_parallel=3 (3/3 使用中 → 空き 0)
        next_three = epic_tracker.compute_next_actions(children_three_active, epic_number=700, max_parallel=3)
        start_three = [a for a in next_three["recommended_actions"] if a["action"] == "start_issue"]

        # Assert 2
        self.assertEqual(next_three["parallel_slots"]["active_slots"], 3)
        self.assertEqual(next_three["parallel_slots"]["available_slots"], 0)
        self.assertEqual(start_three, [])

        # Act 3: --max-parallel 0 はエラー終了
        with mock.patch("sys.stderr"):
            rc_err = epic_tracker.main(["--epic", "700", "--max-parallel", "0", "--offline"])
        self.assertEqual(rc_err, 1)

    def test_fetch_active_worktreesのporcelain解析とmerged後残留ワークツリー警告が動作する(self) -> None:
        # Arrange
        porcelain = (
            "worktree C:/Users/dev/Audio_Effector\n"
            "HEAD 1111111111111111111111111111111111111111\n"
            "branch refs/heads/develop\n"
            "\n"
            "worktree C:/Users/dev/Audio_Effector/.agent_worktrees/issue-367\n"
            "HEAD 2222222222222222222222222222222222222222\n"
            "branch refs/heads/feat/367-build-isolation\n"
            "\n"
            "worktree C:\\Users\\dev\\Audio_Effector\\.agent_worktrees\\issue-368\n"
            "HEAD 3333333333333333333333333333333333333333\n"
            "detached\n"
        )
        with mock.patch.object(epic_tracker, "run_command", return_value=(0, porcelain, "")):
            # Act
            worktrees = epic_tracker.fetch_active_worktrees(timeout=5)

        # Assert: メインリポジトリは除外され、.agent_worktrees/ 配下のみ抽出される
        self.assertEqual(set(worktrees.keys()), {367, 368})
        self.assertEqual(worktrees[367]["branch"], "feat/367-build-isolation")
        self.assertEqual(worktrees[368]["branch"], "")

        # Arrange: #367 は merged なのにワークツリー残留、#368 は implementing
        specs = [
            epic_tracker.ChildIssueSpec(number=367, order=1, depends_on=[], parallel_group=1),
            epic_tracker.ChildIssueSpec(number=368, order=1, depends_on=[], parallel_group=1),
        ]
        live_issues = {
            367: {"number": 367, "title": "ビルド分離", "state": "CLOSED", "url": "", "labels": []},
            368: {"number": 368, "title": "並列トラッカー", "state": "OPEN", "url": "", "labels": []},
        }
        classified = epic_tracker.classify_child_statuses(
            specs=specs,
            live_issues=live_issues,
            prs_by_issue={367: [{"number": 91, "state": "MERGED", "isDraft": False}], 368: []},
            unresolved_threads_by_pr={},
            local_branch_issues=set(),
            blocked_overrides={},
            active_worktrees=worktrees,
        )
        warnings = epic_tracker.collect_worktree_warnings(classified)

        # Assert
        by_num = {c["number"]: c for c in classified}
        self.assertEqual(by_num[367]["status"], "merged")
        self.assertIsNotNone(by_num[367]["worktree"])
        self.assertEqual(by_num[368]["status"], "implementing")
        self.assertEqual(len(warnings), 1)
        self.assertIn("#367", warnings[0])
        self.assertIn("worktree_manager.ps1 -Action remove -IssueNumber 367", warnings[0])

    def test_format_human_reportにPRマージ状態と稼働中ワークツリーと並列スロットが表示される(self) -> None:
        # Arrange
        state = {
            "updated_at": "2026-11-27T21:50:00+09:00",
            "state_file": ".agent_logs/epic_370_state.json",
            "epic": {"number": 370, "title": "並列化Epic", "state": "OPEN", "url": ""},
            "summary": {
                "total": 3,
                "pending": 1,
                "waiting_deps": 0,
                "implementing": 0,
                "in_review": 2,
                "fixing_review": 0,
                "merged": 0,
                "blocked": 0,
            },
            "children": [
                {
                    "number": 367,
                    "order": 1,
                    "parallel_group": 1,
                    "title": "ビルド分離",
                    "depends_on": [],
                    "status": "in_review",
                    "status_reason": "PR #91 コンフリクトあり",
                    "pr": {"number": 91, "state": "OPEN", "is_draft": False, "has_conflict": True, "merge_state_status": "DIRTY"},
                    "worktree": {"path": "C:/repo/.agent_worktrees/issue-367", "branch": "feat/367-build"},
                },
                {
                    "number": 368,
                    "order": 1,
                    "parallel_group": 1,
                    "title": "トラッカー拡張",
                    "depends_on": [],
                    "status": "in_review",
                    "status_reason": "PR #92 ベース追従同期が必要（BEHIND）",
                    "pr": {"number": 92, "state": "OPEN", "is_draft": False, "has_conflict": False, "merge_state_status": "BEHIND"},
                    "worktree": None,
                },
                {
                    "number": 369,
                    "order": 2,
                    "parallel_group": 2,
                    "title": "スキル改訂",
                    "depends_on": [367],
                    "status": "pending",
                    "status_reason": "着手可能",
                    "pr": None,
                    "worktree": None,
                },
            ],
            "next": {
                "epic_halt_recommended": False,
                "epic_halt_reason": "",
                "parallel_slots": {"max_parallel": 3, "active_slots": 2, "available_slots": 1, "active_issues": [367, 368]},
                "recommended_actions": [],
                "primary_action": None,
            },
            "warnings": [],
        }

        # Act
        report = epic_tracker.format_human_report(state, next_only=False)

        # Assert
        self.assertIn("並列スロット: 2/3 使用中", report)
        self.assertIn("PR #91(OPEN/CONFLICT)", report)
        self.assertIn("PR #92(OPEN/BEHIND)", report)
        self.assertIn("worktree: C:/repo/.agent_worktrees/issue-367 (branch: feat/367-build)", report)

    def test_ci_stateがPENDINGのCLEANなPRはready_to_mergeから除外される(self) -> None:
        # Arrange
        children = [
            {
                "number": 801,
                "order": 1,
                "parallel_group": 1,
                "status": "in_review",
                "status_reason": "PR #101 CI 実行中",
                "blocked_reason": "",
                "pr": {
                    "number": 101,
                    "state": "OPEN",
                    "is_draft": False,
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PENDING",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "CLEAN",
                    "has_conflict": False,
                    "needs_sync": False,
                },
                "worktree": None,
            },
        ]

        # Act
        next_info = epic_tracker.compute_next_actions(children, epic_number=800, max_parallel=3)

        # Assert
        merge_actions = [a for a in next_info["recommended_actions"] if a["action"] == "ready_to_merge"]
        review_actions = [a for a in next_info["recommended_actions"] if a["action"] == "review_pr"]
        self.assertEqual(merge_actions, [])
        self.assertEqual(len(review_actions), 1)
        self.assertEqual(review_actions[0]["pr_number"], 101)

    def test_parse_worktree_porcelainは親パスのissue番号に誤マッチせず日本語Windowsパスを解析できる(self) -> None:
        # Arrange
        porcelain = (
            "worktree C:\\Users\\西内\\issue-100\\Audio_Effector\n"
            "HEAD 1111111111111111111111111111111111111111\n"
            "branch refs/heads/develop\n"
            "\n"
            "worktree C:\\Users\\西内\\issue-100\\Audio_Effector\\.agent_worktrees\\issue-368\n"
            "HEAD 2222222222222222222222222222222222222222\n"
            "branch refs/heads/feat/368-parallel-tracker\n"
            "\n"
            "worktree .agent_worktrees/custom-dir\n"
            "HEAD 3333333333333333333333333333333333333333\n"
            "branch refs/heads/feat/369-custom-worktree\n"
        )

        # Act
        worktrees = epic_tracker.parse_worktree_porcelain(porcelain)

        # Assert: 親ディレクトリの issue-100 ではなく .agent_worktrees/ 配下の 368, 369 を抽出する
        self.assertNotIn(100, worktrees)
        self.assertEqual(set(worktrees.keys()), {368, 369})
        self.assertEqual(worktrees[368]["branch"], "feat/368-parallel-tracker")
        self.assertIn("西内", worktrees[368]["path"])
        self.assertEqual(worktrees[369]["branch"], "feat/369-custom-worktree")

    def test_offlineモードでmark_blocked未指定でもmax_parallelが出力に反映されファイルは書き換えない(self) -> None:
        # Arrange
        with tempfile.TemporaryDirectory() as tmpdir:
            state_path = Path(tmpdir) / "epic_810_state.json"
            original_state = {
                "schema_version": 1,
                "updated_at": "2026-11-27T21:00:00+09:00",
                "state_file": str(state_path),
                "epic": {"number": 810, "title": "Offline並列テスト", "state": "OPEN", "url": ""},
                "summary": {
                    "total": 3,
                    "pending": 2,
                    "waiting_deps": 0,
                    "implementing": 1,
                    "in_review": 0,
                    "fixing_review": 0,
                    "merged": 0,
                    "blocked": 0,
                },
                "children": [
                    {"number": 811, "order": 1, "parallel_group": 1, "status": "implementing", "status_reason": "作業中", "blocked_reason": "", "pr": None, "worktree": None},
                    {"number": 812, "order": 2, "parallel_group": 2, "status": "pending", "status_reason": "着手可能", "blocked_reason": "", "pr": None, "worktree": None},
                    {"number": 813, "order": 2, "parallel_group": 2, "status": "pending", "status_reason": "着手可能", "blocked_reason": "", "pr": None, "worktree": None},
                ],
                "next": {
                    "epic_halt_recommended": False,
                    "epic_halt_reason": "",
                    "parallel_slots": {"max_parallel": 3, "active_slots": 1, "available_slots": 2, "active_issues": [811]},
                    "recommended_actions": [],
                    "primary_action": None,
                },
                "blocked_overrides": {},
                "warnings": [],
            }
            epic_tracker.save_state(state_path, original_state)
            before_text = state_path.read_text(encoding="utf-8")

            # Act: --offline --next --json --max-parallel 1（稼働中 1/1 のため新規着手抑制）
            with mock.patch("sys.stdout") as fake_stdout:
                buf = []
                fake_stdout.write.side_effect = buf.append
                rc = epic_tracker.main([
                    "--epic", "810", "--offline", "--next", "--json", "--max-parallel", "1",
                    "--state-file", str(state_path),
                ])

            # Assert
            self.assertEqual(rc, 0)
            out_next = json.loads("".join(buf))
            self.assertEqual(out_next["parallel_slots"]["max_parallel"], 1)
            self.assertEqual(out_next["parallel_slots"]["available_slots"], 0)
            self.assertEqual([a for a in out_next["recommended_actions"] if a["action"] == "start_issue"], [])
            # スナップショットファイル自体は書き換えない
            self.assertEqual(state_path.read_text(encoding="utf-8"), before_text)

    def test_offlineのunblock時にworktree保持Issueはimplementingへ復帰しstart_issueへ二重計上されない(self) -> None:
        # Arrange
        with tempfile.TemporaryDirectory() as tmpdir:
            state_path = Path(tmpdir) / "epic_820_state.json"
            sample_state = {
                "schema_version": 1,
                "updated_at": "2026-11-27T21:00:00+09:00",
                "state_file": str(state_path),
                "epic": {"number": 820, "title": "Worktree復帰テスト", "state": "OPEN", "url": ""},
                "summary": {
                    "total": 1,
                    "pending": 0,
                    "waiting_deps": 0,
                    "implementing": 1,
                    "in_review": 0,
                    "fixing_review": 0,
                    "merged": 0,
                    "blocked": 0,
                },
                "children": [
                    {
                        "number": 821,
                        "order": 1,
                        "parallel_group": 1,
                        "title": "ワークツリー作業中Issue",
                        "scope": "",
                        "url": "",
                        "issue_state": "OPEN",
                        "checked_in_epic": False,
                        "depends_on": [],
                        "unmet_depends": [],
                        "status": "implementing",
                        "status_reason": "稼働中ワークツリーが存在",
                        "blocked_reason": "",
                        "pr": None,
                        "worktree": {"path": "C:/repo/.agent_worktrees/issue-821", "branch": "feat/821-wt"},
                    }
                ],
                "next": {
                    "epic_halt_recommended": False,
                    "epic_halt_reason": "",
                    "recommended_actions": [],
                    "primary_action": None,
                },
                "blocked_overrides": {},
                "warnings": [],
            }
            epic_tracker.save_state(state_path, sample_state)

            # Act: mark-blocked -> unblock
            with mock.patch("sys.stdout"):
                rc_blk = epic_tracker.main([
                    "--epic", "820", "--offline", "--mark-blocked", "821", "--reason", "一時停止",
                    "--state-file", str(state_path),
                ])
                rc_unblk = epic_tracker.main([
                    "--epic", "820", "--offline", "--unblock", "821",
                    "--state-file", str(state_path),
                ])
            after = epic_tracker.load_existing_state(state_path)

            # Assert
            self.assertEqual(rc_blk, 0)
            self.assertEqual(rc_unblk, 0)
            self.assertEqual(after["children"][0]["status"], "implementing")
            self.assertEqual(after["children"][0]["status_reason"], "稼働中ワークツリーが存在")
            self.assertEqual(after["summary"]["implementing"], 1)
            self.assertEqual(after["summary"]["pending"], 0)
            start_actions = [a for a in after["next"]["recommended_actions"] if a["action"] == "start_issue"]
            self.assertEqual(start_actions, [])


class EpicTrackerIntegrationBranchTests(unittest.TestCase):
    """Epic 統合ブランチ・統合 PR 追跡・警告・最終フェーズ遷移のテスト。"""

    def test_統合ブランチの明示指定と状態引き継ぎと自動検出が優先順位通りに動作すること(self) -> None:
        # Arrange: 明示指定は origin/ プレフィックスを正規化して最優先採用
        warnings: list[str] = []
        resolved_explicit = epic_tracker.resolve_integration_branch_name(
            epic_number=401,
            explicit_branch="origin/epic/401-lyrics",
            existing_state={"integration_branch": {"name": "epic/401-from-state"}},
            local_branches=["develop", "epic/401-local"],
            remote_branches=["develop", "epic/401-remote"],
            all_prs=[],
            warnings=warnings,
        )
        self.assertEqual(resolved_explicit, "epic/401-lyrics")
        self.assertEqual(warnings, [])

        # Arrange: 明示指定なし → 状態スナップショットから引き継ぎ
        resolved_state = epic_tracker.resolve_integration_branch_name(
            epic_number=401,
            explicit_branch=None,
            existing_state={"integration_branch": {"name": "epic/401-from-state"}},
            local_branches=["develop", "epic/401-local"],
            remote_branches=["develop"],
            all_prs=[],
            warnings=warnings,
        )
        self.assertEqual(resolved_state, "epic/401-from-state")

        # Arrange: 状態ファイルなし → ローカル/リモート/PR から epic/<番号>-* を自動検出（複数候補時は警告）
        multi_warnings: list[str] = []
        resolved_auto = epic_tracker.resolve_integration_branch_name(
            epic_number=401,
            explicit_branch=None,
            existing_state=None,
            local_branches=["develop", "epic/401-alpha", "epic/401-beta"],
            remote_branches=["develop", "epic/401-beta"],
            all_prs=[{"number": 450, "state": "OPEN", "headRefName": "epic/401-beta", "baseRefName": "develop"}],
            warnings=multi_warnings,
        )
        self.assertEqual(resolved_auto, "epic/401-beta")
        self.assertEqual(len(multi_warnings), 1)
        self.assertIn("複数見つかりました", multi_warnings[0])

    def test_統合ブランチ未作成および未Push時にcreate_integration_branchが最優先で提案されること(self) -> None:
        # Arrange
        children = [
            {
                "number": 402,
                "order": 1,
                "parallel_group": 1,
                "status": "pending",
                "status_reason": "着手可能",
                "blocked_reason": "",
                "pr": None,
                "worktree": None,
            }
        ]

        # Act 1: ローカル・リモートともに未作成
        next_missing = epic_tracker.compute_next_actions(
            children,
            epic_number=401,
            integration_branch={
                "name": None,
                "exists_local": False,
                "exists_remote": False,
                "ahead_of_develop": None,
                "behind_develop": None,
            },
            integration_pr=None,
        )

        # Assert 1
        self.assertEqual(next_missing["primary_action"]["action"], "create_integration_branch")
        self.assertIn("epic/401-<slug>", next_missing["primary_action"]["command_hint"])
        self.assertIn("git push -u origin", next_missing["primary_action"]["command_hint"])
        self.assertEqual(
            [a for a in next_missing["recommended_actions"] if a["action"] == "start_issue"],
            [],
        )

        # Act 2: ローカルのみ存在しリモート未 Push
        next_unpushed = epic_tracker.compute_next_actions(
            children,
            epic_number=401,
            integration_branch={
                "name": "epic/401-lyrics",
                "exists_local": True,
                "exists_remote": False,
                "ahead_of_develop": 0,
                "behind_develop": 0,
            },
            integration_pr=None,
        )

        # Assert 2
        self.assertEqual(next_unpushed["primary_action"]["action"], "create_integration_branch")
        self.assertEqual(next_unpushed["primary_action"]["command_hint"], "git push -u origin epic/401-lyrics")

    def test_子IssuePRのbase_branchが統合ブランチと一致しない場合に警告を検知しready_to_mergeから除外すること(self) -> None:
        # Arrange
        children = [
            {
                "number": 402,
                "order": 1,
                "parallel_group": 1,
                "status": "in_review",
                "status_reason": "PR #410 レビュー待ち",
                "blocked_reason": "",
                "pr": {
                    "number": 410,
                    "state": "OPEN",
                    "is_draft": False,
                    "head_branch": "feat/402-parser",
                    "base_branch": "develop",
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "CLEAN",
                    "has_conflict": False,
                    "needs_sync": False,
                },
                "worktree": None,
            }
        ]
        int_branch = {
            "name": "epic/401-lyrics",
            "exists_local": True,
            "exists_remote": True,
            "ahead_of_develop": 0,
            "behind_develop": 0,
        }

        # Act
        warnings = epic_tracker.collect_base_branch_warnings(children, "epic/401-lyrics")
        next_info = epic_tracker.compute_next_actions(
            children,
            epic_number=401,
            integration_branch=int_branch,
            integration_pr=None,
        )

        # Assert
        self.assertEqual(len(warnings), 1)
        self.assertIn("PR #410", warnings[0])
        self.assertIn("base: develop", warnings[0])
        self.assertIn("gh pr edit 410 --base epic/401-lyrics", warnings[0])
        self.assertEqual(
            [a for a in next_info["recommended_actions"] if a["action"] == "ready_to_merge"],
            [],
        )

    def test_統合PR本文に子Issue参照が含まれても子IssuePRとして誤認せずintegration_prへ分離すること(self) -> None:
        # Arrange
        integration_pr_raw = {
            "number": 450,
            "title": "[Epic] 歌詞表示機能 (#401)",
            "body": "Closes #401\nCloses #402\nCloses #403",
            "state": "OPEN",
            "isDraft": False,
            "headRefName": "epic/401-lyrics",
            "baseRefName": "develop",
            "url": "https://example.test/pull/450",
            "reviewDecision": "",
            "statusCheckRollup": [],
            "mergeable": "MERGEABLE",
            "mergeStateStatus": "CLEAN",
        }
        child_pr_raw = {
            "number": 412,
            "title": "[機能追加] 歌詞パーサー (#402)",
            "body": "Closes #402",
            "state": "MERGED",
            "isDraft": False,
            "headRefName": "feat/402-parser",
            "baseRefName": "epic/401-lyrics",
            "url": "https://example.test/pull/412",
            "reviewDecision": "",
            "statusCheckRollup": [],
            "mergeable": "UNKNOWN",
            "mergeStateStatus": "UNKNOWN",
        }

        # Act 1: 関数単体で統合 PR が子 Issue 番号にマッチしないことを検証
        matched_int = epic_tracker.match_pr_to_issue_numbers(
            integration_pr_raw,
            integration_branch="epic/401-lyrics",
            epic_number=401,
        )
        self.assertEqual(matched_int, set())

        # Act 2: build_state で統合 PR が integration_pr に分離され、#403 が未着手(pending)のままであることを検証
        epic_issue = {
            "number": 401,
            "title": "歌詞表示機能 Epic",
            "state": "OPEN",
            "url": "https://example.test/issues/401",
            "body": "- [x] #402 歌詞パーサー [order: 1]\n- [ ] #403 歌詞UI [order: 1]\n",
        }
        live_issues = {
            402: {"number": 402, "title": "歌詞パーサー", "state": "OPEN", "url": "", "labels": []},
            403: {"number": 403, "title": "歌詞UI", "state": "OPEN", "url": "", "labels": []},
        }
        with tempfile.TemporaryDirectory() as tmpdir:
            state_path = Path(tmpdir) / "epic_401_state.json"
            with mock.patch.object(epic_tracker, "fetch_epic_issue", return_value=epic_issue), \
                 mock.patch.object(epic_tracker, "fetch_child_issues_live", return_value=live_issues), \
                 mock.patch.object(epic_tracker, "fetch_pull_requests", return_value=[integration_pr_raw, child_pr_raw]), \
                 mock.patch.object(epic_tracker, "fetch_unresolved_review_threads", return_value={450: 0}), \
                 mock.patch.object(epic_tracker, "fetch_local_branch_names", return_value=["develop", "epic/401-lyrics"]), \
                 mock.patch.object(epic_tracker, "fetch_remote_branch_names", return_value=["develop", "epic/401-lyrics"]), \
                 mock.patch.object(epic_tracker, "compute_branch_ahead_behind", return_value=(2, 0)), \
                 mock.patch.object(epic_tracker, "fetch_active_worktrees", return_value={}):
                state = epic_tracker.build_state(
                    epic_number=401,
                    state_path=state_path,
                    repo="owner/repo",
                    timeout=10,
                    mark_blocked=None,
                    unblock=None,
                    reason="",
                )

        # Assert
        self.assertEqual(state["integration_branch"]["name"], "epic/401-lyrics")
        self.assertTrue(state["integration_branch"]["exists_local"])
        self.assertTrue(state["integration_branch"]["exists_remote"])
        self.assertEqual(state["integration_branch"]["ahead_of_develop"], 2)
        self.assertEqual(state["integration_branch"]["behind_develop"], 0)
        self.assertIsNotNone(state["integration_pr"])
        self.assertEqual(state["integration_pr"]["number"], 450)
        by_num = {c["number"]: c for c in state["children"]}
        self.assertEqual(by_num[402]["status"], "merged")
        self.assertEqual(by_num[402]["pr"]["number"], 412)
        self.assertEqual(by_num[403]["status"], "pending")
        self.assertIsNone(by_num[403]["pr"])

    def test_統合ブランチ運用時の各アクションcommand_hintにBaseBranchが反映され最終フェーズが正しく遷移すること(self) -> None:
        int_branch = {
            "name": "epic/401-lyrics",
            "exists_local": True,
            "exists_remote": True,
            "ahead_of_develop": 3,
            "behind_develop": 0,
        }

        # Case 1: 進行中（sync_branch / ready_to_merge / start_issue のコマンドヒント検証）
        in_progress_children = [
            {
                "number": 402,
                "order": 1,
                "parallel_group": 1,
                "status": "in_review",
                "status_reason": "PR #412 BEHIND",
                "blocked_reason": "",
                "pr": {
                    "number": 412,
                    "state": "OPEN",
                    "is_draft": False,
                    "base_branch": "epic/401-lyrics",
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "BEHIND",
                    "has_conflict": False,
                    "needs_sync": True,
                },
                "worktree": {"path": "C:/repo/.agent_worktrees/issue-402", "branch": "feat/402-parser"},
            },
            {
                "number": 403,
                "order": 1,
                "parallel_group": 1,
                "status": "in_review",
                "status_reason": "PR #413 CLEAN",
                "blocked_reason": "",
                "pr": {
                    "number": 413,
                    "state": "OPEN",
                    "is_draft": False,
                    "base_branch": "epic/401-lyrics",
                    "unresolved_review_threads": 0,
                    "review_decision": "",
                    "ci_state": "PASS",
                    "mergeable": "MERGEABLE",
                    "merge_state_status": "CLEAN",
                    "has_conflict": False,
                    "needs_sync": False,
                },
                "worktree": None,
            },
            {
                "number": 404,
                "order": 2,
                "parallel_group": 2,
                "status": "pending",
                "status_reason": "着手可能",
                "blocked_reason": "",
                "pr": None,
                "worktree": None,
            },
        ]
        next_prog = epic_tracker.compute_next_actions(
            in_progress_children,
            epic_number=401,
            max_parallel=3,
            integration_branch=int_branch,
            integration_pr=None,
        )
        by_action = {a["action"]: a for a in next_prog["recommended_actions"]}
        self.assertIn('-BaseBranch "epic/401-lyrics"', by_action["sync_branch"]["command_hint"])
        self.assertIn("epic/401-lyrics", by_action["ready_to_merge"]["summary"])
        self.assertEqual(by_action["ready_to_merge"]["base_branch"], "epic/401-lyrics")
        self.assertIn('-BaseBranch "epic/401-lyrics"', by_action["start_issue"]["command_hint"])

        # Case 2: 全子 Issue マージ後、統合 PR 未作成 → create_integration_pr
        all_merged = [
            {"number": 402, "order": 1, "parallel_group": 1, "status": "merged", "status_reason": "", "blocked_reason": "", "pr": {"number": 412}},
            {"number": 403, "order": 2, "parallel_group": 2, "status": "merged", "status_reason": "", "blocked_reason": "", "pr": {"number": 413}},
        ]
        next_create_pr = epic_tracker.compute_next_actions(
            all_merged,
            epic_number=401,
            integration_branch=int_branch,
            integration_pr=None,
        )
        self.assertEqual(next_create_pr["primary_action"]["action"], "create_integration_pr")
        self.assertIn('-BaseBranch "develop"', next_create_pr["primary_action"]["command_hint"])
        self.assertIn("-BaseBranch develop -PreferOriginBase", next_create_pr["primary_action"]["command_hint"])
        self.assertIn('gh pr create --base develop --head "epic/401-lyrics"', next_create_pr["primary_action"]["command_hint"])

        # Case 3: 統合 PR OPEN かつ BEHIND → sync_integration_branch
        next_sync_int = epic_tracker.compute_next_actions(
            all_merged,
            epic_number=401,
            integration_branch=int_branch,
            integration_pr={
                "number": 450,
                "state": "OPEN",
                "is_draft": False,
                "head_branch": "epic/401-lyrics",
                "base_branch": "develop",
                "mergeable": "MERGEABLE",
                "merge_state_status": "BEHIND",
                "has_conflict": False,
                "needs_sync": True,
            },
        )
        self.assertEqual(next_sync_int["primary_action"]["action"], "sync_integration_branch")
        self.assertIn('-BaseBranch "develop"', next_sync_int["primary_action"]["command_hint"])

        # Case 4: 統合 PR OPEN かつ CLEAN → await_user_merge（自動マージコマンドを提案しない）
        next_await = epic_tracker.compute_next_actions(
            all_merged,
            epic_number=401,
            integration_branch=int_branch,
            integration_pr={
                "number": 450,
                "state": "OPEN",
                "is_draft": False,
                "head_branch": "epic/401-lyrics",
                "base_branch": "develop",
                "mergeable": "MERGEABLE",
                "merge_state_status": "CLEAN",
                "has_conflict": False,
                "needs_sync": False,
            },
        )
        self.assertEqual(next_await["primary_action"]["action"], "await_user_merge")
        self.assertEqual(next_await["primary_action"]["command_hint"], "")
        self.assertNotIn("gh pr merge", next_await["primary_action"]["summary"])

        # Case 5: 統合 PR MERGED → complete_epic（統合ブランチ削除済みでも complete_epic になること）
        next_done = epic_tracker.compute_next_actions(
            all_merged,
            epic_number=401,
            integration_branch={
                "name": "epic/401-lyrics",
                "exists_local": False,
                "exists_remote": False,
                "ahead_of_develop": None,
                "behind_develop": None,
            },
            integration_pr={
                "number": 450,
                "state": "MERGED",
                "is_draft": False,
                "head_branch": "epic/401-lyrics",
                "base_branch": "develop",
            },
        )
        self.assertEqual(next_done["primary_action"]["action"], "complete_epic")
        self.assertIn("PR #450", next_done["primary_action"]["summary"])

    def test_format_human_reportに統合ブランチと統合PRステータスが表示されること(self) -> None:
        # Arrange
        state = {
            "updated_at": "2026-11-28T15:00:00+09:00",
            "state_file": ".agent_logs/epic_401_state.json",
            "epic": {"number": 401, "title": "歌詞表示機能 Epic", "state": "OPEN", "url": ""},
            "integration_branch": {
                "name": "epic/401-lyrics",
                "exists_local": True,
                "exists_remote": True,
                "ahead_of_develop": 4,
                "behind_develop": 1,
            },
            "integration_pr": {
                "number": 450,
                "state": "OPEN",
                "is_draft": False,
                "head_branch": "epic/401-lyrics",
                "base_branch": "develop",
                "has_conflict": False,
                "merge_state_status": "BEHIND",
            },
            "summary": {
                "total": 1,
                "pending": 0,
                "waiting_deps": 0,
                "implementing": 0,
                "in_review": 0,
                "fixing_review": 0,
                "merged": 1,
                "blocked": 0,
            },
            "children": [],
            "next": {
                "epic_halt_recommended": False,
                "epic_halt_reason": "",
                "parallel_slots": {"max_parallel": 3, "active_slots": 0, "available_slots": 3, "active_issues": []},
                "recommended_actions": [],
                "primary_action": None,
            },
            "warnings": [],
        }

        # Act
        report = epic_tracker.format_human_report(state, next_only=True)

        # Assert
        self.assertIn("統合ブランチ: epic/401-lyrics (local: あり, remote: あり / vs origin/develop: ahead=4, behind=1)", report)
        self.assertIn("統合 PR: PR #450 (epic/401-lyrics -> develop) [OPEN/BEHIND]", report)


if __name__ == "__main__":
    unittest.main()
