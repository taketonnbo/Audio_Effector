using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// AlbumエンティティのID・等価性・代表トラックパス取得などドメインロジックを検証するテストクラス
/// </summary>
public sealed class AlbumTests
{
    /// <summary>
    /// トラックが空のアルバムでRepresentativeTrackFilePathを参照した場合、例外を送出せず空文字を返すことを検証します
    /// </summary>
    [Fact]
    public void RepresentativeTrackFilePath_トラックが空の場合_空文字を返す()
    {
        // Arrange
        var sut = new Album("Empty Album", "Artist A");

        // Act
        var path = sut.RepresentativeTrackFilePath;

        // Assert
        Assert.Equal(string.Empty, path);
    }

    /// <summary>
    /// 有効なファイルパスを持つトラックが含まれる場合、最初の有効なファイルパスを返すことを検証します
    /// </summary>
    [Fact]
    public void RepresentativeTrackFilePath_有効なパスのトラックがある場合_先頭の有効パスを返す()
    {
        // Arrange
        var sut = new Album("Test Album", "Artist A", 2024, new[]
        {
            new Track { Title = "No Path", FilePath = string.Empty },
            new Track { Title = "Valid Path", FilePath = @"C:\music\track1.flac" },
            new Track { Title = "Second Path", FilePath = @"C:\music\track2.flac" },
        });

        // Act
        var path = sut.RepresentativeTrackFilePath;

        // Assert
        Assert.Equal(@"C:\music\track1.flac", path);
    }

    /// <summary>
    /// コンストラクタでIDを省略した場合、空でない一意のAlbumIdが自動採番されることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_ID未指定時_空でない一意のAlbumIdが採番されること()
    {
        // Arrange & Act
        var sut1 = new Album("Discovery", "Daft Punk");
        var sut2 = new Album("Discovery", "Daft Punk");

        // Assert
        Assert.NotEqual(Guid.Empty, sut1.Id.Value);
        Assert.NotEqual(Guid.Empty, sut2.Id.Value);
        Assert.NotEqual(sut1.Id, sut2.Id);
    }

    /// <summary>
    /// コンストラクタで明示的にAlbumIdを指定した場合、その値が保持されTitleやArtistを変更してもIDが不変であることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_明示的ID指定時_メタデータ変更後も指定されたAlbumIdが維持されること()
    {
        // Arrange
        var explicitId = AlbumId.New();
        var sut = new Album("Discovery", "Daft Punk", 2001, null, explicitId);

        // Act
        sut.Title = "Discovery (Remastered)";
        sut.Artist = "Daft Punk (JP)";

        // Assert
        Assert.Equal(explicitId, sut.Id);
    }

    /// <summary>
    /// AlbumのEqualsおよびGetHashCodeがIdの同一性に基づいて判定されることを検証します。
    /// </summary>
    [Fact]
    public void EqualsおよびGetHashCode_Id基準で同一性が判定されること()
    {
        // Arrange
        var sharedId = AlbumId.New();
        var album1 = new Album("Title A", "Artist A", id: sharedId);
        var album2 = new Album("Title B", "Artist B", id: sharedId);
        var album3 = new Album("Title A", "Artist A", id: AlbumId.New());

        // Act
        var set = new HashSet<Album> { album1, album2, album3 };

        // Assert
        Assert.True(album1.Equals(album2));
        Assert.Equal(album1.GetHashCode(), album2.GetHashCode());
        Assert.False(album1.Equals(album3));
        Assert.Equal(2, set.Count);
    }

    /// <summary>
    /// アルバム内の一部の曲のみがデバイス上に存在する場合、Album.IsOnDeviceはfalseを維持し、アルバム選択時は未転送曲のみ選択されることを検証します。
    /// </summary>
    [Fact]
    public void IsSelected_一部トラックのみIsOnDeviceの場合_未転送トラックのみ選択されること()
    {
        // Arrange
        var transferredTrack = new Track { Title = "Track 1", FilePath = @"C:\music\01.flac", IsOnDevice = true };
        var pendingTrack = new Track { Title = "Track 2", FilePath = @"C:\music\02.flac", IsOnDevice = false };
        var sut = new Album("Discovery", "Daft Punk", 2001, new[] { transferredTrack, pendingTrack });

        // Act
        sut.IsSelected = true;

        // Assert
        Assert.False(sut.IsOnDevice);
        Assert.True(sut.IsSelected);
        Assert.False(transferredTrack.IsSelected);
        Assert.True(pendingTrack.IsSelected);
    }

    /// <summary>
    /// アルバム内の全トラックのIsOnDeviceがtrueになった際、Album.IsOnDeviceが自動でtrueに連動し選択状態が解除されることを検証します。
    /// </summary>
    [Fact]
    public void IsOnDevice_全トラックがIsOnDeviceになった場合_アルバムも自動でIsOnDeviceとなり選択解除されること()
    {
        // Arrange
        var track1 = new Track { Title = "Track 1", FilePath = @"C:\music\01.flac" };
        var track2 = new Track { Title = "Track 2", FilePath = @"C:\music\02.flac" };
        var sut = new Album("Discovery", "Daft Punk", 2001, new[] { track1, track2 });
        sut.IsSelected = true;

        // Act
        track1.IsOnDevice = true;
        bool isOnDeviceAfterFirst = sut.IsOnDevice;
        track2.IsOnDevice = true;

        // Assert
        Assert.False(isOnDeviceAfterFirst);
        Assert.True(sut.IsOnDevice);
        Assert.False(sut.IsSelected);
        Assert.False(track1.IsSelected);
        Assert.False(track2.IsSelected);
    }

    /// <summary>
    /// 全曲転送済みのアルバムに対してIsSelectedをtrueに設定しても選択されないことを検証します。
    /// </summary>
    [Fact]
    public void IsSelected_アルバムがIsOnDeviceの場合_Trueを設定してもFalseを維持すること()
    {
        // Arrange
        var track = new Track { Title = "Track 1", FilePath = @"C:\music\01.flac", IsOnDevice = true };
        var sut = new Album("Discovery", "Daft Punk", 2001, new[] { track });

        // Act
        sut.IsSelected = true;

        // Assert
        Assert.True(sut.IsOnDevice);
        Assert.False(sut.IsSelected);
        Assert.False(track.IsSelected);
    }

    /// <summary>
    /// 全曲転送済みだったアルバム内の1曲がデバイスから削除されIsOnDeviceがfalseへ戻った際、Album.IsOnDeviceもfalseへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void IsOnDevice_トラックがTrueからFalseへ復帰した場合_アルバムのIsOnDeviceもFalseへ自動解除されること()
    {
        // Arrange
        var track1 = new Track { Title = "Track 1", FilePath = @"C:\music\01.flac", IsOnDevice = true };
        var track2 = new Track { Title = "Track 2", FilePath = @"C:\music\02.flac", IsOnDevice = true };
        var sut = new Album("Discovery", "Daft Punk", 2001, new[] { track1, track2 });
        Assert.True(sut.IsOnDevice);

        // Act
        track2.IsOnDevice = false;

        // Assert
        Assert.False(sut.IsOnDevice);
    }

    /// <summary>
    /// トラックが空のアルバムはUpdateIsOnDeviceState後もIsOnDeviceがfalseであることを検証します。
    /// </summary>
    [Fact]
    public void IsOnDevice_トラックが空のアルバムの場合_Falseを維持すること()
    {
        // Arrange
        var sut = new Album("Empty Album", "Artist A");

        // Act
        sut.UpdateIsOnDeviceState();

        // Assert
        Assert.False(sut.IsOnDevice);
    }

    /// <summary>
    /// RemoveTrackおよびTracks差し替え後に旧トラックのIsOnDevice変更がアルバムへ影響しないことを検証します。
    /// </summary>
    [Fact]
    public void RemoveTrackおよびTracks差し替え_旧トラックのIsOnDevice変更がアルバムへ影響しないこと()
    {
        // Arrange
        var removedTrack = new Track { Title = "Removed", FilePath = @"C:\music\01.flac" };
        var keptTrack = new Track { Title = "Kept", FilePath = @"C:\music\02.flac", IsOnDevice = true };
        var sut = new Album("Discovery", "Daft Punk", 2001, new[] { removedTrack, keptTrack });

        // Act
        sut.RemoveTrack(removedTrack);
        bool isOnDeviceAfterRemove = sut.IsOnDevice;
        int propertyChangedCount = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Album.IsOnDevice))
            {
                propertyChangedCount++;
            }
        };
        removedTrack.IsOnDevice = true;
        removedTrack.IsOnDevice = false;

        var newTrack = new Track { Title = "New", FilePath = @"C:\music\03.flac", IsOnDevice = false };
        sut.Tracks = new List<Track> { newTrack };
        int countBeforeOldKeptChange = propertyChangedCount;
        keptTrack.IsOnDevice = false;

        // Assert
        Assert.True(isOnDeviceAfterRemove);
        Assert.False(sut.IsOnDevice);
        Assert.Equal(countBeforeOldKeptChange, propertyChangedCount);
    }

    /// <summary>
    /// ApplyTracksOnDeviceStateによる一括更新時、全トラックの更新後にアルバムのIsOnDeviceが正しく同期されることを検証します。
    /// </summary>
    [Fact]
    public void ApplyTracksOnDeviceState_一括更新時_IsOnDevice通知が最小回数で正しく同期されること()
    {
        // Arrange
        var track1 = new Track { Title = "Track 1", FilePath = @"C:\music\01.flac" };
        var track2 = new Track { Title = "Track 2", FilePath = @"C:\music\02.flac" };
        var sut = new Album("Discovery", "Daft Punk", 2001, new[] { track1, track2 });
        int albumIsOnDeviceNotifyCount = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Album.IsOnDevice))
            {
                albumIsOnDeviceNotifyCount++;
            }
        };

        // Act
        sut.ApplyTracksOnDeviceState(_ => true);

        // Assert
        Assert.True(track1.IsOnDevice);
        Assert.True(track2.IsOnDevice);
        Assert.True(sut.IsOnDevice);
        Assert.Equal(1, albumIsOnDeviceNotifyCount);
    }
}
