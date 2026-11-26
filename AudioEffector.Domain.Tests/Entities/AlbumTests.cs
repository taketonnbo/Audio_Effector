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
}
