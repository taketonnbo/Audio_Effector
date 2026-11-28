#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
サードパーティライセンス記載同期チェックスクリプト (License Sync Checker)
Audio Effector プロジェクトにおいて、製品 .csproj の依存パッケージ（PackageReference）、
外部アセット・モデル定義の変更、PublishSingleFile による LGPL 静的結合リスク、
および bin/ 配下 DLL の誤追跡を機械検証し、THIRD-PARTY-NOTICES.md の更新要否を検出するツール。

使用法:
  python tools/check_license_sync.py [options]

主なオプション:
  --base REF         git diff の比較ベースブランチ/コミット (指定時は差分ベース検証を実行)
  --files FILE ...   git diff の代わりに検査対象ファイルパスを直接指定する
  --committed-only   作業ツリーの未コミット差分を含めず base...HEAD のみ検査する
  --strict           ブロッキング違反がある場合に終了コード 1 を返す
                     (--base / --files 指定時は差分違反・LGPLリスク・bin DLL 誤追跡をエラーとし、
                      ベース以前からの既存未記載パッケージは警告として出力)
  --strict-all       --base / --files 指定時でも全量パッケージ不整合をエラーとして終了コード 1 を返す
  --repo-root PATH   対象リポジトリルートを明示指定する (テスト用、省略時は自動解決)
  --markdown         Issue / PR 本文に貼り付け可能な Markdown 形式で出力する
  --json             JSON 形式で出力する
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass


DEFAULT_REPO_ROOT = Path(__file__).resolve().parent.parent
NOTICES_REL_PATH = "THIRD-PARTY-NOTICES.md"

PRODUCT_CSPROJ_PATHS: Tuple[str, ...] = (
    "AudioEffector/AudioEffector.csproj",
    "AudioEffector.Domain/AudioEffector.Domain.csproj",
    "AudioEffector.Application/AudioEffector.Application.csproj",
    "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
    "AudioEffector.McpServer/AudioEffector.McpServer.csproj",
)

LGPL_PACKAGES: Tuple[str, ...] = (
    "TagLibSharp",
)

EXTERNAL_ASSET_PATTERNS: Tuple[str, ...] = (
    "AudioEffector/Assets/**",
)

MODEL_DEFINITION_PATTERNS: Tuple[str, ...] = (
    "AudioEffector.Infrastructure/LrcGeneration/*Model*.cs",
    "AudioEffector.Infrastructure/**/Model*Manager*.cs",
    "AudioEffector.Domain/Services/ILocalSpeechModelManager.cs",
    "AudioEffector.Domain/ValueObjects/LocalSpeechModelInfo.cs",
)

TEST_PROJECT_PREFIX_RE = re.compile(r"^[^/]+\.Tests/", re.IGNORECASE)
BIN_DLL_RE = re.compile(r"(?:^|/)bin/.+\.dll$", re.IGNORECASE)
HEADING_RE = re.compile(r"^\s*#{2,3}\s+(.+?)\s*$")
VERSION_LINE_RE = re.compile(
    r"^\s*[-*]?\s*(?:\*\*)?Version(?:\*\*)?\s*:\s*(?:\*\*)?\s*`?([^\s*`]+)`?",
    re.IGNORECASE,
)
TABLE_ROW_RE = re.compile(r"^\s*\|(.+)\|\s*$")


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
        prefix, _, suffix = norm_pattern.partition("**")
        if prefix and not norm_path.lower().startswith(prefix.lower()):
            return False
        remainder = norm_path[len(prefix):]
        suffix = suffix.lstrip("/")
        if not suffix:
            return True
        if fnmatch.fnmatch(remainder.lower(), suffix.lower()):
            return True
        parts = remainder.split("/")
        for i in range(len(parts)):
            if fnmatch.fnmatch("/".join(parts[i:]).lower(), suffix.lower()):
                return True
        return False

    return fnmatch.fnmatch(norm_path.lower(), norm_pattern.lower())


def is_test_project_path(rel_path: str) -> bool:
    """パスがテストプロジェクト配下かを判定する。"""
    norm = normalize_path(rel_path)
    return bool(TEST_PROJECT_PREFIX_RE.match(norm)) or norm.lower().endswith(".tests.csproj")


def is_product_csproj(rel_path: str) -> bool:
    """パスが製品プロジェクトの .csproj かを判定する。"""
    norm = normalize_path(rel_path)
    product_lower = {p.lower() for p in PRODUCT_CSPROJ_PATHS}
    return norm.lower() in product_lower


def run_git(args: Sequence[str], repo_root: Path) -> Tuple[int, str, str]:
    """git コマンドを UTF-8 で安全に実行する。"""
    cmd = ["git", "-c", "core.quotepath=false", *args]
    try:
        proc = subprocess.run(
            cmd,
            cwd=str(repo_root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
            check=False,
        )
        return proc.returncode, proc.stdout, proc.stderr
    except (OSError, subprocess.TimeoutExpired) as exc:
        return 1, "", str(exc)


def resolve_base_ref(base_ref: str, repo_root: Path) -> Optional[str]:
    """比較ベース参照（ローカルまたは origin/<base>）を解決する。"""
    candidates = [base_ref]
    if not base_ref.startswith("origin/"):
        candidates.append(f"origin/{base_ref}")
    for cand in candidates:
        rc, out, _ = run_git(["rev-parse", "--verify", "--quiet", f"{cand}^{{commit}}"], repo_root)
        if rc == 0 and out.strip():
            return cand
    return None


def resolve_merge_base(resolved_ref: str, repo_root: Path) -> Optional[str]:
    """resolved_ref と HEAD のマージベースコミット SHA を取得する。"""
    rc, out, _ = run_git(["merge-base", resolved_ref, "HEAD"], repo_root)
    if rc == 0 and out.strip():
        return out.strip().splitlines()[0].strip()
    return None


def collect_changed_files(
    base_ref: str,
    include_working: bool,
    repo_root: Path,
) -> Tuple[List[str], Set[str], Set[str], Optional[str], Optional[str], Optional[str]]:
    """
    base_ref...HEAD および作業ツリーの差分ファイル一覧を取得する。
    Returns:
        (changed_files, added_files, untracked_files, resolved_ref, merge_base_commit, base_error)
    """
    changed: Set[str] = set()
    added: Set[str] = set()
    untracked: Set[str] = set()

    resolved_ref = resolve_base_ref(base_ref, repo_root)
    if resolved_ref is None:
        return (
            [],
            set(),
            set(),
            None,
            None,
            f"比較ベース '{base_ref}'（および 'origin/{base_ref}'）を解決できませんでした。ブランチ名またはコミット参照を確認してください。",
        )

    merge_base_commit = resolve_merge_base(resolved_ref, repo_root)
    if merge_base_commit is None:
        return (
            [],
            set(),
            set(),
            resolved_ref,
            None,
            f"比較ベース '{resolved_ref}' と HEAD のマージベースを解決できませんでした。",
        )

    rc, out, err = run_git(["diff", "--name-status", f"{merge_base_commit}..HEAD"], repo_root)
    if rc != 0:
        return (
            [],
            set(),
            set(),
            resolved_ref,
            merge_base_commit,
            f"比較ベース '{resolved_ref}' ({merge_base_commit}) からの差分取得に失敗しました: {err.strip()}",
        )

    for line in out.splitlines():
        if not line.strip():
            continue
        parts = line.split("\t")
        status = parts[0].strip()
        path_item = normalize_path(parts[-1])
        if path_item:
            changed.add(path_item)
            if status.startswith("A"):
                added.add(path_item)

    if include_working:
        for git_args in (
            ["diff", "--name-status", "--cached"],
            ["diff", "--name-status"],
        ):
            rc_w, out_w, _ = run_git(git_args, repo_root)
            if rc_w == 0:
                for line in out_w.splitlines():
                    if not line.strip():
                        continue
                    parts = line.split("\t")
                    status = parts[0].strip()
                    path_item = normalize_path(parts[-1])
                    if path_item:
                        changed.add(path_item)
                        if status.startswith("A"):
                            added.add(path_item)

        rc_u, out_u, _ = run_git(["ls-files", "--others", "--exclude-standard"], repo_root)
        if rc_u == 0:
            for line in out_u.splitlines():
                path_item = normalize_path(line)
                if path_item:
                    changed.add(path_item)
                    added.add(path_item)
                    untracked.add(path_item)

    return sorted(changed), added, untracked, resolved_ref, merge_base_commit, None


def parse_csproj_xml(xml_text: str) -> Tuple[Dict[str, Tuple[str, str]], bool]:
    """
    .csproj / .props / .pubxml の XML テキストから
    (パッケージ参照マップ: lower_name -> (orig_name, version), PublishSingleFile が true か)
    を抽出する。
    """
    packages: Dict[str, Tuple[str, str]] = {}
    publish_single_file = False

    if not xml_text.strip():
        return packages, publish_single_file

    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError:
        return packages, publish_single_file

    for elem in root.iter():
        tag = elem.tag.split("}", 1)[-1]
        if tag == "PackageReference":
            name = (elem.attrib.get("Include") or elem.attrib.get("Update") or "").strip()
            version = (elem.attrib.get("Version") or "").strip()
            if not version:
                for child in elem:
                    child_tag = child.tag.split("}", 1)[-1]
                    if child_tag == "Version" and child.text:
                        version = child.text.strip()
                        break
            if name:
                packages[name.lower()] = (name, version)
        elif tag == "PublishSingleFile":
            if elem.text and elem.text.strip().lower() == "true":
                publish_single_file = True

    return packages, publish_single_file


def load_product_packages(repo_root: Path) -> Tuple[Dict[str, Dict[str, Any]], List[str]]:
    """
    全製品 .csproj から PackageReference を収集し、
    (lower_name -> {"name", "versions", "projects"}, PublishSingleFile 有効なファイル一覧) を返す。
    """
    merged: Dict[str, Dict[str, Any]] = {}
    single_file_sources: List[str] = []

    for rel_path in PRODUCT_CSPROJ_PATHS:
        full_path = repo_root / rel_path
        if not full_path.is_file():
            continue
        xml_text = full_path.read_text(encoding="utf-8-sig", errors="replace")
        pkgs, has_single_file = parse_csproj_xml(xml_text)
        if has_single_file:
            single_file_sources.append(rel_path)
        for key, (orig_name, version) in pkgs.items():
            entry = merged.setdefault(
                key,
                {"name": orig_name, "versions": [], "projects": []},
            )
            if version and version not in entry["versions"]:
                entry["versions"].append(version)
            if rel_path not in entry["projects"]:
                entry["projects"].append(rel_path)

    # Directory.Build.props / PublishProfiles/*.pubxml での PublishSingleFile も検査
    props_path = repo_root / "Directory.Build.props"
    if props_path.is_file():
        _, has_sf = parse_csproj_xml(props_path.read_text(encoding="utf-8-sig", errors="replace"))
        if has_sf:
            single_file_sources.append("Directory.Build.props")

    # 製品 5 プロジェクト直下の Properties/PublishProfiles/*.pubxml のみを直接走査し、
    # .agent_worktrees/・.git/・.agent_build/・bin/・obj/・*.Tests/ を巻き込まない
    for csproj_rel in PRODUCT_CSPROJ_PATHS:
        profiles_dir = repo_root / Path(csproj_rel).parent / "Properties" / "PublishProfiles"
        if not profiles_dir.is_dir():
            continue
        for pubxml in sorted(profiles_dir.glob("*.pubxml")):
            if not pubxml.is_file():
                continue
            try:
                rel = normalize_path(str(pubxml.relative_to(repo_root)))
            except ValueError:
                continue
            _, has_sf = parse_csproj_xml(pubxml.read_text(encoding="utf-8-sig", errors="replace"))
            if has_sf:
                single_file_sources.append(rel)

    return merged, sorted(set(single_file_sources))


def parse_third_party_notices(markdown_text: str) -> Dict[str, Dict[str, str]]:
    """
    THIRD-PARTY-NOTICES.md からパッケージ名・バージョン一覧を抽出する。
    対応形式:
      1. 見出し形式:
         ## PackageName
         - **Version:** 1.2.3
      2. Markdown テーブル形式:
         | PackageName | 1.2.3 | MIT | ... |
    """
    notices: Dict[str, Dict[str, str]] = {}
    in_code_fence = False
    current_pkg_key: Optional[str] = None

    for line in markdown_text.splitlines():
        stripped = line.strip()
        if stripped.startswith("```") or stripped.startswith("~~~"):
            in_code_fence = not in_code_fence
            continue
        if in_code_fence:
            continue

        m_head = HEADING_RE.match(line)
        if m_head:
            heading_text = m_head.group(1).strip().strip("`")
            # 括弧注記（例: "Whisper.net (MIT)"）がある場合は先頭トークンも認識
            pkg_name = heading_text.split(" (", 1)[0].strip()
            if pkg_name and not pkg_name.lower().startswith("third-party"):
                current_pkg_key = pkg_name.lower()
                notices.setdefault(current_pkg_key, {"name": pkg_name, "version": ""})
            else:
                current_pkg_key = None
            continue

        if current_pkg_key is not None:
            m_ver = VERSION_LINE_RE.match(line)
            if m_ver and not notices[current_pkg_key]["version"]:
                notices[current_pkg_key]["version"] = m_ver.group(1).strip().strip("`")
                continue

        # テーブル行の補助パース（セパレータ行・ヘッダー行はスキップ）
        m_tbl = TABLE_ROW_RE.match(line)
        if m_tbl:
            cells = [c.strip().strip("`*") for c in m_tbl.group(1).split("|")]
            if len(cells) >= 2:
                col0, col1 = cells[0], cells[1]
                if not col0 or set(col0) <= {"-", ":", " "}:
                    continue
                if col0.lower() in ("package", "library", "パッケージ", "ライブラリ", "name", "名称"):
                    continue
                if re.match(r"^\d+\.\d+", col1):
                    notices.setdefault(col0.lower(), {"name": col0, "version": col1})

    return notices


def load_base_csproj_packages(
    resolved_base_ref: str,
    csproj_rel_path: str,
    repo_root: Path,
) -> Dict[str, Tuple[str, str]]:
    """指定ベースコミット時点の .csproj から PackageReference を抽出する。"""
    rc, out, _ = run_git(["show", f"{resolved_base_ref}:{csproj_rel_path}"], repo_root)
    if rc != 0:
        return {}
    pkgs, _ = parse_csproj_xml(out)
    return pkgs


def detect_tracked_bin_dlls(
    repo_root: Path,
    files_override: Optional[Sequence[str]] = None,
) -> List[str]:
    """bin/ 配下の DLL ファイルが Git 追跡対象に含まれていないか検出する。"""
    if files_override is not None:
        candidates = [normalize_path(f) for f in files_override]
    else:
        rc, out, _ = run_git(["ls-files"], repo_root)
        if rc != 0:
            return []
        candidates = [normalize_path(line) for line in out.splitlines() if line.strip()]

    return sorted(
        f for f in candidates
        if BIN_DLL_RE.search(f) and not f.lower().startswith(".agent_build/")
    )


def analyze_license_sync(
    repo_root: Path,
    base_ref: Optional[str] = None,
    files: Optional[Sequence[str]] = None,
    committed_only: bool = False,
) -> Dict[str, Any]:
    """ライセンス記載同期の総合解析を実行し、構造化辞書を返す。"""
    notices_file = repo_root / NOTICES_REL_PATH
    notices_exists = notices_file.is_file()
    notices_text = notices_file.read_text(encoding="utf-8-sig", errors="replace") if notices_exists else ""
    notice_map = parse_third_party_notices(notices_text)

    product_map, single_file_sources = load_product_packages(repo_root)

    # 観点 1: 全量整合チェック
    missing_packages: List[Dict[str, Any]] = []
    version_mismatches: List[Dict[str, Any]] = []

    for key in sorted(product_map.keys()):
        prod = product_map[key]
        csproj_versions = prod["versions"]
        if key not in notice_map:
            missing_packages.append({
                "name": prod["name"],
                "versions": csproj_versions,
                "projects": prod["projects"],
            })
        else:
            notice_ver = notice_map[key]["version"]
            if csproj_versions and notice_ver and not all(v == notice_ver for v in csproj_versions):
                version_mismatches.append({
                    "name": prod["name"],
                    "csproj_versions": csproj_versions,
                    "notice_version": notice_ver,
                    "projects": prod["projects"],
                })
            elif csproj_versions and not notice_ver:
                version_mismatches.append({
                    "name": prod["name"],
                    "csproj_versions": csproj_versions,
                    "notice_version": "",
                    "projects": prod["projects"],
                })

    # モード判定・変更ファイル取得
    mode = "all"
    changed_files: List[str] = []
    added_files: Set[str] = set()
    untracked_files: Set[str] = set()
    resolved_base_ref: Optional[str] = None
    merge_base_commit: Optional[str] = None
    base_error: Optional[str] = None

    if files is not None:
        mode = "files"
        changed_files = sorted({normalize_path(f) for f in files if f.strip()})
        added_files = set(changed_files)
    elif base_ref is not None:
        mode = "base"
        (
            changed_files,
            added_files,
            untracked_files,
            resolved_base_ref,
            merge_base_commit,
            base_error,
        ) = collect_changed_files(
            base_ref=base_ref,
            include_working=not committed_only,
            repo_root=repo_root,
        )

    notices_updated = any(f.lower() == NOTICES_REL_PATH.lower() for f in changed_files)

    # 観点 2: 差分ベースのパッケージ変更・トリガー解析
    diff_changed_packages: List[Dict[str, Any]] = []
    diff_package_violations: List[Dict[str, Any]] = []
    review_triggers: List[Dict[str, Any]] = []

    if mode in ("base", "files") and base_error is None:
        changed_product_csprojs = [f for f in changed_files if is_product_csproj(f)]

        for csproj_rel in changed_product_csprojs:
            full_csproj = repo_root / csproj_rel
            curr_pkgs: Dict[str, Tuple[str, str]] = {}
            if full_csproj.is_file():
                curr_pkgs, _ = parse_csproj_xml(full_csproj.read_text(encoding="utf-8-sig", errors="replace"))

            if mode == "base" and merge_base_commit is not None:
                base_pkgs = load_base_csproj_packages(merge_base_commit, csproj_rel, repo_root)
                for key, (orig_name, curr_ver) in sorted(curr_pkgs.items()):
                    if key not in base_pkgs:
                        change_kind = "added"
                        prev_ver = ""
                    elif base_pkgs[key][1] != curr_ver:
                        change_kind = "updated"
                        prev_ver = base_pkgs[key][1]
                    else:
                        continue

                    pkg_change = {
                        "name": orig_name,
                        "change": change_kind,
                        "previous_version": prev_ver,
                        "current_version": curr_ver,
                        "project": csproj_rel,
                    }
                    diff_changed_packages.append(pkg_change)

                    notice_entry = notice_map.get(key)
                    if notice_entry is None:
                        diff_package_violations.append({
                            **pkg_change,
                            "reason": f"{NOTICES_REL_PATH} に '{orig_name}' ({curr_ver}) の記載がありません。",
                        })
                    elif curr_ver and notice_entry["version"] != curr_ver:
                        diff_package_violations.append({
                            **pkg_change,
                            "reason": (
                                f"{NOTICES_REL_PATH} の '{orig_name}' バージョン ({notice_entry['version'] or '未記載'}) が "
                                f"更新後バージョン ({curr_ver}) と一致しません。"
                            ),
                        })
            else:
                # --files モード（またはベース未解決時）は対象製品 .csproj に含まれる不整合パッケージを検出
                for key, (orig_name, curr_ver) in sorted(curr_pkgs.items()):
                    notice_entry = notice_map.get(key)
                    if notice_entry is None:
                        diff_package_violations.append({
                            "name": orig_name,
                            "change": "files_check",
                            "previous_version": "",
                            "current_version": curr_ver,
                            "project": csproj_rel,
                            "reason": f"{NOTICES_REL_PATH} に '{orig_name}' ({curr_ver}) の記載がありません。",
                        })
                    elif curr_ver and notice_entry["version"] != curr_ver:
                        diff_package_violations.append({
                            "name": orig_name,
                            "change": "files_check",
                            "previous_version": "",
                            "current_version": curr_ver,
                            "project": csproj_rel,
                            "reason": (
                                f"{NOTICES_REL_PATH} の '{orig_name}' バージョン ({notice_entry['version'] or '未記載'}) が "
                                f".csproj のバージョン ({curr_ver}) と一致しません。"
                            ),
                        })

        # 外部アセット新規追加・モデル定義変更トリガーの検知
        for rel in changed_files:
            if is_test_project_path(rel):
                continue
            if any(match_pattern(rel, pat) for pat in EXTERNAL_ASSET_PATTERNS):
                if rel in added_files and not notices_updated:
                    review_triggers.append({
                        "category": "external_asset",
                        "file": rel,
                        "message": f"新規外部アセット '{rel}' が追加されました。出典・ライセンス表記（{NOTICES_REL_PATH}）の更新要否を確認してください。",
                    })
            elif any(match_pattern(rel, pat) for pat in MODEL_DEFINITION_PATTERNS):
                if not notices_updated:
                    review_triggers.append({
                        "category": "model_definition",
                        "file": rel,
                        "message": f"モデルダウンロード定義 '{rel}' が変更されました。モデルライセンス表記（{NOTICES_REL_PATH}）の更新要否を確認してください。",
                    })

    # PublishSingleFile + LGPL ライブラリ静的結合リスク検知
    lgpl_single_file_risks: List[Dict[str, Any]] = []
    present_lgpl = [
        product_map[pkg.lower()]["name"]
        for pkg in LGPL_PACKAGES
        if pkg.lower() in product_map
    ]
    if single_file_sources and present_lgpl:
        for src in single_file_sources:
            lgpl_single_file_risks.append({
                "file": src,
                "lgpl_packages": present_lgpl,
                "message": (
                    f"'{src}' で PublishSingleFile=true が有効化されています。"
                    f"LGPL ライブラリ（{', '.join(present_lgpl)}）との静的結合によるライセンス違反リスクがあります。"
                ),
            })

    # bin/ 配下 DLL の Git 誤追跡検知（git rm --cached 後に .gitignore 配下でローカル残存するファイルは除外し、
    # 現在 Git 追跡中または .gitignore 非該当の新規未追跡ファイルのみ対象とする）
    tracked_bin_dlls = detect_tracked_bin_dlls(
        repo_root=repo_root,
        files_override=changed_files if mode == "files" else None,
    )
    tracked_bin_lower = {p.lower() for p in tracked_bin_dlls}
    untracked_lower = {p.lower() for p in untracked_files}
    if mode == "base":
        diff_tracked_bin_dlls: List[str] = sorted(
            f for f in changed_files
            if BIN_DLL_RE.search(f)
            and not f.lower().startswith(".agent_build/")
            and (f.lower() in tracked_bin_lower or f.lower() in untracked_lower)
        )
    elif mode == "files":
        diff_tracked_bin_dlls = list(tracked_bin_dlls)
    else:
        diff_tracked_bin_dlls = list(tracked_bin_dlls)

    # ブロッキングエラーと警告の集約
    blocking_errors: List[str] = []
    warnings: List[str] = []

    if base_error is not None:
        blocking_errors.append(base_error)

    if not notices_exists:
        blocking_errors.append(f"{NOTICES_REL_PATH} がリポジトリルートに存在しません。")

    for risk in lgpl_single_file_risks:
        blocking_errors.append(risk["message"])

    if mode == "all":
        for dll_path in tracked_bin_dlls:
            blocking_errors.append(
                f"ビルド出力 DLL '{dll_path}' が Git 追跡対象に含まれています（配布物・サードパーティバイナリの誤コミット防止）。"
            )
        for item in missing_packages:
            vers = ", ".join(item["versions"]) if item["versions"] else "バージョン未指定"
            blocking_errors.append(
                f"未記載パッケージ: {item['name']} ({vers}) [参照元: {', '.join(item['projects'])}]"
            )
        for item in version_mismatches:
            vers = ", ".join(item["csproj_versions"])
            blocking_errors.append(
                f"バージョン不一致: {item['name']} (.csproj: {vers} / {NOTICES_REL_PATH}: {item['notice_version'] or '未記載'})"
            )
    else:
        for dll_path in diff_tracked_bin_dlls:
            blocking_errors.append(
                f"ビルド出力 DLL '{dll_path}' が変更・追跡対象に含まれています（配布物・サードパーティバイナリの誤コミット防止）。"
            )
        diff_dll_set = {d.lower() for d in diff_tracked_bin_dlls}
        for dll_path in tracked_bin_dlls:
            if dll_path.lower() not in diff_dll_set:
                warnings.append(
                    f"既存追跡 bin/ DLL: '{dll_path}' が Git 追跡対象に残留しています（#436 等で除外対象）。"
                )
        for viol in diff_package_violations:
            blocking_errors.append(f"差分パッケージ不整合 ({viol['project']}): {viol['reason']}")

        # ベース以前からの既存未記載・不一致パッケージは警告として可視化（#436 完了後は 0 件になる）
        diff_viol_names = {v["name"].lower() for v in diff_package_violations}
        for item in missing_packages:
            if item["name"].lower() not in diff_viol_names:
                vers = ", ".join(item["versions"]) if item["versions"] else "バージョン未指定"
                warnings.append(
                    f"既存未記載パッケージ: {item['name']} ({vers}) [参照元: {', '.join(item['projects'])}]"
                )
        for item in version_mismatches:
            if item["name"].lower() not in diff_viol_names:
                vers = ", ".join(item["csproj_versions"])
                warnings.append(
                    f"既存バージョン不一致: {item['name']} (.csproj: {vers} / {NOTICES_REL_PATH}: {item['notice_version'] or '未記載'})"
                )

    for trig in review_triggers:
        warnings.append(trig["message"])

    product_packages_list = [
        {
            "name": product_map[k]["name"],
            "versions": product_map[k]["versions"],
            "projects": product_map[k]["projects"],
        }
        for k in sorted(product_map.keys())
    ]
    notice_packages_list = [
        {"name": notice_map[k]["name"], "version": notice_map[k]["version"]}
        for k in sorted(notice_map.keys())
    ]

    needs_update = bool(blocking_errors or review_triggers)
    passed = len(blocking_errors) == 0

    if passed:
        summary = (
            f"ライセンス同期チェック OK (製品パッケージ: {len(product_packages_list)}件, "
            f"差分違反: {len(diff_package_violations)}件, リスク検知: 0件, "
            f"既存未記載/警告: {len(warnings)}件)。"
        )
    else:
        summary = (
            f"ライセンス同期チェックで {len(blocking_errors)} 件の違反を検出しました "
            f"(差分違反: {len(diff_package_violations)}件, LGPL単一ファイルリスク: {len(lgpl_single_file_risks)}件, "
            f"bin DLL誤追跡: {len(diff_tracked_bin_dlls)}件, 未記載パッケージ: {len(missing_packages)}件)。"
        )

    return {
        "mode": mode,
        "base_ref": base_ref,
        "resolved_base_ref": resolved_base_ref,
        "merge_base_commit": merge_base_commit,
        "notices_path": NOTICES_REL_PATH,
        "notices_exists": notices_exists,
        "notices_updated": notices_updated,
        "changed_files": changed_files,
        "product_packages": product_packages_list,
        "notice_packages": notice_packages_list,
        "missing_packages": missing_packages,
        "version_mismatches": version_mismatches,
        "diff_changed_packages": diff_changed_packages,
        "diff_package_violations": diff_package_violations,
        "lgpl_single_file_risks": lgpl_single_file_risks,
        "tracked_bin_dlls": tracked_bin_dlls,
        "diff_tracked_bin_dlls": diff_tracked_bin_dlls,
        "review_triggers": review_triggers,
        "blocking_errors": blocking_errors,
        "warnings": warnings,
        "needs_update": needs_update,
        "passed": passed,
        "summary": summary,
    }


def print_text_report(result: Dict[str, Any]) -> None:
    """人間・エージェント向けのテキスト形式で結果を出力する。"""
    mode_label = (
        f"--base {result['base_ref']}"
        if result["mode"] == "base"
        else (f"--files ({len(result['changed_files'])}件)" if result["mode"] == "files" else "全量チェック")
    )
    print("=" * 72)
    print(f"サードパーティライセンス記載同期チェック結果 ({mode_label})")
    print("=" * 72)
    print(f"製品パッケージ数     : {len(result['product_packages'])}")
    print(f"THIRD-PARTY 記載数   : {len(result['notice_packages'])}")
    print(f"THIRD-PARTY 更新有無 : {'更新あり' if result['notices_updated'] else '未更新'}")
    print()

    if result["blocking_errors"]:
        print("[FAIL] ブロッキング違反:")
        for err in result["blocking_errors"]:
            print(f"  - {err}")
        print()

    if result["review_triggers"]:
        print("[WARN] ライセンス記載更新の要否確認トリガー:")
        for trig in result["review_triggers"]:
            print(f"  - [{trig['category']}] {trig['message']}")
        print()

    existing_warns = [w for w in result["warnings"] if w not in {t["message"] for t in result["review_triggers"]}]
    if existing_warns:
        print("[INFO] 既存未記載・不一致パッケージ（参考・#436 等で解消対象）:")
        for w in existing_warns:
            print(f"  - {w}")
        print()

    print(f"[{'OK' if result['passed'] else 'FAIL'}] {result['summary']}")


def print_markdown_report(result: Dict[str, Any]) -> None:
    """Issue / PR 本文へ貼り付け可能な Markdown 形式で出力する。"""
    print("### サードパーティライセンス記載確認結果 (`THIRD-PARTY-NOTICES.md`)")
    if result["blocking_errors"]:
        for err in result["blocking_errors"]:
            print(f"- [ ] **要対応**: {err}")
    elif result["review_triggers"]:
        for trig in result["review_triggers"]:
            print(f"- [ ] **要確認**: {trig['message']}")
    elif result["notices_updated"]:
        print(f"- [x] `{NOTICES_REL_PATH}` を更新済み（差分違反 0 件）")
    else:
        print(f"- [x] 更新不要（新規パッケージ追加・バージョン変更・ライセンスリスクなし / `check_license_sync.py` 合格）")


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="製品 .csproj の依存パッケージおよび関連変更に対する THIRD-PARTY-NOTICES.md の更新要否を検証します。"
    )
    parser.add_argument(
        "--base",
        default=None,
        help="git diff の比較ベースブランチ/コミット（指定時は差分ベース検証を実行）",
    )
    parser.add_argument(
        "--files",
        nargs="+",
        default=None,
        help="git diff の代わりに検査対象のファイルパスを直接指定する",
    )
    parser.add_argument(
        "--committed-only",
        action="store_true",
        help="作業ツリーの未コミット差分を含めず base...HEAD のみ検査する",
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        help="ブロッキング違反がある場合に終了コード 1 を返す",
    )
    parser.add_argument(
        "--strict-all",
        action="store_true",
        help="--base / --files 指定時であっても既存負債を含む全量パッケージ不整合を終了コード 1 とする",
    )
    parser.add_argument(
        "--repo-root",
        default=None,
        help="対象リポジトリルートを明示指定する（省略時は自動解決）",
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

    args = parser.parse_args(argv)
    repo_root = Path(args.repo_root).resolve() if args.repo_root else DEFAULT_REPO_ROOT

    result = analyze_license_sync(
        repo_root=repo_root,
        base_ref=args.base,
        files=args.files,
        committed_only=args.committed_only,
    )

    has_existing_debt = bool(
        result["missing_packages"]
        or result["version_mismatches"]
        or result["tracked_bin_dlls"]
    )
    strict_all_failed = bool(args.strict_all and (result["blocking_errors"] or has_existing_debt))
    if strict_all_failed:
        was_passed = result["passed"]
        result["passed"] = False
        result["needs_update"] = True
        if has_existing_debt:
            debt_msg = (
                f"全量厳格チェック (--strict-all): 未記載パッケージ {len(result['missing_packages'])} 件 / "
                f"バージョン不一致 {len(result['version_mismatches'])} 件 / 追跡 bin DLL {len(result['tracked_bin_dlls'])} 件"
            )
            if debt_msg not in result["blocking_errors"]:
                result["blocking_errors"] = list(result["blocking_errors"]) + [debt_msg]
        if was_passed:
            result["summary"] = (
                f"全量ライセンス同期チェック (--strict-all) で不整合を検出しました "
                f"(未記載: {len(result['missing_packages'])}件, 不一致: {len(result['version_mismatches'])}件, 追跡bin DLL: {len(result['tracked_bin_dlls'])}件)。"
            )

    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    elif args.markdown:
        print_markdown_report(result)
    else:
        print_text_report(result)

    if strict_all_failed:
        return 1
    if args.strict and not result["passed"]:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
