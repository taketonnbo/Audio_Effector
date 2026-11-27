#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Epic 進捗・依存関係トラッカー (Epic Tracker)
Audio Effector プロジェクトにおいて、Epic Issue 本文から子 Issue・実施順序・依存関係を抽出し、
GitHub CLI (`gh`) を通じてライブ状態を取得・分類・永続化および次アクション提案を行うツール。

使用法:
  python tools/epic_tracker.py --epic <Epic番号> [options]

主なオプション:
  --epic NUMBER       対象の Epic Issue 番号（必須）
  --next              次に実行すべきアクションを優先表示する
  --json              結果を構造化 JSON 形式で標準出力へ出力する
  --state-file PATH   状態スナップショット保存先（既定: .agent_logs/epic_<番号>_state.json）
  --no-save           状態スナップショットをファイルへ保存しない
  --offline           GitHub CLI を呼び出さず既存の状態スナップショットから読み込む
  --mark-blocked NUM  指定した子 Issue をローカル状態で blocked に設定する（GitHub 非書き込み）
  --unblock NUM       指定した子 Issue のローカル blocked 設定を解除する
  --reason TEXT       --mark-blocked 時のブロック理由
  --repo OWNER/NAME   対象リポジトリを明示指定する（省略時はカレントリポジトリ）
  --timeout SEC       gh / git コマンドのタイムアウト秒数（既定: 30）
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass


REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_LOG_DIR = REPO_ROOT / ".agent_logs"

VALID_STATUSES = (
    "pending",
    "waiting_deps",
    "implementing",
    "in_review",
    "fixing_review",
    "merged",
    "blocked",
)

# 依存指定パターン: "depends: #321, #322" / "依存: #321" / "(depends on #321)" など
DEPENDS_BLOCK_RE = re.compile(
    r"(?:depends(?:\s+on)?|依存)(?:\s*[:：]\s*|\s+)(#\d+(?:[\s,、/]+#\d+)*)",
    re.IGNORECASE,
)
ISSUE_NUM_RE = re.compile(r"#(\d+)")
CHECKLIST_LINE_RE = re.compile(
    r"^\s*[-*]\s*\[(?P<checked>[ xX])\]\s*(?P<rest>.*)$"
)
ORDER_TAG_RE = re.compile(r"\[(?:order|順序)\s*[:：]\s*(\d+)\]", re.IGNORECASE)
PR_ISSUE_REF_RE = re.compile(
    r"(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s*:?\s*#(\d+)",
    re.IGNORECASE,
)
BRANCH_ISSUE_RE = re.compile(r"(?:^|/)(\d+)(?:[-_]|$)")
WORKTREE_ISSUE_RE = re.compile(r"(?:^|/)issue-(\d+)(?:$|/)", re.IGNORECASE)
REPO_SLUG_RE = re.compile(r"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$")
CODE_FENCE_RE = re.compile(r"^\s*(`{3,}|~{3,})")


def _strip_code_fences(body: str) -> str:
    """Markdown のコードフェンス（``` / ~~~）内の行を除外したテキストを返す。"""
    output_lines: List[str] = []
    in_fence = False
    fence_char = ""
    fence_len = 0
    for line in body.splitlines():
        m = CODE_FENCE_RE.match(line)
        if m:
            marker = m.group(1)
            if not in_fence:
                in_fence = True
                fence_char = marker[0]
                fence_len = len(marker)
            elif marker[0] == fence_char and len(marker) >= fence_len:
                in_fence = False
                fence_char = ""
                fence_len = 0
            continue
        if not in_fence:
            output_lines.append(line)
    return "\n".join(output_lines)


@dataclass
class ChildIssueSpec:
    """Epic 本文から抽出された子 Issue の静的定義。"""

    number: int
    order: int
    title_hint: str = ""
    scope: str = ""
    checked_in_epic: bool = False
    explicit_depends: List[int] = field(default_factory=list)
    depends_on: List[int] = field(default_factory=list)
    parallel_group: int = 0


def extract_explicit_depends(text: str, self_number: int, epic_number: Optional[int] = None) -> List[int]:
    """テキスト内の明示的な依存指定（depends: #321 等）から Issue 番号一覧を抽出する。"""
    deps: List[int] = []
    for block_match in DEPENDS_BLOCK_RE.finditer(text):
        segment = block_match.group(1)
        for num_match in ISSUE_NUM_RE.finditer(segment):
            num = int(num_match.group(1))
            if num == self_number or (epic_number is not None and num == epic_number):
                continue
            if num not in deps:
                deps.append(num)
    return deps


def _split_markdown_table_row(line: str) -> List[str]:
    stripped = line.strip()
    if stripped.startswith("|"):
        stripped = stripped[1:]
    if stripped.endswith("|"):
        stripped = stripped[:-1]
    return [cell.strip() for cell in stripped.split("|")]


def _is_table_separator_row(cells: Sequence[str]) -> bool:
    if not cells:
        return False
    return all(bool(re.fullmatch(r":?-{2,}:?", c.replace(" ", ""))) for c in cells)


def _find_table_columns(headers: Sequence[str]) -> Tuple[Optional[int], Optional[int], Optional[int], Optional[int]]:
    """テーブルヘッダーから (order_idx, issue_idx, scope_idx, depends_idx) を特定する。"""
    order_idx: Optional[int] = None
    issue_idx: Optional[int] = None
    scope_idx: Optional[int] = None
    depends_idx: Optional[int] = None

    for idx, raw in enumerate(headers):
        h = raw.lower()
        if order_idx is None and ("順序" in raw or "order" in h or "step" in h or h == "#"):
            order_idx = idx
        elif depends_idx is None and ("依存" in raw or "depend" in h):
            depends_idx = idx
        elif issue_idx is None and ("issue" in h or "子" in raw):
            issue_idx = idx
        elif scope_idx is None and ("スコープ" in raw or "内容" in raw or "概要" in raw or "scope" in h or "title" in h):
            scope_idx = idx

    return order_idx, issue_idx, scope_idx, depends_idx


def _parse_tables(body: str, epic_number: Optional[int]) -> Dict[int, ChildIssueSpec]:
    """Markdown テーブルから子 Issue と順序・依存情報を抽出する。"""
    lines = body.splitlines()
    specs: Dict[int, ChildIssueSpec] = {}
    i = 0
    while i < len(lines) - 2:
        line = lines[i].strip()
        next_line = lines[i + 1].strip()
        if "|" in line and "|" in next_line:
            headers = _split_markdown_table_row(line)
            sep_cells = _split_markdown_table_row(next_line)
            if len(headers) >= 2 and _is_table_separator_row(sep_cells):
                order_idx, issue_idx, scope_idx, depends_idx = _find_table_columns(headers)
                if issue_idx is not None:
                    row_pos = i + 2
                    auto_order = 1
                    while row_pos < len(lines) and "|" in lines[row_pos]:
                        cells = _split_markdown_table_row(lines[row_pos])
                        row_pos += 1
                        if len(cells) <= issue_idx:
                            continue
                        issue_cell = cells[issue_idx]
                        m_issue = ISSUE_NUM_RE.search(issue_cell)
                        if not m_issue:
                            continue
                        num = int(m_issue.group(1))
                        if epic_number is not None and num == epic_number:
                            continue

                        order_val = auto_order
                        if order_idx is not None and order_idx < len(cells):
                            m_ord = re.search(r"\d+", cells[order_idx])
                            if m_ord:
                                order_val = int(m_ord.group(0))
                        auto_order = max(auto_order + 1, order_val + 1)

                        scope = cells[scope_idx] if (scope_idx is not None and scope_idx < len(cells)) else ""
                        row_text = " ".join(cells)
                        explicit_deps = extract_explicit_depends(row_text, num, epic_number)
                        if depends_idx is not None and depends_idx < len(cells):
                            for m_dep in ISSUE_NUM_RE.finditer(cells[depends_idx]):
                                dep_num = int(m_dep.group(1))
                                if dep_num != num and dep_num != epic_number and dep_num not in explicit_deps:
                                    explicit_deps.append(dep_num)

                        if num not in specs:
                            specs[num] = ChildIssueSpec(
                                number=num,
                                order=order_val,
                                scope=scope,
                                explicit_depends=explicit_deps,
                            )
                        else:
                            existing = specs[num]
                            if not existing.scope and scope:
                                existing.scope = scope
                            for dep in explicit_deps:
                                if dep not in existing.explicit_depends:
                                    existing.explicit_depends.append(dep)
                    i = row_pos
                    continue
        i += 1
    return specs


def parse_epic_body(body: str, epic_number: Optional[int] = None) -> Tuple[List[ChildIssueSpec], List[str]]:
    """
    Epic 本文から子 Issue 一覧・順序・依存関係・並列グループを抽出する。

    Returns:
        (child_specs, warnings)
    """
    warnings: List[str] = []
    sanitized_body = _strip_code_fences(body)
    specs = _parse_tables(sanitized_body, epic_number)
    next_fallback_order = max((s.order for s in specs.values()), default=0) + 1

    # チェックリスト行（- [ ] #123 ... / - [x] #123 ...）を解析・マージ
    for line in sanitized_body.splitlines():
        m_chk = CHECKLIST_LINE_RE.match(line)
        if not m_chk:
            continue
        rest = m_chk.group("rest").strip()
        m_issue = ISSUE_NUM_RE.search(rest)
        if not m_issue:
            continue
        # 行の先頭付近（リンクや箇条書き直後）にある Issue 番号を子 Issue として採用
        # ※依存指定の "depends: #123" のみ含まれる行を誤抽出しないよう、depends より前に # が必要
        dep_block = DEPENDS_BLOCK_RE.search(rest)
        if dep_block and m_issue.start() >= dep_block.start():
            continue

        num = int(m_issue.group(1))
        if epic_number is not None and num == epic_number:
            continue

        checked = m_chk.group("checked").lower() == "x"
        explicit_deps = extract_explicit_depends(rest, num, epic_number)
        m_order = ORDER_TAG_RE.search(rest)
        tag_order = int(m_order.group(1)) if m_order else None

        # タイトルヒントの抽出（#番号 と depends / order タグを除去）
        cleaned = DEPENDS_BLOCK_RE.sub("", rest)
        cleaned = ORDER_TAG_RE.sub("", cleaned)
        cleaned = re.sub(rf"#?{num}\b", "", cleaned, count=1).strip(" -:：|()（）")

        if num in specs:
            spec = specs[num]
            spec.checked_in_epic = spec.checked_in_epic or checked
            if cleaned and not spec.title_hint:
                spec.title_hint = cleaned
            if tag_order is not None:
                spec.order = tag_order
            for dep in explicit_deps:
                if dep not in spec.explicit_depends:
                    spec.explicit_depends.append(dep)
        else:
            order_val = tag_order if tag_order is not None else next_fallback_order
            next_fallback_order = max(next_fallback_order + 1, order_val + 1)
            specs[num] = ChildIssueSpec(
                number=num,
                order=order_val,
                title_hint=cleaned,
                checked_in_epic=checked,
                explicit_depends=explicit_deps,
            )

    ordered = sorted(specs.values(), key=lambda s: (s.order, s.number))
    known_numbers = {s.number for s in ordered}

    # 順序番号ごとにグループ化して並列グループと暗黙依存を解決
    order_levels = sorted({s.order for s in ordered})
    level_to_nums: Dict[int, List[int]] = {
        lvl: [s.number for s in ordered if s.order == lvl] for lvl in order_levels
    }
    level_index = {lvl: idx for idx, lvl in enumerate(order_levels)}

    for spec in ordered:
        spec.parallel_group = spec.order
        if spec.explicit_depends:
            valid_deps = []
            for dep in spec.explicit_depends:
                if dep not in known_numbers:
                    warnings.append(
                        f"子 Issue #{spec.number} の依存先 #{dep} は Epic 本文の子 Issue 一覧に含まれていません（外部依存として保持）。"
                    )
                valid_deps.append(dep)
            spec.depends_on = valid_deps
        else:
            idx = level_index[spec.order]
            if idx > 0:
                prev_lvl = order_levels[idx - 1]
                spec.depends_on = list(level_to_nums[prev_lvl])
            else:
                spec.depends_on = []

    # 循環依存の簡易検出
    cycle_warnings = detect_dependency_cycles(ordered)
    warnings.extend(cycle_warnings)

    return ordered, warnings


def detect_dependency_cycles(specs: Sequence[ChildIssueSpec]) -> List[str]:
    """子 Issue 間の循環依存を検出し、警告メッセージ一覧を返す。"""
    graph: Dict[int, List[int]] = {s.number: list(s.depends_on) for s in specs}
    visiting: Set[int] = set()
    visited: Set[int] = set()
    warnings: List[str] = []

    def dfs(node: int, path: List[int]) -> None:
        visiting.add(node)
        for nxt in graph.get(node, []):
            if nxt not in graph:
                continue
            if nxt in visiting:
                cycle_start = path.index(nxt)
                cycle = " -> ".join(f"#{n}" for n in path[cycle_start:] + [nxt])
                msg = f"循環依存を検出しました: {cycle}"
                if msg not in warnings:
                    warnings.append(msg)
            elif nxt not in visited:
                dfs(nxt, path + [nxt])
        visiting.remove(node)
        visited.add(node)

    for s in specs:
        if s.number not in visited:
            dfs(s.number, [s.number])

    return warnings


def run_command(cmd: Sequence[str], timeout: int = 30) -> Tuple[int, str, str]:
    """外部コマンドを UTF-8 で安全に実行する。"""
    try:
        proc = subprocess.run(
            list(cmd),
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
        return proc.returncode, proc.stdout, proc.stderr
    except subprocess.TimeoutExpired:
        return 124, "", f"コマンドがタイムアウトしました ({timeout}s): {' '.join(cmd)}"
    except FileNotFoundError:
        return 127, "", f"コマンドが見つかりません: {cmd[0]}"


def detect_repo_slug(explicit_repo: Optional[str], timeout: int) -> Optional[str]:
    """対象リポジトリの owner/name を取得・検証する。"""
    if explicit_repo:
        slug = explicit_repo.strip()
        if not REPO_SLUG_RE.fullmatch(slug):
            raise RuntimeError(f"不正な --repo 形式です (owner/name 形式で指定してください): {explicit_repo}")
        return slug
    rc, out, _ = run_command(
        ["gh", "repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"],
        timeout=timeout,
    )
    if rc == 0 and out.strip():
        slug = out.strip()
        if REPO_SLUG_RE.fullmatch(slug):
            return slug
    return None


def fetch_epic_issue(epic_number: int, repo: Optional[str], timeout: int) -> Dict[str, Any]:
    """Epic Issue の本文・タイトル・状態を取得する。"""
    cmd = ["gh", "issue", "view", str(epic_number), "--json", "number,title,body,state,url"]
    if repo:
        cmd.extend(["--repo", repo])
    rc, out, err = run_command(cmd, timeout=timeout)
    if rc != 0:
        raise RuntimeError(f"Epic Issue #{epic_number} の取得に失敗しました: {err.strip() or out.strip()}")
    return json.loads(out)


def fetch_child_issues_live(
    issue_numbers: Sequence[int],
    repo: Optional[str],
    timeout: int,
    warnings: Optional[List[str]] = None,
) -> Dict[int, Dict[str, Any]]:
    """子 Issue（および外部依存 Issue）のライブ状態（OPEN/CLOSED, タイトル, ラベル, URL）を一括取得する。"""
    if not issue_numbers:
        return {}

    result: Dict[int, Dict[str, Any]] = {}
    repo_slug = detect_repo_slug(repo, timeout)
    if repo_slug:
        owner, name = repo_slug.split("/", 1)
        aliases = "\n".join(
            f"    i{n}: issue(number: {n}) {{ number title state url labels(first: 20) {{ nodes {{ name }} }} }}"
            for n in issue_numbers
        )
        query = (
            "query($owner: String!, $name: String!) {\n"
            "  repository(owner: $owner, name: $name) {\n"
            f"{aliases}\n"
            "  }\n"
            "}"
        )
        rc, out, err = run_command(
            [
                "gh",
                "api",
                "graphql",
                "-f",
                f"query={query}",
                "-f",
                f"owner={owner}",
                "-f",
                f"name={name}",
            ],
            timeout=timeout,
        )
        if rc == 0 and out.strip():
            try:
                payload = json.loads(out)
                gql_errors = payload.get("errors")
                if gql_errors and warnings is not None:
                    warnings.append(f"GraphQL Issue 取得で一部エラーが発生しました（個別取得へフォールバック）: {gql_errors[0].get('message', '')}")
                repo_data = (payload.get("data") or {}).get("repository") or {}
                for n in issue_numbers:
                    node = repo_data.get(f"i{n}")
                    if node:
                        labels = [
                            lbl.get("name", "")
                            for lbl in (node.get("labels", {}) or {}).get("nodes", [])
                            if isinstance(lbl, dict)
                        ]
                        result[n] = {
                            "number": node.get("number", n),
                            "title": node.get("title", ""),
                            "state": node.get("state", "OPEN"),
                            "url": node.get("url", ""),
                            "labels": labels,
                        }
                if len(result) == len(issue_numbers):
                    return result
            except json.JSONDecodeError:
                if warnings is not None:
                    warnings.append("GraphQL Issue 取得レスポンスの JSON 解析に失敗しました（個別取得へフォールバック）。")
        elif warnings is not None:
            warnings.append(f"GraphQL Issue 取得に失敗しました（個別取得へフォールバック）: {err.strip() or f'exit={rc}'}")

    # フォールバック: 個別 gh issue view
    failed_numbers: List[int] = []
    for n in issue_numbers:
        if n in result:
            continue
        cmd = ["gh", "issue", "view", str(n), "--json", "number,title,state,url,labels"]
        if repo:
            cmd.extend(["--repo", repo])
        rc, out, _ = run_command(cmd, timeout=timeout)
        if rc == 0 and out.strip():
            try:
                data = json.loads(out)
                labels = [
                    lbl.get("name", "")
                    for lbl in data.get("labels", [])
                    if isinstance(lbl, dict)
                ]
                result[n] = {
                    "number": data.get("number", n),
                    "title": data.get("title", ""),
                    "state": data.get("state", "OPEN"),
                    "url": data.get("url", ""),
                    "labels": labels,
                }
                continue
            except json.JSONDecodeError:
                pass
        failed_numbers.append(n)

    if failed_numbers:
        if len(failed_numbers) == len(issue_numbers):
            failed_str = ", ".join(f"#{n}" for n in failed_numbers)
            raise RuntimeError(f"対象 Issue ({failed_str}) のライブ状態取得にすべて失敗しました。")
        if warnings is not None:
            failed_str = ", ".join(f"#{n}" for n in failed_numbers)
            warnings.append(f"一部 Issue ({failed_str}) のライブ状態取得に失敗しました。")

    return result


def fetch_pull_requests(repo: Optional[str], timeout: int) -> List[Dict[str, Any]]:
    """リポジトリの PR 一覧を取得する（最新 100 件）。失敗時は RuntimeError を送出する。"""
    cmd = [
        "gh",
        "pr",
        "list",
        "--state",
        "all",
        "--limit",
        "100",
        "--json",
        "number,title,body,state,isDraft,headRefName,baseRefName,url,mergedAt,reviewDecision,statusCheckRollup,mergeable,mergeStateStatus",
    ]
    if repo:
        cmd.extend(["--repo", repo])
    rc, out, err = run_command(cmd, timeout=timeout)
    if rc != 0:
        raise RuntimeError(f"PR 一覧の取得に失敗しました: {err.strip() or out.strip() or f'exit={rc}'}")
    if not out.strip():
        return []
    try:
        data = json.loads(out)
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"PR 一覧レスポンスの JSON 解析に失敗しました: {exc}") from exc
    if not isinstance(data, list):
        raise RuntimeError("PR 一覧レスポンスが配列形式ではありません。")
    return data


def fetch_unresolved_review_threads(
    pr_numbers: Sequence[int],
    repo: Optional[str],
    timeout: int,
) -> Dict[int, int]:
    """OPEN PR ごとの未解決レビュースレッド数を GraphQL で取得する。失敗時は RuntimeError を送出する。"""
    if not pr_numbers:
        return {}
    repo_slug = detect_repo_slug(repo, timeout)
    if not repo_slug:
        raise RuntimeError("リポジトリ名 (owner/name) を特定できないため、未解決レビュースレッドを取得できません。")

    owner, name = repo_slug.split("/", 1)
    aliases = "\n".join(
        f"    p{n}: pullRequest(number: {n}) {{ reviewThreads(first: 100) {{ nodes {{ isResolved isOutdated }} }} }}"
        for n in pr_numbers
    )
    query = (
        "query($owner: String!, $name: String!) {\n"
        "  repository(owner: $owner, name: $name) {\n"
        f"{aliases}\n"
        "  }\n"
        "}"
    )
    rc, out, err = run_command(
        [
            "gh",
            "api",
            "graphql",
            "-f",
            f"query={query}",
            "-f",
            f"owner={owner}",
            "-f",
            f"name={name}",
        ],
        timeout=timeout,
    )
    if rc != 0 or not out.strip():
        raise RuntimeError(
            f"未解決レビュースレッドの取得に失敗しました: {err.strip() or out.strip() or f'exit={rc}'}"
        )

    try:
        payload = json.loads(out)
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"未解決レビュースレッド応答の JSON 解析に失敗しました: {exc}") from exc

    gql_errors = payload.get("errors")
    if gql_errors:
        msg = "; ".join(str(e.get("message", e)) for e in gql_errors if isinstance(e, dict)) or str(gql_errors)
        raise RuntimeError(f"GraphQL 未解決レビュースレッド取得でエラーが返されました: {msg}")

    repo_data = (payload.get("data") or {}).get("repository") or {}
    counts: Dict[int, int] = {}
    for n in pr_numbers:
        node = repo_data.get(f"p{n}")
        if node is None:
            raise RuntimeError(f"PR #{n} のレビュースレッド情報を取得できませんでした。")
        threads = (node.get("reviewThreads") or {}).get("nodes") or []
        unresolved = sum(
            1
            for t in threads
            if isinstance(t, dict) and not t.get("isResolved", False)
        )
        counts[n] = unresolved
    return counts


def fetch_local_branches(timeout: int = 10) -> Set[int]:
    """ローカルブランチ名から作業中の Issue 番号を抽出する。"""
    rc, out, _ = run_command(["git", "branch", "--format=%(refname:short)"], timeout=timeout)
    if rc != 0:
        return set()
    found: Set[int] = set()
    for line in out.splitlines():
        branch = line.strip()
        m = BRANCH_ISSUE_RE.search(branch)
        if m:
            found.add(int(m.group(1)))
    return found


def parse_worktree_porcelain(porcelain_output: str) -> Dict[int, Dict[str, str]]:
    """`git worktree list --porcelain` の出力から `.agent_worktrees/` 配下の稼働中ワークツリーを抽出する。"""
    worktrees: Dict[int, Dict[str, str]] = {}
    current_path: Optional[str] = None
    current_branch: str = ""

    def flush_entry() -> None:
        nonlocal current_path, current_branch
        if not current_path:
            return
        normalized = current_path.replace("\\", "/")
        if "/.agent_worktrees/" in normalized or normalized.startswith(".agent_worktrees/"):
            issue_num: Optional[int] = None
            m_path = WORKTREE_ISSUE_RE.search(normalized)
            if m_path:
                issue_num = int(m_path.group(1))
            elif current_branch:
                m_branch = BRANCH_ISSUE_RE.search(current_branch)
                if m_branch:
                    issue_num = int(m_branch.group(1))
            if issue_num is not None:
                worktrees[issue_num] = {
                    "path": current_path,
                    "branch": current_branch,
                }
        current_path = None
        current_branch = ""

    for raw_line in porcelain_output.splitlines():
        line = raw_line.strip()
        if not line:
            flush_entry()
            continue
        if line.startswith("worktree "):
            flush_entry()
            current_path = line[len("worktree "):].strip()
        elif line.startswith("branch "):
            ref = line[len("branch "):].strip()
            if ref.startswith("refs/heads/"):
                current_branch = ref[len("refs/heads/"):]
            else:
                current_branch = ref
        elif line == "detached":
            current_branch = ""

    flush_entry()
    return worktrees


def fetch_active_worktrees(timeout: int = 10) -> Dict[int, Dict[str, str]]:
    """`git worktree list --porcelain` を実行し、`.agent_worktrees/` 配下のワークツリー一覧を Issue 番号キーで返す。"""
    rc, out, _ = run_command(["git", "worktree", "list", "--porcelain"], timeout=timeout)
    if rc != 0 or not out.strip():
        return {}
    return parse_worktree_porcelain(out)


def collect_worktree_warnings(children: Sequence[Dict[str, Any]]) -> List[str]:
    """`merged` 状態にもかかわらず `.agent_worktrees/` にワークツリーが残留している子 Issue の警告を生成する。"""
    warnings: List[str] = []
    for c in children:
        if c.get("status") == "merged" and c.get("worktree"):
            wt = c["worktree"]
            wt_path = wt.get("path", "")
            num = c["number"]
            warnings.append(
                f"子 Issue #{num} は merged 状態ですがワークツリーが残留しています ({wt_path})。"
                f"`.\\tools\\worktree_manager.ps1 -Action remove -IssueNumber {num}` でクリーンアップしてください。"
            )
    return warnings


def match_pr_to_issue_numbers(pr: Dict[str, Any]) -> Set[int]:
    """PR のブランチ名・タイトル・本文から紐づく Issue 番号集合を抽出する。"""
    matched: Set[int] = set()
    head = str(pr.get("headRefName") or "")
    m_branch = BRANCH_ISSUE_RE.search(head)
    if m_branch:
        matched.add(int(m_branch.group(1)))

    title = str(pr.get("title") or "")
    body = str(pr.get("body") or "")
    for text in (title, body):
        for m in PR_ISSUE_REF_RE.finditer(text):
            matched.add(int(m.group(1)))
    return matched


def summarize_ci_state(status_check_rollup: Any) -> str:
    """PR の statusCheckRollup を 'PASS' / 'FAIL' / 'PENDING' / 'NONE' へ集約する。"""
    if not isinstance(status_check_rollup, list) or not status_check_rollup:
        return "NONE"
    has_pending = False
    for check in status_check_rollup:
        if not isinstance(check, dict):
            continue
        conclusion = str(check.get("conclusion") or "").upper()
        status = str(check.get("status") or "").upper()
        state = str(check.get("state") or "").upper()
        if conclusion in ("FAILURE", "TIMED_OUT", "CANCELLED", "ACTION_REQUIRED") or state in ("FAILURE", "ERROR"):
            return "FAIL"
        if status in ("IN_PROGRESS", "QUEUED", "PENDING") or state == "PENDING" or (not conclusion and not state):
            has_pending = True
    return "PENDING" if has_pending else "PASS"


def select_best_pr(prs: Sequence[Dict[str, Any]]) -> Optional[Dict[str, Any]]:
    """同一 Issue に紐づく複数 PR から最も優先度の高い代表 PR を選ぶ。"""
    if not prs:
        return None

    def priority(pr: Dict[str, Any]) -> Tuple[int, int]:
        state = str(pr.get("state") or "").upper()
        if state == "MERGED":
            rank = 3
        elif state == "OPEN":
            rank = 2
        else:
            rank = 1
        return (rank, int(pr.get("number") or 0))

    return max(prs, key=priority)


def classify_child_statuses(
    specs: Sequence[ChildIssueSpec],
    live_issues: Dict[int, Dict[str, Any]],
    prs_by_issue: Dict[int, List[Dict[str, Any]]],
    unresolved_threads_by_pr: Dict[int, int],
    local_branch_issues: Set[int],
    blocked_overrides: Dict[int, Dict[str, Any]],
    active_worktrees: Optional[Dict[int, Dict[str, str]]] = None,
) -> List[Dict[str, Any]]:
    """全子 Issue の状態を分類し、構造化レコード一覧を返す。"""
    preliminary: Dict[int, Dict[str, Any]] = {}
    worktrees_map = active_worktrees or {}

    # 1st pass: 依存関係以外の状態（merged / blocked / fixing_review / in_review / implementing）を判定
    for spec in specs:
        live = live_issues.get(spec.number, {})
        issue_state = str(live.get("state") or "OPEN").upper()
        labels = list(live.get("labels") or [])
        title = live.get("title") or spec.title_hint or spec.scope or f"Issue #{spec.number}"
        url = live.get("url") or ""

        pr = select_best_pr(prs_by_issue.get(spec.number, []))
        pr_info: Optional[Dict[str, Any]] = None
        if pr is not None:
            pr_num = int(pr.get("number") or 0)
            pr_state = str(pr.get("state") or "").upper()
            is_draft = bool(pr.get("isDraft", False))
            unresolved = unresolved_threads_by_pr.get(pr_num, 0)
            ci_state = summarize_ci_state(pr.get("statusCheckRollup"))
            mergeable = str(pr.get("mergeable") or "").upper()
            merge_state_status = str(pr.get("mergeStateStatus") or "").upper()
            is_open_ready = pr_state == "OPEN" and not is_draft
            has_conflict = is_open_ready and (
                mergeable == "CONFLICTING" or merge_state_status == "DIRTY"
            )
            needs_sync = is_open_ready and (
                has_conflict or merge_state_status == "BEHIND"
            )
            pr_info = {
                "number": pr_num,
                "title": pr.get("title", ""),
                "state": pr_state,
                "is_draft": is_draft,
                "head_branch": pr.get("headRefName", ""),
                "base_branch": pr.get("baseRefName", ""),
                "url": pr.get("url", ""),
                "review_decision": pr.get("reviewDecision") or "",
                "unresolved_review_threads": unresolved,
                "ci_state": ci_state,
                "mergeable": mergeable,
                "merge_state_status": merge_state_status,
                "has_conflict": has_conflict,
                "needs_sync": needs_sync,
            }

        wt_raw = worktrees_map.get(spec.number)
        worktree_info: Optional[Dict[str, str]] = (
            {"path": wt_raw["path"], "branch": wt_raw.get("branch", "")}
            if wt_raw is not None
            else None
        )

        has_blocked_label = any("blocked" in lbl.lower() for lbl in labels)
        override = blocked_overrides.get(spec.number)
        status = "pending"
        status_reason = ""

        if issue_state == "CLOSED" or (pr_info is not None and pr_info["state"] == "MERGED"):
            status = "merged"
            status_reason = "Issue クローズまたは PR マージ済み"
        elif override is not None or has_blocked_label:
            status = "blocked"
            status_reason = (override or {}).get("reason") or "blocked ラベルまたは停止条件により保留中"
        elif pr_info is not None and pr_info["state"] == "OPEN":
            if pr_info["is_draft"]:
                status = "implementing"
                status_reason = f"Draft PR #{pr_info['number']} 進行中"
            elif (
                pr_info["unresolved_review_threads"] > 0
                or pr_info["review_decision"] == "CHANGES_REQUESTED"
                or pr_info["ci_state"] == "FAIL"
            ):
                status = "fixing_review"
                reasons = []
                if pr_info["unresolved_review_threads"] > 0:
                    reasons.append(f"未解決コメント {pr_info['unresolved_review_threads']} 件")
                if pr_info["review_decision"] == "CHANGES_REQUESTED":
                    reasons.append("CHANGES_REQUESTED")
                if pr_info["ci_state"] == "FAIL":
                    reasons.append("CI 失敗")
                if pr_info["has_conflict"]:
                    reasons.append("コンフリクトあり")
                elif pr_info["needs_sync"]:
                    reasons.append("ベース追従同期必要(BEHIND)")
                status_reason = f"PR #{pr_info['number']}: " + " / ".join(reasons)
            else:
                status = "in_review"
                if pr_info["has_conflict"]:
                    status_reason = f"PR #{pr_info['number']} コンフリクトあり（sync_branch 必須）"
                elif pr_info["needs_sync"]:
                    status_reason = f"PR #{pr_info['number']} ベース追従同期が必要（BEHIND）"
                else:
                    status_reason = f"PR #{pr_info['number']} レビュー待ち／マージ確認待ち"
        elif spec.number in local_branch_issues or worktree_info is not None:
            status = "implementing"
            status_reason = "稼働中ワークツリーが存在" if worktree_info is not None else "ローカル作業ブランチが存在"

        preliminary[spec.number] = {
            "number": spec.number,
            "order": spec.order,
            "parallel_group": spec.parallel_group,
            "title": title,
            "scope": spec.scope,
            "url": url,
            "issue_state": issue_state,
            "checked_in_epic": spec.checked_in_epic,
            "depends_on": list(spec.depends_on),
            "status": status,
            "status_reason": status_reason,
            "blocked_reason": status_reason if status == "blocked" else "",
            "pr": pr_info,
            "worktree": worktree_info,
        }

    # 2nd pass: pending のうち未完了依存があるものを waiting_deps へ分類
    # Epic 外の外部依存 Issue についても GitHub 上で CLOSED であれば依存解消済みとみなす
    merged_set = {num for num, rec in preliminary.items() if rec["status"] == "merged"}
    for ext_num, ext_live in live_issues.items():
        if ext_num not in preliminary and str(ext_live.get("state") or "").upper() == "CLOSED":
            merged_set.add(ext_num)
    results: List[Dict[str, Any]] = []
    for spec in specs:
        rec = preliminary[spec.number]
        unmet_deps = [d for d in rec["depends_on"] if d not in merged_set]
        rec["unmet_depends"] = unmet_deps
        if rec["status"] == "pending":
            if unmet_deps:
                rec["status"] = "waiting_deps"
                dep_str = ", ".join(f"#{d}" for d in unmet_deps)
                rec["status_reason"] = f"先行 Issue ({dep_str}) のマージ待ち"
            else:
                rec["status_reason"] = "依存解消済み・着手可能"
        results.append(rec)

    return results


def compute_next_actions(children: Sequence[Dict[str, Any]], epic_number: int) -> Dict[str, Any]:
    """現在状態から次に実行すべきアクション一覧と Epic 停止判定を算出する。"""
    actions: List[Dict[str, Any]] = []

    blocked_items = [c for c in children if c["status"] == "blocked"]
    fixing_items = [c for c in children if c["status"] == "fixing_review"]
    in_review_items = [c for c in children if c["status"] == "in_review"]
    implementing_items = [c for c in children if c["status"] == "implementing"]
    pending_items = [c for c in children if c["status"] == "pending"]
    merged_items = [c for c in children if c["status"] == "merged"]

    # Epic レベルの停止条件チェック（連続または累計 2 件以上の blocked）
    halt_recommended = len(blocked_items) >= 2
    halt_reason = (
        f"blocked 状態の子 Issue が {len(blocked_items)} 件に達したため、"
        "Outer Loop を一時停止してユーザー判断を仰いでください。"
        if halt_recommended
        else ""
    )

    if halt_recommended:
        actions.append(
            {
                "priority": 0,
                "action": "halt_epic",
                "issues": [c["number"] for c in blocked_items],
                "command_hint": "",
                "summary": halt_reason,
            }
        )

    for c in fixing_items:
        pr_num = c["pr"]["number"] if c.get("pr") else None
        actions.append(
            {
                "priority": 1,
                "action": "fix_review",
                "issues": [c["number"]],
                "pr_number": pr_num,
                "command_hint": f"run-dev-loop Phase 5 / address-pr-review (Issue #{c['number']}, PR #{pr_num})",
                "summary": f"#{c['number']} (PR #{pr_num}) のレビュー指摘対応を実施する（{c['status_reason']}）",
            }
        )

    for c in in_review_items:
        pr_num = c["pr"]["number"] if c.get("pr") else None
        actions.append(
            {
                "priority": 2,
                "action": "review_pr",
                "issues": [c["number"]],
                "pr_number": pr_num,
                "command_hint": f"別セッション／独立サブエージェントで review-pr (PR #{pr_num})",
                "summary": f"#{c['number']} (PR #{pr_num}) を実装セッションとは独立したコンテキストでレビューする",
            }
        )

    for c in implementing_items:
        actions.append(
            {
                "priority": 3,
                "action": "resume_implementing",
                "issues": [c["number"]],
                "command_hint": f"run-dev-loop (Issue #{c['number']})",
                "summary": f"#{c['number']} の進行中実装を再開・完了させる（{c['status_reason']}）",
            }
        )

    if pending_items:
        # 最も早い並列グループに属する pending Issue 群を提案
        min_group = min(c["parallel_group"] for c in pending_items)
        ready_batch = [c for c in pending_items if c["parallel_group"] == min_group]
        issue_list_str = ", ".join(f"#{c['number']}" for c in ready_batch)
        parallel_note = (
            "（同一順序グループのため git worktree 等で並列実行可能）"
            if len(ready_batch) > 1
            else ""
        )
        actions.append(
            {
                "priority": 4,
                "action": "start_issue",
                "issues": [c["number"] for c in ready_batch],
                "parallel": len(ready_batch) > 1,
                "command_hint": f"run-dev-loop (Issue {issue_list_str})",
                "summary": f"{issue_list_str} の Inner Loop を起動する{parallel_note}",
            }
        )

    if blocked_items and not halt_recommended:
        for c in blocked_items:
            actions.append(
                {
                    "priority": 5,
                    "action": "escalate_blocked",
                    "issues": [c["number"]],
                    "command_hint": f"python tools/epic_tracker.py --epic {epic_number} --unblock {c['number']}",
                    "summary": f"#{c['number']} がブロック中のため確認・解消を依頼する（理由: {c['blocked_reason']}）",
                }
            )

    if children and len(merged_items) == len(children):
        actions.append(
            {
                "priority": 1,
                "action": "complete_epic",
                "issues": [],
                "command_hint": ".\\tools\\verify_loop.ps1 -Full -JsonOut \".agent_logs\\verify_epic_final.json\"",
                "summary": f"全子 Issue ({len(children)} 件) がマージ済みです。最新 develop で統合検証を実行し Epic #{epic_number} を完了確認してください。",
            }
        )
    elif not actions:
        actions.append(
            {
                "priority": 9,
                "action": "wait",
                "issues": [],
                "command_hint": "",
                "summary": "現在即時実行可能なアクションはありません（依存先の完了待ち）。",
            }
        )

    actions.sort(key=lambda a: a["priority"])
    return {
        "epic_halt_recommended": halt_recommended,
        "epic_halt_reason": halt_reason,
        "recommended_actions": actions,
        "primary_action": actions[0] if actions else None,
    }


def load_existing_state(state_path: Path) -> Optional[Dict[str, Any]]:
    """既存の状態スナップショット JSON を読み込む。"""
    if not state_path.exists():
        return None
    try:
        with state_path.open("r", encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else None
    except (OSError, json.JSONDecodeError):
        return None


def extract_blocked_overrides(existing_state: Optional[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
    """既存状態ファイルからローカルの blocked 指定を引き継ぐ。"""
    overrides: Dict[int, Dict[str, Any]] = {}
    if not existing_state:
        return overrides
    raw_overrides = existing_state.get("blocked_overrides")
    if isinstance(raw_overrides, dict):
        for k, v in raw_overrides.items():
            try:
                num = int(k)
            except ValueError:
                continue
            if isinstance(v, dict):
                overrides[num] = v
    return overrides


def save_state(state_path: Path, state: Dict[str, Any]) -> None:
    """状態スナップショットを UTF-8 JSON として保存する。"""
    state_path.parent.mkdir(parents=True, exist_ok=True)
    with state_path.open("w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)
        f.write("\n")


def format_human_report(state: Dict[str, Any], next_only: bool) -> str:
    """人間・エージェント双方に読みやすいテキストレポートを整形する。"""
    lines: List[str] = []
    epic = state["epic"]
    summary = state["summary"]
    next_info = state["next"]

    lines.append("=" * 72)
    lines.append(f"Epic #{epic['number']}: {epic['title']}")
    lines.append(f"状態: {epic['state']} | 更新日時: {state['updated_at']}")
    lines.append(
        f"進捗: merged {summary['merged']}/{summary['total']} "
        f"(pending={summary['pending']}, waiting_deps={summary['waiting_deps']}, "
        f"implementing={summary['implementing']}, in_review={summary['in_review']}, "
        f"fixing_review={summary['fixing_review']}, blocked={summary['blocked']})"
    )
    lines.append("=" * 72)

    if not next_only:
        lines.append("")
        lines.append("【子 Issue 一覧】")
        for c in state["children"]:
            deps = ", ".join(f"#{d}" for d in c["depends_on"]) if c["depends_on"] else "-"
            pr_str = f"PR #{c['pr']['number']}({c['pr']['state']})" if c.get("pr") else "-"
            lines.append(
                f"  [順:{c['order']}] #{c['number']:<4} | {c['status']:<13} | 依存:{deps:<10} | {pr_str:<16} | {c['title']}"
            )
            if c.get("status_reason"):
                lines.append(f"           └─ {c['status_reason']}")

    if state.get("warnings"):
        lines.append("")
        lines.append("【警告】")
        for w in state["warnings"]:
            lines.append(f"  - {w}")

    lines.append("")
    lines.append("【次に実行すべきアクション】")
    if next_info.get("epic_halt_recommended"):
        lines.append(f"  [HALT] {next_info['epic_halt_reason']}")
    for idx, act in enumerate(next_info.get("recommended_actions", []), start=1):
        lines.append(f"  {idx}. [{act['action']}] {act['summary']}")
        if act.get("command_hint"):
            lines.append(f"     ヒント: {act['command_hint']}")

    if state.get("state_file"):
        lines.append("")
        lines.append(f"状態保存先: {state['state_file']}")

    return "\n".join(lines)


def build_state(
    epic_number: int,
    state_path: Path,
    repo: Optional[str],
    timeout: int,
    mark_blocked: Optional[int],
    unblock: Optional[int],
    reason: str,
) -> Dict[str, Any]:
    """GitHub からライブ情報を取得し、完全な Epic 状態辞書を構築する。"""
    existing_state = load_existing_state(state_path)
    blocked_overrides = extract_blocked_overrides(existing_state)

    now_iso = datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")
    if mark_blocked is not None:
        blocked_overrides[mark_blocked] = {
            "reason": reason or "停止条件超過・人間判断待ち",
            "marked_at": now_iso,
        }
    if unblock is not None and unblock in blocked_overrides:
        del blocked_overrides[unblock]

    epic_data = fetch_epic_issue(epic_number, repo, timeout)
    specs, warnings = parse_epic_body(epic_data.get("body") or "", epic_number=epic_number)
    child_numbers = [s.number for s in specs]
    child_set = set(child_numbers)
    external_dep_numbers = sorted({d for s in specs for d in s.depends_on if d not in child_set})
    all_issue_numbers = list(dict.fromkeys([*child_numbers, *external_dep_numbers]))

    live_issues = fetch_child_issues_live(all_issue_numbers, repo, timeout, warnings=warnings)
    all_prs = fetch_pull_requests(repo, timeout)
    prs_by_issue: Dict[int, List[Dict[str, Any]]] = {n: [] for n in child_numbers}
    open_pr_numbers: List[int] = []
    for pr in all_prs:
        matched = match_pr_to_issue_numbers(pr)
        for num in matched:
            if num in prs_by_issue:
                prs_by_issue[num].append(pr)
                if str(pr.get("state") or "").upper() == "OPEN":
                    pr_num = int(pr.get("number") or 0)
                    if pr_num and pr_num not in open_pr_numbers:
                        open_pr_numbers.append(pr_num)

    unresolved_by_pr = fetch_unresolved_review_threads(open_pr_numbers, repo, timeout)
    local_branch_issues = fetch_local_branches(timeout=min(timeout, 15))
    active_worktrees = fetch_active_worktrees(timeout=min(timeout, 15))

    children = classify_child_statuses(
        specs=specs,
        live_issues=live_issues,
        prs_by_issue=prs_by_issue,
        unresolved_threads_by_pr=unresolved_by_pr,
        local_branch_issues=local_branch_issues,
        blocked_overrides=blocked_overrides,
        active_worktrees=active_worktrees,
    )
    warnings.extend(collect_worktree_warnings(children))

    # マージ済みになった Issue の古い blocked_override は自動クリア
    for c in children:
        if c["status"] == "merged" and c["number"] in blocked_overrides:
            del blocked_overrides[c["number"]]

    summary: Dict[str, int] = {"total": len(children)}
    for st in VALID_STATUSES:
        summary[st] = sum(1 for c in children if c["status"] == st)

    next_info = compute_next_actions(children, epic_number=epic_number)

    try:
        rel_state_file = str(state_path.relative_to(REPO_ROOT)).replace("\\", "/")
    except ValueError:
        rel_state_file = str(state_path)

    return {
        "schema_version": 1,
        "updated_at": now_iso,
        "state_file": rel_state_file,
        "epic": {
            "number": int(epic_data.get("number", epic_number)),
            "title": epic_data.get("title", ""),
            "state": epic_data.get("state", "OPEN"),
            "url": epic_data.get("url", ""),
        },
        "summary": summary,
        "children": children,
        "next": next_info,
        "blocked_overrides": {str(k): v for k, v in blocked_overrides.items()},
        "warnings": warnings,
    }


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Epic Issue の子 Issue・依存関係・進捗状態を解析し、次アクションを提案します。"
    )
    parser.add_argument("--epic", type=int, required=True, help="対象 Epic Issue 番号")
    parser.add_argument("--next", action="store_true", dest="next_only", help="次に実行すべきアクションを中心に表示する")
    parser.add_argument("--json", action="store_true", dest="as_json", help="結果を JSON 形式で出力する")
    parser.add_argument("--state-file", type=str, default=None, help="状態スナップショット JSON のパス")
    parser.add_argument("--no-save", action="store_true", help="状態スナップショットを保存しない")
    parser.add_argument("--offline", action="store_true", help="GitHub に問い合わせず既存の状態ファイルから読み込む")
    parser.add_argument("--mark-blocked", type=int, default=None, help="指定した子 Issue をローカルで blocked にする")
    parser.add_argument("--unblock", type=int, default=None, help="指定した子 Issue のローカル blocked を解除する")
    parser.add_argument("--reason", type=str, default="", help="--mark-blocked 時の理由")
    parser.add_argument("--repo", type=str, default=None, help="対象リポジトリ (owner/name)")
    parser.add_argument("--timeout", type=int, default=30, help="外部コマンドのタイムアウト秒数 (既定: 30)")
    return parser.parse_args(argv)


def apply_offline_block_updates(
    state: Dict[str, Any],
    mark_blocked: Optional[int],
    unblock: Optional[int],
    reason: str,
) -> Dict[str, Any]:
    """オフライン状態スナップショットに対してローカルの blocked 記録・解除を反映する。"""
    if mark_blocked is None and unblock is None:
        return state

    now_iso = datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")
    blocked_overrides = extract_blocked_overrides(state)
    if mark_blocked is not None:
        blocked_overrides[mark_blocked] = {
            "reason": reason or "停止条件超過・人間判断待ち",
            "marked_at": now_iso,
        }
    if unblock is not None and unblock in blocked_overrides:
        del blocked_overrides[unblock]

    children = state.get("children") or []
    for c in children:
        num = c.get("number")
        if c.get("status") == "merged":
            if num in blocked_overrides:
                del blocked_overrides[num]
            continue
        if num in blocked_overrides:
            blk_reason = blocked_overrides[num].get("reason") or "停止条件超過・人間判断待ち"
            c["status"] = "blocked"
            c["status_reason"] = blk_reason
            c["blocked_reason"] = blk_reason
        elif c.get("status") == "blocked" and num == unblock:
            c["blocked_reason"] = ""
            pr_info = c.get("pr")
            unmet_deps = c.get("unmet_depends") or []
            if pr_info and pr_info.get("state") == "OPEN":
                if pr_info.get("is_draft"):
                    c["status"] = "implementing"
                    c["status_reason"] = f"Draft PR #{pr_info['number']} 進行中"
                elif (
                    pr_info.get("unresolved_review_threads", 0) > 0
                    or pr_info.get("review_decision") == "CHANGES_REQUESTED"
                    or pr_info.get("ci_state") == "FAIL"
                ):
                    c["status"] = "fixing_review"
                    c["status_reason"] = f"PR #{pr_info['number']} レビュー指摘対応待ち"
                else:
                    c["status"] = "in_review"
                    c["status_reason"] = f"PR #{pr_info['number']} レビュー待ち／マージ確認待ち"
            elif unmet_deps:
                c["status"] = "waiting_deps"
                dep_str = ", ".join(f"#{d}" for d in unmet_deps)
                c["status_reason"] = f"先行 Issue ({dep_str}) のマージ待ち"
            else:
                c["status"] = "pending"
                c["status_reason"] = "依存解消済み・着手可能"

    summary: Dict[str, int] = {"total": len(children)}
    for st in VALID_STATUSES:
        summary[st] = sum(1 for c in children if c.get("status") == st)

    epic_num = int((state.get("epic") or {}).get("number") or 0)
    state["updated_at"] = now_iso
    state["summary"] = summary
    state["next"] = compute_next_actions(children, epic_number=epic_num)
    state["blocked_overrides"] = {str(k): v for k, v in blocked_overrides.items()}
    return state


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)
    if args.mark_blocked is not None and args.unblock is not None and args.mark_blocked == args.unblock:
        print(
            f"[ERROR] --mark-blocked と --unblock に同一の Issue 番号 (#{args.mark_blocked}) を同時指定することはできません。",
            file=sys.stderr,
        )
        return 1

    state_path = (
        Path(args.state_file)
        if args.state_file
        else DEFAULT_LOG_DIR / f"epic_{args.epic}_state.json"
    )
    if not state_path.is_absolute():
        state_path = REPO_ROOT / state_path

    if args.offline:
        state = load_existing_state(state_path)
        if state is None:
            print(f"[ERROR] オフライン読み込み対象の状態ファイルが見つかりません: {state_path}", file=sys.stderr)
            return 1
        if args.mark_blocked is not None or args.unblock is not None:
            state = apply_offline_block_updates(
                state=state,
                mark_blocked=args.mark_blocked,
                unblock=args.unblock,
                reason=args.reason,
            )
            if not args.no_save:
                save_state(state_path, state)
    else:
        try:
            state = build_state(
                epic_number=args.epic,
                state_path=state_path,
                repo=args.repo,
                timeout=args.timeout,
                mark_blocked=args.mark_blocked,
                unblock=args.unblock,
                reason=args.reason,
            )
        except RuntimeError as exc:
            print(f"[ERROR] {exc}", file=sys.stderr)
            return 1

        if not args.no_save:
            save_state(state_path, state)

    if args.as_json:
        output_obj = state["next"] if args.next_only else state
        print(json.dumps(output_obj, ensure_ascii=False, indent=2))
    else:
        print(format_human_report(state, next_only=args.next_only))

    return 0


if __name__ == "__main__":
    sys.exit(main())
