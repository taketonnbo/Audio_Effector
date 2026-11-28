#!/usr/bin/env python3
"""
オープン中の GitHub Issue を棚卸しし、Epic / 通常 Issue 分類・マージ済み PR・
オープン PR・origin/develop の Git 履歴・直近クローズ済み Issue・Epic チェックリスト／
テーブル進捗不整合および進行中子 Issue との突合結果を出力する読み取り専用監査スクリプト。
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from typing import Any


STALE_TABLE_STATUS_KEYWORDS: tuple[str, ...] = (
    "未着手",
    "進行中",
    "未完了",
    "作業中",
    "⬜",
    "🔄",
)


def run_cmd(cmd: list[str]) -> str:
    return subprocess.check_output(cmd, text=True, encoding="utf-8")


def is_epic_issue(issue: dict[str, Any]) -> bool:
    labels = [lb["name"].lower() for lb in issue.get("labels", [])]
    title = issue.get("title", "")
    return (
        "epic" in labels
        or "[epic]" in title.lower()
        or "親issue" in title.lower()
    )


def extract_issue_refs(text: str) -> set[int]:
    return {int(m) for m in re.findall(r"(?:#|/|issue[ -]?)(\d+)", text, re.IGNORECASE)}


def extract_closing_refs(text: str) -> set[int]:
    return {
        int(m)
        for m in re.findall(
            r"(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+#(\d+)",
            text,
            re.IGNORECASE,
        )
    }


def resolve_git_target_ref(preferred_ref: str) -> str:
    for candidate in (preferred_ref, "develop", "HEAD"):
        res = subprocess.run(
            ["git", "rev-parse", "--verify", "--quiet", candidate],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if res.returncode == 0:
            return candidate
    return "HEAD"


def audit_issues(base_ref: str, issue_limit: int, pr_limit: int, git_limit: int) -> dict[str, Any]:
    open_issues = json.loads(
        run_cmd(
            [
                "gh",
                "issue",
                "list",
                "--state",
                "open",
                "--limit",
                str(issue_limit),
                "--json",
                "number,title,labels,body,comments,url,createdAt,updatedAt",
            ]
        )
    )
    closed_issues = json.loads(
        run_cmd(
            [
                "gh",
                "issue",
                "list",
                "--state",
                "closed",
                "--limit",
                str(issue_limit),
                "--json",
                "number,title,closedAt",
            ]
        )
    )
    merged_prs = json.loads(
        run_cmd(
            [
                "gh",
                "pr",
                "list",
                "--state",
                "merged",
                "--limit",
                str(pr_limit),
                "--json",
                "number,title,headRefName,baseRefName,mergedAt,body",
            ]
        )
    )
    open_prs = json.loads(
        run_cmd(
            [
                "gh",
                "pr",
                "list",
                "--state",
                "open",
                "--limit",
                "100",
                "--json",
                "number,title,headRefName,baseRefName,body",
            ]
        )
    )

    git_ref = resolve_git_target_ref(base_ref)
    git_log = run_cmd(["git", "log", git_ref, "--oneline", "-n", str(git_limit)])
    git_head_sha = run_cmd(["git", "rev-parse", "--short", git_ref]).strip()

    closed_issue_map = {i["number"]: i for i in closed_issues}
    open_issue_nums = {i["number"] for i in open_issues}

    merged_pr_by_issue: dict[int, list[dict[str, Any]]] = {}
    for pr in merged_prs:
        text = f"{pr['title']} {pr['headRefName']} {pr.get('body') or ''}"
        refs = extract_issue_refs(text)
        closing = extract_closing_refs(text)
        for num in refs:
            if num == pr["number"]:
                continue
            merged_pr_by_issue.setdefault(num, []).append(
                {
                    "prNumber": pr["number"],
                    "title": pr["title"],
                    "headRefName": pr["headRefName"],
                    "baseRefName": pr.get("baseRefName", ""),
                    "mergedAt": pr.get("mergedAt", ""),
                    "isClosingRef": num in closing,
                }
            )

    open_pr_by_issue: dict[int, list[dict[str, Any]]] = {}
    for pr in open_prs:
        text = f"{pr['title']} {pr['headRefName']} {pr.get('body') or ''}"
        refs = extract_issue_refs(text)
        for num in refs:
            if num == pr["number"]:
                continue
            open_pr_by_issue.setdefault(num, []).append(
                {
                    "prNumber": pr["number"],
                    "title": pr["title"],
                    "headRefName": pr["headRefName"],
                    "baseRefName": pr.get("baseRefName", ""),
                }
            )

    git_by_issue: dict[int, list[str]] = {}
    for line in git_log.splitlines():
        for m in re.findall(r"#(\d+)", line):
            git_by_issue.setdefault(int(m), []).append(line)

    epics: list[dict[str, Any]] = []
    non_epics: list[dict[str, Any]] = []

    for iss in sorted(open_issues, key=lambda x: x["number"]):
        num = iss["number"]
        labels = [lb["name"] for lb in iss.get("labels", [])]
        if is_epic_issue(iss):
            checked_refs: set[int] = set()
            unchecked_refs: set[int] = set()
            table_refs: set[int] = set()
            stale_table_refs: set[int] = set()
            for line in (iss.get("body") or "").splitlines():
                m_checked = re.search(r"^\s*-\s*\[[xX]\]\s*(?:\*\*[^*]+\*\*:\s*)?#(\d+)", line)
                if m_checked:
                    checked_refs.add(int(m_checked.group(1)))
                    continue
                m_unchecked = re.search(r"^\s*-\s*\[\s\]\s*(?:\*\*[^*]+\*\*:\s*)?#(\d+)", line)
                if m_unchecked:
                    unchecked_refs.add(int(m_unchecked.group(1)))
                    continue
                stripped = line.strip()
                if stripped.startswith("|") and stripped.endswith("|"):
                    # テーブル行は最初に出現する #<番号> を対象子 Issue として抽出（依存列の前提 Issue を誤検知しない）
                    m_tbl_issue = re.search(r"#(\d+)", stripped)
                    if m_tbl_issue:
                        tbl_num = int(m_tbl_issue.group(1))
                        if tbl_num != num:
                            table_refs.add(tbl_num)
                            if any(kw in stripped for kw in STALE_TABLE_STATUS_KEYWORDS):
                                stale_table_refs.add(tbl_num)

            checklist_refs = checked_refs | unchecked_refs
            all_child_refs = checklist_refs | table_refs
            unchecked_closed_children = sorted(
                n for n in (unchecked_refs - checked_refs) if n in closed_issue_map
            )
            stale_table_closed_children = sorted(
                n for n in stale_table_refs if n in closed_issue_map
            )
            open_children = sorted(n for n in checklist_refs if n in open_issue_nums and n != num)

            # オープン中の子 Issue のうち、統合ブランチマージ済み PR または OPEN PR が紐づく進行中タスクを検出
            in_progress_children: list[dict[str, Any]] = []
            for child_num in sorted(n for n in all_child_refs if n in open_issue_nums and n != num):
                closing_merged_prs = [
                    pr for pr in merged_pr_by_issue.get(child_num, [])
                    if pr["isClosingRef"]
                ]
                child_open_prs = open_pr_by_issue.get(child_num, [])
                if closing_merged_prs or child_open_prs:
                    in_progress_children.append(
                        {
                            "issue": child_num,
                            "mergedClosingPrs": [
                                {"prNumber": p["prNumber"], "baseRefName": p["baseRefName"]}
                                for p in closing_merged_prs
                            ],
                            "openPrs": [
                                {"prNumber": p["prNumber"], "baseRefName": p["baseRefName"]}
                                for p in child_open_prs
                            ],
                        }
                    )

            epics.append(
                {
                    "number": num,
                    "title": iss["title"],
                    "labels": labels,
                    "url": iss.get("url", ""),
                    "uncheckedClosedChildren": unchecked_closed_children,
                    "staleTableClosedChildren": stale_table_closed_children,
                    "inProgressChildren": in_progress_children,
                    "openChecklistChildren": open_children,
                    "checklistChildCount": len(checklist_refs),
                }
            )
        else:
            non_epics.append(
                {
                    "number": num,
                    "title": iss["title"],
                    "labels": labels,
                    "url": iss.get("url", ""),
                    "commentCount": len(iss.get("comments", [])),
                    "matchedMergedPrs": merged_pr_by_issue.get(num, []),
                    "matchedOpenPrs": open_pr_by_issue.get(num, []),
                    "matchedGitCommits": git_by_issue.get(num, [])[:10],
                }
            )

    recent_closed = sorted(
        closed_issues,
        key=lambda x: x.get("closedAt") or "",
        reverse=True,
    )[:15]

    return {
        "gitRef": git_ref,
        "gitHeadSha": git_head_sha,
        "summary": {
            "totalOpenIssues": len(open_issues),
            "epicOpenIssues": len(epics),
            "nonEpicOpenIssues": len(non_epics),
            "mergedPrCount": len(merged_prs),
            "openPrCount": len(open_prs),
        },
        "epics": epics,
        "nonEpics": non_epics,
        "recentClosedIssues": recent_closed,
    }


def print_report(data: dict[str, Any]) -> None:
    s = data["summary"]
    print(
        f"Base ref: {data['gitRef']} ({data['gitHeadSha']}) | "
        f"Open Issues: {s['totalOpenIssues']} (Epic: {s['epicOpenIssues']}, Non-Epic: {s['nonEpicOpenIssues']}) | "
        f"Merged PRs: {s['mergedPrCount']} | Open PRs: {s['openPrCount']}"
    )

    print(f"\n=== EPICS (EXCLUDE FROM AUTO-CLOSE / UPDATE PROGRESS ONLY: {len(data['epics'])}) ===")
    for e in data["epics"]:
        notes: list[str] = []
        if e["checklistChildCount"] > 0 and not e["openChecklistChildren"]:
            notes.append("all checklist children closed")
        if e["uncheckedClosedChildren"]:
            nums = ", ".join(f"#{n}" for n in e["uncheckedClosedChildren"])
            notes.append(f"unchecked but closed: {nums}")
        if e.get("staleTableClosedChildren"):
            nums = ", ".join(f"#{n}" for n in e["staleTableClosedChildren"])
            notes.append(f"table stale (closed): {nums}")
        if e.get("inProgressChildren"):
            prog_parts: list[str] = []
            for item in e["inProgressChildren"]:
                pr_tags = [
                    f"MERGED PR #{p['prNumber']}->{p['baseRefName']}"
                    for p in item["mergedClosingPrs"]
                ] + [
                    f"OPEN PR #{p['prNumber']}->{p['baseRefName']}"
                    for p in item["openPrs"]
                ]
                prog_parts.append(f"#{item['issue']}({', '.join(pr_tags)})")
            notes.append(f"in-progress: {'; '.join(prog_parts)}")
        suffix = f"  <-- {'; '.join(notes)}" if notes else ""
        print(f"  #{e['number']}: {e['title']} {e['labels']}{suffix}")

    print(f"\n=== NON-EPIC OPEN ISSUES ({len(data['nonEpics'])}) ===")
    for iss in data["nonEpics"]:
        tags: list[str] = []
        if iss["matchedMergedPrs"]:
            tags.append("MERGED_PR")
        if iss["matchedGitCommits"]:
            tags.append("GIT")
        if iss["matchedOpenPrs"]:
            tags.append("OPEN_PR")
        if iss["commentCount"] > 0:
            tags.append(f"COMMENTS:{iss['commentCount']}")
        tag_str = f" [{' '.join(tags)}]" if tags else ""
        print(f"#{iss['number']}{tag_str}: {iss['title']} {iss['labels']}")
        for pr in iss["matchedMergedPrs"]:
            cl = " (CLOSES)" if pr["isClosingRef"] else " (REF)"
            merged_day = pr["mergedAt"][:10] if pr["mergedAt"] else ""
            print(
                f"    MERGED PR #{pr['prNumber']}{cl} -> {pr['baseRefName']} "
                f"({pr['headRefName']}, {merged_day}): {pr['title']}"
            )
        for pr in iss["matchedOpenPrs"]:
            print(
                f"    OPEN PR #{pr['prNumber']} -> {pr['baseRefName']} "
                f"({pr['headRefName']}): {pr['title']}"
            )
        for commit in iss["matchedGitCommits"][:5]:
            print(f"    GIT: {commit}")

    print("\n=== RECENTLY CLOSED ISSUES (TOP 15) ===")
    for c in data["recentClosedIssues"]:
        print(f"  #{c['number']} ({c.get('closedAt', '')}): {c['title']}")


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(
        description="Audit open GitHub issues against merged PRs and git history."
    )
    parser.add_argument(
        "--base-ref",
        default="origin/develop",
        help="Git ref to inspect commit history (default: origin/develop)",
    )
    parser.add_argument("--issue-limit", type=int, default=500)
    parser.add_argument("--pr-limit", type=int, default=500)
    parser.add_argument("--git-limit", type=int, default=2000)
    parser.add_argument("--json", action="store_true", help="Output raw JSON to stdout")
    parser.add_argument("--json-out", help="Write raw JSON (UTF-8 without BOM) to the specified file path")
    args = parser.parse_args()

    data = audit_issues(
        base_ref=args.base_ref,
        issue_limit=args.issue_limit,
        pr_limit=args.pr_limit,
        git_limit=args.git_limit,
    )

    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
    elif not args.json_out:
        print_report(data)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
