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

    def test_git_rm_cached後にgitignore配下のbin_dllがディスク残存しても偽陽性ブロックしないこと(self) -> None:
        # Arrange
        self._init_git_repo()
        _write_file(self.repo / ".gitignore", "bin/\n")
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("NAudio", "2.2.1")]),
        )
        _write_file(self.repo / "AudioEffector/bin/Release/Legacy.dll", "legacy")
        _run_git(["add", "-f", "AudioEffector/bin/Release/Legacy.dll"], cwd=self.repo)
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: base with tracked dll"], cwd=self.repo)
        _run_git(["checkout", "-b", "fix/436-untrack-dll"], cwd=self.repo)

        # git rm --cached で追跡解除しつつローカルディスクにはファイルを残存させる
        _run_git(["rm", "--cached", "AudioEffector/bin/Release/Legacy.dll"], cwd=self.repo)
        _run_git(["commit", "-m", "chore: untrack legacy dll"], cwd=self.repo)
        self.assertTrue((self.repo / "AudioEffector/bin/Release/Legacy.dll").is_file())

        # Act
        rc, out = self._run_cli(["--base", "develop", "--strict", "--json"])
        data = json.loads(out)

        # Assert
        self.assertEqual(rc, 0)
        self.assertTrue(data["passed"])
        self.assertEqual(data["tracked_bin_dlls"], [])
        self.assertEqual(data["diff_tracked_bin_dlls"], [])

    def test_無効なbase参照はstrictで失敗しベース前進時もmerge_base基準で誤検知しないこと(self) -> None:
        # Arrange
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

        # 作業ブランチを分岐し、.csproj にコメントのみ追記（NLog には触れない）
        _run_git(["checkout", "-b", "feat/unrelated-csproj-edit"], cwd=self.repo)
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            "<!-- unrelated comment -->\n" + _sample_csproj([("NAudio", "2.2.1"), ("NLog", "6.2.0")]),
        )
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: comment only"], cwd=self.repo)

        # ベース側 (develop) を進めて NLog を 6.3.0 へ更新
        _run_git(["checkout", "develop"], cwd=self.repo)
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("NLog", "6.3.0")]),
        )
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: bump nlog on develop"], cwd=self.repo)
        _run_git(["checkout", "feat/unrelated-csproj-edit"], cwd=self.repo)

        # Act 1: 存在しない --base 参照は --strict で非 0
        rc_invalid, out_invalid = self._run_cli(["--base", "nonexistent-ref", "--strict", "--json"])
        data_invalid = json.loads(out_invalid)
        self.assertEqual(rc_invalid, 1)
        self.assertFalse(data_invalid["passed"])
        self.assertTrue(any("nonexistent-ref" in e for e in data_invalid["blocking_errors"]))

        # Act 2: 分岐後に develop が進んだ状態でも merge-base 比較のため NLog は誤検知されない
        rc_mb, out_mb = self._run_cli(["--base", "develop", "--strict", "--json"])
        data_mb = json.loads(out_mb)
        self.assertEqual(rc_mb, 0)
        self.assertTrue(data_mb["passed"])
        self.assertEqual(data_mb["diff_package_violations"], [])
        self.assertIsNotNone(data_mb["merge_base_commit"])

    def test_別ワークツリーのpubxmlを巻き込まず製品直下のpubxmlのみ検知すること(self) -> None:
        # Arrange
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("TagLibSharp", "2.3.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            _sample_notices([("TagLibSharp", "2.3.0")]),
        )
        # 別ワークツリー配下に PublishSingleFile=true の pubxml を配置
        _write_file(
            self.repo / ".agent_worktrees/issue-999/AudioEffector/Properties/PublishProfiles/Win.pubxml",
            "<Project><PropertyGroup><PublishSingleFile>true</PublishSingleFile></PropertyGroup></Project>\n",
        )

        # Act 1: 別ワークツリーの pubxml は無視されること
        rc_ok, out_ok = self._run_cli(["--strict", "--json"])
        self.assertEqual(rc_ok, 0)
        self.assertEqual(json.loads(out_ok)["lgpl_single_file_risks"], [])

        # Act 2: 製品プロジェクト直下の pubxml は確実に検知されること
        _write_file(
            self.repo / "AudioEffector/Properties/PublishProfiles/Release.pubxml",
            "<Project><PropertyGroup><PublishSingleFile>true</PublishSingleFile></PropertyGroup></Project>\n",
        )
        rc_fail, out_fail = self._run_cli(["--strict", "--json"])
        data_fail = json.loads(out_fail)
        self.assertEqual(rc_fail, 1)
        self.assertEqual(len(data_fail["lgpl_single_file_risks"]), 1)
        self.assertEqual(
            data_fail["lgpl_single_file_risks"][0]["file"],
            "AudioEffector/Properties/PublishProfiles/Release.pubxml",
        )

    def test_strict_all時のneeds_update整合性とバッククォートおよびテーブル記法パースが正しいこと(self) -> None:
        # Arrange: バッククォート記法とテーブル記法を含む THIRD-PARTY-NOTICES.md + 既存未記載 NLog
        self._init_git_repo()
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("MediaDevices", "1.10.0"), ("NLog", "6.2.0")]),
        )
        _write_file(
            self.repo / "THIRD-PARTY-NOTICES.md",
            "# Third-Party Notices\n\n"
            "## NAudio\n"
            "- **Version:** `2.2.1`\n\n"
            "| Package | Version | License |\n"
            "| :--- | :--- | :--- |\n"
            "| `MediaDevices` | `1.10.0` | MIT |\n",
        )
        _run_git(["add", "."], cwd=self.repo)
        _run_git(["commit", "-m", "chore: base"], cwd=self.repo)
        _run_git(["checkout", "-b", "feat/strict-all-check"], cwd=self.repo)

        # Act 1: 差分なし＋--strict-all → NAudio / MediaDevices は正しく一致認識され、NLog のみ未記載かつ needs_update=True
        rc_all, out_all = self._run_cli(["--base", "develop", "--strict", "--strict-all", "--json"])
        data_all = json.loads(out_all)
        self.assertEqual(rc_all, 1)
        self.assertFalse(data_all["passed"])
        self.assertTrue(data_all["needs_update"])
        self.assertEqual([p["name"] for p in data_all["missing_packages"]], ["NLog"])
        self.assertEqual(data_all["version_mismatches"], [])
        self.assertTrue(any("--strict-all" in err for err in data_all["blocking_errors"]))

        # Act 2: 差分違反併発時も --strict-all の既存負債サマリが blocking_errors に併記されること
        _write_file(
            self.repo / "AudioEffector.Infrastructure/AudioEffector.Infrastructure.csproj",
            _sample_csproj([("NAudio", "2.2.1"), ("MediaDevices", "1.10.0"), ("NLog", "6.2.0"), ("Whisper.net", "1.9.0")]),
        )
        rc_both, out_both = self._run_cli(["--base", "develop", "--strict", "--strict-all", "--json"])
        data_both = json.loads(out_both)
        self.assertEqual(rc_both, 1)
        self.assertFalse(data_both["passed"])
        self.assertTrue(data_both["needs_update"])
        self.assertTrue(any("Whisper.net" in err for err in data_both["blocking_errors"]))
        self.assertTrue(any("--strict-all" in err for err in data_both["blocking_errors"]))


if __name__ == "__main__":
    unittest.main()
