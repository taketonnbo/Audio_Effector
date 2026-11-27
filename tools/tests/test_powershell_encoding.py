#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/ 配下の PowerShell スクリプト (.ps1) の UTF-8 BOM 維持を検証する単体テスト。

Windows PowerShell 5.1 では UTF-8 BOM のない .ps1 ファイルが Shift-JIS として誤解釈され、
日本語文字列を含む場合にパースエラーを引き起こすため、全 .ps1 ファイルが UTF-8 with BOM で
保存されていることを機械的に保証する。
"""

from __future__ import annotations

import codecs
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
TOOLS_DIR = REPO_ROOT / "tools"
EXCLUDED_DIRS = {"bin", "obj", "__pycache__"}


def find_ps1_files(root: Path) -> list[Path]:
    """対象ディレクトリ配下の .ps1 ファイル一覧を返す（bin/obj 等は除外）。"""
    result: list[Path] = []
    for path in sorted(root.rglob("*.ps1")):
        if any(part in EXCLUDED_DIRS for part in path.parts):
            continue
        result.append(path)
    return result


class PowerShellEncodingTests(unittest.TestCase):
    """tools/ 配下の .ps1 スクリプトの文字コード検証テスト。"""

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


if __name__ == "__main__":
    unittest.main()
