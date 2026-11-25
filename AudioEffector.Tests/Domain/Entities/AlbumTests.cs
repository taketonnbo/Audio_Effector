using System.Collections.Generic;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.Entities;

/// <summary>
/// Albumエンティティの代表トラックパス取得などドメインロジックを検証するテストクラス
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
    /// コンストラクタでIDを省略した場合、アルバム名とアーティスト名から決定論的なAlbumIdが設定されることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_ID未指定時_決定論的AlbumIdが設定されること()
    {
        // Arrange & Act
        var sut = new Album("Discovery", "Daft Punk");

        // Assert
        Assert.Equal(AlbumId.FromAlbumKey("Discovery", "Daft Punk"), sut.Id);
    }

    /// <summary>
    /// コンストラクタで明示的にAlbumIdを指定した場合、その値が保持されることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_明示的ID指定時_指定されたAlbumIdが保持されること()
    {
        // Arrange
        var explicitId = AlbumId.FromAlbumKey("Custom Album", "Custom Artist");

        // Act
        var sut = new Album("Discovery", "Daft Punk", 2001, null, explicitId);

        // Assert
        Assert.Equal(explicitId, sut.Id);
    }

    /// <summary>
    /// TitleまたはArtistを更新した際、Album.Idが最新キーで再計算されPropertyChangedが通知されることを検証します。
    /// </summary>
    [Fact]
    public void TitleおよびArtist更新時_Idが再計算されPropertyChangedが通知されること()
    {
        // Arrange
        var sut = new Album("Old Album", "Old Artist");
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.Title = "New Album";
        sut.Artist = "New Artist";

        // Assert
        Assert.Equal(AlbumId.FromAlbumKey("New Album", "New Artist"), sut.Id);
        Assert.Contains(nameof(Album.Id), notified);
    }

    /// <summary>
    /// 同名アルバムであってもアーティストが異なる場合、異なるIdとなりEqualsもfalseとなることを検証します。
    /// </summary>
    [Fact]
    public void Equals_同名アルバムで別アーティスト_異なるIdかつ不等価であること()
    {
        // Arrange
        var sut1 = new Album("Best Of", "Artist A");
        var sut2 = new Album("Best Of", "Artist B");

        // Act & Assert
        Assert.NotEqual(sut1.Id, sut2.Id);
        Assert.False(sut1.Equals(sut2));
    }
}
