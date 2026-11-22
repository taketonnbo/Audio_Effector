#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
仕様書同期チェックスクリプト (Spec Sync Checker)
Audio Effector プロジェクトにおいて、ソースコード変更に対応する設計仕様書（設計/）の
更新漏れを機械的に検出・一覧化するツール。

使用法:
  python tools/check_spec_sync.py [options]

主なオプション:
  --base REF         比較対象のベースブランチ/コミット (デフォルト: develop)
  --files FILE ...   git diff の代わりに指定ファイル一覧で該当仕様書を逆引きする
  --include-working  未コミット差分（staged/unstaged/untracked）も含める (デフォルト: 有効)
  --committed-only   コミット済み差分（base...HEAD）のみを対象とする
  --validate         設計/spec_map.json 内の仕様書パス実在チェックを実行する
  --strict           未更新の Primary 仕様書がある場合に終了コード 1 を返す
  --markdown         Issue / PR 本文に貼り付け可能な Markdown 形式で出力する
  --json             JSON 形式で出力する
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass


REPO_ROOT = Path(__file__).resolve().parent.parent
SPEC_MAP_PATH = REPO_ROOT / "設計" / "spec_map.json"


def normalize_path(path_str: str) -> str:
    """パス区切りをスラッシュへ正規化し、先頭の ./ を除去する。"""
    normalized = path_str.strip().replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized


def match_pattern(file_path: str, pattern: str) -> bool:
    """グロブパターン（** を含む）に対してパスが一致するか判定する。"""
    norm_path = normalize_path(file_path)
    norm_pattern = normalize_path(pattern)

    if "**" in norm_pattern:
        # 簡易的な ** 対応: ディレクトリプレフィックス一致 + 末尾ワイルドカード
        prefix, _, suffix = norm_pattern.partition("**")
        if prefix and not norm_path.startswith(prefix):
            return False
        remainder = norm_path[len(prefix):]
        suffix = suffix.lstrip("/")
        if not suffix:
            return True
        if fnmatch.fnmatch(remainder, suffix):
            return True
        parts = remainder.split("/")
        for i in range(len(parts)):
            if fnmatch.fnmatch("/".join(parts[i:]), suffix):
                return True
        return False

    return fnmatch.fnmatch(norm_path, norm_pattern)


def load_spec_map(path: Path) -> Dict[str, Any]:
    """spec_map.json を読み込む。"""
    if not path.exists():
        print(f"[ERROR] spec_map.json が見つかりません: {path}", file=sys.stderr)
        sys.exit(2)
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def run_git(args: List[str]) -> Tuple[int, List[str]]:
    """git コマンドを UTF-8 で実行し、出力行リストを返す。"""
    cmd = ["git", "-c", "core.quotepath=false", *args]
    proc = subprocess.run(
        cmd,
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    lines = [normalize_path(line) for line in proc.stdout.splitlines() if line.strip()]
    return proc.returncode, lines


def collect_changed_files(base_ref: str, include_working: bool) -> List[str]:
    """base_ref...HEAD および必要に応じて作業ツリーの差分ファイル一覧を取得する。"""
    changed: Set[str] = set()

    rc, diff_lines = run_git(["diff", "--name-only", f"{base_ref}...HEAD"])
    if rc == 0:
        changed.update(diff_lines)
    else:
        # origin/<base> フォールバック
        rc_origin, origin_lines = run_git(["diff", "--name-only", f"origin/{base_ref}...HEAD"])
        if rc_origin == 0:
            changed.update(origin_lines)

    if include_working:
        for git_args in (
            ["diff", "--name-only", "--cached"],
            ["diff", "--name-only"],
            ["ls-files", "--others", "--exclude-standard"],
        ):
            rc_w, lines_w = run_git(git_args)
            if rc_w == 0:
                changed.update(lines_w)

    return sorted(changed)


def validate_spec_map(spec_map: Dict[str, Any]) -> List[str]:
    """spec_map.json に定義された全仕様書ファイルが実在するか検証する。"""
    missing: List[str] = []
    seen: Set[str] = set()

    for entry in spec_map.get("mappings", []):
        for key in ("primary_specs", "related_specs"):
            for spec_path in entry.get(key, []):
                norm = normalize_path(spec_path)
                if norm in seen:
                    continue
                seen.add(norm)
                if not (REPO_ROOT / norm).exists():
                    missing.append(norm)

    return sorted(missing)


def analyze_changes(
    changed_files: List[str],
    spec_map: Dict[str, Any],
) -> Dict[str, Any]:
    """変更ファイル一覧と spec_map を照合し、更新済み・未更新の仕様書を分類する。"""
    ignore_patterns = spec_map.get("ignore_patterns", [])
    mappings = spec_map.get("mappings", [])

    updated_specs = {f for f in changed_files if f.startswith("設計/")}
    code_files = [
        f for f in changed_files
        if not any(match_pattern(f, pat) for pat in ignore_patterns)
    ]

    matched_entries: List[Dict[str, Any]] = []
    unmapped_code_files: List[str] = []

    primary_to_triggers: Dict[str, Set[str]] = {}
    related_to_triggers: Dict[str, Set[str]] = {}

    for code_file in code_files:
        file_matched = False
        for entry in mappings:
            patterns = entry.get("code_patterns", [])
            if any(match_pattern(code_file, pat) for pat in patterns):
                file_matched = True
                for spec in entry.get("primary_specs", []):
                    primary_to_triggers.setdefault(normalize_path(spec), set()).add(code_file)
                for spec in entry.get("related_specs", []):
                    related_to_triggers.setdefault(normalize_path(spec), set()).add(code_file)
        if not file_matched:
            unmapped_code_files.append(code_file)

    for entry in mappings:
        triggers = [
            f for f in code_files
            if any(match_pattern(f, pat) for pat in entry.get("code_patterns", []))
        ]
        if triggers:
            matched_entries.append({
                "id": entry.get("id", ""),
                "name": entry.get("name", ""),
                "triggered_by": sorted(triggers),
                "primary_specs": [normalize_path(s) for s in entry.get("primary_specs", [])],
                "related_specs": [normalize_path(s) for s in entry.get("related_specs", [])],
            })

    # Primary に含まれる仕様書は Related から除外
    for spec in list(related_to_triggers.keys()):
        if spec in primary_to_triggers:
            del related_to_triggers[spec]

    primary_updated = sorted(s for s in primary_to_triggers if s in updated_specs)
    primary_missing = sorted(s for s in primary_to_triggers if s not in updated_specs)
    related_updated = sorted(s for s in related_to_triggers if s in updated_specs)
    related_missing = sorted(s for s in related_to_triggers if s not in updated_specs)

    return {
        "changed_files": changed_files,
        "code_files": code_files,
        "updated_specs": sorted(updated_specs),
        "matched_entries": matched_entries,
        "unmapped_code_files": sorted(unmapped_code_files),
        "primary_updated": [
            {"spec": s, "triggered_by": sorted(primary_to_triggers[s])}
            for s in primary_updated
        ],
        "primary_missing": [
            {"spec": s, "triggered_by": sorted(primary_to_triggers[s])}
            for s in primary_missing
        ],
        "related_updated": [
            {"spec": s, "triggered_by": sorted(related_to_triggers[s])}
            for s in related_updated
        ],
        "related_missing": [
            {"spec": s, "triggered_by": sorted(related_to_triggers[s])}
            for s in related_missing
        ],
    }


def print_text_report(result: Dict[str, Any], base_label: str) -> None:
    """人間・エージェント向けのテキスト形式で結果を出力する。"""
    print("=" * 72)
    print(f"仕様書同期チェック結果 (対象: {base_label})")
    print("=" * 72)
    print(f"変更コードファイル数 : {len(result['code_files'])}")
    print(f"更新済み仕様書数     : {len(result['updated_specs'])}")
    print()

    if not result["code_files"]:
        print("[OK] 仕様書同期の対象となるコード変更はありません。")
        return

    if result["primary_updated"]:
        print("[OK] 更新済みの主要仕様書 (Primary):")
        for item in result["primary_updated"]:
            print(f"  - {item['spec']}")
        print()

    if result["primary_missing"]:
        print("[WARN] 未更新の主要仕様書 (Primary - 必ず影響有無を確認してください):")
        for item in result["primary_missing"]:
            triggers = ", ".join(item["triggered_by"])
            print(f"  - {item['spec']}")
            print(f"      トリガー: {triggers}")
        print()

    if result["related_updated"]:
        print("[OK] 更新済みの関連仕様書 (Related):")
        for item in result["related_updated"]:
            print(f"  - {item['spec']}")
        print()

    if result["related_missing"]:
        print("[INFO] 未更新の関連仕様書 (Related - 仕様・境界変更時のみ確認):")
        for item in result["related_missing"]:
            triggers = ", ".join(item["triggered_by"])
            print(f"  - {item['spec']}")
            print(f"      トリガー: {triggers}")
        print()

    if result["unmapped_code_files"]:
        print("[INFO] spec_map.json 未登録の変更コードファイル:")
        for f in result["unmapped_code_files"]:
            print(f"  - {f}")
        print("  ※ 新規画面・新規機能の場合は 設計/spec_map.json への登録を検討してください。")
        print()

    if not result["primary_missing"]:
        print("[OK] 主要仕様書 (Primary) の更新漏れは検出されませんでした。")


def print_markdown_report(result: Dict[str, Any]) -> None:
    """Issue / PR 本文へ貼り付け可能な Markdown 形式で出力する。"""
    print("### 更新対象の仕様書 / 仕様書同期結果")
    if not result["primary_updated"] and not result["primary_missing"] and not result["related_updated"]:
        print("- なし（仕様変更を伴わない内部変更のため）")
        return

    for item in result["primary_updated"]:
        print(f"- [x] `{item['spec']}`（更新済み）")
    for item in result["primary_missing"]:
        print(f"- [ ] `{item['spec']}`（要確認・更新または不要理由を記載）")
    for item in result["related_updated"]:
        print(f"- [x] `{item['spec']}`（関連・更新済み）")
    if result["related_missing"]:
        print("- 関連仕様書（影響時のみ確認）:")
        for item in result["related_missing"]:
            print(f"  - `{item['spec']}`")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="ソースコード変更に対応する設計仕様書（設計/）の更新漏れをチェックします。"
    )
    parser.add_argument(
        "--base",
        default="develop",
        help="git diff の比較ベースブランチ/コミット (デフォルト: develop)",
    )
    parser.add_argument(
        "--files",
        nargs="+",
        help="git diff の代わりに検査対象のファイルパスを直接指定する",
    )
    parser.add_argument(
        "--committed-only",
        action="store_true",
        help="作業ツリーの未コミット差分を含めず base...HEAD のみ検査する",
    )
    parser.add_argument(
        "--validate",
        action="store_true",
        help="設計/spec_map.json に定義された仕様書パスの実在チェックのみ実行する",
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        help="未更新の主要仕様書 (Primary) がある場合に終了コード 1 を返す",
    )
    parser.add_argument(
        "--markdown",
        action="store_true",
        help="Issue / PR 本文用の Markdown チェックリスト形式で出力する",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="結果を JSON 形式で出力する",
    )

    args = parser.parse_args()
    spec_map = load_spec_map(SPEC_MAP_PATH)

    if args.validate:
        missing = validate_spec_map(spec_map)
        if args.json:
            print(json.dumps({"valid": len(missing) == 0, "missing_specs": missing}, ensure_ascii=False, indent=2))
        elif missing:
            print("[ERROR] spec_map.json に存在しない仕様書パスが含まれています:")
            for m in missing:
                print(f"  - {m}")
        else:
            print(f"[OK] spec_map.json の全マッピング ({len(spec_map.get('mappings', []))} 件) の仕様書パスが実在します。")
        return 1 if missing else 0

    if args.files:
        changed_files = sorted({normalize_path(f) for f in args.files})
        base_label = f"指定ファイル ({len(changed_files)} 件)"
    else:
        changed_files = collect_changed_files(args.base, include_working=not args.committed_only)
        base_label = f"{args.base}...HEAD" + (" + 作業ツリー" if not args.committed_only else "")

    result = analyze_changes(changed_files, spec_map)

    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    elif args.markdown:
        print_markdown_report(result)
    else:
        print_text_report(result, base_label)

    if args.strict and result["primary_missing"]:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
