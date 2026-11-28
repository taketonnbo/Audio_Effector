using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// DeviceBrowserViewModel の重複判定インデックス化・曲単位非活性・転送対象抽出・非同期キャンセル制御を検証するテストクラス
/// </summary>
public sealed class DeviceBrowserViewModelTests
{
    /// <summary>
    /// 異なる拡張子（変換前後）や大文字小文字の違いを含むファイルパスから、同一の重複判定キーが生成されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Music\Artist\Album\01_Song.mp4", "01_Song")]
    [InlineData(@"\Music\Artist\Album\01_Song.m4a", "01_Song")]
    [InlineData(@"E:\Music\02 Track.FLAC", "02 Track")]
    [InlineData(@"E:\Music\02 Track.mp3", "02 Track")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeTrackDuplicateKey_各種パス形式_拡張子除去済みの正規化キーを返すこと(string input, string expected)
    {
        // Arrange & Act
        var actual = DeviceBrowserViewModel.NormalizeTrackDuplicateKey(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// MTPデバイスおよびファイルシステムドライブにおいて、ルート直下パスとサブフォルダパスが正しく判定されることを検証します。
    /// </summary>
    [Fact]
    public void IsDeviceRootPath_ルート直下とサブフォルダ_正しく区別されること()
    {
        // Arrange
        var mtpDevice = new MainViewModel.DeviceViewModel
        {
            Name = "WALKMAN",
            Type = MainViewModel.DeviceType.MTP,
            RootPath = @"\"
        };
        var fsDevice = new MainViewModel.DeviceViewModel
        {
            Name = "USB (E:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = @"E:\"
        };

        // Act & Assert
        Assert.True(DeviceBrowserViewModel.IsDeviceRootPath(mtpDevice, @"\"));
        Assert.True(DeviceBrowserViewModel.IsDeviceRootPath(mtpDevice, "/"));
        Assert.False(DeviceBrowserViewModel.IsDeviceRootPath(mtpDevice, @"\Internal\Music"));

        Assert.True(DeviceBrowserViewModel.IsDeviceRootPath(fsDevice, @"E:\"));
        Assert.True(DeviceBrowserViewModel.IsDeviceRootPath(fsDevice, @"E:"));
        Assert.False(DeviceBrowserViewModel.IsDeviceRootPath(fsDevice, @"E:\Music"));
    }

    /// <summary>
    /// 指定フォルダ配下の一括インデックス構築で、サブフォルダ配下の音声ファイルが相対パス基準キーで収集され非音声ファイルが除外されることを検証します。
    /// </summary>
    [Fact]
    public void BuildFileSystemAudioIndex_再帰有効時_サブフォルダ配下の音声ファイルが相対パス基準でインデックス化されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Test_{Guid.NewGuid():N}");
        string subDir = Path.Combine(tempRoot, "ArtistA", "AlbumA");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "root_song.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(subDir, "01_Track.m4a"), string.Empty);
            File.WriteAllText(Path.Combine(subDir, "02_Track.flac"), string.Empty);
            File.WriteAllText(Path.Combine(subDir, "cover.jpg"), string.Empty);

            // Act
            var recursiveIndex = DeviceBrowserViewModel.BuildFileSystemAudioIndex(tempRoot, recursive: true);
            var nonRecursiveIndex = DeviceBrowserViewModel.BuildFileSystemAudioIndex(tempRoot, recursive: false);

            // Assert
            Assert.Contains("root_song", recursiveIndex);
            Assert.Contains(@"ArtistA\AlbumA\01_Track", recursiveIndex);
            Assert.Contains(@"ArtistA\AlbumA\02_Track", recursiveIndex);
            Assert.DoesNotContain(@"ArtistA\AlbumA\cover", recursiveIndex);

            Assert.Contains("root_song", nonRecursiveIndex);
            Assert.DoesNotContain(@"ArtistA\AlbumA\01_Track", nonRecursiveIndex);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// キャンセル済みのCancellationTokenを渡した場合、BuildFileSystemAudioIndexがOperationCanceledExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public void BuildFileSystemAudioIndex_キャンセル済みトークン指定時_OperationCanceledExceptionを送出すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Cancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            // Act & Assert
            Assert.Throws<OperationCanceledException>(
                () => DeviceBrowserViewModel.BuildFileSystemAudioIndex(tempRoot, recursive: true, cts.Token));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// インデックス照合により、別アルバムの同名ファイルと衝突せず、拡張子違いの既転送トラックのみIsOnDevice=trueとなることを検証します。
    /// </summary>
    [Fact]
    public void ApplyDeviceTrackIndex_別アルバム同名ファイルと一部転送済みアルバム_衝突せず正しく判定されること()
    {
        // Arrange
        var partialTrack1 = new Track { Title = "Song 1", Artist = "Artist A", Album = "Partial Album", FilePath = @"C:\Music\01.mp4", IsSelected = true };
        var partialTrack2 = new Track { Title = "Song 2", Artist = "Artist A", Album = "Partial Album", FilePath = @"C:\Music\02.flac", IsSelected = true };
        var partialAlbum = new Album("Partial Album", "Artist A", 2024, new[] { partialTrack1, partialTrack2 });

        // 別アルバムの同名ファイル 01.flac（未転送）
        var otherAlbumTrack = new Track { Title = "Other Song 1", Artist = "Artist B", Album = "Other Album", FilePath = @"C:\Music\Other\01.flac", IsSelected = true };
        var otherAlbum = new Album("Other Album", "Artist B", 2024, new[] { otherAlbumTrack });

        // 全曲転送済みアルバム（フラット直下配置とのフォールバックも検証）
        var fullTrack1 = new Track { Title = "Full 1", Artist = "Artist C", Album = "Full Album", FilePath = @"C:\Music\full_01.flac", IsSelected = true };
        var fullAlbum = new Album("Full Album", "Artist C", 2024, new[] { fullTrack1 }) { IsSelected = true };

        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"Artist A\Partial Album\01",
            "FULL_01"
        };

        // Act
        DeviceBrowserViewModel.ApplyDeviceTrackIndex(new[] { partialAlbum, otherAlbum, fullAlbum }, index);

        // Assert
        Assert.True(partialTrack1.IsOnDevice);
        Assert.False(partialTrack1.IsSelected);
        Assert.False(partialTrack2.IsOnDevice);
        Assert.True(partialTrack2.IsSelected);
        Assert.False(partialAlbum.IsOnDevice);

        // 別アルバムの同名ファイル 01.flac は衝突せず未転送のまま
        Assert.False(otherAlbumTrack.IsOnDevice);
        Assert.True(otherAlbumTrack.IsSelected);
        Assert.False(otherAlbum.IsOnDevice);

        Assert.True(fullTrack1.IsOnDevice);
        Assert.False(fullTrack1.IsSelected);
        Assert.True(fullAlbum.IsOnDevice);
        Assert.False(fullAlbum.IsSelected);
    }

    /// <summary>
    /// 転送対象収集時に、IsOnDeviceがtrueの楽曲が除外され未転送曲のみが重複なく抽出されることを検証します。
    /// </summary>
    [Fact]
    public void CollectTransferTargetTracks_既転送曲と未転送曲が混在する場合_未転送曲のみ抽出されること()
    {
        // Arrange
        var transferred = new Track { Title = "Transferred", FilePath = @"C:\Music\01.mp3", IsOnDevice = true };
        var pending = new Track { Title = "Pending", FilePath = @"C:\Music\02.mp3", IsOnDevice = false };
        var album = new Album("Mixed Album", "Artist A", 2024, new[] { transferred, pending })
        {
            IsSelected = true
        };

        // Act
        var targets = DeviceBrowserViewModel.CollectTransferTargetTracks(new[] { album });

        // Assert
        Assert.Single(targets);
        Assert.Same(pending, targets[0]);
    }

    /// <summary>
    /// デバイス未選択（SelectedDevice == null）状態でCheckDeviceAlbumsAsyncを実行すると、既転送フラグがfalseへリセットされることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task CheckDeviceAlbumsAsync_デバイス未選択時_全トラックのIsOnDeviceがFalseへリセットされること()
    {
        // Arrange
        var track = new Track { Title = "Song 1", Artist = "Artist A", Album = "Album A", FilePath = @"C:\Music\01.mp3", IsOnDevice = true };
        var album = new Album("Album A", "Artist A", 2024, new[] { track });
        var albums = new ObservableCollection<Album> { album };
        using var sut = new DeviceBrowserViewModel(albums, () => albums, () => { });

        // Act
        await sut.CheckDeviceAlbumsAsync();

        // Assert
        Assert.False(track.IsOnDevice);
        Assert.False(album.IsOnDevice);
    }

    /// <summary>
    /// CheckDeviceAlbumsAsyncを異なるフォルダに対して連続呼び出しした場合、最新フォルダのスキャン結果が最終的に反映されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task CheckDeviceAlbumsAsync_連続呼び出し時_最新フォルダのスキャン結果が反映されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Race_{Guid.NewGuid():N}");
        string folder1 = Path.Combine(tempRoot, "Folder1");
        string folder2 = Path.Combine(tempRoot, "Folder2");
        string subDir2 = Path.Combine(folder2, "Artist A", "Album A");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(subDir2);
        File.WriteAllText(Path.Combine(subDir2, "01_Song.m4a"), string.Empty);

        var track = new Track { Title = "Song 1", Artist = "Artist A", Album = "Album A", FilePath = @"C:\Music\01_Song.mp4" };
        var album = new Album("Album A", "Artist A", 2024, new[] { track });
        var albums = new ObservableCollection<Album> { album };
        using var sut = new DeviceBrowserViewModel(albums, () => albums, () => { });

        try
        {
            var fsDevice = new MainViewModel.DeviceViewModel
            {
                Name = "TestDrive",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = tempRoot
            };
            sut.SelectedDevice = fsDevice;

            // Act
            sut.CurrentDevicePath = folder1;
            var firstTask = sut.CheckDeviceAlbumsAsync();
            sut.CurrentDevicePath = folder2;
            var secondTask = sut.CheckDeviceAlbumsAsync();
            await Task.WhenAll(firstTask, secondTask);

            // Assert
            Assert.True(track.IsOnDevice);
            Assert.True(album.IsOnDevice);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// DeviceViewModelのUpdateStorageSpaceおよびClearStorageSpaceにより、使用量・使用率・サマリ文字列が正しく算出されることを検証します。
    /// </summary>
    [Fact]
    public void DeviceViewModel_ストレージ容量更新およびクリア_使用量とサマリ表示が正しく計算されること()
    {
        // Arrange
        var sut = new MainViewModel.DeviceViewModel
        {
            Name = "WALKMAN",
            Type = MainViewModel.DeviceType.MTP
        };
        long total = 64L * 1024 * 1024 * 1024; // 64.0 GB
        long free = 24L * 1024 * 1024 * 1024;  // 24.0 GB

        // Act
        sut.UpdateStorageSpace(total, free);
        bool hasInfoAfterUpdate = sut.HasStorageInfo;
        long usedAfterUpdate = sut.UsedSpaceBytes;
        double ratioAfterUpdate = sut.UsedSpaceRatio;
        string summaryAfterUpdate = sut.StorageSummaryText;

        sut.ClearStorageSpace();

        // Assert
        Assert.Equal("MTP", sut.ConnectionTypeLabel);
        Assert.True(hasInfoAfterUpdate);
        Assert.Equal(total - free, usedAfterUpdate);
        Assert.Equal(40.0 / 64.0, ratioAfterUpdate, precision: 4);
        Assert.Equal("24.0 GB 空き / 64.0 GB", summaryAfterUpdate);

        Assert.False(sut.HasStorageInfo);
        Assert.Equal(0.0, sut.UsedSpaceRatio);
        Assert.Equal("容量情報取得不可", sut.StorageSummaryText);
    }

    /// <summary>
    /// アルバムおよびトラックの選択状態変化に応じて、選択件数・合計サイズ・空き容量判定（空き十分／容量不足）がリアルタイム更新されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task UpdateSelectionSummary_選択状態変更時_合計サイズと空き容量判定がリアルタイム更新されること()
    {
        // Arrange
        var track1 = new Track { Title = "Track 1", Artist = "Artist A", Album = "Album A", FilePath = @"C:\Music\01.flac" };
        var track2 = new Track { Title = "Track 2", Artist = "Artist A", Album = "Album A", FilePath = @"C:\Music\02.flac" };
        var album = new Album("Album A", "Artist A", 2024, new[] { track1, track2 });
        var albums = new ObservableCollection<Album> { album };
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [track1.FilePath] = 600L * 1024 * 1024, // 600 MB
            [track2.FilePath] = 700L * 1024 * 1024, // 700 MB
        };

        long totalCapacity = 2L * 1024 * 1024 * 1024;
        long freeCapacity = 1L * 1024 * 1024 * 1024; // 空き 1.0 GB

        using var sut = new DeviceBrowserViewModel(
            albums,
            () => albums,
            () => { },
            path => sizes.TryGetValue(path, out var len) ? len : 0L,
            (_, _) => (true, totalCapacity, freeCapacity));

        var device = new MainViewModel.DeviceViewModel
        {
            Name = "USB (E:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = @"E:\"
        };
        device.UpdateStorageSpace(totalCapacity, freeCapacity);
        sut.RemovableDrives.Add(device);
        sut.SelectedDevice = device;
        await sut.CheckDeviceAlbumsAsync();
        await sut.RefreshSelectedDeviceStorageAsync();

        // Act & Assert: 1曲選択（600 MB <= 1.0 GB → 空き十分）
        track1.IsSelected = true;
        Assert.True(sut.HasConnectedDevices);
        Assert.Equal(1, sut.SelectedAlbumCount);
        Assert.Equal(1, sut.SelectedTrackCount);
        Assert.Equal(600L * 1024 * 1024, sut.SelectedTotalBytes);
        Assert.False(sut.IsInsufficientSpace);
        Assert.Contains("空き十分", sut.SelectedSizeSummaryText);

        // Act & Assert: 2曲目も選択（1300 MB > 1.0 GB → 容量不足）
        track2.IsSelected = true;
        Assert.Equal(1, sut.SelectedAlbumCount);
        Assert.Equal(2, sut.SelectedTrackCount);
        Assert.Equal(1300L * 1024 * 1024, sut.SelectedTotalBytes);
        Assert.True(sut.IsInsufficientSpace);
        Assert.Contains("容量不足", sut.SelectedSizeSummaryText);

        // Act & Assert: 選択解除で 0 曲に戻る
        track1.IsSelected = false;
        track2.IsSelected = false;
        Assert.Equal(0, sut.SelectedTrackCount);
        Assert.False(sut.IsInsufficientSpace);
        Assert.Equal("選択中: 0 曲", sut.SelectedSizeSummaryText);
    }

    /// <summary>
    /// 接続機器一覧が空の場合と追加された場合でHasConnectedDevicesが正しく切り替わることを検証します。
    /// </summary>
    [Fact]
    public void HasConnectedDevices_接続機器一覧の増減時_状態とPropertyChanged通知が正しく連動すること()
    {
        // Arrange
        var albums = new ObservableCollection<Album>();
        using var sut = new DeviceBrowserViewModel(albums, () => albums, () => { });
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        bool initial = sut.HasConnectedDevices;
        var device = new MainViewModel.DeviceViewModel { Name = "USB", Type = MainViewModel.DeviceType.FileSystem };
        sut.RemovableDrives.Add(device);
        bool afterAdd = sut.HasConnectedDevices;
        sut.RemovableDrives.Clear();
        bool afterClear = sut.HasConnectedDevices;

        // Assert
        Assert.False(initial);
        Assert.True(afterAdd);
        Assert.False(afterClear);
        Assert.Contains(nameof(DeviceBrowserViewModel.HasConnectedDevices), notified);
    }

    /// <summary>
    /// 機器ごとに既定転送先フォルダを登録・永続化でき、再選択時に各機器の既定フォルダへ独立して自動遷移することを検証します。
    /// </summary>
    [Fact]
    public void 既定転送先フォルダ_複数機器で登録して再選択時_各機器の既定フォルダへ自動遷移すること()
    {
        // Arrange
        string tempRootA = Path.Combine(Path.GetTempPath(), $"AE_DefA_{Guid.NewGuid():N}");
        string musicDirA = Path.Combine(tempRootA, "MusicA");
        string tempRootB = Path.Combine(Path.GetTempPath(), $"AE_DefB_{Guid.NewGuid():N}");
        string musicDirB = Path.Combine(tempRootB, "MusicB");
        Directory.CreateDirectory(musicDirA);
        Directory.CreateDirectory(musicDirB);

        var settingsService = new InMemorySettingsService();
        var albums = new ObservableCollection<Album>();

        try
        {
            var deviceA = new MainViewModel.DeviceViewModel
            {
                Name = "Device A",
                DeviceKey = "FS:DEVICE_A",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = tempRootA
            };
            var deviceB = new MainViewModel.DeviceViewModel
            {
                Name = "Device B",
                DeviceKey = "FS:DEVICE_B",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = tempRootB
            };

            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService))
            {
                sut.SelectedDevice = deviceA;
                sut.CurrentDevicePath = musicDirA;
                sut.SetDefaultFolderCommand.Execute(null);
                Assert.True(sut.IsCurrentFolderDefault);

                sut.SelectedDevice = deviceB;
                sut.CurrentDevicePath = musicDirB;
                sut.SetDefaultFolderCommand.Execute(null);
                Assert.True(sut.IsCurrentFolderDefault);
            }

            // Act: アプリ再起動相当の新規 ViewModel で各機器を再選択
            using var reloadedSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService);

            reloadedSut.SelectedDevice = deviceA;
            string restoredPathA = reloadedSut.CurrentDevicePath;
            bool isDefaultA = reloadedSut.IsCurrentFolderDefault;

            reloadedSut.SelectedDevice = deviceB;
            string restoredPathB = reloadedSut.CurrentDevicePath;
            bool isDefaultB = reloadedSut.IsCurrentFolderDefault;

            // Assert
            Assert.Equal(musicDirA, restoredPathA);
            Assert.True(isDefaultA);
            Assert.Equal(musicDirB, restoredPathB);
            Assert.True(isDefaultB);
        }
        finally
        {
            if (Directory.Exists(tempRootA)) Directory.Delete(tempRootA, recursive: true);
            if (Directory.Exists(tempRootB)) Directory.Delete(tempRootB, recursive: true);
        }
    }

    /// <summary>
    /// 登録済みの既定転送先フォルダが機器上から削除されていた場合、例外なくルートフォルダへフォールバックし通知メッセージが表示されることを検証します。
    /// </summary>
    [Fact]
    public void 既定転送先フォルダ_登録先フォルダ削除済みの場合_ルートへフォールバックし通知すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Fallback_{Guid.NewGuid():N}");
        string deletedDir = Path.Combine(tempRoot, "DeletedMusic");
        Directory.CreateDirectory(tempRoot);

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.DeviceDefaultTransferFolders["FS:TEST_FALLBACK"] = deletedDir);
        var albums = new ObservableCollection<Album>();

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                DeviceKey = "FS:TEST_FALLBACK",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = tempRoot
            };

            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService);

            // Act
            sut.SelectedDevice = device;

            // Assert
            Assert.Equal(tempRoot, sut.CurrentDevicePath);
            Assert.False(sut.IsCurrentFolderDefault);
            Assert.True(sut.HasDefaultFolderStatusMessage);
            Assert.Contains("ルートフォルダ", sut.DefaultFolderStatusMessage);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ToggleDefaultFolderCommandおよびClearDefaultFolderCommandにより、既定フォルダ登録・解除が正常に切り替わることを検証します。
    /// </summary>
    [Fact]
    public void ToggleDefaultFolderCommand_登録済みフォルダで実行時_設定解除されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Toggle_{Guid.NewGuid():N}");
        string musicDir = Path.Combine(tempRoot, "Music");
        Directory.CreateDirectory(musicDir);
        var settingsService = new InMemorySettingsService();
        var albums = new ObservableCollection<Album>();

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                DeviceKey = "FS:TOGGLE_DEV",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = tempRoot
            };

            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService);

            sut.SelectedDevice = device;
            sut.CurrentDevicePath = musicDir;

            // Act
            sut.ToggleDefaultFolderCommand.Execute(null);
            bool afterRegister = sut.IsCurrentFolderDefault;
            bool hasDefaultAfterRegister = sut.HasDefaultFolderForSelectedDevice;

            sut.ToggleDefaultFolderCommand.Execute(null);
            bool afterClear = sut.IsCurrentFolderDefault;
            bool hasDefaultAfterClear = sut.HasDefaultFolderForSelectedDevice;

            // Assert
            Assert.True(afterRegister);
            Assert.True(hasDefaultAfterRegister);
            Assert.False(afterClear);
            Assert.False(hasDefaultAfterClear);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ResolveDeviceKeyがMTP/FileSystem/明示DeviceKeyを正しく解決し、パス末尾区切りや大文字小文字の違いを吸収してIsCurrentFolderDefaultを判定することを検証します。
    /// </summary>
    [Fact]
    public void ResolveDeviceKeyおよびパス正規化_各種機器形式とパス表記ゆれ_正しく識別および既定判定されること()
    {
        // Arrange
        var explicitDevice = new MainViewModel.DeviceViewModel
        {
            Name = "WALKMAN",
            DeviceKey = "MTP:EXPLICIT_ID",
            Type = MainViewModel.DeviceType.MTP
        };
        var mtpFallbackDevice = new MainViewModel.DeviceViewModel
        {
            Name = "WALKMAN NW-A100",
            Type = MainViewModel.DeviceType.MTP
        };
        var fsDevice = new MainViewModel.DeviceViewModel
        {
            Name = "USB (E:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = @"E:\"
        };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.DeviceDefaultTransferFolders["FS:E:\\"] = @"E:\Music\");
        var albums = new ObservableCollection<Album>();

        using var sut = new DeviceBrowserViewModel(
            albums,
            () => albums,
            () => { },
            readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
            settingsService: settingsService);

        // Act
        string keyExplicit = DeviceBrowserViewModel.ResolveDeviceKey(explicitDevice);
        string keyMtp = DeviceBrowserViewModel.ResolveDeviceKey(mtpFallbackDevice);
        string keyFs = DeviceBrowserViewModel.ResolveDeviceKey(fsDevice);

        sut.SelectedDevice = fsDevice;
        sut.CurrentDevicePath = @"e:/music";

        // Assert
        Assert.Equal("MTP:EXPLICIT_ID", keyExplicit);
        Assert.Equal("MTP:WALKMAN NW-A100", keyMtp);
        Assert.Equal(@"FS:E:\", keyFs);
        Assert.True(sut.IsCurrentFolderDefault);
    }

    /// <summary>
    /// ルートフォールバック通知が表示された後、ユーザーが手動で別フォルダへ移動すると通知がクリアされることを検証します。
    /// </summary>
    [Fact]
    public void NavigateDirectory_フォールバック通知表示後に手動移動時_通知がクリアされること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_NavClear_{Guid.NewGuid():N}");
        string subDir = Path.Combine(tempRoot, "ExistingMusic");
        Directory.CreateDirectory(subDir);

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.DeviceDefaultTransferFolders["FS:NAV_TEST"] = Path.Combine(tempRoot, "Deleted"));
        var albums = new ObservableCollection<Album>();

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                DeviceKey = "FS:NAV_TEST",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = tempRoot
            };

            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService);

            sut.SelectedDevice = device;
            Assert.True(sut.HasDefaultFolderStatusMessage);

            // Act
            sut.NavigateDirectoryCommand.Execute(new MainViewModel.DirectoryItem
            {
                Name = "ExistingMusic",
                FullPath = subDir,
                IsFolder = true
            });

            // Assert
            Assert.Equal(subDir, sut.CurrentDevicePath);
            Assert.False(sut.HasDefaultFolderStatusMessage);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private AppSettings _settings = new();

        public AppSettings LoadSettings() => _settings;

        public void SaveSettings(AppSettings settings) => _settings = settings;

        public void UpdateSettings(Action<AppSettings> update) => update(_settings);

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public Task UpdateSettingsAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(_settings);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// ConvertMp4ToM4aが有効な場合、.mp4トラックが.m4aへ変換されて転送され、変換用一時ファイルが転送後に自動クリーンアップされることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksToFileSystemAsync_ConvertMp4ToM4a有効時_M4Aへ変換転送され一時ファイルが削除されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Mp4Transfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string destDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        string sourceMp4 = Path.Combine(sourceDir, "01_Sample.mp4");
        File.WriteAllText(sourceMp4, "original-mp4-bytes");

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.ConvertMp4ToM4a = true);

        var fakeTranscoder = new FakeAudioTranscodeService();
        var track = new Track
        {
            Title = "Sample",
            Artist = "Artist A",
            Album = "Album A",
            FilePath = sourceMp4
        };
        var albums = new ObservableCollection<Album> { new Album("Album A", "Artist A", 2024, new[] { track }) };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: fakeTranscoder);

            // Act
            var options = sut.GetCurrentTranscodeOptions();
            await sut.TransferTracksToFileSystemAsync(new[] { track }, destDir, options);

            // Assert
            string expectedDestM4a = Path.Combine(destDir, "Artist A", "Album A", "01_Sample.m4a");
            Assert.True(options.ConvertMp4ToM4a);
            Assert.Equal(1, fakeTranscoder.TranscodeCallCount);
            Assert.True(File.Exists(expectedDestM4a));
            Assert.Equal("transcoded-m4a-bytes", File.ReadAllText(expectedDestM4a));
            Assert.NotNull(fakeTranscoder.LastTemporaryDirectory);
            Assert.False(Directory.Exists(fakeTranscoder.LastTemporaryDirectory));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ConvertMp4ToM4aが無効な場合は変換を行わず元拡張子(.mp4)のまま転送され、転送先に既存(.m4a/.mp4)が存在する場合は変換をスキップすることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksToFileSystemAsync_設定OFF時および既存ファイル重複時_不要な変換を行わず正しく処理されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Mp4Skip_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string destDir = Path.Combine(tempRoot, "Dest");
        string existingAlbumDir = Path.Combine(destDir, "Artist A", "Album A");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(existingAlbumDir);

        string sourceMp4Off = Path.Combine(sourceDir, "01_NoConvert.mp4");
        string sourceMp4DupM4a = Path.Combine(sourceDir, "02_DupM4a.mp4");
        string sourceMp4DupMp4 = Path.Combine(sourceDir, "03_DupMp4.mp4");
        File.WriteAllText(sourceMp4Off, "raw-mp4");
        File.WriteAllText(sourceMp4DupM4a, "raw-mp4");
        File.WriteAllText(sourceMp4DupMp4, "raw-mp4");
        File.WriteAllText(Path.Combine(existingAlbumDir, "02_DupM4a.m4a"), "already-m4a");
        File.WriteAllText(Path.Combine(existingAlbumDir, "03_DupMp4.mp4"), "already-mp4");

        var fakeTranscoder = new FakeAudioTranscodeService();
        var tracks = new[]
        {
            new Track { Title = "NoConvert", Artist = "Artist A", Album = "Album A", FilePath = sourceMp4Off },
            new Track { Title = "DupM4a", Artist = "Artist A", Album = "Album A", FilePath = sourceMp4DupM4a },
            new Track { Title = "DupMp4", Artist = "Artist A", Album = "Album A", FilePath = sourceMp4DupMp4 },
        };
        var albums = new ObservableCollection<Album> { new Album("Album A", "Artist A", 2024, tracks) };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                audioTranscodeService: fakeTranscoder);

            // Act 1: 設定 OFF で 01_NoConvert.mp4 を転送 → 変換されず .mp4 のままコピー
            await sut.TransferTracksToFileSystemAsync(new[] { tracks[0] }, destDir, new AudioTranscodeOptions { ConvertMp4ToM4a = false });

            // Act 2: 設定 ON で既存 .m4a / .mp4 がある 2 曲を転送 → どちらもスキップされ変換呼び出し 0 回
            await sut.TransferTracksToFileSystemAsync(new[] { tracks[1], tracks[2] }, destDir, new AudioTranscodeOptions { ConvertMp4ToM4a = true });

            // Assert
            Assert.Equal(0, fakeTranscoder.TranscodeCallCount);
            Assert.True(File.Exists(Path.Combine(existingAlbumDir, "01_NoConvert.mp4")));
            Assert.Equal("already-m4a", File.ReadAllText(Path.Combine(existingAlbumDir, "02_DupM4a.m4a")));
            Assert.Equal("already-mp4", File.ReadAllText(Path.Combine(existingAlbumDir, "03_DupMp4.mp4")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ConvertFlacToMp3が有効な場合、.flacトラックが.mp3へ変換されて転送され一時ファイルが削除されること、および既存.mp3がある場合は重複スキップされることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksToFileSystemAsync_ConvertFlacToMp3有効時_MP3へ変換転送され一時ファイル削除と重複スキップが行われること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_FlacTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string destDir = Path.Combine(tempRoot, "Dest");
        string existingAlbumDir = Path.Combine(destDir, "Artist A", "Album A");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(existingAlbumDir);

        string sourceFlac = Path.Combine(sourceDir, "01_HiRes.flac");
        string dupFlac = Path.Combine(sourceDir, "02_AlreadyMp3.flac");
        File.WriteAllText(sourceFlac, "original-flac-bytes");
        File.WriteAllText(dupFlac, "original-flac-bytes");
        File.WriteAllText(Path.Combine(existingAlbumDir, "02_AlreadyMp3.mp3"), "already-mp3");

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.ConvertFlacToMp3 = true);

        var fakeTranscoder = new FakeAudioTranscodeService();
        var track1 = new Track { Title = "HiRes", Artist = "Artist A", Album = "Album A", FilePath = sourceFlac };
        var track2 = new Track { Title = "AlreadyMp3", Artist = "Artist A", Album = "Album A", FilePath = dupFlac };
        var albums = new ObservableCollection<Album> { new Album("Album A", "Artist A", 2024, new[] { track1, track2 }) };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: fakeTranscoder);

            // Act
            var options = sut.GetCurrentTranscodeOptions();
            await sut.TransferTracksToFileSystemAsync(new[] { track1, track2 }, destDir, options);

            // Assert
            string expectedDestMp3 = Path.Combine(existingAlbumDir, "01_HiRes.mp3");
            Assert.True(options.ConvertFlacToMp3);
            Assert.Equal(1, fakeTranscoder.TranscodeCallCount);
            Assert.True(File.Exists(expectedDestMp3));
            Assert.Equal("transcoded-mp3-bytes", File.ReadAllText(expectedDestMp3));
            Assert.Equal("already-mp3", File.ReadAllText(Path.Combine(existingAlbumDir, "02_AlreadyMp3.mp3")));
            Assert.NotNull(fakeTranscoder.LastTemporaryDirectory);
            Assert.False(Directory.Exists(fakeTranscoder.LastTemporaryDirectory));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// SanitizeFileName が末尾のピリオド・空白・無効文字を OS 作成フォルダ名と一致するよう正規化し、先頭・途中のピリオドは保持することを検証します。
    /// </summary>
    [Theory]
    [InlineData("DIVERSITY.", "DIVERSITY")]
    [InlineData("DIVERSITY...", "DIVERSITY")]
    [InlineData("DIVERSITY . ", "DIVERSITY")]
    [InlineData(".hack", ".hack")]
    [InlineData("Mr. Children", "Mr. Children")]
    [InlineData("...", "Unknown")]
    [InlineData("   ", "Unknown")]
    [InlineData("", "Unknown")]
    public void SanitizeFileName_末尾ピリオドや空白を含む名称_保存と照合で一貫したフォルダ名へ正規化されること(string input, string expected)
    {
        // Arrange & Act
        string actual = DeviceBrowserViewModel.SanitizeFileName(input);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 末尾がピリオドのアルバムおよび通常名アルバムをファイルシステムのサブフォルダへ転送した場合、完了通知時点で再走査結果が反映されチェック解除・非活性となり、ライブラリの表示名が維持されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_末尾ピリオドのアルバムをサブフォルダへ転送時_再走査完了後に通知され非活性となり表示名を維持すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_DotSub_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string musicDir = Path.Combine(deviceRoot, "Music");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(musicDir);

        string dotTrackPath = Path.Combine(sourceDir, "01.mp3");
        string normalTrackPath = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(dotTrackPath, "dot-track-data");
        File.WriteAllText(normalTrackPath, "normal-track-data");

        var dotTrack = new Track { Title = "Track 01", Artist = "M2U", Album = "DIVERSITY.", FilePath = dotTrackPath };
        var normalTrack = new Track { Title = "Track 02", Artist = "M2U", Album = "Control", FilePath = normalTrackPath };
        var dotAlbum = new Album("DIVERSITY.", "M2U", 2024, new[] { dotTrack }) { IsSelected = true };
        var normalAlbum = new Album("Control", "M2U", 2024, new[] { normalTrack }) { IsSelected = true };
        var albums = new ObservableCollection<Album> { dotAlbum, normalAlbum };

        bool dotAlbumOnDeviceAtNotification = false;
        bool dotAlbumSelectedAtNotification = true;
        bool isTransferringAtNotification = false;
        var messages = new List<(string Message, string Caption)>();

        DeviceBrowserViewModel? sut = null;
        try
        {
            sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (msg, caption) =>
                {
                    messages.Add((msg, caption));
                    dotAlbumOnDeviceAtNotification = dotAlbum.IsOnDevice;
                    dotAlbumSelectedAtNotification = dotAlbum.IsSelected;
                    isTransferringAtNotification = sut!.IsTransferring;
                });

            var fsDevice = new MainViewModel.DeviceViewModel
            {
                Name = "TestDrive",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };
            sut.SelectedDevice = fsDevice;
            sut.CurrentDevicePath = musicDir;

            // Act
            await sut.TransferSelectedAsync();

            // 同じフォルダの再読込後も状態が維持されることを確認
            await sut.CheckDeviceAlbumsAsync();

            // Assert
            Assert.Single(messages);
            Assert.Equal("Success", messages[0].Caption);
            Assert.True(dotAlbumOnDeviceAtNotification);
            Assert.False(dotAlbumSelectedAtNotification);
            Assert.True(isTransferringAtNotification);
            Assert.False(sut.IsTransferring);

            Assert.True(File.Exists(Path.Combine(musicDir, "M2U", "DIVERSITY", "01.mp3")));
            Assert.True(File.Exists(Path.Combine(musicDir, "M2U", "Control", "02.mp3")));
            Assert.True(dotTrack.IsOnDevice);
            Assert.False(dotTrack.IsSelected);
            Assert.True(dotAlbum.IsOnDevice);
            Assert.False(dotAlbum.IsSelected);
            Assert.True(normalTrack.IsOnDevice);
            Assert.False(normalTrack.IsSelected);
            Assert.True(normalAlbum.IsOnDevice);
            Assert.False(normalAlbum.IsSelected);
            Assert.Empty(DeviceBrowserViewModel.CollectTransferTargetTracks(albums));

            // ライブラリ表示名は元のタグ表記を維持する
            Assert.Equal("DIVERSITY.", dotAlbum.Title);
            Assert.Equal("DIVERSITY.", dotTrack.Album);
        }
        finally
        {
            sut?.Dispose();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 機器ルートを転送先として通常名および末尾ピリオド名のアルバムを転送した場合、ルート全域の再帰走査を行わずに既知転送先のみを照合し、再読込後も非活性状態が維持されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_機器ルートへ転送時_ルート全域を再帰走査せず既知パスのみ照合し再読込後も維持されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_RootTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceRoot = Path.Combine(tempRoot, "DeviceRoot");
        string unrelatedDeepDir = Path.Combine(deviceRoot, "DCIM", "Nested");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(unrelatedDeepDir);

        // ルート配下の無関係ディレクトリにある同名音声ファイル（走査対象外であることの確認用）
        File.WriteAllText(Path.Combine(unrelatedDeepDir, "01.mp3"), "unrelated-audio");

        string rootNormalPath = Path.Combine(sourceDir, "01.mp3");
        string rootDotPath = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(rootNormalPath, "root-normal-data");
        File.WriteAllText(rootDotPath, "root-dot-data");

        var normalTrack = new Track { Title = "Root Song", Artist = "M2U", Album = "RootControl", FilePath = rootNormalPath };
        var dotTrack = new Track { Title = "Dot Song", Artist = "M2U", Album = "DIVERSITY.", FilePath = rootDotPath };
        var normalAlbum = new Album("RootControl", "M2U", 2024, new[] { normalTrack }) { IsSelected = true };
        var dotAlbum = new Album("DIVERSITY.", "M2U", 2024, new[] { dotTrack }) { IsSelected = true };
        var albums = new ObservableCollection<Album> { normalAlbum, dotAlbum };

        var messages = new List<(string Message, string Caption)>();

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (msg, caption) => messages.Add((msg, caption)));

            var fsDevice = new MainViewModel.DeviceViewModel
            {
                Name = "RootDrive",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };
            sut.SelectedDevice = fsDevice;

            // Act
            await sut.TransferSelectedAsync();

            // 同じルートフォルダの再読込を実行しても状態が維持されること
            sut.RefreshDirectoryCommand.Execute(null);
            await sut.CheckDeviceAlbumsAsync();

            // 既知フォルダのみを対象としたルートインデックスに無関係フォルダのファイルが含まれないことも検証
            var knownTargets = DeviceBrowserViewModel.CollectKnownTrackFolderTargets(albums);
            var rootIndex = DeviceBrowserViewModel.BuildFileSystemAudioIndex(deviceRoot, recursive: false, knownTargetFolders: knownTargets);

            // Assert
            Assert.Single(messages);
            Assert.Equal("Success", messages[0].Caption);
            Assert.True(normalTrack.IsOnDevice);
            Assert.False(normalTrack.IsSelected);
            Assert.True(normalAlbum.IsOnDevice);
            Assert.False(normalAlbum.IsSelected);
            Assert.True(dotTrack.IsOnDevice);
            Assert.False(dotTrack.IsSelected);
            Assert.True(dotAlbum.IsOnDevice);
            Assert.False(dotAlbum.IsSelected);
            Assert.Empty(DeviceBrowserViewModel.CollectTransferTargetTracks(albums));

            Assert.Contains(@"M2U\RootControl\01", rootIndex);
            Assert.Contains(@"M2U\DIVERSITY\02", rootIndex);
            Assert.DoesNotContain(@"DCIM\Nested\01", rootIndex);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 一部の曲のみ転送したアルバム、ソース欠落曲、別アルバムの同名曲、および拡張子変換を伴う転送で、成功した実在曲のみが非活性となり残りの未転送曲を選択できることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_一部曲転送と欠落曲および拡張子変換混在時_成功曲のみ非活性となり残り曲を選択できること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_PartialTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string otherSourceDir = Path.Combine(tempRoot, "OtherSource");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string musicDir = Path.Combine(deviceRoot, "Music");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(otherSourceDir);
        Directory.CreateDirectory(musicDir);

        string flacPath1 = Path.Combine(sourceDir, "01.flac");
        string flacPath2 = Path.Combine(sourceDir, "02.flac");
        string missingPath = Path.Combine(sourceDir, "03.flac"); // 実体なし（欠落）
        string otherSameNamePath = Path.Combine(otherSourceDir, "01.flac");
        File.WriteAllText(flacPath1, "flac-1");
        File.WriteAllText(flacPath2, "flac-2");
        File.WriteAllText(otherSameNamePath, "other-flac-1");

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.ConvertFlacToMp3 = true);
        var fakeTranscoder = new FakeAudioTranscodeService();

        var track1 = new Track { Title = "Track 1", Artist = "M2U", Album = "DIVERSITY.", FilePath = flacPath1, IsSelected = true };
        var track2 = new Track { Title = "Track 2", Artist = "M2U", Album = "DIVERSITY.", FilePath = flacPath2, IsSelected = false };
        var missingTrack = new Track { Title = "Track 3 (Missing)", Artist = "M2U", Album = "DIVERSITY.", FilePath = missingPath, IsSelected = true };
        var partialAlbum = new Album("DIVERSITY.", "M2U", 2024, new[] { track1, track2, missingTrack });

        // 別アルバムの同名ファイル（01.flac）は未転送のままであること
        var otherTrack = new Track { Title = "Other 1", Artist = "OtherArtist", Album = "OtherAlbum", FilePath = otherSameNamePath, IsSelected = false };
        var otherAlbum = new Album("OtherAlbum", "OtherArtist", 2024, new[] { otherTrack });

        var albums = new ObservableCollection<Album> { partialAlbum, otherAlbum };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: fakeTranscoder,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: _ => true);

            sut.SelectedDevice = new MainViewModel.DeviceViewModel
            {
                Name = "TestDrive",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };
            sut.CurrentDevicePath = musicDir;

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.True(File.Exists(Path.Combine(musicDir, "M2U", "DIVERSITY", "01.mp3")));
            Assert.True(track1.IsOnDevice);
            Assert.False(track1.IsSelected);

            // 未選択だった track2 と、実体欠落の missingTrack は転送済みにしない
            Assert.False(track2.IsOnDevice);
            Assert.False(missingTrack.IsOnDevice);
            Assert.False(partialAlbum.IsOnDevice);

            // 別アルバムの同名曲は誤って転送済みにされない
            Assert.False(otherTrack.IsOnDevice);
            Assert.False(otherAlbum.IsOnDevice);

            // 残りの曲は引き続き選択可能
            track2.IsSelected = true;
            Assert.True(track2.IsSelected);
            var nextTargets = DeviceBrowserViewModel.CollectTransferTargetTracks(albums);
            Assert.Contains(track2, nextTargets);
            Assert.DoesNotContain(track1, nextTargets);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 書き込み可能な転送先を持つ MTP 機器のルートおよびサブフォルダへの転送において、再走査完了まで完了通知を待機し、無関係フォルダを走査せず既知パスおよび旧末尾ピリオド配置を認識して再読込後も非活性状態を維持することを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_書き込み可能なMTP機器のルートおよびサブフォルダ転送時_再走査完了後に通知され再読込後も非活性を維持すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_MtpTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);

        string dotPath = Path.Combine(sourceDir, "01.mp3");
        string legacyDotPath = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(dotPath, "mtp-dot-data");
        File.WriteAllText(legacyDotPath, "mtp-legacy-dot-data");

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddDirectory(@"\DCIM");
        fakeMtp.AddDirectory(@"\DCIM\Camera");
        fakeMtp.AddFile(@"\DCIM\Camera\01.mp3");
        // 旧版で末尾ピリオドフォルダのまま保存済みのファイルを模擬
        fakeMtp.AddFile(@"\M2U\DIVERSITY.\02.mp3");

        var scanStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowScanToComplete = new ManualResetEventSlim(false);
        bool shouldBlockNextScan = false;
        fakeMtp.OnGetFiles = dir =>
        {
            if (shouldBlockNextScan && string.Equals(dir, @"\M2U\DIVERSITY", StringComparison.OrdinalIgnoreCase))
            {
                shouldBlockNextScan = false;
                scanStarted.TrySetResult(true);
                allowScanToComplete.Wait(TimeSpan.FromSeconds(5));
            }
        };

        var track1 = new Track { Title = "Track 01", Artist = "M2U", Album = "DIVERSITY.", FilePath = dotPath };
        var track2 = new Track { Title = "Track 02", Artist = "M2U", Album = "DIVERSITY.", FilePath = legacyDotPath };
        var album = new Album("DIVERSITY.", "M2U", 2024, new[] { track1, track2 });
        var albums = new ObservableCollection<Album> { album };

        bool notifiedBeforeScanReleased = false;
        bool albumOnDeviceAtNotification = false;
        var notifications = new List<string>();

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (msg, caption) =>
                {
                    if (!allowScanToComplete.IsSet)
                    {
                        notifiedBeforeScanReleased = true;
                    }
                    albumOnDeviceAtNotification = album.IsOnDevice;
                    notifications.Add($"{caption}:{msg}");
                });

            var mtpDevice = new MainViewModel.DeviceViewModel
            {
                Name = "Fake WALKMAN",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };
            sut.SelectedDevice = mtpDevice;

            // 初回走査で旧末尾ピリオド配置の track2 が既に認識されていること
            await sut.CheckDeviceAlbumsAsync();
            Assert.False(track1.IsOnDevice);
            Assert.True(track2.IsOnDevice);
            Assert.False(album.IsOnDevice);

            // 未転送の track1 のみ選択（アルバム全体選択しても track2 は既転送のため除外）
            album.IsSelected = true;
            Assert.True(track1.IsSelected);
            Assert.False(track2.IsSelected);

            // Act: 再走査を一時停止させた状態でルート転送を実行
            fakeMtp.ClearEnumeratedDirectories();
            shouldBlockNextScan = true;
            var transferTask = sut.TransferSelectedAsync();

            await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(notifications);
            Assert.True(sut.IsTransferring);

            allowScanToComplete.Set();
            await transferTask;

            // 同じルートフォルダの再読込後も状態が維持されること
            await sut.CheckDeviceAlbumsAsync();

            // Assert
            Assert.False(notifiedBeforeScanReleased);
            Assert.True(albumOnDeviceAtNotification);
            Assert.Single(notifications);
            Assert.StartsWith("Success:", notifications[0]);
            Assert.False(sut.IsTransferring);
            Assert.True(fakeMtp.FileExists(@"\M2U\DIVERSITY\01.mp3"));
            Assert.DoesNotContain(@"\DCIM", fakeMtp.EnumeratedDirectories, StringComparer.OrdinalIgnoreCase);
            Assert.True(track1.IsOnDevice);
            Assert.False(track1.IsSelected);
            Assert.True(track2.IsOnDevice);
            Assert.True(album.IsOnDevice);
            Assert.False(album.IsSelected);

            // さらに MTP サブフォルダへの転送・再走査も同様に機能すること
            fakeMtp.AddDirectory(@"\Music");
            sut.CurrentDevicePath = @"\Music";
            await sut.CheckDeviceAlbumsAsync();
            Assert.False(track1.IsOnDevice);
            Assert.False(album.IsOnDevice);

            album.IsSelected = true;
            await sut.TransferSelectedAsync();
            await sut.CheckDeviceAlbumsAsync();

            Assert.True(fakeMtp.FileExists(@"\Music\M2U\DIVERSITY\01.mp3"));
            Assert.True(fakeMtp.FileExists(@"\Music\M2U\DIVERSITY\02.mp3"));
            Assert.True(track1.IsOnDevice);
            Assert.True(track2.IsOnDevice);
            Assert.True(album.IsOnDevice);
            Assert.False(album.IsSelected);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ピリオドや空白のみで構成されるアーティスト名・アルバム名について、旧配置互換キーおよび既知フォルダ候補に "." や ".." が混入せず "Unknown" へ正規化されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(".", "..")]
    [InlineData("..", "...")]
    [InlineData(" . ", " .. ")]
    public void BuildExpectedTrackRelativeKeys_ピリオドのみの名称_親ディレクトリ参照を組み立てずUnknownへ正規化されること(string artist, string albumName)
    {
        // Arrange
        var track = new Track { Title = "Track 01", Artist = artist, Album = albumName, FilePath = @"C:\Music\01.mp3" };
        var album = new Album(albumName, artist, 2024, new[] { track });

        // Act
        var keys = DeviceBrowserViewModel.BuildExpectedTrackRelativeKeys(track);
        var knownTargets = DeviceBrowserViewModel.CollectKnownTrackFolderTargets(new[] { album });

        // Assert
        Assert.Single(keys);
        Assert.Equal(@"Unknown\Unknown\01", keys[0]);
        Assert.True(knownTargets.TryGetValue("Unknown", out var albumFolders));
        Assert.Single(albumFolders!);
        Assert.Contains("Unknown", albumFolders!);
        Assert.DoesNotContain(".", knownTargets.Keys);
        Assert.DoesNotContain("..", knownTargets.Keys);
    }

    /// <summary>
    /// 未接続状態の MTP 機器に対して TransferSelectedAsync を実行した場合、誤って成功通知を行わずエラー通知で終了することを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_未接続のMTP機器で実行時_成功通知せずエラー通知で終了すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_MtpDisconnected_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string sourcePath = Path.Combine(tempRoot, "01.mp3");
        File.WriteAllText(sourcePath, "audio-bytes");

        var fakeMtp = new FakeMtpDeviceClient();
        var track = new Track { Title = "Song", Artist = "Artist A", Album = "Album A", FilePath = sourcePath };
        var album = new Album("Album A", "Artist A", 2024, new[] { track }) { IsSelected = true };
        var albums = new ObservableCollection<Album> { album };
        var notifications = new List<(string Message, string Caption)>();

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (msg, caption) => notifications.Add((msg, caption)));

            var mtpDevice = new MainViewModel.DeviceViewModel
            {
                Name = "Disconnected MTP",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };
            sut.SelectedDevice = mtpDevice;
            fakeMtp.Disconnect();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Single(notifications);
            Assert.Equal("Error", notifications[0].Caption);
            Assert.False(fakeMtp.FileExists(@"\Artist A\Album A\01.mp3"));
            Assert.False(track.IsOnDevice);
            Assert.False(sut.IsTransferring);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 転送途中で例外が発生し後続のディレクトリ再読込も失敗した場合、部分転送成功分のみ IsOnDevice=true が反映され、エラー通知が二重発火せず 1 回のみ行われることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_転送途中失敗かつ再読込例外発生時_部分転送分を反映しエラー通知が1回のみ行われること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_MtpPartialFail_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string source1 = Path.Combine(tempRoot, "01.mp3");
        string source2 = Path.Combine(tempRoot, "02.mp3");
        File.WriteAllText(source1, "track-1");
        File.WriteAllText(source2, "track-2");

        var fakeMtp = new FakeMtpDeviceClient();
        bool failRootDirectoryListing = false;
        fakeMtp.OnUploadFile = (_, destPath) =>
        {
            if (destPath.EndsWith("02.mp3", StringComparison.OrdinalIgnoreCase))
            {
                failRootDirectoryListing = true;
                throw new IOException("Simulated cable disconnect");
            }
        };
        fakeMtp.OnGetFiles = dir =>
        {
            if (failRootDirectoryListing && string.Equals(dir, @"\", StringComparison.OrdinalIgnoreCase))
            {
                // LoadDeviceDirectoriesCore の GetFiles("\") で例外を起こさせ、二重通知抑止を検証
                failRootDirectoryListing = false;
                throw new IOException("Directory read failed after disconnect");
            }
        };

        var track1 = new Track { Title = "Track 01", Artist = "M2U", Album = "DIVERSITY.", FilePath = source1 };
        var track2 = new Track { Title = "Track 02", Artist = "M2U", Album = "DIVERSITY.", FilePath = source2 };
        var album = new Album("DIVERSITY.", "M2U", 2024, new[] { track1, track2 }) { IsSelected = true };
        var albums = new ObservableCollection<Album> { album };
        var notifications = new List<(string Message, string Caption)>();

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (msg, caption) => notifications.Add((msg, caption)));

            sut.SelectedDevice = new MainViewModel.DeviceViewModel
            {
                Name = "Fake MTP",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };
            await sut.CheckDeviceAlbumsAsync();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Single(notifications);
            Assert.Equal("Error", notifications[0].Caption);
            Assert.Contains("Simulated cable disconnect", notifications[0].Message);
            Assert.True(track1.IsOnDevice);
            Assert.False(track1.IsSelected);
            Assert.False(track2.IsOnDevice);
            Assert.False(album.IsOnDevice);
            Assert.False(sut.IsTransferring);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 複数演奏者が含まれるアルバムを一括転送・1曲ずつ追加転送・FLAC→MP3変換転送した場合、いずれも代表アーティスト配下の同一フォルダへ保存され、各曲の演奏者タグが保持され再走査で非活性になることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_複数演奏者アルバムの一括転送と1曲追加およびFLAC変換時_代表アーティスト配下に揃い演奏者タグを保持すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_AlbumArtistFs_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string bulkDir = Path.Combine(deviceRoot, "Bulk");
        string singleDir = Path.Combine(deviceRoot, "Single");
        string mp3Dir = Path.Combine(deviceRoot, "Mp3");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(bulkDir);
        Directory.CreateDirectory(singleDir);
        Directory.CreateDirectory(mp3Dir);

        string flac1Path = Path.Combine(sourceDir, "01.flac");
        string flac2Path = Path.Combine(sourceDir, "02.flac");
        File.WriteAllText(flac1Path, "flac-track-1");
        File.WriteAllText(flac2Path, "flac-track-2");

        var track1 = new Track { Title = "Track 1", Artist = "A", AlbumArtist = "A", Album = "作品X", FilePath = flac1Path };
        var track2 = new Track { Title = "Track 2", Artist = "B", AlbumArtist = "A", Album = "作品X", FilePath = flac2Path };
        var album = new Album("作品X", "A", 2024, new[] { track1, track2 });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        var fakeTranscoder = new FakeAudioTranscodeService();

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: fakeTranscoder,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: _ => true);

            sut.SelectedDevice = new MainViewModel.DeviceViewModel
            {
                Name = "TestDrive",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };

            // Act 1: アルバム一括転送（無変換）
            sut.CurrentDevicePath = bulkDir;
            await sut.CheckDeviceAlbumsAsync();
            album.IsSelected = true;
            await sut.TransferSelectedAsync();

            // Assert 1
            Assert.True(File.Exists(Path.Combine(bulkDir, "A", "作品X", "01.flac")));
            Assert.True(File.Exists(Path.Combine(bulkDir, "A", "作品X", "02.flac")));
            Assert.False(Directory.Exists(Path.Combine(bulkDir, "B")));
            Assert.True(track1.IsOnDevice);
            Assert.True(track2.IsOnDevice);
            Assert.True(album.IsOnDevice);
            Assert.False(album.IsSelected);
            Assert.Equal("A", track1.Artist);
            Assert.Equal("B", track2.Artist);

            // Act 2: 1曲ずつ追加転送（2曲目単体でも代表アーティスト "A" 配下に揃うこと）
            sut.CurrentDevicePath = singleDir;
            await sut.CheckDeviceAlbumsAsync();
            Assert.False(track1.IsOnDevice);
            Assert.False(track2.IsOnDevice);

            track1.IsSelected = true;
            await sut.TransferSelectedAsync();
            Assert.True(File.Exists(Path.Combine(singleDir, "A", "作品X", "01.flac")));
            Assert.True(track1.IsOnDevice);
            Assert.False(track2.IsOnDevice);
            Assert.False(album.IsOnDevice);

            track2.IsSelected = true;
            await sut.TransferSelectedAsync();

            // Assert 2
            Assert.True(File.Exists(Path.Combine(singleDir, "A", "作品X", "02.flac")));
            Assert.False(Directory.Exists(Path.Combine(singleDir, "B")));
            Assert.True(track2.IsOnDevice);
            Assert.True(album.IsOnDevice);
            Assert.Equal("B", track2.Artist);

            // Act 3: FLAC→MP3 変換有効時も親フォルダが変わらず同一フォルダへ保存されること
            settingsService.UpdateSettings(s => s.ConvertFlacToMp3 = true);
            sut.CurrentDevicePath = mp3Dir;
            await sut.CheckDeviceAlbumsAsync();
            album.IsSelected = true;
            await sut.TransferSelectedAsync();

            // Assert 3
            Assert.True(File.Exists(Path.Combine(mp3Dir, "A", "作品X", "01.mp3")));
            Assert.True(File.Exists(Path.Combine(mp3Dir, "A", "作品X", "02.mp3")));
            Assert.False(Directory.Exists(Path.Combine(mp3Dir, "B")));
            Assert.True(track1.IsOnDevice);
            Assert.True(track2.IsOnDevice);
            Assert.True(album.IsOnDevice);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 明示的な AlbumArtist がないコンピレーション作品（AlbumGroupingResolver により Various Artists 解決）および代表アーティストが異なる同名アルバムについて、PCライブラリの作品判定に従って保存・分離されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_コンピレーションおよび同名異アーティストアルバム_PCライブラリ判定に従い分離配置されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_CompAndSameTitle_{Guid.NewGuid():N}");
        string compSourceDir = Path.Combine(tempRoot, "Source", "Summer Hits");
        string sameSourceDirX = Path.Combine(tempRoot, "Source", "ArtistX_Best");
        string sameSourceDirY = Path.Combine(tempRoot, "Source", "ArtistY_Best");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string musicDir = Path.Combine(deviceRoot, "Music");
        Directory.CreateDirectory(compSourceDir);
        Directory.CreateDirectory(sameSourceDirX);
        Directory.CreateDirectory(sameSourceDirY);
        Directory.CreateDirectory(musicDir);

        string comp1Path = Path.Combine(compSourceDir, "01.mp3");
        string comp2Path = Path.Combine(compSourceDir, "02.mp3");
        string sameXPath = Path.Combine(sameSourceDirX, "01.mp3");
        string sameYPath = Path.Combine(sameSourceDirY, "01.mp3");
        File.WriteAllText(comp1Path, "comp-1");
        File.WriteAllText(comp2Path, "comp-2");
        File.WriteAllText(sameXPath, "same-x");
        File.WriteAllText(sameYPath, "same-y");

        // 明示的 AlbumArtist なしのコンピレーション 2 曲（同一フォルダ・連番・異なる演奏者）
        var comp1 = new Track { Title = "Song 1", Artist = "Singer 1", Album = "Summer Hits", TrackNumber = 1, FilePath = comp1Path };
        var comp2 = new Track { Title = "Song 2", Artist = "Singer 2", Album = "Summer Hits", TrackNumber = 2, FilePath = comp2Path };
        // 同名アルバム "Best" で代表アーティストが異なる 2 作品
        var bestX = new Track { Title = "Hit X", Artist = "Artist X", Album = "Best", TrackNumber = 1, FilePath = sameXPath };
        var bestY = new Track { Title = "Hit Y", Artist = "Artist Y", Album = "Best", TrackNumber = 1, FilePath = sameYPath };

        var allTracks = new[] { comp1, comp2, bestX, bestY };
        var resolved = AlbumGroupingResolver.ResolveAndApply(allTracks);

        var compAlbum = new Album("Summer Hits", resolved[comp1.Id].AlbumArtist, 2024, new[] { comp1, comp2 }, resolved[comp1.Id].AlbumId);
        var bestAlbumX = new Album("Best", resolved[bestX.Id].AlbumArtist, 2024, new[] { bestX }, resolved[bestX.Id].AlbumId);
        var bestAlbumY = new Album("Best", resolved[bestY.Id].AlbumArtist, 2024, new[] { bestY }, resolved[bestY.Id].AlbumId);
        var albums = new ObservableCollection<Album> { compAlbum, bestAlbumX, bestAlbumY };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { });

            sut.SelectedDevice = new MainViewModel.DeviceViewModel
            {
                Name = "TestDrive",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };
            sut.CurrentDevicePath = musicDir;
            await sut.CheckDeviceAlbumsAsync();

            // Act: コンピレーションと Artist X の "Best" のみ転送（Artist Y の "Best" は未選択）
            compAlbum.IsSelected = true;
            bestAlbumX.IsSelected = true;
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Equal(AlbumGroupingResolver.VariousArtistsName, compAlbum.Artist);
            Assert.True(File.Exists(Path.Combine(musicDir, "Various Artists", "Summer Hits", "01.mp3")));
            Assert.True(File.Exists(Path.Combine(musicDir, "Various Artists", "Summer Hits", "02.mp3")));
            Assert.False(Directory.Exists(Path.Combine(musicDir, "Singer 1")));
            Assert.False(Directory.Exists(Path.Combine(musicDir, "Singer 2")));
            Assert.True(compAlbum.IsOnDevice);

            Assert.True(File.Exists(Path.Combine(musicDir, "Artist X", "Best", "01.mp3")));
            Assert.False(Directory.Exists(Path.Combine(musicDir, "Artist Y")));
            Assert.True(bestAlbumX.IsOnDevice);
            Assert.False(bestAlbumY.IsOnDevice);
            Assert.False(bestY.IsOnDevice);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// MTP 機器で複数演奏者アルバムを転送した場合（FLAC→MP3変換有無を含む）、FileSystem と同じ代表アーティスト配下へ保存され再走査で全曲非活性になることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_MTP機器で複数演奏者アルバムとFLAC変換転送時_FileSystemと同じ代表アーティスト規則で配置されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_AlbumArtistMtp_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);

        string flac1Path = Path.Combine(sourceDir, "01.flac");
        string flac2Path = Path.Combine(sourceDir, "02.flac");
        File.WriteAllText(flac1Path, "flac-1");
        File.WriteAllText(flac2Path, "flac-2");

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddDirectory(@"\Music");
        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.ConvertFlacToMp3 = true);
        var fakeTranscoder = new FakeAudioTranscodeService();

        var track1 = new Track { Title = "Track 1", Artist = "A", AlbumArtist = "A", Album = "作品X", FilePath = flac1Path };
        var track2 = new Track { Title = "Track 2", Artist = "B", AlbumArtist = "A", Album = "作品X", FilePath = flac2Path };
        var album = new Album("作品X", "A", 2024, new[] { track1, track2 });
        var albums = new ObservableCollection<Album> { album };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: fakeTranscoder,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { },
                confirmTransfer: _ => true);

            sut.SelectedDevice = new MainViewModel.DeviceViewModel
            {
                Name = "Fake MTP",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };
            sut.CurrentDevicePath = @"\Music";
            await sut.CheckDeviceAlbumsAsync();

            // Act
            album.IsSelected = true;
            await sut.TransferSelectedAsync();

            // Assert
            Assert.True(fakeMtp.FileExists(@"\Music\A\作品X\01.mp3"));
            Assert.True(fakeMtp.FileExists(@"\Music\A\作品X\02.mp3"));
            Assert.False(fakeMtp.DirectoryExists(@"\Music\B"));
            Assert.True(track1.IsOnDevice);
            Assert.True(track2.IsOnDevice);
            Assert.True(album.IsOnDevice);
            Assert.False(album.IsSelected);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 旧版の曲別アーティスト配下に一部の曲が転送済みの機器（FileSystem ルート／サブフォルダおよび MTP ルート）において、既存曲を認識して再選択不可にし、残りの未転送曲だけを代表アーティスト配下へ追加転送して変換ON/OFF双方で二重転送を防ぐことを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_旧配置に一部転送済みの機器で再走査および追加転送時_既存曲を認識し残り曲のみ新配置へ追加転送されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_LegacyLayout_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceRoot = Path.Combine(tempRoot, "DeviceRoot");
        string legacyGuestDir = Path.Combine(deviceRoot, "B", "作品X");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(legacyGuestDir);

        string track1Path = Path.Combine(sourceDir, "01.flac");
        string track2Path = Path.Combine(sourceDir, "02.flac");
        File.WriteAllText(track1Path, "flac-1");
        File.WriteAllText(track2Path, "flac-2");
        // 旧版の曲別アーティスト "B" 配下に track2 が既に転送済み（変換済み .mp3 で模擬）および非音声ファイル（誤スキップ防止確認用）
        File.WriteAllText(Path.Combine(legacyGuestDir, "02.mp3"), "legacy-transferred-mp3");
        File.WriteAllText(Path.Combine(legacyGuestDir, "01.jpg"), "cover-image");

        var track1 = new Track { Title = "Track 1", Artist = "A", AlbumArtist = "A", Album = "作品X", FilePath = track1Path };
        var track2 = new Track { Title = "Track 2", Artist = "B", AlbumArtist = "A", Album = "作品X", FilePath = track2Path };
        var album = new Album("作品X", "A", 2024, new[] { track1, track2 });
        var albums = new ObservableCollection<Album> { album };

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddFile(@"\B\作品X\02.mp3");
        fakeMtp.AddFile(@"\B\作品X\01.jpg");

        var fsTranscoder = new FakeAudioTranscodeService();
        var mtpTranscoder = new FakeAudioTranscodeService();

        try
        {
            // FileSystem ルートでの検証
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: fsTranscoder,
                resolveMtpClient: null,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "LegacyFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = deviceRoot
                };
                await sut.CheckDeviceAlbumsAsync();

                // 旧配置の track2 が既転送として認識され選択不可（非音声 01.jpg は track1 に影響しない）
                Assert.False(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.False(album.IsOnDevice);

                track2.IsSelected = true;
                Assert.False(track2.IsSelected);

                // Act 1: アルバム選択 → 未転送の track1 のみ追加転送される
                album.IsSelected = true;
                Assert.True(track1.IsSelected);
                Assert.False(track2.IsSelected);

                await sut.TransferSelectedAsync();

                // Assert 1: track1 は新配置へ保存され、track2 は新配置へ二重転送されない
                Assert.True(File.Exists(Path.Combine(deviceRoot, "A", "作品X", "01.flac")));
                Assert.False(File.Exists(Path.Combine(deviceRoot, "A", "作品X", "02.flac")));
                Assert.True(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.True(album.IsOnDevice);

                // 直接 TransferTracksToFileSystemAsync へ track2（旧配置に 02.mp3 が存在）を変換 ON / OFF 双方で渡してもスキップされ二重転送されないこと
                await sut.TransferTracksToFileSystemAsync(
                    new[] { track2 },
                    deviceRoot,
                    new AudioTranscodeOptions { ConvertFlacToMp3 = true });
                await sut.TransferTracksToFileSystemAsync(
                    new[] { track2 },
                    deviceRoot,
                    new AudioTranscodeOptions { ConvertFlacToMp3 = false });
                Assert.Equal(0, fsTranscoder.TranscodeCallCount);
                Assert.False(File.Exists(Path.Combine(deviceRoot, "A", "作品X", "02.mp3")));
                Assert.False(File.Exists(Path.Combine(deviceRoot, "A", "作品X", "02.flac")));
            }

            // MTP ルートでの旧配置認識・残り曲のみ追加転送・変換 ON/OFF 二重転送防止検証
            using (var mtpSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: mtpTranscoder,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { }))
            {
                mtpSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "LegacyMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };
                await mtpSut.CheckDeviceAlbumsAsync();

                Assert.False(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.False(album.IsOnDevice);

                album.IsSelected = true;
                await mtpSut.TransferSelectedAsync();

                Assert.True(fakeMtp.FileExists(@"\A\作品X\01.flac"));
                Assert.False(fakeMtp.FileExists(@"\A\作品X\02.flac"));
                Assert.True(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.True(album.IsOnDevice);

                // MTP でも変換 ON / OFF 双方で旧配置 02.mp3 を検知し二重転送しないこと
                await mtpSut.TransferTracksToMtpAsync(
                    fakeMtp,
                    new[] { track2 },
                    @"\",
                    new AudioTranscodeOptions { ConvertFlacToMp3 = true });
                await mtpSut.TransferTracksToMtpAsync(
                    fakeMtp,
                    new[] { track2 },
                    @"\",
                    new AudioTranscodeOptions { ConvertFlacToMp3 = false });
                Assert.Equal(0, mtpTranscoder.TranscodeCallCount);
                Assert.False(fakeMtp.FileExists(@"\A\作品X\02.mp3"));
                Assert.False(fakeMtp.FileExists(@"\A\作品X\02.flac"));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 同名アルバム・異代表アーティストの 2 作品（例: Various Artists / Best と Artist X / Best）で演奏者名が重複する場合、一方を転送しても他方の未転送曲が旧配置フォールバックで誤衝突せず分離管理できることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_同名アルバム異代表アーティストで演奏者重複時_旧配置誤衝突せず分離管理できること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_SameAlbumCollision_{Guid.NewGuid():N}");
        string compSourceDir = Path.Combine(tempRoot, "Source", "CompBest");
        string soloSourceDir = Path.Combine(tempRoot, "Source", "SoloBest");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string musicDir = Path.Combine(deviceRoot, "Music");
        Directory.CreateDirectory(compSourceDir);
        Directory.CreateDirectory(soloSourceDir);
        Directory.CreateDirectory(musicDir);

        string compTrackPath = Path.Combine(compSourceDir, "01.mp3");
        string soloTrackPath = Path.Combine(soloSourceDir, "01.mp3");
        File.WriteAllText(compTrackPath, "comp-track");
        File.WriteAllText(soloTrackPath, "solo-track");

        var compTrack = new Track { Title = "Comp Hit", Artist = "Artist X", Album = "Best", FilePath = compTrackPath };
        var soloTrack = new Track { Title = "Solo Hit", Artist = "Artist X", Album = "Best", FilePath = soloTrackPath };
        var compAlbum = new Album("Best", AlbumGroupingResolver.VariousArtistsName, 2024, new[] { compTrack });
        var soloAlbum = new Album("Best", "Artist X", 2024, new[] { soloTrack });
        var albums = new ObservableCollection<Album> { compAlbum, soloAlbum };

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddDirectory(@"\Music");

        try
        {
            // FileSystem 検証
            using (var fsSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { }))
            {
                fsSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "CollisionFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = deviceRoot
                };
                fsSut.CurrentDevicePath = musicDir;
                await fsSut.CheckDeviceAlbumsAsync();

                // Act 1: Artist X / Best のみ転送
                soloAlbum.IsSelected = true;
                await fsSut.TransferSelectedAsync();

                // Assert 1: Various Artists / Best は旧配置誤衝突せず未転送のまま選択可能
                Assert.True(File.Exists(Path.Combine(musicDir, "Artist X", "Best", "01.mp3")));
                Assert.True(soloTrack.IsOnDevice);
                Assert.True(soloAlbum.IsOnDevice);
                Assert.False(compTrack.IsOnDevice);
                Assert.False(compAlbum.IsOnDevice);

                // Act 2: 続けて Various Artists / Best を転送 → 別フォルダへ分離保存
                compAlbum.IsSelected = true;
                Assert.True(compTrack.IsSelected);
                await fsSut.TransferSelectedAsync();

                // Assert 2
                Assert.True(File.Exists(Path.Combine(musicDir, "Various Artists", "Best", "01.mp3")));
                Assert.True(compTrack.IsOnDevice);
                Assert.True(compAlbum.IsOnDevice);
            }

            // MTP 検証
            using (var mtpSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { }))
            {
                mtpSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "CollisionMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };
                mtpSut.CurrentDevicePath = @"\Music";
                await mtpSut.CheckDeviceAlbumsAsync();

                soloAlbum.IsSelected = true;
                await mtpSut.TransferSelectedAsync();

                Assert.True(fakeMtp.FileExists(@"\Music\Artist X\Best\01.mp3"));
                Assert.True(soloAlbum.IsOnDevice);
                Assert.False(compTrack.IsOnDevice);
                Assert.False(compAlbum.IsOnDevice);

                compAlbum.IsSelected = true;
                await mtpSut.TransferSelectedAsync();

                Assert.True(fakeMtp.FileExists(@"\Music\Various Artists\Best\01.mp3"));
                Assert.True(compAlbum.IsOnDevice);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// albums 内と別インスタンスの Track（同一 TrackId または同一 FilePath）を 1 曲だけ転送対象に渡した場合でも、所属アルバムの解決済み代表アーティスト（コンピレーション含む）へ配置されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksToFileSystemAsync_別インスタンスのトラック1曲のみ渡した場合_所属アルバムの代表アーティストへ解決されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_TrackClone_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source", "Summer Hits");
        string destByIdDir = Path.Combine(tempRoot, "DestById");
        string destByPathDir = Path.Combine(tempRoot, "DestByPath");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destByIdDir);
        Directory.CreateDirectory(destByPathDir);

        string track1Path = Path.Combine(sourceDir, "01.mp3");
        string track2Path = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(track1Path, "comp-1");
        File.WriteAllText(track2Path, "comp-2");

        var original1 = new Track { Title = "Song 1", Artist = "Singer 1", Album = "Summer Hits", TrackNumber = 1, FilePath = track1Path };
        var original2 = new Track { Title = "Song 2", Artist = "Singer 2", Album = "Summer Hits", TrackNumber = 2, FilePath = track2Path };
        var compAlbum = new Album("Summer Hits", AlbumGroupingResolver.VariousArtistsName, 2024, new[] { original1, original2 });
        var albums = new ObservableCollection<Album> { compAlbum };

        // 同一 TrackId を持つ別インスタンス（AlbumArtist 未設定・1 曲単独）
        var cloneWithSameId = new Track(
            original1.Id,
            AudioEffector.Domain.ValueObjects.AudioPath.Create(track1Path),
            "Song 1",
            "Singer 1",
            "Summer Hits",
            TimeSpan.FromSeconds(180),
            trackNumber: 1);

        // 別 TrackId だが同一 FilePath を持つ別インスタンス（AlbumArtist 未設定・1 曲単独）
        var cloneWithSamePath = new Track
        {
            Title = "Song 2",
            Artist = "Singer 2",
            Album = "Summer Hits",
            TrackNumber = 2,
            FilePath = track2Path
        };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L));

            // Act
            await sut.TransferTracksToFileSystemAsync(new[] { cloneWithSameId }, destByIdDir, new AudioTranscodeOptions());
            await sut.TransferTracksToFileSystemAsync(new[] { cloneWithSamePath }, destByPathDir, new AudioTranscodeOptions());

            // Assert
            Assert.True(File.Exists(Path.Combine(destByIdDir, "Various Artists", "Summer Hits", "01.mp3")));
            Assert.False(Directory.Exists(Path.Combine(destByIdDir, "Singer 1")));
            Assert.True(File.Exists(Path.Combine(destByPathDir, "Various Artists", "Summer Hits", "02.mp3")));
            Assert.False(Directory.Exists(Path.Combine(destByPathDir, "Singer 2")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 末尾がピリオドのアルバムにゲスト参加曲が含まれる複合ケース（Epic #399 完了条件）において、全曲が代表アーティスト配下の同一フォルダへ保存され、再走査後に全曲およびアルバムが選択不可になることを FileSystem・MTP 双方で検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_末尾ピリオドのアルバムにゲスト参加曲がある複合ケース_同一フォルダ保存と再走査後の全曲非活性が機能すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_Epic399Combined_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceRoot = Path.Combine(tempRoot, "DeviceRoot");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceRoot);

        string mainTrackPath = Path.Combine(sourceDir, "01.mp3");
        string guestTrackPath = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(mainTrackPath, "main-track");
        File.WriteAllText(guestTrackPath, "guest-track");

        var mainTrack = new Track { Title = "Main Song", Artist = "M2U", AlbumArtist = "M2U", Album = "DIVERSITY.", FilePath = mainTrackPath };
        var guestTrack = new Track { Title = "Guest Song", Artist = "GuestArtist.", AlbumArtist = "M2U", Album = "DIVERSITY.", FilePath = guestTrackPath };
        var album = new Album("DIVERSITY.", "M2U", 2024, new[] { mainTrack, guestTrack });
        var albums = new ObservableCollection<Album> { album };

        var fakeMtp = new FakeMtpDeviceClient();

        try
        {
            // Act & Assert 1: FileSystem ルートでの複合ケース
            using (var fsSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { }))
            {
                fsSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "CombinedFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = deviceRoot
                };
                await fsSut.CheckDeviceAlbumsAsync();

                album.IsSelected = true;
                await fsSut.TransferSelectedAsync();

                Assert.True(File.Exists(Path.Combine(deviceRoot, "M2U", "DIVERSITY", "01.mp3")));
                Assert.True(File.Exists(Path.Combine(deviceRoot, "M2U", "DIVERSITY", "02.mp3")));
                Assert.False(Directory.Exists(Path.Combine(deviceRoot, "GuestArtist")));
                Assert.True(mainTrack.IsOnDevice);
                Assert.False(mainTrack.IsSelected);
                Assert.True(guestTrack.IsOnDevice);
                Assert.False(guestTrack.IsSelected);
                Assert.True(album.IsOnDevice);
                Assert.False(album.IsSelected);
                Assert.Empty(DeviceBrowserViewModel.CollectTransferTargetTracks(albums));
                Assert.Equal("DIVERSITY.", album.Title);
                Assert.Equal("GuestArtist.", guestTrack.Artist);
            }

            // Act & Assert 2: MTP ルートでの複合ケース
            using (var mtpSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { }))
            {
                mtpSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "CombinedMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };
                await mtpSut.CheckDeviceAlbumsAsync();

                album.IsSelected = true;
                await mtpSut.TransferSelectedAsync();

                Assert.True(fakeMtp.FileExists(@"\M2U\DIVERSITY\01.mp3"));
                Assert.True(fakeMtp.FileExists(@"\M2U\DIVERSITY\02.mp3"));
                Assert.False(fakeMtp.DirectoryExists(@"\GuestArtist"));
                Assert.True(mainTrack.IsOnDevice);
                Assert.True(guestTrack.IsOnDevice);
                Assert.True(album.IsOnDevice);
                Assert.False(album.IsSelected);
                Assert.Empty(DeviceBrowserViewModel.CollectTransferTargetTracks(albums));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// SanitizeFileName がファイル名禁止文字および制御文字をアンダースコア（_）へ置換し、既存のアンダースコア・記号・末尾ピリオド除去規則を維持することを検証します。
    /// </summary>
    /// <param name="input">入力文字列</param>
    /// <param name="expected">期待されるサニタイズ結果</param>
    [Theory]
    [InlineData("VISUAL ARTS / Key", "VISUAL ARTS _ Key")]
    [InlineData("My Soul, Your Beats!/Brave Song", "My Soul, Your Beats!_Brave Song")]
    [InlineData("A/B\\C:D*E?F\"G<H>I|J", "A_B_C_D_E_F_G_H_I_J")]
    [InlineData("Already_Underscore! , Name", "Already_Underscore! , Name")]
    [InlineData("Artist\u0001Name", "Artist_Name")]
    [InlineData("Album/Name.", "Album_Name")]
    [InlineData(" / ", "_")]
    public void SanitizeFileName_ファイル名禁止文字や既存アンダースコアを含む名称_禁止文字がアンダースコアへ置換されること(string input, string expected)
    {
        // Arrange
        var sourceName = input;

        // Act
        string actual = DeviceBrowserViewModel.SanitizeFileName(sourceName);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// ファイル名禁止文字を含むアーティスト名・アルバム名を新規転送した場合、FileSystem・MTP 双方かつサブフォルダ・機器ルート直下のいずれでもアンダースコア置換フォルダへ保存され、再走査で非活性・チェック解除となることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_ファイル名禁止文字を含む名称を新規転送時_アンダースコア置換フォルダへ保存され再走査で非活性になること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_SanitizeNewTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string fsDeviceRoot = Path.Combine(tempRoot, "FsDevice");
        string fsMusicDir = Path.Combine(fsDeviceRoot, "Music");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(fsMusicDir);

        string trackPath = Path.Combine(sourceDir, "01.flac");
        File.WriteAllText(trackPath, "source-audio-data");

        const string rawArtist = "VISUAL ARTS / Key";
        const string rawAlbum = "My Soul, Your Beats!/Brave Song";
        const string expectedArtistFolder = "VISUAL ARTS _ Key";
        const string expectedAlbumFolder = "My Soul, Your Beats!_Brave Song";

        var track = new Track { Title = "My Soul, Your Beats!", Artist = rawArtist, AlbumArtist = rawArtist, Album = rawAlbum, FilePath = trackPath };
        var album = new Album(rawAlbum, rawArtist, 2010, new[] { track });
        var albums = new ObservableCollection<Album> { album };

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddDirectory(@"\Music");

        try
        {
            // Act & Assert 1: FileSystem サブフォルダおよびルート直下
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "SanitizeFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = fsDeviceRoot
                };
                sut.CurrentDevicePath = fsMusicDir;
                await sut.CheckDeviceAlbumsAsync();

                album.IsSelected = true;
                await sut.TransferSelectedAsync();

                Assert.True(File.Exists(Path.Combine(fsMusicDir, expectedArtistFolder, expectedAlbumFolder, "01.flac")));
                Assert.False(Directory.Exists(Path.Combine(fsMusicDir, "VISUAL ARTS  Key")));
                Assert.True(track.IsOnDevice);
                Assert.False(track.IsSelected);
                Assert.True(album.IsOnDevice);
                Assert.False(album.IsSelected);

                // FileSystem ルート直下へ切り替え
                sut.CurrentDevicePath = fsDeviceRoot;
                await sut.CheckDeviceAlbumsAsync();
                Assert.False(track.IsOnDevice);

                album.IsSelected = true;
                await sut.TransferSelectedAsync();
                await sut.CheckDeviceAlbumsAsync();

                Assert.True(File.Exists(Path.Combine(fsDeviceRoot, expectedArtistFolder, expectedAlbumFolder, "01.flac")));
                Assert.True(track.IsOnDevice);
                Assert.True(album.IsOnDevice);
                Assert.Equal(rawArtist, album.Artist);
                Assert.Equal(rawAlbum, album.Title);
            }

            // Act & Assert 2: MTP サブフォルダおよびルート直下
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "SanitizeMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };
                sut.CurrentDevicePath = @"\Music";
                await sut.CheckDeviceAlbumsAsync();

                album.IsSelected = true;
                await sut.TransferSelectedAsync();

                Assert.True(fakeMtp.FileExists($@"\Music\{expectedArtistFolder}\{expectedAlbumFolder}\01.flac"));
                Assert.True(track.IsOnDevice);
                Assert.False(track.IsSelected);
                Assert.True(album.IsOnDevice);

                // MTP ルート直下へ切り替え
                sut.CurrentDevicePath = @"\";
                await sut.CheckDeviceAlbumsAsync();
                Assert.False(track.IsOnDevice);

                album.IsSelected = true;
                await sut.TransferSelectedAsync();
                await sut.CheckDeviceAlbumsAsync();

                Assert.True(fakeMtp.FileExists($@"\{expectedArtistFolder}\{expectedAlbumFolder}\01.flac"));
                Assert.True(track.IsOnDevice);
                Assert.True(album.IsOnDevice);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 機器上にアンダースコア置換済みフォルダが既に存在する場合（他ソフトで転送済みを含む）、FileSystem・MTP およびサブフォルダ・ルート直下のいずれでも再走査で非活性となり二重転送されないことを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_アンダースコア置換済みフォルダが既存の場合_再走査で非活性となり二重転送されないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_SanitizeExistingUnderscore_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string fsDeviceRoot = Path.Combine(tempRoot, "FsDevice");
        string fsMusicDir = Path.Combine(fsDeviceRoot, "Music");
        const string artistFolder = "VISUAL ARTS _ Key";
        const string albumFolder = "My Soul, Your Beats!_Brave Song";
        string fsSubExistingDir = Path.Combine(fsMusicDir, artistFolder, albumFolder);
        string fsRootExistingDir = Path.Combine(fsDeviceRoot, artistFolder, albumFolder);
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(fsSubExistingDir);
        Directory.CreateDirectory(fsRootExistingDir);

        string sourceFlacPath = Path.Combine(sourceDir, "01_Track.flac");
        File.WriteAllText(sourceFlacPath, "new-flac-bytes");
        File.WriteAllText(Path.Combine(fsSubExistingDir, "01_Track.mp3"), "existing-sub-mp3");
        File.WriteAllText(Path.Combine(fsRootExistingDir, "01_Track.mp3"), "existing-root-mp3");

        var track = new Track
        {
            Title = "My Soul, Your Beats!",
            Artist = "VISUAL ARTS / Key",
            AlbumArtist = "VISUAL ARTS / Key",
            Album = "My Soul, Your Beats!/Brave Song",
            FilePath = sourceFlacPath
        };
        var album = new Album("My Soul, Your Beats!/Brave Song", "VISUAL ARTS / Key", 2010, new[] { track });
        var albums = new ObservableCollection<Album> { album };

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddFile($@"\Music\{artistFolder}\{albumFolder}\01_Track.mp3");
        fakeMtp.AddFile($@"\{artistFolder}\{albumFolder}\01_Track.mp3");

        var fsTranscoder = new FakeAudioTranscodeService();
        var mtpTranscoder = new FakeAudioTranscodeService();

        try
        {
            // Act & Assert 1: FileSystem サブフォルダおよびルート直下
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: fsTranscoder,
                resolveMtpClient: null,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "ExistingFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = fsDeviceRoot
                };

                // サブフォルダ確認
                sut.CurrentDevicePath = fsMusicDir;
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track.IsOnDevice);
                Assert.True(album.IsOnDevice);
                Assert.Empty(DeviceBrowserViewModel.CollectTransferTargetTracks(albums));

                await sut.TransferTracksToFileSystemAsync(new[] { track }, fsMusicDir, new AudioTranscodeOptions { ConvertFlacToMp3 = true });
                Assert.Equal(0, fsTranscoder.TranscodeCallCount);
                Assert.Equal("existing-sub-mp3", File.ReadAllText(Path.Combine(fsSubExistingDir, "01_Track.mp3")));
                Assert.False(File.Exists(Path.Combine(fsSubExistingDir, "01_Track.flac")));

                // ルート直下確認
                sut.CurrentDevicePath = fsDeviceRoot;
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track.IsOnDevice);
                Assert.True(album.IsOnDevice);

                await sut.TransferTracksToFileSystemAsync(new[] { track }, fsDeviceRoot, new AudioTranscodeOptions { ConvertFlacToMp3 = false });
                Assert.Equal("existing-root-mp3", File.ReadAllText(Path.Combine(fsRootExistingDir, "01_Track.mp3")));
                Assert.False(File.Exists(Path.Combine(fsRootExistingDir, "01_Track.flac")));
            }

            // Act & Assert 2: MTP サブフォルダおよびルート直下
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: mtpTranscoder,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "ExistingMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };

                sut.CurrentDevicePath = @"\Music";
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track.IsOnDevice);
                Assert.True(album.IsOnDevice);

                await sut.TransferTracksToMtpAsync(fakeMtp, new[] { track }, @"\Music", new AudioTranscodeOptions { ConvertFlacToMp3 = true });
                Assert.Equal(0, mtpTranscoder.TranscodeCallCount);
                Assert.False(fakeMtp.FileExists($@"\Music\{artistFolder}\{albumFolder}\01_Track.flac"));

                sut.CurrentDevicePath = @"\";
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track.IsOnDevice);
                Assert.True(album.IsOnDevice);

                await sut.TransferTracksToMtpAsync(fakeMtp, new[] { track }, @"\", new AudioTranscodeOptions { ConvertFlacToMp3 = false });
                Assert.False(fakeMtp.FileExists($@"\{artistFolder}\{albumFolder}\01_Track.flac"));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 旧版 Audio Effector の禁止文字除去フォルダ（代表アーティストおよび旧曲別アーティスト配置）が機器上に存在する場合、後方互換により転送済み認識され、未転送曲のみアンダースコア置換フォルダへ追加転送され二重転送されないことを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferSelectedAsync_旧版の禁止文字除去フォルダが既存の場合_後方互換により転送済み認識され二重転送されないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_SanitizeLegacyRemoved_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string fsDeviceRoot = Path.Combine(tempRoot, "FsDevice");
        string fsMusicDir = Path.Combine(fsDeviceRoot, "Music");

        const string legacyRepArtistFolder = "VISUAL ARTS  Key";
        const string legacyGuestArtistFolder = "Guest  Singer";
        const string legacyAlbumFolder = "My Soul, Your Beats!Brave Song";
        const string newArtistFolder = "VISUAL ARTS _ Key";
        const string newAlbumFolder = "My Soul, Your Beats!_Brave Song";

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(fsMusicDir, legacyRepArtistFolder, legacyAlbumFolder));
        Directory.CreateDirectory(Path.Combine(fsMusicDir, legacyGuestArtistFolder, legacyAlbumFolder));
        Directory.CreateDirectory(Path.Combine(fsDeviceRoot, legacyRepArtistFolder, legacyAlbumFolder));
        Directory.CreateDirectory(Path.Combine(fsDeviceRoot, legacyGuestArtistFolder, legacyAlbumFolder));

        string track1Path = Path.Combine(sourceDir, "01.flac");
        string track2Path = Path.Combine(sourceDir, "02.flac");
        string track3Path = Path.Combine(sourceDir, "03.flac");
        File.WriteAllText(track1Path, "track-1");
        File.WriteAllText(track2Path, "track-2");
        File.WriteAllText(track3Path, "track-3");

        // サブフォルダ・ルート直下の双方に旧禁止文字除去フォルダ（代表・旧曲別）で 01 / 02 が既存
        File.WriteAllText(Path.Combine(fsMusicDir, legacyRepArtistFolder, legacyAlbumFolder, "01.mp3"), "legacy-sub-01");
        File.WriteAllText(Path.Combine(fsMusicDir, legacyGuestArtistFolder, legacyAlbumFolder, "02.mp3"), "legacy-sub-02");
        File.WriteAllText(Path.Combine(fsDeviceRoot, legacyRepArtistFolder, legacyAlbumFolder, "01.mp3"), "legacy-root-01");
        File.WriteAllText(Path.Combine(fsDeviceRoot, legacyGuestArtistFolder, legacyAlbumFolder, "02.mp3"), "legacy-root-02");

        var track1 = new Track { Title = "Track 1", Artist = "VISUAL ARTS / Key", AlbumArtist = "VISUAL ARTS / Key", Album = "My Soul, Your Beats!/Brave Song", FilePath = track1Path };
        var track2 = new Track { Title = "Track 2", Artist = "Guest / Singer", AlbumArtist = "VISUAL ARTS / Key", Album = "My Soul, Your Beats!/Brave Song", FilePath = track2Path };
        var track3 = new Track { Title = "Track 3", Artist = "VISUAL ARTS / Key", AlbumArtist = "VISUAL ARTS / Key", Album = "My Soul, Your Beats!/Brave Song", FilePath = track3Path };
        var album = new Album("My Soul, Your Beats!/Brave Song", "VISUAL ARTS / Key", 2010, new[] { track1, track2, track3 });
        var albums = new ObservableCollection<Album> { album };

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddFile($@"\Music\{legacyRepArtistFolder}\{legacyAlbumFolder}\01.mp3");
        fakeMtp.AddFile($@"\Music\{legacyGuestArtistFolder}\{legacyAlbumFolder}\02.mp3");
        fakeMtp.AddFile($@"\{legacyRepArtistFolder}\{legacyAlbumFolder}\01.mp3");
        fakeMtp.AddFile($@"\{legacyGuestArtistFolder}\{legacyAlbumFolder}\02.mp3");

        var fsTranscoder = new FakeAudioTranscodeService();
        var mtpTranscoder = new FakeAudioTranscodeService();

        try
        {
            // Act & Assert 1: FileSystem サブフォルダおよびルート直下
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: fsTranscoder,
                resolveMtpClient: null,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "LegacyRemovedFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = fsDeviceRoot
                };

                // サブフォルダで再走査 → 1・2 曲目は既転送、3 曲目のみ未転送
                sut.CurrentDevicePath = fsMusicDir;
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.False(track3.IsOnDevice);
                Assert.False(album.IsOnDevice);

                // アルバム選択＆転送 → 未転送の 3 曲目だけ新 _ 置換フォルダへ保存
                album.IsSelected = true;
                await sut.TransferSelectedAsync();
                Assert.True(File.Exists(Path.Combine(fsMusicDir, newArtistFolder, newAlbumFolder, "03.flac")));
                Assert.False(File.Exists(Path.Combine(fsMusicDir, newArtistFolder, newAlbumFolder, "01.flac")));
                Assert.False(File.Exists(Path.Combine(fsMusicDir, newArtistFolder, newAlbumFolder, "02.flac")));
                Assert.True(album.IsOnDevice);

                // 直接転送呼び出しでも旧禁止文字除去配置の 1・2 曲目は変換 ON/OFF 双方でスキップされること
                await sut.TransferTracksToFileSystemAsync(new[] { track1, track2 }, fsMusicDir, new AudioTranscodeOptions { ConvertFlacToMp3 = true });
                await sut.TransferTracksToFileSystemAsync(new[] { track1, track2 }, fsMusicDir, new AudioTranscodeOptions { ConvertFlacToMp3 = false });
                Assert.Equal(0, fsTranscoder.TranscodeCallCount);
                Assert.False(File.Exists(Path.Combine(fsMusicDir, newArtistFolder, newAlbumFolder, "01.mp3")));
                Assert.False(File.Exists(Path.Combine(fsMusicDir, newArtistFolder, newAlbumFolder, "02.mp3")));

                // ルート直下でも同様に旧禁止文字除去フォルダを認識し、3 曲目のみ追加転送されること
                sut.CurrentDevicePath = fsDeviceRoot;
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.False(track3.IsOnDevice);

                album.IsSelected = true;
                await sut.TransferSelectedAsync();
                Assert.True(File.Exists(Path.Combine(fsDeviceRoot, newArtistFolder, newAlbumFolder, "03.flac")));
                Assert.False(File.Exists(Path.Combine(fsDeviceRoot, newArtistFolder, newAlbumFolder, "01.flac")));
                Assert.False(File.Exists(Path.Combine(fsDeviceRoot, newArtistFolder, newAlbumFolder, "02.flac")));
                Assert.True(album.IsOnDevice);
            }

            // Act & Assert 2: MTP サブフォルダおよびルート直下
            using (var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: mtpTranscoder,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { }))
            {
                sut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "LegacyRemovedMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };

                sut.CurrentDevicePath = @"\Music";
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.False(track3.IsOnDevice);

                album.IsSelected = true;
                await sut.TransferSelectedAsync();
                Assert.True(fakeMtp.FileExists($@"\Music\{newArtistFolder}\{newAlbumFolder}\03.flac"));
                Assert.False(fakeMtp.FileExists($@"\Music\{newArtistFolder}\{newAlbumFolder}\01.flac"));
                Assert.False(fakeMtp.FileExists($@"\Music\{newArtistFolder}\{newAlbumFolder}\02.flac"));
                Assert.True(album.IsOnDevice);

                await sut.TransferTracksToMtpAsync(fakeMtp, new[] { track1, track2 }, @"\Music", new AudioTranscodeOptions { ConvertFlacToMp3 = true });
                await sut.TransferTracksToMtpAsync(fakeMtp, new[] { track1, track2 }, @"\Music", new AudioTranscodeOptions { ConvertFlacToMp3 = false });
                Assert.Equal(0, mtpTranscoder.TranscodeCallCount);
                Assert.False(fakeMtp.FileExists($@"\Music\{newArtistFolder}\{newAlbumFolder}\01.mp3"));
                Assert.False(fakeMtp.FileExists($@"\Music\{newArtistFolder}\{newAlbumFolder}\02.mp3"));

                sut.CurrentDevicePath = @"\";
                await sut.CheckDeviceAlbumsAsync();
                Assert.True(track1.IsOnDevice);
                Assert.True(track2.IsOnDevice);
                Assert.False(track3.IsOnDevice);

                album.IsSelected = true;
                await sut.TransferSelectedAsync();
                Assert.True(fakeMtp.FileExists($@"\{newArtistFolder}\{newAlbumFolder}\03.flac"));
                Assert.False(fakeMtp.FileExists($@"\{newArtistFolder}\{newAlbumFolder}\01.flac"));
                Assert.False(fakeMtp.FileExists($@"\{newArtistFolder}\{newAlbumFolder}\02.flac"));
                Assert.True(album.IsOnDevice);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 禁止文字除去の互換候補フォルダが別アルバムの正規新配置フォルダと一致する場合、別アルバムの転送によって誤って転送済み判定されないことを検証します。
    /// </summary>
    [Fact]
    public void ApplyDeviceTrackIndex_禁止文字除去互換候補が別アルバムの正規配置と衝突する場合_誤って転送済み判定されないこと()
    {
        // Arrange
        var slashTrack = new Track { Title = "Track A", Artist = "A / B", AlbumArtist = "A / B", Album = "Title", FilePath = @"C:\Music\Slash\01.mp3" };
        var doubleSpaceTrack = new Track { Title = "Track B", Artist = "A  B", AlbumArtist = "A  B", Album = "Title", FilePath = @"C:\Music\Space\01.mp3" };
        var slashAlbum = new Album("Title", "A / B", 2024, new[] { slashTrack });
        var doubleSpaceAlbum = new Album("Title", "A  B", 2024, new[] { doubleSpaceTrack });
        var albums = new[] { slashAlbum, doubleSpaceAlbum };

        // 別アルバム（正規配置 "A  B\Title"）のみ機器上に存在する状態
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"A  B\Title\01"
        };

        // Act
        DeviceBrowserViewModel.ApplyDeviceTrackIndex(albums, index);

        // Assert
        Assert.True(doubleSpaceTrack.IsOnDevice);
        Assert.True(doubleSpaceAlbum.IsOnDevice);
        Assert.False(slashTrack.IsOnDevice);
        Assert.False(slashAlbum.IsOnDevice);
    }

    /// <summary>
    /// FileSystem・MTP 双方の機器転送において、3 種別（手動・外部 / テキスト自動生成 / 音声自動文字起こし）の個別 ON/OFF 設定に応じて
    /// 有効種別の .lrc のみが同梱転送され、形式変換（MP4→M4A / FLAC→MP3）併用時も変換後音声ファイルと同名ベースで保存されることを検証します。
    /// </summary>
    /// <param name="enableManual">手動・外部 LRC 同梱設定</param>
    /// <param name="enableTextAligned">テキスト自動生成 LRC 同梱設定</param>
    /// <param name="enableTranscribed">音声自動文字起こし LRC 同梱設定</param>
    /// <returns>非同期タスク</returns>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task TransferTracks_FileSystemおよびMTPでLrc種別設定と形式変換併用時_有効種別のみ変換後同名ベースで同梱転送されること(
        bool enableManual,
        bool enableTextAligned,
        bool enableTranscribed)
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_LrcTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string fsDeviceDir = Path.Combine(tempRoot, "FsDevice");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(fsDeviceDir);

        string manualPath = Path.Combine(sourceDir, "01_Manual.mp3");
        string alignedPath = Path.Combine(sourceDir, "02_Aligned.flac");
        string transcribedPath = Path.Combine(sourceDir, "03_Transcribed.mp4");
        string noLrcPath = Path.Combine(sourceDir, "04_NoLrc.mp3");

        File.WriteAllText(manualPath, "mp3-1");
        File.WriteAllText(Path.ChangeExtension(manualPath, ".lrc"), "[00:01.00]手動歌詞");
        File.WriteAllText(alignedPath, "flac-2");
        File.WriteAllText(Path.ChangeExtension(alignedPath, ".lrc"), "[ti:Aligned]\n[re:Audio Effector (TextAlignment)]\n[00:02.00]テキスト生成歌詞");
        File.WriteAllText(transcribedPath, "mp4-3");
        File.WriteAllText(Path.ChangeExtension(transcribedPath, ".lrc"), "[re:Audio Effector (AudioTranscription)]\n[00:03.00]自動文字起こし歌詞");
        File.WriteAllText(noLrcPath, "mp3-4");

        var t1 = new Track { Title = "Manual", Artist = "ArtistA", Album = "AlbumA", FilePath = manualPath };
        var t2 = new Track { Title = "Aligned", Artist = "ArtistA", Album = "AlbumA", FilePath = alignedPath };
        var t3 = new Track { Title = "Transcribed", Artist = "ArtistA", Album = "AlbumA", FilePath = transcribedPath };
        var t4 = new Track { Title = "NoLrc", Artist = "ArtistA", Album = "AlbumA", FilePath = noLrcPath };
        var album = new Album("AlbumA", "ArtistA", 2026, new[] { t1, t2, t3, t4 });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s =>
        {
            s.ConvertMp4ToM4a = true;
            s.ConvertFlacToMp3 = true;
            s.TransferManualLrcFiles = enableManual;
            s.TransferTextAlignedLrcFiles = enableTextAligned;
            s.TransferTranscribedLrcFiles = enableTranscribed;
        });

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddDirectory(@"\Music");

        try
        {
            var lrcParser = new AudioEffector.Infrastructure.Lyrics.LrcParser();

            // Act & Assert: FileSystem 転送
            using (var fsSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: new FakeAudioTranscodeService(),
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                lrcParser: lrcParser,
                confirmTransfer: _ => true))
            {
                fsSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "LrcFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = fsDeviceDir
                };
                await fsSut.CheckDeviceAlbumsAsync();

                album.IsSelected = true;
                await fsSut.TransferSelectedAsync();

                string fsAlbumDir = Path.Combine(fsDeviceDir, "ArtistA", "AlbumA");
                Assert.True(File.Exists(Path.Combine(fsAlbumDir, "01_Manual.mp3")));
                Assert.True(File.Exists(Path.Combine(fsAlbumDir, "02_Aligned.mp3")));
                Assert.True(File.Exists(Path.Combine(fsAlbumDir, "03_Transcribed.m4a")));
                Assert.True(File.Exists(Path.Combine(fsAlbumDir, "04_NoLrc.mp3")));

                Assert.Equal(enableManual, File.Exists(Path.Combine(fsAlbumDir, "01_Manual.lrc")));
                Assert.Equal(enableTextAligned, File.Exists(Path.Combine(fsAlbumDir, "02_Aligned.lrc")));
                Assert.Equal(enableTranscribed, File.Exists(Path.Combine(fsAlbumDir, "03_Transcribed.lrc")));
                Assert.False(File.Exists(Path.Combine(fsAlbumDir, "04_NoLrc.lrc")));
            }

            // Act & Assert: MTP 転送
            using (var mtpSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: new FakeAudioTranscodeService(),
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { },
                lrcParser: lrcParser,
                confirmTransfer: _ => true))
            {
                mtpSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "LrcMTP",
                    Type = MainViewModel.DeviceType.MTP,
                    RootPath = @"\"
                };
                mtpSut.CurrentDevicePath = @"\Music";
                await mtpSut.CheckDeviceAlbumsAsync();

                album.IsSelected = true;
                await mtpSut.TransferSelectedAsync();

                Assert.True(fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\01_Manual.mp3"));
                Assert.True(fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\02_Aligned.mp3"));
                Assert.True(fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\03_Transcribed.m4a"));
                Assert.True(fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\04_NoLrc.mp3"));

                Assert.Equal(enableManual, fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\01_Manual.lrc"));
                Assert.Equal(enableTextAligned, fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\02_Aligned.lrc"));
                Assert.Equal(enableTranscribed, fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\03_Transcribed.lrc"));
                Assert.False(fakeMtp.FileExists(@"\Music\ArtistA\AlbumA\04_NoLrc.lrc"));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 同梱 .lrc ファイルの転送中に例外が発生しても、音声ファイル本体および後続トラックの転送が中断されず完了すること、
    /// および音声ファイル既存スキップ時には .lrc が転送されず .lrc のみ存在する機器上で曲が誤って転送済み判定されないことを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracks_Lrc転送失敗時継続および音声既存スキップ時非同梱とLrc単独非重複判定が正しく動作すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_LrcEdge_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string fsDeviceDir = Path.Combine(tempRoot, "FsDevice");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(fsDeviceDir);

        string track1Path = Path.Combine(sourceDir, "01.mp3");
        string track2Path = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(track1Path, "audio-1");
        File.WriteAllText(Path.ChangeExtension(track1Path, ".lrc"), "[00:01.00]lyric-1");
        File.WriteAllText(track2Path, "audio-2");
        File.WriteAllText(Path.ChangeExtension(track2Path, ".lrc"), "[00:01.00]lyric-2");

        var t1 = new Track { Title = "Track 1", Artist = "ArtistB", Album = "AlbumB", FilePath = track1Path };
        var t2 = new Track { Title = "Track 2", Artist = "ArtistB", Album = "AlbumB", FilePath = track2Path };
        var album = new Album("AlbumB", "ArtistB", 2026, new[] { t1, t2 });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.TransferManualLrcFiles = true);

        // 1. MTP 経路で 1 曲目の .lrc アップロードのみ例外を投げる
        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.AddDirectory(@"\Music");
        fakeMtp.OnUploadFile = (_, dest) =>
        {
            if (dest.EndsWith("01.lrc", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("MTP LRC upload failed");
            }
        };

        // 2. FileSystem 側に 01.lrc のみ配置（音声 01.mp3 なし）＋ 02.mp3 のみ既配置（音声既存スキップ確認）
        string fsAlbumDir = Path.Combine(fsDeviceDir, "ArtistB", "AlbumB");
        Directory.CreateDirectory(fsAlbumDir);
        File.WriteAllText(Path.Combine(fsAlbumDir, "01.lrc"), "orphan-lrc");
        File.WriteAllText(Path.Combine(fsAlbumDir, "02.mp3"), "existing-audio-2");

        try
        {
            var lrcParser = new AudioEffector.Infrastructure.Lyrics.LrcParser();

            // Act & Assert 1: MTP で 01.lrc 転送失敗しても 01.mp3・02.mp3・02.lrc がすべて正常転送されること
            using (var mtpSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: (_, _) => { },
                lrcParser: lrcParser))
            {
                await mtpSut.TransferTracksToMtpAsync(
                    fakeMtp,
                    new[] { t1, t2 },
                    @"\Music",
                    new AudioTranscodeOptions());

                Assert.True(fakeMtp.FileExists(@"\Music\ArtistB\AlbumB\01.mp3"));
                Assert.False(fakeMtp.FileExists(@"\Music\ArtistB\AlbumB\01.lrc"));
                Assert.True(fakeMtp.FileExists(@"\Music\ArtistB\AlbumB\02.mp3"));
                Assert.True(fakeMtp.FileExists(@"\Music\ArtistB\AlbumB\02.lrc"));
            }

            // Act & Assert 2: FileSystem で 01.lrc 単独配置は重複判定に影響せず（t1 未転送）、02.mp3 既存スキップ時は 02.lrc を転送しないこと
            using (var fsSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                lrcParser: lrcParser))
            {
                fsSut.SelectedDevice = new MainViewModel.DeviceViewModel
                {
                    Name = "EdgeFS",
                    Type = MainViewModel.DeviceType.FileSystem,
                    RootPath = fsDeviceDir
                };
                await fsSut.CheckDeviceAlbumsAsync();

                Assert.False(t1.IsOnDevice);
                Assert.True(t2.IsOnDevice);

                await fsSut.TransferTracksToFileSystemAsync(
                    new[] { t2 },
                    fsDeviceDir,
                    new AudioTranscodeOptions());

                Assert.False(File.Exists(Path.Combine(fsAlbumDir, "02.lrc")));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// MTP で孤立 .lrc 残存時に既存削除のうえ最新 .lrc で上書き更新されること、音声既存スキップ時は .lrc が非同梱・非削除であること、
    /// FileSystem 側で .lrc コピーが失敗しても音声本体・後続トラック転送が継続すること、および設定読み込み例外時に既定値へフォールバックすることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracks_MTP孤立Lrc上書きと既存スキップ非同梱およびFileSystemコピー失敗時継続と設定例外フォールバックが動作すること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_LrcRobust_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string fsDeviceDir = Path.Combine(tempRoot, "FsDevice");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(fsDeviceDir);

        string track1Path = Path.Combine(sourceDir, "01.mp3");
        string track2Path = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(track1Path, "audio-1");
        File.WriteAllText(Path.ChangeExtension(track1Path, ".lrc"), "[00:01.00]updated-lyric-1");
        File.WriteAllText(track2Path, "audio-2");
        File.WriteAllText(Path.ChangeExtension(track2Path, ".lrc"), "[00:01.00]lyric-2");

        var t1 = new Track { Title = "Track 1", Artist = "ArtistC", Album = "AlbumC", FilePath = track1Path };
        var t2 = new Track { Title = "Track 2", Artist = "ArtistC", Album = "AlbumC", FilePath = track2Path };
        var album = new Album("AlbumC", "ArtistC", 2026, new[] { t1, t2 });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.TransferManualLrcFiles = true);
        var lrcParser = new AudioEffector.Infrastructure.Lyrics.LrcParser();

        // MTP 実機同様に既存ファイルへの直接 UploadFile で例外を送出する設定で検証
        var strictMtp = new FakeMtpDeviceClient { ThrowIfDestinationExists = true };
        strictMtp.AddFile(@"\Music\ArtistC\AlbumC\01.lrc"); // 孤立した旧 .lrc のみ残存（音声 01.mp3 なし）
        strictMtp.AddFile(@"\Music\ArtistC\AlbumC\02.mp3"); // 既存音声あり（02.lrc なし → スキップ時は非同梱）

        // FileSystem 側では 01.lrc と同名のディレクトリを配置して File.Copy に失敗させる
        string fsAlbumDir = Path.Combine(fsDeviceDir, "ArtistC", "AlbumC");
        Directory.CreateDirectory(Path.Combine(fsAlbumDir, "01.lrc"));

        var throwingSettingsMock = new Moq.Mock<ISettingsService>();
        throwingSettingsMock.Setup(s => s.LoadSettings()).Throws(new IOException("settings read error"));

        try
        {
            // Act 1: MTP 孤立 .lrc 上書きおよび既存音声スキップ非同梱
            using (var mtpSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: null,
                resolveMtpClient: _ => strictMtp,
                showMessage: (_, _) => { },
                lrcParser: lrcParser))
            {
                await mtpSut.TransferTracksToMtpAsync(strictMtp, new[] { t1, t2 }, @"\Music", new AudioTranscodeOptions());
            }

            // Assert 1
            Assert.True(strictMtp.FileExists(@"\Music\ArtistC\AlbumC\01.mp3"));
            Assert.True(strictMtp.FileExists(@"\Music\ArtistC\AlbumC\01.lrc"));
            Assert.Contains(@"\Music\ArtistC\AlbumC\01.lrc", strictMtp.DeletedFiles, StringComparer.OrdinalIgnoreCase);
            Assert.False(strictMtp.FileExists(@"\Music\ArtistC\AlbumC\02.lrc"));

            // Act 2: FileSystem で 01.lrc コピーが失敗しても 01.mp3・02.mp3・02.lrc の転送が継続すること
            using (var fsSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: settingsService,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                lrcParser: lrcParser))
            {
                await fsSut.TransferTracksToFileSystemAsync(new[] { t1, t2 }, fsDeviceDir, new AudioTranscodeOptions());
            }

            // Assert 2
            Assert.True(File.Exists(Path.Combine(fsAlbumDir, "01.mp3")));
            Assert.True(File.Exists(Path.Combine(fsAlbumDir, "02.mp3")));
            Assert.True(File.Exists(Path.Combine(fsAlbumDir, "02.lrc")));

            // Act 3: 設定読み込み例外時は既定値（全 false）へフォールバックすること
            using var fallbackSut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                settingsService: throwingSettingsMock.Object,
                lrcParser: lrcParser);
            var fallbackOptions = fallbackSut.GetCurrentLrcTransferOptions();

            // Assert 3
            Assert.False(fallbackOptions.IsAnyEnabled);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// SanitizeFileNameCore の旧禁止文字除去モード（非 LINQ 実装）および新置換モードにおいて、
    /// 空文字・空白・禁止文字のみ・制御文字・末尾ピリオド・長文字列など全境界ケースで期待通りの出力を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null, false, true, "Unknown")]
    [InlineData("", false, true, "Unknown")]
    [InlineData("   \t  ", false, true, "Unknown")]
    [InlineData(":*?<>|", false, true, "Unknown")]
    [InlineData(":*?<>|", true, true, "______")]
    [InlineData(" : . . : ", false, true, "Unknown")]
    [InlineData("...", false, true, "Unknown")]
    [InlineData("...", false, false, "Unknown")]
    [InlineData("  Artist:Name...  ", false, true, "ArtistName")]
    [InlineData("  Artist:Name...  ", false, false, "ArtistName...")]
    [InlineData("  Artist:Name...  ", true, true, "Artist_Name")]
    [InlineData("  Artist:Name...  ", true, false, "Artist_Name...")]
    [InlineData("A\0B\u0001C:D?", false, true, "ABCD")]
    [InlineData("A\0B\u0001C:D?", true, true, "A_B_C_D_")]
    [InlineData("  AC/DC  ", false, true, "ACDC")]
    [InlineData("  AC/DC  ", true, true, "AC_DC")]
    [InlineData("Title . . ", false, false, "Title . .")]
    [InlineData("Title . . ", false, true, "Title")]
    public void SanitizeFileNameCore_旧除去モードと新置換モードの境界ケース_期待される正規化結果を返すこと(
        string? input,
        bool replaceInvalidWithUnderscore,
        bool trimTrailingDots,
        string expected)
    {
        // Arrange & Act
        string actual = DeviceBrowserViewModel.SanitizeFileNameCore(
            input,
            replaceInvalidWithUnderscore,
            trimTrailingDots);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// SanitizeFileNameCore がスタック確保閾値（256文字）を超える長い文字列に対しても
    /// 旧除去モード・新置換モードの双方で正確にサニタイズできることを検証します。
    /// </summary>
    [Fact]
    public void SanitizeFileNameCore_256文字超の長文字列_旧除去および新置換モードとも正確に正規化されること()
    {
        // Arrange
        string prefix = new string('A', 200);
        string suffix = new string('B', 100);
        string input = $"  {prefix}:*?{suffix}...  ";

        // Act
        string legacyTrimmed = DeviceBrowserViewModel.SanitizeFileNameCore(input, replaceInvalidWithUnderscore: false, trimTrailingDots: true);
        string legacyKeepDots = DeviceBrowserViewModel.SanitizeFileNameCore(input, replaceInvalidWithUnderscore: false, trimTrailingDots: false);
        string canonicalTrimmed = DeviceBrowserViewModel.SanitizeFileNameCore(input, replaceInvalidWithUnderscore: true, trimTrailingDots: true);

        // Assert
        Assert.Equal(prefix + suffix, legacyTrimmed);
        Assert.Equal(prefix + suffix + "...", legacyKeepDots);
        Assert.Equal(prefix + "___" + suffix, canonicalTrimmed);
    }

    /// <summary>
    /// BuildPrimaryDirectoryOwnerMap のアルバム単位ショートカット適用時に、
    /// Artist/Title 確定済みの複数トラックアルバム・空白メタデータでフォールバック解決を要するアルバム・空アルバムが混在しても
    /// 別アルバム衝突防止が従来と完全に等価に動作することを検証します。
    /// </summary>
    [Fact]
    public void ApplyDeviceTrackIndex_確定済みアルバムと空白フォールバックアルバム混在時_所有者マップによる別アルバム衝突防止が等価に動作すること()
    {
        // Arrange
        // 1. Artist/Title が確定済みの複数トラックアルバム（前後空白付きプロパティでショートカット側の Trim() を直接検証）
        var confirmedTrack1 = new Track { Title = "Track 1", Artist = "Performer X", AlbumArtist = "A_B", Album = "Album_1", FilePath = @"C:\Music\confirmed\01.mp3" };
        var confirmedTrack2 = new Track { Title = "Track 2", Artist = "Performer Y", AlbumArtist = "A_B", Album = "Album_1", FilePath = @"C:\Music\confirmed\02.mp3" };
        var confirmedAlbum = new Album
        {
            Title = "  Album_1  ",
            Artist = "  A_B  ",
            Year = 2026,
            Tracks = new List<Track> { confirmedTrack1, confirmedTrack2 }
        };

        // 2. 禁止文字を含む別アルバム（旧除去互換候補 "AB\Album1" が下の空白アルバム正規配置と衝突）
        var colonTrack = new Track { Title = "Colon Song", Artist = "A:B", AlbumArtist = "A:B", Album = "Album:1", FilePath = @"C:\Music\colon\01.mp3" };
        var colonAlbum = new Album("Album:1", "A:B", 2026, new[] { colonTrack });

        // 3. Album.Artist / Album.Title が空白で fallbackIdentities / Track フォールバック経路を通るアルバム
        var fallbackTrack1 = new Track { Title = "Fallback 1", Artist = "AB", AlbumArtist = "AB", Album = "Album1", FilePath = @"C:\Music\fallback\01.mp3" };
        var fallbackTrack2 = new Track { Title = "Fallback 2", Artist = "AB", AlbumArtist = "AB", Album = "Album1", FilePath = @"C:\Music\fallback\02.mp3" };
        var fallbackAlbum = new Album
        {
            Title = "   ",
            Artist = "",
            Year = 2026,
            Tracks = new List<Track> { fallbackTrack1, fallbackTrack2 }
        };

        // 4. 旧曲別アーティスト配置が confirmedAlbum の正規配置 "A_B\Album_1" と衝突する別アルバム
        var compilationTrack = new Track { Title = "Comp Song", Artist = "A_B", AlbumArtist = "Various Artists", Album = "Album_1", FilePath = @"C:\Music\comp\02.mp3" };
        var compilationAlbum = new Album("Album_1", "Various Artists", 2026, new[] { compilationTrack });

        // 5. 空トラックアルバム（Tracks.Count == 0）は所有権を登録せず他アルバムをブロックしない
        var emptyAlbum = new Album("Legacy", "LegacyArtist", 2026, Array.Empty<Track>());
        var legacyMatchTrack = new Track { Title = "Legacy Match", Artist = "Legacy:Artist", AlbumArtist = "Legacy:Artist", Album = "Legacy", FilePath = @"C:\Music\legacy\03.mp3" };
        var legacyMatchAlbum = new Album("Legacy", "Legacy:Artist", 2026, new[] { legacyMatchTrack });

        var albums = new[] { confirmedAlbum, colonAlbum, fallbackAlbum, compilationAlbum, emptyAlbum, legacyMatchAlbum };

        var deviceFileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"A_B\Album_1\02",        // confirmedAlbum の正規配置（compilationTrack の旧曲別配置と衝突）
            @"AB\Album1\01",          // fallbackAlbum の正規配置（colonTrack の旧除去配置と衝突）
            @"LegacyArtist\Legacy\03" // legacyMatchTrack の旧除去配置（emptyAlbum は空なのでブロックされない）
        };

        // Act
        DeviceBrowserViewModel.ApplyDeviceTrackIndex(albums, deviceFileKeys);

        // Assert
        Assert.False(confirmedTrack1.IsOnDevice);
        Assert.True(confirmedTrack2.IsOnDevice);
        Assert.True(fallbackTrack1.IsOnDevice);
        Assert.False(fallbackTrack2.IsOnDevice);
        Assert.False(colonTrack.IsOnDevice);
        Assert.False(compilationTrack.IsOnDevice);
        Assert.True(legacyMatchTrack.IsOnDevice);
    }

    /// <summary>
    /// 選択楽曲の合計サイズが接続機器の空き容量を超える場合、転送前に容量不足警告ダイアログが呼ばれ、
    /// キャンセル選択時はファイルが転送されないことを検証します。
    /// </summary>
    [Fact]
    public async Task TransferSelectedAsync_空き容量不足でキャンセル選択時_警告確認が呼ばれ転送が開始されないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ConfirmSpaceCancel_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string trackPath = Path.Combine(sourceDir, "01.mp3");
        File.WriteAllText(trackPath, "audio-content");

        var track = new Track { Title = "Song 1", Artist = "ArtistA", Album = "AlbumA", FilePath = trackPath, IsSelected = true };
        var album = new Album("AlbumA", "ArtistA", 2026, new[] { track });
        var albums = new ObservableCollection<Album> { album };

        var device = new MainViewModel.DeviceViewModel
        {
            Name = "USB (E:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = deviceDir
        };
        device.UpdateStorageSpace(totalBytes: 1000L * 1024 * 1024, freeBytes: 50L * 1024 * 1024);

        TransferConfirmationRequest? capturedRequest = null;
        int confirmCallCount = 0;

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: _ => 80L * 1024 * 1024,
                readDeviceStorageSpace: (_, _) => (true, 1000L * 1024 * 1024, 50L * 1024 * 1024),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: req =>
                {
                    confirmCallCount++;
                    capturedRequest = req;
                    return false;
                });

            sut.SelectedDevice = device;
            await sut.CheckDeviceAlbumsAsync();
            await sut.RefreshSelectedDeviceStorageAsync();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Equal(1, confirmCallCount);
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest!.IsInsufficientSpace);
            Assert.False(capturedRequest.HasTranscodeTargets);
            Assert.Equal(80L * 1024 * 1024, capturedRequest.RequiredBytes);
            Assert.Equal(50L * 1024 * 1024, capturedRequest.FreeSpaceBytes);
            Assert.Equal(30L * 1024 * 1024, capturedRequest.ShortageBytes);
            Assert.False(File.Exists(Path.Combine(deviceDir, "ArtistA", "AlbumA", "01.mp3")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 形式変換（MP4→M4A および FLAC→MP3）が ON で変換対象曲が含まれ、かつ空き容量も不足している場合、
    /// 二重表示にならず 1 回の確認コールバックに両方の情報と変換内訳が集約され、続行選択で転送されることを検証します。
    /// </summary>
    [Fact]
    public async Task TransferSelectedAsync_容量不足と形式変換が同時該当し続行選択時_1回のみ統合確認され転送されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ConfirmCombined_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string flacPath = Path.Combine(sourceDir, "01.flac");
        string mp4Path = Path.Combine(sourceDir, "02.mp4");
        string mp3Path = Path.Combine(sourceDir, "03.mp3");
        File.WriteAllText(flacPath, "flac-data");
        File.WriteAllText(mp4Path, "mp4-data");
        File.WriteAllText(mp3Path, "mp3-data");

        var t1 = new Track { Title = "Track 1", Artist = "ArtistB", Album = "AlbumB", FilePath = flacPath, IsSelected = true };
        var t2 = new Track { Title = "Track 2", Artist = "ArtistB", Album = "AlbumB", FilePath = mp4Path, IsSelected = true };
        var t3 = new Track { Title = "Track 3", Artist = "ArtistB", Album = "AlbumB", FilePath = mp3Path, IsSelected = true };
        var album = new Album("AlbumB", "ArtistB", 2026, new[] { t1, t2, t3 });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s =>
        {
            s.ConvertFlacToMp3 = true;
            s.ConvertMp4ToM4a = true;
        });

        var transcodeService = new FakeAudioTranscodeService();
        var device = new MainViewModel.DeviceViewModel
        {
            Name = "USB (F:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = deviceDir
        };

        TransferConfirmationRequest? capturedRequest = null;
        int confirmCallCount = 0;

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: _ => 40L * 1024 * 1024,
                readDeviceStorageSpace: (_, _) => (true, 500L * 1024 * 1024, 100L * 1024 * 1024),
                settingsService: settingsService,
                audioTranscodeService: transcodeService,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: req =>
                {
                    confirmCallCount++;
                    capturedRequest = req;
                    return true;
                });

            sut.SelectedDevice = device;
            await sut.CheckDeviceAlbumsAsync();
            await sut.RefreshSelectedDeviceStorageAsync();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Equal(1, confirmCallCount);
            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest!.IsInsufficientSpace);
            Assert.True(capturedRequest.HasTranscodeTargets);
            Assert.True(capturedRequest.ShowEstimatedSizeNote);
            Assert.Equal(3, capturedRequest.TotalTrackCount);
            Assert.Equal(1, capturedRequest.FlacToMp3Count);
            Assert.Equal(1, capturedRequest.Mp4ToM4aCount);
            Assert.Equal(1, capturedRequest.CopyOnlyCount);
            Assert.Equal(120L * 1024 * 1024, capturedRequest.RequiredBytes);
            Assert.Equal(20L * 1024 * 1024, capturedRequest.ShortageBytes);
            Assert.True(File.Exists(Path.Combine(deviceDir, "ArtistB", "AlbumB", "01.mp3")));
            Assert.True(File.Exists(Path.Combine(deviceDir, "ArtistB", "AlbumB", "02.m4a")));
            Assert.True(File.Exists(Path.Combine(deviceDir, "ArtistB", "AlbumB", "03.mp3")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 空き容量が十分かつ形式変換対象がない通常転送では、確認ダイアログが表示されず即座に転送が実行されることを検証します。
    /// </summary>
    [Fact]
    public async Task TransferSelectedAsync_空き容量十分かつ形式変換対象なしの場合_確認ダイアログを表示せず転送されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ConfirmSkip_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string mp3Path = Path.Combine(sourceDir, "01.mp3");
        File.WriteAllText(mp3Path, "mp3-data");

        var track = new Track { Title = "Track 1", Artist = "ArtistC", Album = "AlbumC", FilePath = mp3Path, IsSelected = true };
        var album = new Album("AlbumC", "ArtistC", 2026, new[] { track });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s =>
        {
            // 設定が ON でも対象拡張子（.flac / .mp4）が無ければ変換確認は不要
            s.ConvertFlacToMp3 = true;
            s.ConvertMp4ToM4a = true;
        });

        var device = new MainViewModel.DeviceViewModel
        {
            Name = "USB (G:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = deviceDir
        };

        int confirmCallCount = 0;

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: _ => 5L * 1024 * 1024,
                readDeviceStorageSpace: (_, _) => (true, 1000L * 1024 * 1024, 500L * 1024 * 1024),
                settingsService: settingsService,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: _ =>
                {
                    confirmCallCount++;
                    return false;
                });

            sut.SelectedDevice = device;
            await sut.CheckDeviceAlbumsAsync();
            await sut.RefreshSelectedDeviceStorageAsync();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Equal(0, confirmCallCount);
            Assert.True(File.Exists(Path.Combine(deviceDir, "ArtistC", "AlbumC", "01.mp3")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 空き容量は十分だが形式変換対象曲が含まれる場合、形式変換確認ダイアログが表示され、
    /// キャンセル選択時は転送されないことを検証します。
    /// </summary>
    [Fact]
    public async Task TransferSelectedAsync_空き容量十分で形式変換対象ありかつキャンセル選択時_変換確認が呼ばれ転送されないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ConfirmTranscodeOnly_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string flacPath = Path.Combine(sourceDir, "01.flac");
        File.WriteAllText(flacPath, "flac-data");

        var track = new Track { Title = "Track 1", Artist = "ArtistD", Album = "AlbumD", FilePath = flacPath, IsSelected = true };
        var album = new Album("AlbumD", "ArtistD", 2026, new[] { track });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.ConvertFlacToMp3 = true);

        var device = new MainViewModel.DeviceViewModel
        {
            Name = "USB (H:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = deviceDir
        };

        TransferConfirmationRequest? capturedRequest = null;
        int confirmCallCount = 0;

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: _ => 10L * 1024 * 1024,
                readDeviceStorageSpace: (_, _) => (true, 1000L * 1024 * 1024, 500L * 1024 * 1024),
                settingsService: settingsService,
                audioTranscodeService: new FakeAudioTranscodeService(),
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: req =>
                {
                    confirmCallCount++;
                    capturedRequest = req;
                    return false;
                });

            sut.SelectedDevice = device;
            await sut.CheckDeviceAlbumsAsync();
            await sut.RefreshSelectedDeviceStorageAsync();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Equal(1, confirmCallCount);
            Assert.NotNull(capturedRequest);
            Assert.False(capturedRequest!.IsInsufficientSpace);
            Assert.True(capturedRequest.HasTranscodeTargets);
            Assert.Equal(1, capturedRequest.FlacToMp3Count);
            Assert.Equal(0, capturedRequest.Mp4ToM4aCount);
            Assert.Equal(0, capturedRequest.CopyOnlyCount);
            Assert.False(File.Exists(Path.Combine(deviceDir, "ArtistD", "AlbumD", "01.mp3")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 必要容量と空き容量が完全に一致する境界値、および容量情報未取得の機器において、
    /// 形式変換対象がなければ容量不足として誤検知せず確認ダイアログなしで転送が開始されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(true, 100L * 1024 * 1024, 100L * 1024 * 1024)]
    [InlineData(false, 0L, 100L * 1024 * 1024)]
    public async Task TransferSelectedAsync_空き容量一致または容量情報未取得で変換なしの場合_確認ダイアログを表示せず転送されること(
        bool hasStorageInfo,
        long freeBytes,
        long trackBytes)
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ConfirmBoundary_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string mp3Path = Path.Combine(sourceDir, "01.mp3");
        File.WriteAllText(mp3Path, "mp3-data");

        var track = new Track { Title = "Track 1", Artist = "ArtistE", Album = "AlbumE", FilePath = mp3Path, IsSelected = true };
        var album = new Album("AlbumE", "ArtistE", 2026, new[] { track });
        var albums = new ObservableCollection<Album> { album };

        var device = new MainViewModel.DeviceViewModel
        {
            Name = "USB (I:\\)",
            Type = MainViewModel.DeviceType.FileSystem,
            RootPath = deviceDir
        };

        int confirmCallCount = 0;

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: _ => trackBytes,
                readDeviceStorageSpace: (_, _) => (hasStorageInfo, hasStorageInfo ? 1000L * 1024 * 1024 : 0L, freeBytes),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: null,
                showMessage: (_, _) => { },
                confirmTransfer: _ =>
                {
                    confirmCallCount++;
                    return false;
                });

            sut.SelectedDevice = device;
            await sut.CheckDeviceAlbumsAsync();
            await sut.RefreshSelectedDeviceStorageAsync();

            // Act
            await sut.TransferSelectedAsync();

            // Assert
            Assert.Equal(0, confirmCallCount);
            Assert.True(File.Exists(Path.Combine(deviceDir, "ArtistE", "AlbumE", "01.mp3")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 複数曲の転送中に 1 曲目の変換・転送完了後に CancelTransfer を実行した場合、
    /// 2 曲目以降が安全に中止され、一時ファイルや不完全ファイルが残らず、成功済み曲のみ IsOnDevice が反映されてキャンセル結果サマリが通知されることを検証します。
    /// </summary>
    [Fact]
    public async Task TransferSelectedAsync_転送中にCancelTransfer呼び出し時_安全に中止され一時ファイルが残らずキャンセルサマリが通知されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_CancelTransfer_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string track1Path = Path.Combine(sourceDir, "01.flac");
        string track2Path = Path.Combine(sourceDir, "02.flac");
        File.WriteAllText(track1Path, "flac-track-1");
        File.WriteAllText(track2Path, "flac-track-2");

        var t1 = new Track { Title = "Track 1", Artist = "ArtistCancel", Album = "AlbumCancel", FilePath = track1Path, IsSelected = true };
        var t2 = new Track { Title = "Track 2", Artist = "ArtistCancel", Album = "AlbumCancel", FilePath = track2Path, IsSelected = true };
        var album = new Album("AlbumCancel", "ArtistCancel", 2026, new[] { t1, t2 });
        var albums = new ObservableCollection<Album> { album };

        var settingsService = new InMemorySettingsService();
        settingsService.UpdateSettings(s => s.ConvertFlacToMp3 = true);

        using var secondTrackStarted = new ManualResetEventSlim(false);
        string? secondTempDir = null;
        var cancelableTranscoder = new CallbackAudioTranscodeService(async (sourcePath, options, ct) =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"AE_CancelTmp_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string targetName = Path.ChangeExtension(Path.GetFileName(sourcePath), ".mp3");
            string outputPath = Path.Combine(tempDir, targetName);
            File.WriteAllText(outputPath, "transcoded-mp3");

            if (sourcePath.EndsWith("02.flac", StringComparison.OrdinalIgnoreCase))
            {
                secondTempDir = tempDir;
                secondTrackStarted.Set();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
                catch (OperationCanceledException)
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }

                    throw;
                }
            }

            return new TranscodedAudioResult(sourcePath, outputPath, targetName, wasTranscoded: true, temporaryDirectoryPath: tempDir);
        });

        TransferResultSummary? capturedSummary = null;

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (true, 1000L * 1024 * 1024, 500L * 1024 * 1024),
                settingsService: settingsService,
                audioTranscodeService: cancelableTranscoder,
                resolveMtpClient: null,
                showMessage: null,
                confirmTransfer: _ => true,
                showTransferResult: summary => capturedSummary = summary);

            sut.SelectedDevice = new MainViewModel.DeviceViewModel
            {
                Name = "USB (C:\\)",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceDir
            };
            await sut.CheckDeviceAlbumsAsync();
            await sut.RefreshSelectedDeviceStorageAsync();

            // Act
            var transferTask = sut.TransferSelectedAsync();
            Assert.True(secondTrackStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(sut.CancelTransferCommand.CanExecute(null));

            sut.CancelTransferCommand.Execute(null);
            Assert.True(sut.IsCancelling);
            Assert.False(sut.CancelTransferCommand.CanExecute(null));

            await transferTask;

            // Assert
            Assert.NotNull(capturedSummary);
            Assert.True(capturedSummary!.WasCancelled);
            Assert.Equal(1, capturedSummary.SuccessCount);
            Assert.Equal(0, capturedSummary.FailedCount);
            Assert.True(File.Exists(Path.Combine(deviceDir, "ArtistCancel", "AlbumCancel", "01.mp3")));
            Assert.False(File.Exists(Path.Combine(deviceDir, "ArtistCancel", "AlbumCancel", "02.mp3")));
            Assert.True(secondTempDir == null || !Directory.Exists(secondTempDir));
            Assert.True(t1.IsOnDevice);
            Assert.False(t2.IsOnDevice);
            Assert.False(sut.IsTransferring);
            Assert.False(sut.IsCancelling);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 転送対象のうち 1 曲で変換エラー・1 曲でファイル不在が発生しても残りの曲の転送が継続され、
    /// 成功件数・スキップ件数・失敗曲一覧（曲名・エラー理由）および詳細進捗（変換中／コピー中／スキップ中）が正しく通知されることを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTracksToFileSystemAsync_個別曲エラーおよび重複スキップ混在時_残り曲が継続転送され結果サマリと詳細進捗が正確であること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_PerTrackError_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string deviceDir = Path.Combine(tempRoot, "Device");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(deviceDir);

        string existingPath = Path.Combine(sourceDir, "01_Existing.mp3");
        string failTranscodePath = Path.Combine(sourceDir, "02_Corrupt.flac");
        string missingPath = Path.Combine(sourceDir, "03_Missing.mp3");
        string okFlacPath = Path.Combine(sourceDir, "04_Success.flac");
        File.WriteAllText(existingPath, "mp3-1");
        File.WriteAllText(failTranscodePath, "corrupt-flac");
        File.WriteAllText(okFlacPath, "good-flac");

        // 01_Existing は既にデバイス上に存在させて重複スキップにする
        string destAlbumDir = Path.Combine(deviceDir, "ArtistErr", "AlbumErr");
        Directory.CreateDirectory(destAlbumDir);
        File.WriteAllText(Path.Combine(destAlbumDir, "01_Existing.mp3"), "already-on-device");

        var t1 = new Track { Title = "Existing Song", Artist = "ArtistErr", Album = "AlbumErr", FilePath = existingPath };
        var t2 = new Track { Title = "Corrupt FLAC", Artist = "ArtistErr", Album = "AlbumErr", FilePath = failTranscodePath };
        var t3 = new Track { Title = "Missing File", Artist = "ArtistErr", Album = "AlbumErr", FilePath = missingPath };
        var t4 = new Track { Title = "Good FLAC", Artist = "ArtistErr", Album = "AlbumErr", FilePath = okFlacPath };
        var album = new Album("AlbumErr", "ArtistErr", 2026, new[] { t1, t2, t3, t4 });
        var albums = new ObservableCollection<Album> { album };

        var transcoder = new CallbackAudioTranscodeService((sourcePath, options, ct) =>
        {
            if (sourcePath.EndsWith("02_Corrupt.flac", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("FLAC デコードに失敗しました");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), $"AE_OkTrans_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string targetName = Path.ChangeExtension(Path.GetFileName(sourcePath), ".mp3");
            string outPath = Path.Combine(tempDir, targetName);
            File.WriteAllText(outPath, "ok-mp3");
            return Task.FromResult(new TranscodedAudioResult(sourcePath, outPath, targetName, wasTranscoded: true, temporaryDirectoryPath: tempDir));
        });

        var observedPhases = new List<string>();
        var detailProgress = new SynchronousProgress<TransferProgressInfo>(info => observedPhases.Add(info.PhaseText));

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                audioTranscodeService: transcoder);

            // Act
            var summary = await sut.TransferTracksToFileSystemAsync(
                new[] { t1, t2, t3, t4 },
                deviceDir,
                new AudioTranscodeOptions { ConvertFlacToMp3 = true },
                detailProgress: detailProgress);

            // Assert
            Assert.False(summary.WasCancelled);
            Assert.False(summary.WasAbortedByFatalError);
            Assert.Equal(1, summary.SuccessCount);
            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(2, summary.FailedCount);
            Assert.Contains(summary.FailedItems, item => item.TrackTitle == "Corrupt FLAC" && item.ErrorReason.Contains("FLAC デコードに失敗しました"));
            Assert.Contains(summary.FailedItems, item => item.TrackTitle == "Missing File" && item.ErrorReason.Contains("見つかりません"));
            Assert.True(File.Exists(Path.Combine(destAlbumDir, "04_Success.mp3")));
            Assert.Contains("スキップ中...", observedPhases);
            Assert.Contains("変換中 (FLAC → MP3)...", observedPhases);
            Assert.Contains("コピー中...", observedPhases);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// MTP 転送中にデバイス切断が発生した場合、回復不能エラーとして残りの転送を安全に中断し、
    /// WasAbortedByFatalError とエラーメッセージが結果サマリに記録されることを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTracksToMtpAsync_転送中にデバイス切断発生時_回復不能エラーとして安全中断されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_MtpDisconnect_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);

        string track1Path = Path.Combine(sourceDir, "01.mp3");
        string track2Path = Path.Combine(sourceDir, "02.mp3");
        File.WriteAllText(track1Path, "mp3-1");
        File.WriteAllText(track2Path, "mp3-2");

        var t1 = new Track { Title = "Track 1", Artist = "ArtistMtp", Album = "AlbumMtp", FilePath = track1Path };
        var t2 = new Track { Title = "Track 2", Artist = "ArtistMtp", Album = "AlbumMtp", FilePath = track2Path };
        var album = new Album("AlbumMtp", "ArtistMtp", 2026, new[] { t1, t2 });
        var albums = new ObservableCollection<Album> { album };

        var fakeMtp = new FakeMtpDeviceClient();
        fakeMtp.Connect();
        fakeMtp.AddDirectory(@"\Music");
        int uploadAttempts = 0;
        fakeMtp.OnUploadFile = (_, _) =>
        {
            uploadAttempts++;
            fakeMtp.Disconnect();
            throw new IOException("デバイスが切断されました");
        };

        try
        {
            using var sut = new DeviceBrowserViewModel(
                albums,
                () => albums,
                () => { },
                getFileSizeBytes: null,
                readDeviceStorageSpace: (_, _) => (false, 0L, 0L),
                settingsService: null,
                audioTranscodeService: null,
                resolveMtpClient: _ => fakeMtp,
                showMessage: null);

            // Act
            var summary = await sut.TransferTracksToMtpAsync(
                fakeMtp,
                new[] { t1, t2 },
                @"\Music",
                new AudioTranscodeOptions());

            // Assert
            Assert.True(summary.WasAbortedByFatalError);
            Assert.True(summary.HasFatalError);
            Assert.Equal(0, summary.SuccessCount);
            Assert.Equal(1, summary.FailedCount);
            Assert.Equal(1, uploadAttempts);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public SynchronousProgress(Action<T> onReport)
        {
            _onReport = onReport;
        }

        public void Report(T value) => _onReport(value);
    }

    private sealed class CallbackAudioTranscodeService : IAudioTranscodeService
    {
        private readonly Func<string, AudioTranscodeOptions, CancellationToken, Task<TranscodedAudioResult>> _prepare;

        public CallbackAudioTranscodeService(
            Func<string, AudioTranscodeOptions, CancellationToken, Task<TranscodedAudioResult>> prepare)
        {
            _prepare = prepare;
        }

        public bool NeedsTranscode(string sourceFilePath, AudioTranscodeOptions options)
        {
            string ext = Path.GetExtension(sourceFilePath);
            return (options.ConvertFlacToMp3 && ext.Equals(".flac", StringComparison.OrdinalIgnoreCase))
                || (options.ConvertMp4ToM4a && ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase));
        }

        public string ResolveTargetFileName(string sourceFilePath, AudioTranscodeOptions options)
        {
            string fileName = Path.GetFileName(sourceFilePath);
            if (NeedsTranscode(sourceFilePath, options))
            {
                string ext = Path.GetExtension(fileName);
                return ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(fileName, ".mp3")
                    : Path.ChangeExtension(fileName, ".m4a");
            }

            return fileName;
        }

        public Task<TranscodedAudioResult> PrepareForTransferAsync(
            string sourceFilePath,
            AudioTranscodeOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!NeedsTranscode(sourceFilePath, options))
            {
                return Task.FromResult(TranscodedAudioResult.Passthrough(sourceFilePath));
            }

            return _prepare(sourceFilePath, options, cancellationToken);
        }
    }

    private sealed class FakeMtpDeviceClient : DeviceBrowserViewModel.IMtpDeviceClient
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase) { @"\" };
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _enumeratedDirectories = new();
        private readonly List<string> _deletedFiles = new();
        private bool _isConnected;

        public bool ThrowIfDestinationExists { get; set; }

        public IReadOnlyList<string> DeletedFiles
        {
            get
            {
                lock (_sync)
                {
                    return _deletedFiles.ToArray();
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                {
                    return _isConnected;
                }
            }
        }

        public Action<string>? OnGetFiles { get; set; }
        public Action<string, string>? OnUploadFile { get; set; }

        public IReadOnlyList<string> EnumeratedDirectories
        {
            get
            {
                lock (_sync)
                {
                    return _enumeratedDirectories.ToArray();
                }
            }
        }

        public void Connect()
        {
            lock (_sync)
            {
                _isConnected = true;
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                _isConnected = false;
            }
        }

        public void ClearEnumeratedDirectories()
        {
            lock (_sync)
            {
                _enumeratedDirectories.Clear();
            }
        }

        public void AddDirectory(string path)
        {
            lock (_sync)
            {
                AddDirectoryUnsafe(path);
            }
        }

        public void AddFile(string path)
        {
            lock (_sync)
            {
                string normalized = NormalizePath(path);
                string? parent = Path.GetDirectoryName(normalized);
                if (!string.IsNullOrEmpty(parent))
                {
                    AddDirectoryUnsafe(parent);
                }
                _files.Add(normalized);
            }
        }

        public bool DirectoryExists(string path)
        {
            lock (_sync)
            {
                return _directories.Contains(NormalizePath(path));
            }
        }

        public void CreateDirectory(string path) => AddDirectory(path);

        public IEnumerable<string> GetDirectories(string path)
        {
            string normalized = NormalizePath(path);
            lock (_sync)
            {
                _enumeratedDirectories.Add(normalized);
                var result = new List<string>();
                foreach (var dir in _directories)
                {
                    if (string.Equals(dir, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent) && string.Equals(NormalizePath(parent), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(dir);
                    }
                }
                return result;
            }
        }

        public IEnumerable<string> GetFiles(string path)
        {
            string normalized = NormalizePath(path);
            OnGetFiles?.Invoke(normalized);
            lock (_sync)
            {
                var result = new List<string>();
                foreach (var file in _files)
                {
                    string? parent = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(parent) && string.Equals(NormalizePath(parent), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(file);
                    }
                }
                return result;
            }
        }

        public bool FileExists(string path)
        {
            lock (_sync)
            {
                return _files.Contains(NormalizePath(path));
            }
        }

        public void DeleteFile(string path)
        {
            lock (_sync)
            {
                string normalized = NormalizePath(path);
                _files.Remove(normalized);
                _deletedFiles.Add(normalized);
            }
        }

        public void UploadFile(string sourceFilePath, string destinationFilePath)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Source file not found.", sourceFilePath);
            }

            if (ThrowIfDestinationExists && FileExists(destinationFilePath))
            {
                throw new IOException($"Destination file already exists on MTP device: {destinationFilePath}");
            }

            OnUploadFile?.Invoke(sourceFilePath, destinationFilePath);
            AddFile(destinationFilePath);
        }

        private void AddDirectoryUnsafe(string path)
        {
            string normalized = NormalizePath(path);
            _directories.Add(normalized);
            string? parent = Path.GetDirectoryName(normalized);
            while (!string.IsNullOrEmpty(parent))
            {
                _directories.Add(NormalizePath(parent));
                if (parent == @"\")
                {
                    break;
                }
                parent = Path.GetDirectoryName(parent);
            }
        }

        private static string NormalizePath(string path)
        {
            string normalized = (path ?? string.Empty).Replace('/', '\\').Trim();
            if (string.IsNullOrEmpty(normalized) || normalized == @"\")
            {
                return @"\";
            }

            if (!normalized.StartsWith('\\'))
            {
                normalized = @"\" + normalized;
            }

            return normalized.TrimEnd('\\');
        }
    }

    private sealed class FakeAudioTranscodeService : IAudioTranscodeService
    {
        public int TranscodeCallCount { get; private set; }
        public string? LastTemporaryDirectory { get; private set; }

        public bool NeedsTranscode(string sourceFilePath, AudioTranscodeOptions options)
        {
            string ext = Path.GetExtension(sourceFilePath);
            return (options.ConvertMp4ToM4a && ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                || (options.ConvertFlacToMp3 && ext.Equals(".flac", StringComparison.OrdinalIgnoreCase));
        }

        public string ResolveTargetFileName(string sourceFilePath, AudioTranscodeOptions options)
        {
            string fileName = Path.GetFileName(sourceFilePath);
            string ext = Path.GetExtension(fileName);
            if (options.ConvertMp4ToM4a && ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(fileName, ".m4a");
            }

            if (options.ConvertFlacToMp3 && ext.Equals(".flac", StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(fileName, ".mp3");
            }

            return fileName;
        }

        public Task<TranscodedAudioResult> PrepareForTransferAsync(
            string sourceFilePath,
            AudioTranscodeOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!NeedsTranscode(sourceFilePath, options))
            {
                return Task.FromResult(TranscodedAudioResult.Passthrough(sourceFilePath));
            }

            TranscodeCallCount++;
            string tempDir = Path.Combine(Path.GetTempPath(), $"AE_FakeTranscode_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            LastTemporaryDirectory = tempDir;

            string targetName = ResolveTargetFileName(sourceFilePath, options);
            string outputPath = Path.Combine(tempDir, targetName);
            string content = targetName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                ? "transcoded-mp3-bytes"
                : "transcoded-m4a-bytes";
            File.WriteAllText(outputPath, content);

            return Task.FromResult(new TranscodedAudioResult(
                sourceFilePath,
                outputPath,
                targetName,
                wasTranscoded: true,
                temporaryDirectoryPath: tempDir));
        }
    }
}
