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


if __name__ == "__main__":
    unittest.main()
