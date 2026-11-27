#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/ 配下の PowerShell スクリプト (.ps1) の Windows PowerShell 5.1 互換性を検証する単体テスト。

Windows PowerShell 5.1 環境で頻発する以下の落とし穴を事前に機械検証する:
1. UTF-8 BOM 欠落による日本語文字列の Shift-JIS 誤解釈・パースエラー
2. PowerShell 7 専用構文（&& / || / 三項演算子等）の混入による AST パースエラー
3. git diff --name-only 実行時の core.quotepath=false 欠落による日本語パス 8 進エスケープ
4. [System.IO.File]::WriteAllText 利用時の PowerShell カレントディレクトリ基準パス解決漏れ
"""

from __future__ import annotations

import codecs
import json
import re
import subprocess
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
TOOLS_DIR = REPO_ROOT / "tools"
EXCLUDED_DIRS = {"bin", "obj", "__pycache__"}

GIT_DIFF_NAME_ONLY_RE = re.compile(r'diff"\s*,\s*"--name-only"|git\s+diff\s+--name-only', re.IGNORECASE)


def find_ps1_files(root: Path) -> list[Path]:
    """対象ディレクトリ配下の .ps1 ファイル一覧を返す（bin/obj 等は除外）。"""
    result: list[Path] = []
    for path in sorted(root.rglob("*.ps1")):
        if any(part in EXCLUDED_DIRS for part in path.parts):
            continue
        result.append(path)
    return result


class PowerShellEncodingTests(unittest.TestCase):
    """tools/ 配下の .ps1 スクリプトの文字コード・Windows PowerShell 5.1 互換性検証テスト。"""

    def test_tools配下の全ps1ファイルがUTF8_BOM付きで正常デコードできること(self) -> None:
        # Arrange
        ps1_files = find_ps1_files(TOOLS_DIR)
        self.assertGreater(len(ps1_files), 0, "tools/ 配下に .ps1 ファイルが見つかりません。")

        # Act & Assert
        for ps1_path in ps1_files:
            rel_path = ps1_path.relative_to(REPO_ROOT).as_posix()
            with self.subTest(file=rel_path):
                raw = ps1_path.read_bytes()
                self.assertTrue(
                    raw.startswith(codecs.BOM_UTF8),
                    f"{rel_path} に UTF-8 BOM がありません。Windows PowerShell 5.1 互換のため UTF-8 (BOM 付き) で保存してください。",
                )
                # BOM 除去後に UTF-8 として正常デコードできることも検証
                decoded = raw[len(codecs.BOM_UTF8):].decode("utf-8")
                self.assertGreater(len(decoded), 0)

    def test_tools配下の全ps1ファイルがPowerShell5_1パーサーで構文エラー0件であること(self) -> None:
        # Arrange
        ps1_files = find_ps1_files(TOOLS_DIR)
        self.assertGreater(len(ps1_files), 0)
        paths_json = json.dumps([str(p) for p in ps1_files], ensure_ascii=False)
        ps_script = (
            "$ErrorActionPreference = 'Stop'; "
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; "
            f"$paths = ConvertFrom-Json '{paths_json}'; "
            "$results = @(); "
            "foreach ($p in $paths) { "
            "  $tokens = $null; $errors = $null; "
            "  [void][System.Management.Automation.Language.Parser]::ParseFile($p, [ref]$tokens, [ref]$errors); "
            "  $errs = @($errors | ForEach-Object { \"$($_.Extent.StartLineNumber): $($_.Message)\" }); "
            "  $results += [ordered]@{ file = $p; errors = $errs }; "
            "}; "
            "ConvertTo-Json -InputObject $results -Depth 4"
        )

        # Act
        proc = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_script],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )

        # Assert
        self.assertEqual(proc.returncode, 0, msg=f"PowerShell AST パース実行に失敗しました:\n{proc.stderr}")
        parsed = json.loads(proc.stdout)
        if isinstance(parsed, dict):
            parsed = [parsed]
        for item in parsed:
            with self.subTest(file=item["file"]):
                self.assertEqual(
                    item["errors"],
                    [],
                    msg=f"{item['file']} に Windows PowerShell 5.1 構文エラーがあります: {item['errors']}",
                )

    def test_git_diff_name_only使用時にcore_quotepath_falseが指定されていること(self) -> None:
        # Arrange
        ps1_files = find_ps1_files(TOOLS_DIR)

        # Act & Assert
        for ps1_path in ps1_files:
            rel_path = ps1_path.relative_to(REPO_ROOT).as_posix()
            text = ps1_path.read_text(encoding="utf-8-sig")
            for lineno, line in enumerate(text.splitlines(), start=1):
                if GIT_DIFF_NAME_ONLY_RE.search(line):
                    with self.subTest(file=rel_path, line=lineno):
                        self.assertIn(
                            "core.quotepath=false",
                            line,
                            msg=(
                                f"{rel_path}:{lineno} の git diff --name-only に core.quotepath=false がありません。"
                                "日本語パス（設計/ 配下等）の 8 進エスケープを防ぐため -c core.quotepath=false を付与してください。"
                            ),
                        )

    def test_WriteAllText使用時にPowerShellカレントディレクトリ基準のパス解決を行っていること(self) -> None:
        # Arrange
        ps1_files = find_ps1_files(TOOLS_DIR)

        # Act & Assert
        for ps1_path in ps1_files:
            rel_path = ps1_path.relative_to(REPO_ROOT).as_posix()
            text = ps1_path.read_text(encoding="utf-8-sig")
            if "[System.IO.File]::WriteAllText" not in text:
                continue
            with self.subTest(file=rel_path):
                self.assertIn(
                    "GetUnresolvedProviderPathFromPSPath",
                    text,
                    msg=(
                        f"{rel_path} は [System.IO.File]::WriteAllText を使用していますが "
                        "GetUnresolvedProviderPathFromPSPath による PowerShell カレントディレクトリ基準のパス解決が見つかりません。"
                    ),
                )


if __name__ == "__main__":
    unittest.main()
