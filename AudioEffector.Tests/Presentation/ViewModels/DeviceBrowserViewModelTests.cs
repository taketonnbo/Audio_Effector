using System;
using System.Collections.Generic;
using System.IO;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// DeviceBrowserViewModel の重複判定インデックス化・曲単位非活性・転送対象抽出ロジックを検証するテストクラス
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
    /// 指定フォルダ配下の一括インデックス構築で、サブフォルダ配下の音声ファイルのみが収集され非音声ファイルが除外されることを検証します。
    /// </summary>
    [Fact]
    public void BuildFileSystemAudioIndex_再帰有効時_サブフォルダ配下の音声ファイルのみインデックス化されること()
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
            Assert.Contains("01_Track", recursiveIndex);
            Assert.Contains("02_Track", recursiveIndex);
            Assert.DoesNotContain("cover", recursiveIndex);

            Assert.Contains("root_song", nonRecursiveIndex);
            Assert.DoesNotContain("01_Track", nonRecursiveIndex);
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
    /// インデックス照合により、既転送トラックのみIsOnDevice=true・選択解除となり、全曲転送済みアルバムのみAlbum.IsOnDevice=trueとなることを検証します。
    /// </summary>
    [Fact]
    public void ApplyDeviceTrackIndex_一部転送済みと全曲転送済みアルバム_曲単位およびアルバム単位の状態が正しく更新されること()
    {
        // Arrange
        var partialTrack1 = new Track { Title = "Song 1", FilePath = @"C:\Music\01_Song.mp4", IsSelected = true };
        var partialTrack2 = new Track { Title = "Song 2", FilePath = @"C:\Music\02_Song.flac", IsSelected = true };
        var partialAlbum = new Album("Partial Album", "Artist A", 2024, new[] { partialTrack1, partialTrack2 });

        var fullTrack1 = new Track { Title = "Full 1", FilePath = @"C:\Music\full_01.flac", IsSelected = true };
        var fullAlbum = new Album("Full Album", "Artist B", 2024, new[] { fullTrack1 }) { IsSelected = true };

        // デバイス側には 01_Song.m4a（拡張子変換後想定）と full_01.mp3（拡張子変換後想定）が存在
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "01_Song",
            "FULL_01"
        };

        // Act
        DeviceBrowserViewModel.ApplyDeviceTrackIndex(new[] { partialAlbum, fullAlbum }, index);

        // Assert
        Assert.True(partialTrack1.IsOnDevice);
        Assert.False(partialTrack1.IsSelected);
        Assert.False(partialTrack2.IsOnDevice);
        Assert.True(partialTrack2.IsSelected);
        Assert.False(partialAlbum.IsOnDevice);

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
}
