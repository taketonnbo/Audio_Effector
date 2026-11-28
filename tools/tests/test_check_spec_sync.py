#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tools/check_spec_sync.py および 設計/spec_map.json の単体テスト。"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent.parent
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import check_spec_sync  # noqa: E402


class CheckSpecSyncUnitTests(unittest.TestCase):
    """check_spec_sync のパターン判定・解析ロジックの単体テスト。"""

    def test_matches_entry_exclude_patternsで一致ファイルを除外できる(self) -> None:
        # Arrange
        entry = {
            "id": "playback-test",
            "code_patterns": ["AudioEffector.Infrastructure/**/*Playback*"],
            "exclude_patterns": ["AudioEffector.Infrastructure/Ipc/**"],
        }

        # Act & Assert
        self.assertTrue(
            check_spec_sync.matches_entry(
                "AudioEffector.Infrastructure/Audio/NAudioPlaybackEngine.cs",
                entry,
            )
        )
        self.assertFalse(
            check_spec_sync.matches_entry(
                "AudioEffector.Infrastructure/Ipc/PlaybackIpcCommandHandler.cs",
                entry,
            )
        )

    def test_matches_entry_exclude_patterns未指定でも後方互換で動作する(self) -> None:
        # Arrange
        entry = {
            "id": "sidebar-test",
            "code_patterns": ["AudioEffector/Presentation/**/*Sidebar*"],
        }

        # Act & Assert
        self.assertTrue(
            check_spec_sync.matches_entry(
                "AudioEffector/Presentation/Views/SidebarControl.xaml",
                entry,
            )
        )
        self.assertFalse(
            check_spec_sync.matches_entry(
                "AudioEffector/Presentation/Views/MainWindow.xaml",
                entry,
            )
        )

    def test_matches_entry_patternsがNoneでも例外なく判定できる(self) -> None:
        # Arrange
        null_patterns_entry = {
            "id": "null-patterns",
            "code_patterns": None,
            "exclude_patterns": None,
        }
        null_excludes_entry = {
            "id": "null-excludes",
            "code_patterns": ["AudioEffector/Presentation/**/*Sidebar*"],
            "exclude_patterns": None,
        }

        # Act & Assert
        self.assertFalse(
            check_spec_sync.matches_entry(
                "AudioEffector/Presentation/Views/SidebarControl.xaml",
                null_patterns_entry,
            )
        )
        self.assertTrue(
            check_spec_sync.matches_entry(
                "AudioEffector/Presentation/Views/SidebarControl.xaml",
                null_excludes_entry,
            )
        )


class SpecMapRegressionTests(unittest.TestCase):
    """実 設計/spec_map.json に対する回帰テスト。"""

    @classmethod
    def setUpClass(cls) -> None:
        cls.spec_map = check_spec_sync.load_spec_map(check_spec_sync.SPEC_MAP_PATH)

    def test_spec_mapの全仕様書パスが実在すること(self) -> None:
        # Arrange & Act
        missing = check_spec_sync.validate_spec_map(self.spec_map)

        # Assert
        self.assertEqual(missing, [], f"存在しない仕様書パスがあります: {missing}")

    def test_IPCファイル変更時にmcp_serverのみマッチしWPF再生UI仕様書が誤検知されないこと(self) -> None:
        # Arrange
        changed_files = [
            "AudioEffector.Infrastructure/Ipc/PlaybackIpcCommandHandler.cs",
            "AudioEffector.Infrastructure/Ipc/AppIpcMessages.cs",
        ]

        # Act
        result = check_spec_sync.analyze_changes(changed_files, self.spec_map)
        matched_ids = [m["id"] for m in result["matched_entries"]]
        primary_specs = {item["spec"] for item in result["primary_missing"]}

        # Assert
        self.assertEqual(matched_ids, ["mcp-server"])
        self.assertEqual(
            primary_specs,
            {
                "設計/機能仕様/MCPサーバ機能仕様書.md",
                "設計/詳細設計/MCPサーバ詳細設計書.md",
            },
        )
        self.assertNotIn("設計/画面設計/再生操作/再生操作UI仕様書.md", primary_specs)
        self.assertNotIn(
            "設計/画面設計/再生キュースライドパネル/再生キュースライドパネルUI仕様書.md",
            primary_specs,
        )

    def test_将来の機能別IPCハンドラ追加時もmcp_serverのみマッチすること(self) -> None:
        # Arrange
        changed_files = [
            "AudioEffector.Infrastructure/Ipc/PlaylistIpcCommandHandler.cs",
            "AudioEffector.Infrastructure/Ipc/EqualizerIpcCommandHandler.cs",
            "AudioEffector.Infrastructure/Ipc/LibraryIpcCommandHandler.cs",
            "AudioEffector.Infrastructure/Ipc/SettingsIpcCommandHandler.cs",
            "AudioEffector.Infrastructure/Ipc/RecentIpcCommandHandler.cs",
        ]

        # Act
        result = check_spec_sync.analyze_changes(changed_files, self.spec_map)
        matched_ids = [m["id"] for m in result["matched_entries"]]

        # Assert
        self.assertEqual(matched_ids, ["mcp-server"])
        self.assertEqual(result["unmapped_code_files"], [])

    def test_Application層のPlaylist変更時にWPF画面UI仕様書がPrimary必須とならないこと(self) -> None:
        # Arrange
        changed_files = [
            "AudioEffector.Application/ApplicationServices/PlaylistApplicationService.cs",
        ]

        # Act
        result = check_spec_sync.analyze_changes(changed_files, self.spec_map)
        matched_ids = [m["id"] for m in result["matched_entries"]]
        primary_specs = {item["spec"] for item in result["primary_missing"]}
        related_specs = {item["spec"] for item in result["related_missing"]}

        # Assert
        self.assertEqual(matched_ids, ["playlists-backend"])
        self.assertEqual(primary_specs, set())
        self.assertEqual(result["unmapped_code_files"], [])
        self.assertIn("設計/ユースケース/03_プレイリスト管理.md", related_specs)
        self.assertIn("設計/画面設計/プレイリスト一覧/プレイリスト一覧UI仕様書.md", related_specs)

    def test_Application層のAudioApplicationService変更時にWPF再生UI仕様書がPrimary必須とならないこと(self) -> None:
        # Arrange
        changed_files = [
            "AudioEffector.Application/ApplicationServices/AudioApplicationService.cs",
            "AudioEffector.Infrastructure/Audio/AudioService.cs",
        ]

        # Act
        result = check_spec_sync.analyze_changes(changed_files, self.spec_map)
        matched_ids = [m["id"] for m in result["matched_entries"]]
        primary_specs = {item["spec"] for item in result["primary_missing"]}
        related_specs = {item["spec"] for item in result["related_missing"]}

        # Assert
        self.assertEqual(matched_ids, ["playback-backend"])
        self.assertEqual(primary_specs, set())
        self.assertEqual(result["unmapped_code_files"], [])
        self.assertIn("設計/機能仕様/再生キュー機能仕様書.md", related_specs)
        self.assertIn("設計/画面設計/再生操作/再生操作UI仕様書.md", related_specs)

    def test_Presentation層のUIやViewModel変更時は従来通りUI仕様書がPrimaryとして検知されること(self) -> None:
        # Arrange
        cases = [
            (
                "AudioEffector/Presentation/ViewModels/PlaylistViewModel.cs",
                "設計/画面設計/プレイリスト一覧/プレイリスト一覧UI仕様書.md",
            ),
            (
                "AudioEffector/Presentation/Views/PlayQueueSidePanel.xaml",
                "設計/画面設計/再生キュースライドパネル/再生キュースライドパネルUI仕様書.md",
            ),
            (
                "AudioEffector/Presentation/ViewModels/PlayerControlViewModel.cs",
                "設計/画面設計/再生操作/再生操作UI仕様書.md",
            ),
            (
                "AudioEffector/Presentation/ViewModels/LibraryViewModel.cs",
                "設計/画面設計/アルバム一覧/アルバム一覧UI仕様書.md",
            ),
            (
                "AudioEffector/Presentation/Views/MainWindow.xaml",
                "設計/画面設計/全体レイアウト・ペイン構成/全体レイアウト・ペイン構成UI仕様書.md",
            ),
            (
                "AudioEffector/Presentation/ViewModels/EqualizerViewModel.cs",
                "設計/画面設計/イコライザー/イコライザーUI仕様書.md",
            ),
            (
                "AudioEffector/Presentation/Views/SettingsDialog.xaml",
                "設計/画面設計/設定/設定UI仕様書.md",
            ),
        ]

        # Act & Assert
        for code_file, expected_primary in cases:
            with self.subTest(code_file=code_file):
                result = check_spec_sync.analyze_changes([code_file], self.spec_map)
                primary_specs = {item["spec"] for item in result["primary_missing"]}
                self.assertIn(expected_primary, primary_specs)

    def test_全5テストプロジェクト配下のファイル変更がignore_patternsで除外されること(self) -> None:
        # Arrange
        changed_files = [
            "AudioEffector.Tests/Presentation/ViewModels/PlaylistViewModelTests.cs",
            "AudioEffector.Domain.Tests/Entities/UserPlaylistTests.cs",
            "AudioEffector.Application.Tests/ApplicationServices/PlaylistApplicationServiceTests.cs",
            "AudioEffector.Infrastructure.Tests/Ipc/PlaybackIpcCommandHandlerTests.cs",
            "AudioEffector.McpServer.Tests/Tools/PlaylistToolsTests.cs",
        ]

        # Act
        result = check_spec_sync.analyze_changes(changed_files, self.spec_map)

        # Assert
        self.assertEqual(result["code_files"], [])
        self.assertEqual(result["matched_entries"], [])
        self.assertEqual(result["primary_missing"], [])
        self.assertEqual(result["unmapped_code_files"], [])

    def test_MCPツールとIPC変更時にMCP仕様書を更新すればPrimary未更新が0件になること(self) -> None:
        # Arrange
        changed_files = [
            "AudioEffector.McpServer/Tools/PlaylistTools.cs",
            "AudioEffector.Infrastructure/Ipc/PlaybackIpcCommandHandler.cs",
            "AudioEffector.Application/ApplicationServices/PlaylistApplicationService.cs",
            "AudioEffector.McpServer.Tests/Tools/PlaylistToolsTests.cs",
            "設計/機能仕様/MCPサーバ機能仕様書.md",
            "設計/詳細設計/MCPサーバ詳細設計書.md",
        ]

        # Act
        result = check_spec_sync.analyze_changes(changed_files, self.spec_map)

        # Assert
        self.assertEqual(result["primary_missing"], [])
        primary_updated = {item["spec"] for item in result["primary_updated"]}
        self.assertEqual(
            primary_updated,
            {
                "設計/機能仕様/MCPサーバ機能仕様書.md",
                "設計/詳細設計/MCPサーバ詳細設計書.md",
            },
        )

    def test_機器転送UI変更時にdevice_transferのみ一致し機器ストレージ管理UI仕様書がPrimary誤検知されないこと(self) -> None:
        # Arrange
        cases = [
            ["AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs"],
            ["AudioEffector/Presentation/Views/DeviceSyncView.xaml"],
            [
                "AudioEffector/Presentation/ViewModels/DeviceBrowserViewModel.cs",
                "AudioEffector/Presentation/Views/DeviceSyncView.xaml",
            ],
        ]

        # Act & Assert
        for changed_files in cases:
            with self.subTest(changed_files=changed_files):
                result = check_spec_sync.analyze_changes(changed_files, self.spec_map)
                matched_ids = [m["id"] for m in result["matched_entries"]]
                primary_specs = {item["spec"] for item in result["primary_missing"]}
                related_specs = {item["spec"] for item in result["related_missing"]}

                self.assertEqual(matched_ids, ["device-transfer"])
                self.assertEqual(result["unmapped_code_files"], [])
                self.assertEqual(
                    primary_specs,
                    {
                        "設計/画面設計/機器転送/機器転送UI仕様書.md",
                        "設計/機能仕様/機器転送.md",
                    },
                )
                self.assertNotIn(
                    "設計/画面設計/機器ストレージ管理/機器ストレージ管理UI仕様書.md",
                    primary_specs,
                )
                self.assertIn(
                    "設計/画面設計/機器ストレージ管理/機器ストレージ管理UI仕様書.md",
                    related_specs,
                )

    def test_機器ストレージ管理UI変更時にdevice_managerのみ一致し機器ストレージ管理UI仕様書がPrimary検知されること(self) -> None:
        # Arrange
        cases = [
            ["AudioEffector/Presentation/ViewModels/DeviceManagerViewModel.cs"],
            ["AudioEffector/Presentation/Views/DeviceManagerDialog.xaml"],
            [
                "AudioEffector/Presentation/ViewModels/DeviceManagerViewModel.cs",
                "AudioEffector/Presentation/Views/DeviceManagerDialog.xaml",
            ],
        ]

        # Act & Assert
        for changed_files in cases:
            with self.subTest(changed_files=changed_files):
                result = check_spec_sync.analyze_changes(changed_files, self.spec_map)
                matched_ids = [m["id"] for m in result["matched_entries"]]
                primary_specs = {item["spec"] for item in result["primary_missing"]}
                related_specs = {item["spec"] for item in result["related_missing"]}

                self.assertEqual(matched_ids, ["device-manager"])
                self.assertEqual(result["unmapped_code_files"], [])
                self.assertEqual(
                    primary_specs,
                    {
                        "設計/画面設計/機器ストレージ管理/機器ストレージ管理UI仕様書.md",
                    },
                )
                self.assertNotIn("設計/画面設計/機器転送/機器転送UI仕様書.md", primary_specs)
                self.assertNotIn("設計/機能仕様/機器転送.md", primary_specs)
                self.assertIn("設計/画面設計/機器転送/機器転送UI仕様書.md", related_specs)
                self.assertIn("設計/機能仕様/機器転送.md", related_specs)


if __name__ == "__main__":
    unittest.main()
