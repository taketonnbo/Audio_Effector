using System;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.Services;

/// <summary>
/// AlbumGroupingResolverの作品単位AlbumId解決およびコンピレーションフォールバックを検証するテストクラス
/// </summary>
public sealed class AlbumGroupingResolverTests
{
    /// <summary>
    /// 共通のAlbumArtistを持つ異なる演奏者の2曲が同一のAlbumIdへ集約され、各曲のArtist（演奏者）は維持されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveAndApply_共通AlbumArtistを持つ複数演奏者_同一AlbumIdへ集約され各曲のArtistは保持されること()
    {
        // Arrange
        var track1 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\BlueNote\01.flac"),
            title: "Moanin'",
            artist: "Clifford Brown",
            album: "Blue Note X Blue Giant",
            duration: TimeSpan.FromMinutes(4),
            trackNumber: 1,
            albumArtist: "Blue Note");
        var track2 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\BlueNote\02.flac"),
            title: "Maiden Voyage",
            artist: "Herbie Hancock",
            album: "Blue Note X Blue Giant",
            duration: TimeSpan.FromMinutes(5),
            trackNumber: 2,
            albumArtist: "Blue Note");

        // Act
        var resolved = AlbumGroupingResolver.ResolveAndApply([track1, track2]);

        // Assert
        var expectedAlbumId = AlbumId.FromAlbumKey("Blue Note X Blue Giant", "Blue Note");
        Assert.Equal(expectedAlbumId, track1.AlbumId);
        Assert.Equal(expectedAlbumId, track2.AlbumId);
        Assert.Equal("Blue Note", resolved[track1.Id].AlbumArtist);
        Assert.Equal("Clifford Brown", track1.Artist);
        Assert.Equal("Herbie Hancock", track2.Artist);
    }

    /// <summary>
    /// AlbumArtistが未設定であっても、同一フォルダ内で連番トラック番号を持つ複数演奏者の同名アルバムはVarious Artistsとして1枚のAlbumIdへ集約されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveAndApply_AlbumArtist未設定かつ同一フォルダ連番のコンピレーション_VariousArtistsとして1枚のAlbumIdに集約されること()
    {
        // Arrange
        var track1 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\Compilations\01_brown.flac"),
            title: "Track 1",
            artist: "Clifford Brown",
            album: "Blue Note X Blue Giant",
            duration: TimeSpan.FromMinutes(4),
            trackNumber: 1);
        var track2 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\Compilations\02_coltrane.flac"),
            title: "Track 2",
            artist: "John Coltrane",
            album: "Blue Note X Blue Giant",
            duration: TimeSpan.FromMinutes(5),
            trackNumber: 2);

        // Act
        var resolved = AlbumGroupingResolver.ResolveAndApply([track1, track2]);

        // Assert
        var expectedAlbumId = AlbumId.FromAlbumKey("Blue Note X Blue Giant", AlbumGroupingResolver.VariousArtistsName);
        Assert.Equal(expectedAlbumId, track1.AlbumId);
        Assert.Equal(expectedAlbumId, track2.AlbumId);
        Assert.Equal(AlbumGroupingResolver.VariousArtistsName, resolved[track1.Id].AlbumArtist);
        Assert.Equal("Clifford Brown", track1.Artist);
        Assert.Equal("John Coltrane", track2.Artist);
    }

    /// <summary>
    /// 別フォルダに配置された別作品の同名アルバムは、AlbumArtist未設定であっても別々のAlbumIdへ分離されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveAndApply_AlbumArtist未設定かつ別フォルダの同名アルバム_別々のAlbumIdへ分離されること()
    {
        // Arrange
        var trackA = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\ArtistA\BestOf\01.flac"),
            title: "Song A",
            artist: "Artist A",
            album: "Best Of",
            duration: TimeSpan.FromMinutes(3),
            trackNumber: 1);
        var trackB = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\ArtistB\BestOf\01.flac"),
            title: "Song B",
            artist: "Artist B",
            album: "Best Of",
            duration: TimeSpan.FromMinutes(3),
            trackNumber: 1);

        // Act
        AlbumGroupingResolver.ResolveAndApply([trackA, trackB]);

        // Assert
        Assert.Equal(AlbumId.FromAlbumKey("Best Of", "Artist A"), trackA.AlbumId);
        Assert.Equal(AlbumId.FromAlbumKey("Best Of", "Artist B"), trackB.AlbumId);
        Assert.NotEqual(trackA.AlbumId, trackB.AlbumId);
    }

    /// <summary>
    /// 異なる明示的AlbumArtistを持つ同名アルバムは、同一フォルダ内であっても別々のAlbumIdへ分離されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveAndApply_異なるAlbumArtistを持つ同名アルバム_別々のAlbumIdへ分離されること()
    {
        // Arrange
        var trackA = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\Flat\01.flac"),
            title: "Song A",
            artist: "Artist A",
            album: "Greatest Hits",
            duration: TimeSpan.FromMinutes(3),
            trackNumber: 1,
            albumArtist: "Artist A");
        var trackB = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\Flat\02.flac"),
            title: "Song B",
            artist: "Artist B",
            album: "Greatest Hits",
            duration: TimeSpan.FromMinutes(3),
            trackNumber: 2,
            albumArtist: "Artist B");

        // Act
        AlbumGroupingResolver.ResolveAndApply([trackA, trackB]);

        // Assert
        Assert.Equal(AlbumId.FromAlbumKey("Greatest Hits", "Artist A"), trackA.AlbumId);
        Assert.Equal(AlbumId.FromAlbumKey("Greatest Hits", "Artist B"), trackB.AlbumId);
        Assert.NotEqual(trackA.AlbumId, trackB.AlbumId);
    }
}
