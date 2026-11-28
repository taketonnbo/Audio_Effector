#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/check_license_sync.py の単体テスト。"""

from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent.parent
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import check_license_sync  # noqa: E402


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
        raise AssertionError(f"git {' '.join(args)} failed: {result.stderr}")
    return result


def _write_file(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _sample_csproj(packages: list[tuple[str, str]], publish_single_file: bool = False) -> str:
    sf_tag = "\n    <PublishSingleFile>true</PublishSingleFile>" if publish_single_file else ""
    refs = "\n".join(
        f'    <PackageReference Include="{name}" Version="{ver}" />'
        for name, ver in packages
    )
    return (
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
        "  <PropertyGroup>\n"
        "    <TargetFramework>net8.0</TargetFramework>"
        f"{sf_tag}\n"
        "  </PropertyGroup>\n"
        "  <ItemGroup>\n"
        f"{refs}\n"
        "  </ItemGroup>\n"
        "</Project>\n"
    )


def _sample_notices(entries: list[tuple[str, str]]) -> str:
    lines = ["# Third-Party Notices", "", "This software uses:", ""]
    for name, ver in entries:
        lines.extend([
            f"## {name}",
            f"- **Version:** {ver}",
            "- **License:** MIT License",
            "",
        ])
    return "\n".join(lines)


class CheckLicenseSyncUnitTests(unittest.TestCase):
    """check_license_sync.py の整合検証・差分トリガー・リスク検知・CLI 出力テスト。"""

    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory(prefix="ae_lic_test_")
        self.repo = Path(self._tmp.name)

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def _init_git_repo(self) -> None:
        _run_git(["init"], cwd=self.repo)
        _run_git(["config", "user.name", "Test User"], cwd=self.repo)
        _run_git(["config", "user.email", "test@example.com"], cwd=self.repo)
        _run_git(["checkout", "-b", "develop"], cwd=self.repo)

    def _run_cli(self, argv: list[str]) -> tuple[int, str]:
        buf = io.StringIO()
        with redirect_stdout(buf):
            rc = check_license_sync.main(["--repo-root", str(self.repo), *argv])
        return rc, buf.getvalue()

    def test_全製品パッケージがTHIRD_PARTY_NOTICESに一致記載されている場合は正常終了すること(self) -> None:
        # Arrange
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("TagLibSharp", "2.3.0")]),
        )
        _write_file(
            self.repo / "AudioEffector/AudioEffector.csproj",
            _sample_csproj([("Microsoft.Extensions.DependencyInjection", "8.0.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([
                ("NAudio", "2.2.1"),
                ("TagLibSharp", "2.3.0"),
                ("Microsoft.Extensions.DependencyInjection", "8.0.0"),
            ]),
        )

        # Act
        rc, out = self._run_cli(["--strict", "--json"])
        data = json.loads(out)

        # Assert
        self.assertEqual(rc, 0)
        self.assertTrue(data["passed"])
        self.assertEqual(data["missing_packages"], [])
        self.assertEqual(data["version_mismatches"], [])
        self.assertEqual(data["blocking_errors"], [])

    def test_全量モードで未記載パッケージとバージョン不一致を検出しstrictで非0終了すること(self) -> None:
        # Arrange
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("Whisper.net", "1.9.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("NAudio", "2.1.0")]),
        )

        # Act
        rc_strict, out_strict = self._run_cli(["--strict", "--json"])
        rc_normal, _ = self._run_cli(["--json"])
        data = json.loads(out_strict)

        # Assert
        self.assertEqual(rc_strict, 1)
        self.assertEqual(rc_normal, 0)
        self.assertFalse(data["passed"])
        self.assertEqual([p["name"] for p in data["missing_packages"]], ["Whisper.net"])
        self.assertEqual([p["name"] for p in data["version_mismatches"]], ["NAudio"])

    def test_baseモードで新規PackageReference追加とバージョン変更の未記載を検出し更新後は合格すること(self) -> None:
        # Arrange: ベースコミット（既存未記載 NLog を含む状態）
        self._init_git_repo()
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("NLog", "6.2.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("NAudio", "2.2.1")]),
        )
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: base"], cwd=self.repo)
        _run_git(["checkout", "-b", "feat/437-test"], cwd=self.repo)

        # Act 1: 差分なし（既存未記載 NLog のみ）→ --strict は警告で合格、--strict-all は非 0
        rc_base_ok, out_base_ok = self._run_cli(["--base", "develop", "--strict", "--json"])
        data_base_ok = json.loads(out_base_ok)
        rc_strict_all, _ = self._run_cli(["--base", "develop", "--strict", "--strict-all", "--json"])

        self.assertEqual(rc_base_ok, 0)
        self.assertTrue(data_base_ok["passed"])
        self.assertEqual(len(data_base_ok["missing_packages"]), 1)
        self.assertTrue(any("NLog" in w for w in data_base_ok["warnings"]))
        self.assertEqual(rc_strict_all, 1)

        # Act 2: 新規パッケージ追加＋NAudio バージョン更新（THIRD-PARTY-NOTICES.md 未更新）
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.3.0"), ("NLog", "6.2.0"), ("Whisper.net", "1.9.0")]),
        )
        rc_fail, out_fail = self._run_cli(["--base", "develop", "--strict", "--json"])
        data_fail = json.loads(out_fail)

        self.assertEqual(rc_fail, 1)
        self.assertFalse(data_fail["passed"])
        viol_names = sorted(v["name"] for v in data_fail["diff_package_violations"])
        self.assertEqual(viol_names, ["NAudio", "Whisper.net"])

        # Act 3: THIRD-PARTY-NOTICES.md を同期更新すると --strict で合格
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("NAudio", "2.3.0"), ("Whisper.net", "1.9.0")]),
        )
        rc_fixed, out_fixed = self._run_cli(["--base", "develop", "--strict", "--json"])
        data_fixed = json.loads(out_fixed)

        self.assertEqual(rc_fixed, 0)
        self.assertTrue(data_fixed["passed"])
        self.assertEqual(data_fixed["diff_package_violations"], [])
        self.assertTrue(data_fixed["notices_updated"])

    def test_テストプロジェクト専用パッケージの変更のみの場合は誤検知しないこと(self) -> None:
        # Arrange
        self._init_git_repo()
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1")]),
        )
        _write_file(
            self.repo / "AudioEffector.Tests/AudioEffector.Tests.csproj",
            _sample_csproj([("xunit", "2.9.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("NAudio", "2.2.1")]),
        )
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: base"], cwd=self.repo)
        _run_git(["checkout", "-b", "test/only-test-pkg"], cwd=self.repo)

        # テストプロジェクトに Moq を追加
        _write_file(
            self.repo / "AudioEffector.Tests/AudioEffector.Tests.csproj",
            _sample_csproj([("xunit", "2.9.0"), ("Moq", "4.20.72")]),
        )

        # Act
        rc_base, out_base = self._run_cli(["--base", "develop", "--strict", "--json"])
        rc_files, out_files = self._run_cli([
            "--files",
            "AudioEffector.Tests/AudioEffector.Tests.csproj",
            "--strict",
            "--json",
        ])

        # Assert
        self.assertEqual(rc_base, 0)
        self.assertEqual(rc_files, 0)
        self.assertEqual(json.loads(out_base)["missing_packages"], [])
        self.assertEqual(json.loads(out_files)["diff_package_violations"], [])

    def test_PublishSingleFileとLGPLリスクおよびbin配下DLL誤追跡をブロック検出すること(self) -> None:
        # Arrange: ベースコミット（既存追跡 bin DLL 1 件を含む状態）
        self._init_git_repo()
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("TagLibSharp", "2.3.0")]),
        )
        _write_file(
            self.repo / "AudioEffector/AudioEffector.csproj",
            _sample_csproj([], publish_single_file=False),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("TagLibSharp", "2.3.0")]),
        )
        _write_file(self.repo / "AudioEffector/bin/Release/Legacy.dll", "legacy")
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: base with legacy dll"], cwd=self.repo)
        _run_git(["checkout", "-b", "feat/437-risk"], cwd=self.repo)

        # Act 1: 差分なし（既存追跡 DLL のみ）→ --base --strict は警告で合格、--strict-all と全量モードは非 0
        rc_legacy_ok, out_legacy_ok = self._run_cli(["--base", "develop", "--strict", "--json"])
        rc_legacy_all, _ = self._run_cli(["--base", "develop", "--strict", "--strict-all", "--json"])
        rc_full_fail, _ = self._run_cli(["--strict", "--json"])
        self.assertEqual(rc_legacy_ok, 0)
        self.assertTrue(any("Legacy.dll" in w for w in json.loads(out_legacy_ok)["warnings"]))
        self.assertEqual(rc_legacy_all, 1)
        self.assertEqual(rc_full_fail, 1)

        # Act 2: PublishSingleFile=true 追加 + 新規 bin DLL 追加 → --base --strict で非 0
        _write_file(
            self.repo / "AudioEffector/AudioEffector.csproj",
            _sample_csproj([], publish_single_file=True),
        )
        _write_file(self.repo / "AudioEffector/bin/Release/Sample.dll", "dummy")
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: risky"], cwd=self.repo)

        rc, out = self._run_cli(["--base", "develop", "--strict", "--json"])
        data = json.loads(out)

        # Assert
        self.assertEqual(rc, 1)
        self.assertFalse(data["passed"])
        self.assertEqual(len(data["lgpl_single_file_risks"]), 1)
        self.assertIn("AudioEffector/bin/Release/Sample.dll", data["tracked_bin_dlls"])
        self.assertTrue(any("Sample.dll" in err for err in data["blocking_errors"]))

        # Act 3: PublishSingleFile を解除し、既存・新規の bin DLL を git rm で削除すれば合格すること
        _write_file(
            self.repo / "AudioEffector/AudioEffector.csproj",
            _sample_csproj([], publish_single_file=False),
        )
        _run_git(["rm", "-f", "AudioEffector/bin/Release/Legacy.dll", "AudioEffector/bin/Release/Sample.dll"], cwd=self.repo)
        rc_clean, out_clean = self._run_cli(["--base", "develop", "--strict", "--strict-all", "--json"])
        data_clean = json.loads(out_clean)
        self.assertEqual(rc_clean, 0)
        self.assertTrue(data_clean["passed"])
        self.assertEqual(data_clean["tracked_bin_dlls"], [])
        self.assertEqual(data_clean["diff_tracked_bin_dlls"], [])

    def test_filesモードと外部アセット追加およびモデル定義変更トリガーとmarkdown出力が動作すること(self) -> None:
        # Arrange
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("Whisper.net", "1.9.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("NAudio", "2.2.1")]),
        )

        # Act 1: --files に未記載パッケージを含む製品 .csproj を渡すと --strict で非 0
        rc_csproj, out_csproj = self._run_cli([
            "--files",
            "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            "--strict",
            "--json",
        ])
        data_csproj = json.loads(out_csproj)
        self.assertEqual(rc_csproj, 1)
        self.assertEqual([v["name"] for v in data_csproj["diff_package_violations"]], ["Whisper.net"])

        # Act 2: 新規外部アセット・モデル定義ファイル変更時は review_triggers に警告出力
        rc_trig, out_trig = self._run_cli([
            "--files",
            "AudioEffector/Assets/Images/new_bg.png",
            "AudioEffector.Infrastructure/LrcGeneration/WhisperModelManager.cs",
            "--strict",
            "--json",
        ])
        data_trig = json.loads(out_trig)
        self.assertEqual(rc_trig, 0)
        self.assertEqual(len(data_trig["review_triggers"]), 2)
        categories = {t["category"] for t in data_trig["review_triggers"]}
        self.assertEqual(categories, {"external_asset", "model_definition"})

        # Act 3: --markdown 出力
        rc_md, out_md = self._run_cli([
            "--files",
            "AudioEffector/Assets/Images/new_bg.png",
            "--markdown",
        ])
        self.assertEqual(rc_md, 0)
        self.assertIn("THIRD-PARTY-NOTICES.md", out_md)
        self.assertIn("要確認", out_md)


if __name__ == "__main__":
    unittest.main()
