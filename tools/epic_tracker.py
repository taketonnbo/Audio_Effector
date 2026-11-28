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
  --integration-branch BRANCH
                      Epic 統合ブランチ名（例: epic/<Epic番号>-<説明>、省略時は状態ファイルまたはローカル/リモートから自動検出）
  --next              次に実行すべきアクションを優先表示する
  --json              結果を構造化 JSON 形式で標準出力へ出力する
  --state-file PATH   状態スナップショット保存先（既定: .agent_logs/epic_<番号>_state.json）
  --no-save           状態スナップショットをファイルへ保存しない
  --offline           GitHub CLI を呼び出さず既存の状態スナップショットから読み込む
  --mark-blocked NUM  指定した子 Issue をローカル状態で blocked に設定する（GitHub 非書き込み）
  --unblock NUM       指定した子 Issue のローカル blocked 設定を解除する
  --reason TEXT       --mark-blocked 時のブロック理由
  --repo OWNER/NAME   対象リポジトリを明示指定する（省略時はカレントリポジトリ）
  --max-parallel N    同時稼働させる最大並列スロット数（既定: 3、最小: 1）
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
EPIC_INTEGRATION_BRANCH_RE = re.compile(r"^epic/(\d+)(?:[-_/].+)?$", re.IGNORECASE)
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


def normalize_branch_name(branch: Optional[str]) -> Optional[str]:
    """ブランチ名から前後空白および refs/heads/・origin/ プレフィックスを除去する。"""
    if branch is None:
        return None
    cleaned = str(branch).strip()
    if cleaned.startswith("refs/heads/"):
        cleaned = cleaned[len("refs/heads/"):]
    elif cleaned.startswith("refs/remotes/origin/"):
        cleaned = cleaned[len("refs/remotes/origin/"):]
    elif cleaned.startswith("origin/"):
        cleaned = cleaned[len("origin/"):]
    return cleaned or None


def is_epic_integration_branch_name(branch: str, epic_number: Optional[int] = None) -> bool:
    """ブランチ名が Epic 統合ブランチ命名規則（epic/<番号>-*）に一致するか判定する。"""
    normalized = normalize_branch_name(branch)
    if not normalized:
        return False
    m = EPIC_INTEGRATION_BRANCH_RE.fullmatch(normalized)
    if not m:
        return False
    if epic_number is not None:
        return int(m.group(1)) == epic_number
    return True


def fetch_local_branch_names(timeout: int = 10) -> List[str]:
    """ローカルブランチ名の一覧を取得する。"""
    rc, out, _ = run_command(["git", "branch", "--format=%(refname:short)"], timeout=timeout)
    if rc != 0:
        return []
    return [line.strip() for line in out.splitlines() if line.strip()]


def fetch_remote_branch_names(timeout: int = 10) -> List[str]:
    """origin リモート追跡ブランチ名の一覧（origin/ 除去済み）を取得する。"""
    rc, out, _ = run_command(["git", "branch", "-r", "--format=%(refname:short)"], timeout=timeout)
    if rc != 0:
        return []
    names: List[str] = []
    for line in out.splitlines():
        raw = line.strip()
        if not raw or raw == "origin/HEAD" or " -> " in raw:
            continue
        if raw.startswith("origin/"):
            short = raw[len("origin/"):]
            if short and short != "HEAD":
                names.append(short)
    return names


def fetch_local_branches(timeout: int = 10) -> Set[int]:
    """ローカルブランチ名から作業中の子 Issue 番号を抽出する（epic/* 統合ブランチは除外）。"""
    found: Set[int] = set()
    for branch in fetch_local_branch_names(timeout=timeout):
        if branch.lower().startswith("epic/"):
            continue
        m = BRANCH_ISSUE_RE.search(branch)
        if m:
            found.add(int(m.group(1)))
    return found


def extract_integration_branch_from_state(existing_state: Optional[Dict[str, Any]]) -> Optional[str]:
    """既存状態ファイルから統合ブランチ名を引き継ぐ。"""
    if not isinstance(existing_state, dict):
        return None
    raw = existing_state.get("integration_branch")
    if isinstance(raw, dict):
        return normalize_branch_name(raw.get("name"))
    if isinstance(raw, str):
        return normalize_branch_name(raw)
    return None


def resolve_integration_branch_name(
    epic_number: int,
    explicit_branch: Optional[str],
    existing_state: Optional[Dict[str, Any]],
    local_branches: Sequence[str],
    remote_branches: Sequence[str],
    all_prs: Sequence[Dict[str, Any]],
    warnings: Optional[List[str]] = None,
) -> Optional[str]:
    """Epic 統合ブランチ名を優先順位（明示指定 > 状態引き継ぎ > ローカル/リモート/PR 自動検出）で解決する。"""
    normalized_explicit = normalize_branch_name(explicit_branch)
    if normalized_explicit:
        return normalized_explicit

    from_state = extract_integration_branch_from_state(existing_state)
    if from_state:
        return from_state

    local_set = set(local_branches)
    remote_set = set(remote_branches)
    pr_candidates: Dict[str, int] = {}
    for pr in all_prs:
        head = normalize_branch_name(pr.get("headRefName"))
        base = normalize_branch_name(pr.get("baseRefName"))
        if head and base == "develop" and is_epic_integration_branch_name(head, epic_number=epic_number):
            pr_state = str(pr.get("state") or "").upper()
            rank = 3 if pr_state == "OPEN" else (2 if pr_state == "MERGED" else 1)
            pr_candidates[head] = max(pr_candidates.get(head, 0), rank)

    candidates: Set[str] = set()
    for b in (*local_branches, *remote_branches, *pr_candidates.keys()):
        if is_epic_integration_branch_name(b, epic_number=epic_number):
            candidates.add(b)

    if not candidates:
        return None

    sorted_candidates = sorted(
        candidates,
        key=lambda b: (
            -pr_candidates.get(b, 0),
            0 if (b in local_set and b in remote_set) else (1 if b in remote_set else 2),
            b,
        ),
    )
    if len(sorted_candidates) > 1 and warnings is not None:
        cand_list = ", ".join(sorted_candidates)
        warnings.append(
            f"Epic #{epic_number} に一致する統合ブランチ候補が複数見つかりました ({cand_list})。"
            f"`{sorted_candidates[0]}` を採用しました（明示指定する場合は `--integration-branch` を使用してください）。"
        )
    return sorted_candidates[0]


def compute_branch_ahead_behind(
    branch_ref: str,
    base_ref: str,
    timeout: int = 10,
) -> Tuple[Optional[int], Optional[int]]:
    """branch_ref の base_ref に対する (ahead, behind) コミット数を取得する。"""
    rc, out, _ = run_command(
        ["git", "rev-list", "--left-right", "--count", f"{branch_ref}...{base_ref}"],
        timeout=timeout,
    )
    if rc != 0 or not out.strip():
        return None, None
    parts = out.strip().split()
    if len(parts) < 2:
        return None, None
    try:
        return int(parts[0]), int(parts[1])
    except ValueError:
        return None, None


def inspect_integration_branch(
    branch_name: Optional[str],
    local_branches: Sequence[str],
    remote_branches: Sequence[str],
    timeout: int = 10,
) -> Dict[str, Any]:
    """Epic 統合ブランチのローカル/リモート存在状態および origin/develop との差分件数を調査する。"""
    if not branch_name:
        return {
            "name": None,
            "exists_local": False,
            "exists_remote": False,
            "ahead_of_develop": None,
            "behind_develop": None,
        }

    local_set = set(local_branches)
    remote_set = set(remote_branches)
    exists_local = branch_name in local_set
    exists_remote = branch_name in remote_set

    ahead: Optional[int] = None
    behind: Optional[int] = None
    target_ref = f"origin/{branch_name}" if exists_remote else (branch_name if exists_local else None)
    develop_ref = "origin/develop" if "develop" in remote_set else ("develop" if "develop" in local_set else None)
    if target_ref and develop_ref:
        ahead, behind = compute_branch_ahead_behind(target_ref, develop_ref, timeout=timeout)

    return {
        "name": branch_name,
        "exists_local": exists_local,
        "exists_remote": exists_remote,
        "ahead_of_develop": ahead,
        "behind_develop": behind,
    }


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
        rel_part: Optional[str] = None
        if "/.agent_worktrees/" in normalized:
            rel_part = normalized.split("/.agent_worktrees/", 1)[1]
        elif normalized.startswith(".agent_worktrees/"):
            rel_part = normalized[len(".agent_worktrees/"):]

        if rel_part is not None:
            issue_num: Optional[int] = None
            m_path = WORKTREE_ISSUE_RE.search(rel_part)
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


def collect_base_branch_warnings(
    children: Sequence[Dict[str, Any]],
    integration_branch_name: Optional[str],
) -> List[str]:
    """子 Issue の OPEN PR の base ブランチが Epic 統合ブランチと一致しない場合の警告を生成する。"""
    if not integration_branch_name:
        return []
    warnings: List[str] = []
    for c in children:
        pr = c.get("pr")
        if not pr or str(pr.get("state") or "").upper() != "OPEN":
            continue
        actual_base = normalize_branch_name(pr.get("base_branch"))
        if actual_base and actual_base != integration_branch_name:
            pr_num = pr.get("number")
            num = c["number"]
            warnings.append(
                f"子 Issue #{num} の PR #{pr_num} のターゲットブランチ (base: {actual_base}) が "
                f"Epic 統合ブランチ ({integration_branch_name}) と一致していません。"
                f"`gh pr edit {pr_num} --base {integration_branch_name}` で修正してください。"
            )
    return warnings


def is_integration_pr(
    pr: Dict[str, Any],
    integration_branch: Optional[str] = None,
    epic_number: Optional[int] = None,
) -> bool:
    """PR が Epic 統合 PR（<統合ブランチ> -> develop）かどうかを判定する。"""
    head = normalize_branch_name(pr.get("headRefName"))
    base = normalize_branch_name(pr.get("baseRefName"))
    if not head or base != "develop":
        return False
    normalized_int = normalize_branch_name(integration_branch)
    if normalized_int is not None:
        return head == normalized_int
    return is_epic_integration_branch_name(head, epic_number=epic_number)


def match_pr_to_issue_numbers(
    pr: Dict[str, Any],
    integration_branch: Optional[str] = None,
    epic_number: Optional[int] = None,
) -> Set[int]:
    """PR のブランチ名・タイトル・本文から紐づく子 Issue 番号集合を抽出する（Epic 統合 PR は除外）。"""
    if is_integration_pr(pr, integration_branch=integration_branch, epic_number=epic_number):
        return set()
    if is_integration_pr(pr, integration_branch=None, epic_number=None):
        return set()

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


def build_pr_info(
    pr: Dict[str, Any],
    unresolved_threads_by_pr: Dict[int, int],
) -> Dict[str, Any]:
    """GitHub PR 生データからトラッカー標準形式の PR 情報辞書を構築する。"""
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
    return {
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
        pr_info: Optional[Dict[str, Any]] = (
            build_pr_info(pr, unresolved_threads_by_pr) if pr is not None else None
        )

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


def compute_next_actions(
    children: Sequence[Dict[str, Any]],
    epic_number: int,
    max_parallel: int = 3,
    integration_branch: Optional[Dict[str, Any]] = None,
    integration_pr: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """現在状態から次に実行すべきアクション一覧・並列スロット状況・Epic 停止判定を算出する。"""
    max_parallel = max(1, int(max_parallel))
    actions: List[Dict[str, Any]] = []

    use_integration_flow = integration_branch is not None
    int_branch_name = normalize_branch_name((integration_branch or {}).get("name")) if use_integration_flow else None
    int_exists_local = bool((integration_branch or {}).get("exists_local", False)) if use_integration_flow else False
    int_exists_remote = bool((integration_branch or {}).get("exists_remote", False)) if use_integration_flow else False
    int_behind_develop = (integration_branch or {}).get("behind_develop") if use_integration_flow else None
    int_pr_state = str((integration_pr or {}).get("state") or "").upper() if integration_pr else ""
    target_base_label = int_branch_name or "develop"

    blocked_items = [c for c in children if c["status"] == "blocked"]
    fixing_items = [c for c in children if c["status"] == "fixing_review"]
    in_review_items = [c for c in children if c["status"] == "in_review"]
    implementing_items = [c for c in children if c["status"] == "implementing"]
    pending_items = [c for c in children if c["status"] == "pending"]
    merged_items = [c for c in children if c["status"] == "merged"]
    all_children_merged = bool(children and len(merged_items) == len(children))

    # 現在稼働中の並列スロット数を算出（implementing / in_review / fixing_review または未完了ワークツリー確保済み）
    active_items = [
        c
        for c in children
        if c["status"] in ("implementing", "in_review", "fixing_review")
        or (c["status"] not in ("merged", "blocked") and c.get("worktree") is not None)
    ]
    active_slots = len(active_items)
    available_slots = max(0, max_parallel - active_slots)

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

    # 統合ブランチ未作成／未 Push のチェック（Epic 完了済みかつ統合 PR マージ済みの場合は除外）
    integration_branch_missing = (
        use_integration_flow
        and not (all_children_merged and int_pr_state == "MERGED")
        and (not int_branch_name or not int_exists_remote)
    )
    if integration_branch_missing:
        suggested_name = int_branch_name or f"epic/{epic_number}-<slug>"
        if int_branch_name and int_exists_local and not int_exists_remote:
            cmd_hint = f"git push -u origin {suggested_name}"
            summary = (
                f"Epic #{epic_number} の統合ブランチ ({suggested_name}) がリモート (origin) へ未 Push です。"
                "リモートへ Push してから子 Issue 作業・PR 作成を進めてください。"
            )
        else:
            cmd_hint = (
                f"git checkout develop; if ($?) {{ git pull origin develop }}; "
                f"if ($?) {{ git checkout -b {suggested_name} }}; "
                f"if ($?) {{ git push -u origin {suggested_name} }}"
            )
            summary = (
                f"Epic #{epic_number} の統合ブランチ ({suggested_name}) が未作成です。"
                "最新 develop から作成して origin へ Push してください。"
            )
        actions.append(
            {
                "priority": 1,
                "action": "create_integration_branch",
                "issues": [],
                "branch": suggested_name,
                "command_hint": cmd_hint,
                "summary": summary,
            }
        )

    # 子 Issue OPEN PR のターゲットブランチ不一致（例: 誤って develop 向き PR）を優先検知
    base_mismatch_issues: Set[int] = set()
    if int_branch_name:
        for c in children:
            if c["status"] in ("merged", "blocked"):
                continue
            pr = c.get("pr")
            if not pr:
                continue
            pr_state = str(pr.get("state") or "OPEN").upper()
            if pr_state != "OPEN":
                continue
            actual_base = normalize_branch_name(pr.get("base_branch"))
            if actual_base and actual_base != int_branch_name:
                base_mismatch_issues.add(c["number"])
                pr_num = pr.get("number")
                actions.append(
                    {
                        "priority": 1,
                        "action": "fix_pr_base",
                        "issues": [c["number"]],
                        "pr_number": pr_num,
                        "actual_base": actual_base,
                        "expected_base": int_branch_name,
                        "command_hint": f'gh pr edit {pr_num} --base "{int_branch_name}"',
                        "summary": (
                            f"#{c['number']} (PR #{pr_num}) のターゲットブランチ (base: {actual_base}) が "
                            f"Epic 統合ブランチ ({int_branch_name}) と一致していないため、Base ブランチを修正する"
                        ),
                    }
                )

    # コンフリクト / ベース追従同期が必要な OPEN 非 Draft PR を検知（コンフリクトありを高優先）
    sync_target_issues: Set[int] = set()
    for c in children:
        if c["status"] in ("merged", "blocked") or c["number"] in base_mismatch_issues:
            continue
        pr = c.get("pr")
        if not pr:
            continue
        pr_state = str(pr.get("state") or "OPEN").upper()
        if pr_state != "OPEN" or pr.get("is_draft"):
            continue
        has_conflict = bool(pr.get("has_conflict", False))
        needs_sync = bool(pr.get("needs_sync", False)) or has_conflict
        if not needs_sync:
            continue
        sync_target_issues.add(c["number"])
        pr_num = pr.get("number")
        wt = c.get("worktree")
        base_flag = f"-BaseBranch \"{int_branch_name}\" " if int_branch_name else ""
        if wt and wt.get("path"):
            cmd_hint = f".\\tools\\sync_branch.ps1 {base_flag}-WorkingDirectory \"{wt['path']}\" -Json"
        else:
            cmd_hint = f".\\tools\\sync_branch.ps1 {base_flag}-Json"
        if has_conflict:
            summary = f"#{c['number']} (PR #{pr_num}) にコンフリクトが発生しているため、sync_branch で最新 {target_base_label} を取り込み解消する"
            priority = 1
        else:
            summary = f"#{c['number']} (PR #{pr_num}) がベースブランチ ({target_base_label}) から遅れている（BEHIND）ため、sync_branch で追従同期する"
            priority = 2
        actions.append(
            {
                "priority": priority,
                "action": "sync_branch",
                "issues": [c["number"]],
                "pr_number": pr_num,
                "has_conflict": has_conflict,
                "command_hint": cmd_hint,
                "summary": summary,
            }
        )

    for c in fixing_items:
        pr_num = c["pr"]["number"] if c.get("pr") else None
        base_note = f", -BaseBranch \"{int_branch_name}\"" if int_branch_name else ""
        actions.append(
            {
                "priority": 2,
                "action": "fix_review",
                "issues": [c["number"]],
                "pr_number": pr_num,
                "command_hint": f"run-dev-loop Phase 5 / address-pr-review (Issue #{c['number']}, PR #{pr_num}{base_note})",
                "summary": f"#{c['number']} (PR #{pr_num}) のレビュー指摘対応を実施する（{c['status_reason']}）",
            }
        )

    # 直列マージキュー候補（レビュー指摘 0 件・CI 失敗なし・CLEAN・ターゲット一致な OPEN 非 Draft PR）
    ready_merge_candidates: List[Dict[str, Any]] = []
    for c in in_review_items:
        pr = c.get("pr")
        if not pr or c["number"] in sync_target_issues or c["number"] in base_mismatch_issues:
            continue
        actual_base = normalize_branch_name(pr.get("base_branch"))
        if int_branch_name and actual_base and actual_base != int_branch_name:
            continue
        if (
            str(pr.get("merge_state_status") or "").upper() == "CLEAN"
            and int(pr.get("unresolved_review_threads") or 0) == 0
            and str(pr.get("review_decision") or "").upper() != "CHANGES_REQUESTED"
            and str(pr.get("ci_state") or "").upper() not in ("FAIL", "PENDING")
            and not pr.get("has_conflict", False)
            and not pr.get("needs_sync", False)
        ):
            ready_merge_candidates.append(c)

    ready_merge_candidates.sort(key=lambda c: (c.get("order", 0), c["number"]))
    ready_merge_issue_set = {c["number"] for c in ready_merge_candidates}
    if ready_merge_candidates:
        head = ready_merge_candidates[0]
        head_pr_num = head["pr"]["number"]
        queued_prs = [c["pr"]["number"] for c in ready_merge_candidates[1:]]
        queue_note = (
            f"（直列マージキュー先頭 / 後続待機: {', '.join(f'PR #{p}' for p in queued_prs)}）"
            if queued_prs
            else "（CLEAN・指摘 0 件・CI 失敗なし）"
        )
        merge_target_desc = f"統合ブランチ ({int_branch_name}) へ" if int_branch_name else ""
        actions.append(
            {
                "priority": 3,
                "action": "ready_to_merge",
                "issues": [head["number"]],
                "pr_number": head_pr_num,
                "base_branch": target_base_label,
                "queued_prs": queued_prs,
                "command_hint": f"gh pr merge {head_pr_num} --merge",
                "summary": f"#{head['number']} (PR #{head_pr_num}) を{merge_target_desc}直列マージする{queue_note}",
            }
        )

    for c in in_review_items:
        if (
            c["number"] in sync_target_issues
            or c["number"] in ready_merge_issue_set
            or c["number"] in base_mismatch_issues
        ):
            continue
        pr_num = c["pr"]["number"] if c.get("pr") else None
        actions.append(
            {
                "priority": 4,
                "action": "review_pr",
                "issues": [c["number"]],
                "pr_number": pr_num,
                "command_hint": f"別セッション／独立サブエージェントで review-pr (PR #{pr_num})",
                "summary": f"#{c['number']} (PR #{pr_num}) を実装セッションとは独立したコンテキストでレビューする",
            }
        )

    for c in implementing_items:
        base_note = f", -BaseBranch \"{int_branch_name}\"" if int_branch_name else ""
        actions.append(
            {
                "priority": 5,
                "action": "resume_implementing",
                "issues": [c["number"]],
                "command_hint": f"run-dev-loop (Issue #{c['number']}{base_note})",
                "summary": f"#{c['number']} の進行中実装を再開・完了させる（{c['status_reason']}）",
            }
        )

    if pending_items and available_slots > 0 and not integration_branch_missing:
        # 最も早い並列グループに属する pending Issue 群から空きスロット上限の範囲内で提案
        min_group = min(c["parallel_group"] for c in pending_items)
        group_candidates = [c for c in pending_items if c["parallel_group"] == min_group]
        ready_batch = group_candidates[:available_slots]
        issue_list_str = ", ".join(f"#{c['number']}" for c in ready_batch)
        notes: List[str] = []
        if len(ready_batch) > 1:
            notes.append("同一順序グループのため git worktree 等で並列実行可能")
        if len(group_candidates) > len(ready_batch):
            notes.append(f"並列スロット上限 {active_slots}/{max_parallel} により最大 {available_slots} 件を提案")
        note_str = f"（{' / '.join(notes)}）" if notes else ""
        if int_branch_name:
            start_cmd_hint = f"run-dev-loop (Issue {issue_list_str}, -BaseBranch \"{int_branch_name}\")"
            start_summary = f"{issue_list_str} の Inner Loop を統合ブランチ ({int_branch_name}) ベースで起動する{note_str}"
        else:
            start_cmd_hint = f"run-dev-loop (Issue {issue_list_str})"
            start_summary = f"{issue_list_str} の Inner Loop を起動する{note_str}"
        actions.append(
            {
                "priority": 6,
                "action": "start_issue",
                "issues": [c["number"] for c in ready_batch],
                "parallel": len(ready_batch) > 1,
                "base_branch": target_base_label,
                "command_hint": start_cmd_hint,
                "summary": start_summary,
            }
        )

    if blocked_items and not halt_recommended:
        for c in blocked_items:
            actions.append(
                {
                    "priority": 7,
                    "action": "escalate_blocked",
                    "issues": [c["number"]],
                    "command_hint": f"python tools/epic_tracker.py --epic {epic_number} --unblock {c['number']}",
                    "summary": f"#{c['number']} がブロック中のため確認・解消を依頼する（理由: {c['blocked_reason']}）",
                }
            )

    if all_children_merged:
        if not use_integration_flow:
            actions.append(
                {
                    "priority": 1,
                    "action": "complete_epic",
                    "issues": [],
                    "command_hint": ".\\tools\\verify_loop.ps1 -Full -JsonOut \".agent_logs\\verify_epic_final.json\"",
                    "summary": f"全子 Issue ({len(children)} 件) がマージ済みです。最新 develop で統合検証を実行し Epic #{epic_number} を完了確認してください。",
                }
            )
        elif int_pr_state == "MERGED":
            int_pr_num = integration_pr["number"]
            cleanup_hint = (
                f"git branch -d {int_branch_name}; git push origin --delete {int_branch_name}"
                if int_branch_name
                else ""
            )
            actions.append(
                {
                    "priority": 1,
                    "action": "complete_epic",
                    "issues": [],
                    "pr_number": int_pr_num,
                    "command_hint": cleanup_hint,
                    "summary": (
                        f"全子 Issue ({len(children)} 件) および統合 PR #{int_pr_num} "
                        f"({int_branch_name or 'epic'} -> develop) がマージ済みです。"
                        f"統合ブランチの削除と Epic #{epic_number} のクローズ準備を実施してください。"
                    ),
                }
            )
        elif int_pr_state == "OPEN":
            int_pr_num = integration_pr["number"]
            int_has_conflict = bool(integration_pr.get("has_conflict", False))
            int_needs_sync = (
                bool(integration_pr.get("needs_sync", False))
                or int_has_conflict
                or (isinstance(int_behind_develop, int) and int_behind_develop > 0)
            )
            if int_needs_sync:
                sync_reason = "コンフリクトが発生している" if int_has_conflict else "origin/develop から遅れている（BEHIND）"
                push_step = (
                    f'; if ($?) {{ git push origin "{int_branch_name}" }}'
                    if int_branch_name
                    else ""
                )
                actions.append(
                    {
                        "priority": 1,
                        "action": "sync_integration_branch",
                        "issues": [],
                        "pr_number": int_pr_num,
                        "has_conflict": int_has_conflict,
                        "command_hint": (
                            '.\\tools\\sync_branch.ps1 -BaseBranch "develop" -Json; '
                            'if ($?) { .\\tools\\verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase -JsonOut ".agent_logs\\verify_epic_final.json" }'
                            f"{push_step}"
                        ),
                        "summary": (
                            f"統合 PR #{int_pr_num} ({int_branch_name} -> develop) に{sync_reason}ため、"
                            "統合ブランチへ最新 origin/develop を追従同期し統合検証・Push を実行してください。"
                        ),
                    }
                )
            else:
                int_cautions: List[str] = []
                if integration_pr.get("is_draft"):
                    int_cautions.append("Draft 状態")
                if str(integration_pr.get("ci_state") or "").upper() == "FAIL":
                    int_cautions.append("CI 失敗")
                if str(integration_pr.get("review_decision") or "").upper() == "CHANGES_REQUESTED":
                    int_cautions.append("CHANGES_REQUESTED")
                if int(integration_pr.get("unresolved_review_threads") or 0) > 0:
                    int_cautions.append(f"未解決コメント {integration_pr['unresolved_review_threads']} 件")
                if int_cautions:
                    caution_str = " / ".join(int_cautions)
                    await_summary = (
                        f"全子 Issue ({len(children)} 件) が統合ブランチ ({int_branch_name}) へマージ済みです。"
                        f"統合 PR #{int_pr_num} ({int_branch_name} -> develop) は要確認状態（{caution_str}）のため、"
                        "自動マージせずユーザー確認・対応のうえ develop へのマージ可否を判断してください。"
                    )
                else:
                    await_summary = (
                        f"全子 Issue ({len(children)} 件) が統合ブランチ ({int_branch_name}) へマージ済みで、"
                        f"統合 PR #{int_pr_num} ({int_branch_name} -> develop) がマージ可能です。"
                        "自動マージせず、ユーザー判断による確認・承認・develop マージを待ってください。"
                    )
                actions.append(
                    {
                        "priority": 1,
                        "action": "await_user_merge",
                        "issues": [],
                        "pr_number": int_pr_num,
                        "command_hint": "",
                        "summary": await_summary,
                    }
                )
        elif not integration_branch_missing and int_branch_name:
            actions.append(
                {
                    "priority": 1,
                    "action": "create_integration_pr",
                    "issues": [],
                    "branch": int_branch_name,
                    "command_hint": (
                        '.\\tools\\sync_branch.ps1 -BaseBranch "develop" -Json; '
                        'if ($?) { .\\tools\\verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase -JsonOut ".agent_logs\\verify_epic_final.json" }; '
                        f'if ($?) {{ git push origin "{int_branch_name}" }}; '
                        f'if ($?) {{ gh pr create --base develop --head "{int_branch_name}" --title "[Epic] ..." --body-file "<pr_body.md>" }}'
                    ),
                    "summary": (
                        f"全子 Issue ({len(children)} 件) が統合ブランチ ({int_branch_name}) へマージ済みです。"
                        "origin/develop 同期・verify_loop.ps1 -Full -BaseBranch develop -PreferOriginBase 合格・Push 後に "
                        "develop 向け統合 PR を作成してください（最終マージはユーザー判断待ち）。"
                    ),
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
        "parallel_slots": {
            "max_parallel": max_parallel,
            "active_slots": active_slots,
            "available_slots": available_slots,
            "active_issues": [c["number"] for c in active_items],
        },
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
    parallel_slots = next_info.get("parallel_slots")
    if isinstance(parallel_slots, dict):
        lines.append(
            f"並列スロット: {parallel_slots.get('active_slots', 0)}/{parallel_slots.get('max_parallel', 3)} 使用中 "
            f"(空き: {parallel_slots.get('available_slots', 0)})"
        )
    int_branch = state.get("integration_branch")
    if isinstance(int_branch, dict):
        b_name = int_branch.get("name")
        if b_name:
            local_txt = "あり" if int_branch.get("exists_local") else "なし"
            remote_txt = "あり" if int_branch.get("exists_remote") else "なし"
            ahead = int_branch.get("ahead_of_develop")
            behind = int_branch.get("behind_develop")
            diff_txt = (
                f" / vs origin/develop: ahead={ahead}, behind={behind}"
                if ahead is not None and behind is not None
                else ""
            )
            lines.append(f"統合ブランチ: {b_name} (local: {local_txt}, remote: {remote_txt}{diff_txt})")
        else:
            lines.append(f"統合ブランチ: 未作成 (epic/{epic['number']}-<slug> を作成してください)")

        int_pr = state.get("integration_pr")
        if isinstance(int_pr, dict):
            ipr_state = int_pr.get("state", "")
            ipr_tag = ""
            if ipr_state == "OPEN" and not int_pr.get("is_draft"):
                if int_pr.get("has_conflict"):
                    ipr_tag = "/CONFLICT"
                elif int_pr.get("merge_state_status"):
                    ipr_tag = f"/{int_pr['merge_state_status']}"
            elif ipr_state == "OPEN" and int_pr.get("is_draft"):
                ipr_tag = "/DRAFT"
            lines.append(
                f"統合 PR: PR #{int_pr.get('number')} ({int_pr.get('head_branch', b_name or '')} -> "
                f"{int_pr.get('base_branch', 'develop')}) [{ipr_state}{ipr_tag}]"
            )
        else:
            lines.append("統合 PR: 未作成 (-> develop)")
    lines.append("=" * 72)

    if not next_only:
        lines.append("")
        lines.append("【子 Issue 一覧】")
        for c in state["children"]:
            deps = ", ".join(f"#{d}" for d in c["depends_on"]) if c["depends_on"] else "-"
            pr = c.get("pr")
            if pr:
                pr_state = pr.get("state", "")
                merge_tag = ""
                if pr_state == "OPEN" and not pr.get("is_draft"):
                    if pr.get("has_conflict"):
                        merge_tag = "/CONFLICT"
                    elif pr.get("merge_state_status"):
                        merge_tag = f"/{pr['merge_state_status']}"
                pr_str = f"PR #{pr['number']}({pr_state}{merge_tag})"
            else:
                pr_str = "-"
            lines.append(
                f"  [順:{c['order']}] #{c['number']:<4} | {c['status']:<13} | 依存:{deps:<10} | {pr_str:<22} | {c['title']}"
            )
            if c.get("status_reason"):
                lines.append(f"           └─ {c['status_reason']}")
            wt = c.get("worktree")
            if wt:
                branch_str = wt.get("branch") or "(detached)"
                lines.append(f"           └─ worktree: {wt.get('path', '')} (branch: {branch_str})")

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
    max_parallel: int = 3,
    integration_branch: Optional[str] = None,
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
    git_timeout = min(timeout, 15)
    local_branch_names = fetch_local_branch_names(timeout=git_timeout)
    remote_branch_names = fetch_remote_branch_names(timeout=git_timeout)

    resolved_int_branch_name = resolve_integration_branch_name(
        epic_number=epic_number,
        explicit_branch=integration_branch,
        existing_state=existing_state,
        local_branches=local_branch_names,
        remote_branches=remote_branch_names,
        all_prs=all_prs,
        warnings=warnings,
    )
    integration_branch_info = inspect_integration_branch(
        branch_name=resolved_int_branch_name,
        local_branches=local_branch_names,
        remote_branches=remote_branch_names,
        timeout=git_timeout,
    )

    prs_by_issue: Dict[int, List[Dict[str, Any]]] = {n: [] for n in child_numbers}
    integration_pr_candidates: List[Dict[str, Any]] = []
    open_pr_numbers: List[int] = []
    has_remote_pr_evidence = False
    for pr in all_prs:
        if is_integration_pr(pr, integration_branch=resolved_int_branch_name, epic_number=epic_number):
            integration_pr_candidates.append(pr)
            if str(pr.get("state") or "").upper() == "OPEN":
                has_remote_pr_evidence = True
                pr_num = int(pr.get("number") or 0)
                if pr_num and pr_num not in open_pr_numbers:
                    open_pr_numbers.append(pr_num)
            continue
        # 他 Epic の統合 PR も子 Issue PR として誤認しないよう除外
        if is_integration_pr(pr, integration_branch=None, epic_number=None):
            continue

        matched = match_pr_to_issue_numbers(
            pr,
            integration_branch=resolved_int_branch_name,
            epic_number=epic_number,
        )
        for num in matched:
            if num in prs_by_issue:
                prs_by_issue[num].append(pr)
                if str(pr.get("state") or "").upper() == "OPEN":
                    if (
                        resolved_int_branch_name
                        and normalize_branch_name(pr.get("baseRefName")) == resolved_int_branch_name
                    ):
                        has_remote_pr_evidence = True
                    pr_num = int(pr.get("number") or 0)
                    if pr_num and pr_num not in open_pr_numbers:
                        open_pr_numbers.append(pr_num)

    if resolved_int_branch_name and has_remote_pr_evidence and not integration_branch_info["exists_remote"]:
        integration_branch_info["exists_remote"] = True

    unresolved_by_pr = fetch_unresolved_review_threads(open_pr_numbers, repo, timeout)
    best_integration_pr = select_best_pr(integration_pr_candidates)
    integration_pr_info: Optional[Dict[str, Any]] = (
        build_pr_info(best_integration_pr, unresolved_by_pr)
        if best_integration_pr is not None
        else None
    )

    local_branch_issues: Set[int] = set()
    for b_name in local_branch_names:
        if b_name.lower().startswith("epic/"):
            continue
        m_b = BRANCH_ISSUE_RE.search(b_name)
        if m_b:
            local_branch_issues.add(int(m_b.group(1)))
    active_worktrees = fetch_active_worktrees(timeout=git_timeout)

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
    warnings.extend(collect_base_branch_warnings(children, resolved_int_branch_name))

    # マージ済みになった Issue の古い blocked_override は自動クリア
    for c in children:
        if c["status"] == "merged" and c["number"] in blocked_overrides:
            del blocked_overrides[c["number"]]

    summary: Dict[str, int] = {"total": len(children)}
    for st in VALID_STATUSES:
        summary[st] = sum(1 for c in children if c["status"] == st)

    next_info = compute_next_actions(
        children,
        epic_number=epic_number,
        max_parallel=max_parallel,
        integration_branch=integration_branch_info,
        integration_pr=integration_pr_info,
    )

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
        "integration_branch": integration_branch_info,
        "integration_pr": integration_pr_info,
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
    parser.add_argument(
        "--integration-branch",
        type=str,
        default=None,
        help="Epic 統合ブランチ名 (例: epic/<Epic番号>-<説明>)",
    )
    parser.add_argument("--next", action="store_true", dest="next_only", help="次に実行すべきアクションを中心に表示する")
    parser.add_argument("--json", action="store_true", dest="as_json", help="結果を JSON 形式で出力する")
    parser.add_argument("--state-file", type=str, default=None, help="状態スナップショット JSON のパス")
    parser.add_argument("--no-save", action="store_true", help="状態スナップショットを保存しない")
    parser.add_argument("--offline", action="store_true", help="GitHub に問い合わせず既存の状態ファイルから読み込む")
    parser.add_argument("--mark-blocked", type=int, default=None, help="指定した子 Issue をローカルで blocked にする")
    parser.add_argument("--unblock", type=int, default=None, help="指定した子 Issue のローカル blocked を解除する")
    parser.add_argument("--reason", type=str, default="", help="--mark-blocked 時の理由")
    parser.add_argument("--repo", type=str, default=None, help="対象リポジトリ (owner/name)")
    parser.add_argument("--max-parallel", type=int, default=3, help="同時稼働させる最大並列スロット数 (既定: 3、最小: 1)")
    parser.add_argument("--timeout", type=int, default=30, help="外部コマンドのタイムアウト秒数 (既定: 30)")
    return parser.parse_args(argv)


def apply_offline_block_updates(
    state: Dict[str, Any],
    mark_blocked: Optional[int],
    unblock: Optional[int],
    reason: str,
    max_parallel: int = 3,
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
            elif c.get("worktree"):
                c["status"] = "implementing"
                c["status_reason"] = "稼働中ワークツリーが存在"
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
    int_branch = state.get("integration_branch") if isinstance(state.get("integration_branch"), dict) else None
    int_pr = state.get("integration_pr") if isinstance(state.get("integration_pr"), dict) else None
    state["updated_at"] = now_iso
    state["summary"] = summary
    state["next"] = compute_next_actions(
        children,
        epic_number=epic_num,
        max_parallel=max_parallel,
        integration_branch=int_branch,
        integration_pr=int_pr,
    )
    state["blocked_overrides"] = {str(k): v for k, v in blocked_overrides.items()}
    return state


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)
    if args.max_parallel < 1:
        print(
            f"[ERROR] --max-parallel には 1 以上の整数を指定してください（指定値: {args.max_parallel}）。",
            file=sys.stderr,
        )
        return 1
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
        if args.integration_branch:
            normalized_arg = normalize_branch_name(args.integration_branch)
            if normalized_arg:
                existing_int = state.get("integration_branch")
                existing_name = (
                    normalize_branch_name(existing_int.get("name"))
                    if isinstance(existing_int, dict)
                    else None
                )
                if existing_name != normalized_arg:
                    state["integration_branch"] = {
                        "name": normalized_arg,
                        "exists_local": False,
                        "exists_remote": False,
                        "ahead_of_develop": None,
                        "behind_develop": None,
                    }
                    existing_int_pr = state.get("integration_pr")
                    if (
                        isinstance(existing_int_pr, dict)
                        and normalize_branch_name(existing_int_pr.get("head_branch")) != normalized_arg
                    ):
                        state["integration_pr"] = None
                    if isinstance(state.get("warnings"), list) and isinstance(state.get("children"), list):
                        filtered_warnings = [
                            w
                            for w in state["warnings"]
                            if "のターゲットブランチ (base:" not in str(w)
                        ]
                        filtered_warnings.extend(
                            collect_base_branch_warnings(state["children"], normalized_arg)
                        )
                        state["warnings"] = filtered_warnings
        if args.mark_blocked is not None or args.unblock is not None:
            state = apply_offline_block_updates(
                state=state,
                mark_blocked=args.mark_blocked,
                unblock=args.unblock,
                reason=args.reason,
                max_parallel=args.max_parallel,
            )
            if not args.no_save:
                save_state(state_path, state)
        elif isinstance(state.get("children"), list):
            epic_num = int((state.get("epic") or {}).get("number") or args.epic)
            int_branch = state.get("integration_branch") if isinstance(state.get("integration_branch"), dict) else None
            int_pr = state.get("integration_pr") if isinstance(state.get("integration_pr"), dict) else None
            state["next"] = compute_next_actions(
                state["children"],
                epic_number=epic_num,
                max_parallel=args.max_parallel,
                integration_branch=int_branch,
                integration_pr=int_pr,
            )
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
                max_parallel=args.max_parallel,
                integration_branch=args.integration_branch,
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
