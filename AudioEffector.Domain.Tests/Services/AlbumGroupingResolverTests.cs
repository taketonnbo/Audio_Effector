using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Domain.Tests.Services;

/// <summary>
/// AlbumGroupingResolverの作品単位AlbumId割り当て・既存ID引き継ぎ・コンピレーションフォールバックを検証するテストクラス
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
        Assert.Equal(track1.AlbumId, track2.AlbumId);
        Assert.Equal("Blue Note", resolved[track1.Id].AlbumArtist);
        Assert.Equal("Clifford Brown", track1.Artist);
        Assert.Equal("Herbie Hancock", track2.Artist);
    }

    /// <summary>
    /// 既存のAlbumIdマップが渡された場合、新規採番せず既存のAlbumIdが引き継がれることを検証します。
    /// </summary>
    [Fact]
    public void ResolveAndApply_既存AlbumIdマップ指定時_既存のAlbumIdが引き継がれること()
    {
        // Arrange
        var existingAlbumId = AlbumId.New();
        var existingMap = new Dictionary<(string, string), AlbumId>
        {
            [AlbumGroupingResolver.CreateGroupKey("Blue Note X Blue Giant", "Blue Note")] = existingAlbumId,
        };
        var track1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\BlueNote\01.flac"), "T1", "Clifford Brown", "Blue Note X Blue Giant", TimeSpan.FromMinutes(4), trackNumber: 1, albumArtist: "Blue Note");
        var track2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\BlueNote\02.flac"), "T2", "Herbie Hancock", "Blue Note X Blue Giant", TimeSpan.FromMinutes(5), trackNumber: 2, albumArtist: "Blue Note");

        // Act
        AlbumGroupingResolver.ResolveAndApply([track1, track2], existingMap);

        // Assert
        Assert.Equal(existingAlbumId, track1.AlbumId);
        Assert.Equal(existingAlbumId, track2.AlbumId);
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
        Assert.Equal(track1.AlbumId, track2.AlbumId);
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
        Assert.NotEqual(trackA.AlbumId, trackB.AlbumId);
    }

    /// <summary>
    /// 同一作品Aに属していた2曲のうち片方のアルバム名だけをBへ変更して再集約した際、
    /// 入力順に関わらず変更していない作品Aの既存IDが維持され、移動した曲（作品B）には新しいAlbumIdが採番されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveAndApply_作品分割時に片方の曲だけ別アルバムへ移動した場合_入力順に関わらず既存作品AのIDが維持され新規作品Bへ別IDが採番されること(bool renameFirstTrack)
    {
        // Arrange
        var albumAId = AlbumId.New();
        var track1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\AlbumA\01.flac"), "Song 1", "Artist A", "Album A", TimeSpan.FromMinutes(3), trackNumber: 1, albumId: albumAId);
        var track2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\AlbumA\02.flac"), "Song 2", "Artist A", "Album A", TimeSpan.FromMinutes(4), trackNumber: 2, albumId: albumAId);

        var existingMap = new Dictionary<(string, string), AlbumId>
        {
            [AlbumGroupingResolver.CreateGroupKey("Album A", "Artist A")] = albumAId,
        };

        var movedTrack = renameFirstTrack ? track1 : track2;
        var stayedTrack = renameFirstTrack ? track2 : track1;
        movedTrack.Album = "Album B";

        // Act
        AlbumGroupingResolver.ResolveAndApply([track1, track2], existingMap);

        // Assert: 残った作品 A の ID が維持され、移動した曲は旧作品 A の ID を奪わず新規 ID を持つ
        Assert.Equal(albumAId, stayedTrack.AlbumId);
        Assert.NotEqual(albumAId, movedTrack.AlbumId);
    }
}
