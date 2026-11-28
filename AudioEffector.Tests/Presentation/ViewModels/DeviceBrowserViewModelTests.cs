using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
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
    [Fact]
    public void UpdateSelectionSummary_選択状態変更時_合計サイズと空き容量判定がリアルタイム更新されること()
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
}
