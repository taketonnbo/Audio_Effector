using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using Xunit;

namespace AudioEffector.Tests.Domain.Services;

/// <summary>
/// FavoriteTrackMatcherのメタデータベース照合、同名曲判別、およびフォールバック解決ロジックを検証するテストクラス
/// </summary>
public sealed class FavoriteTrackMatcherTests
{
    private static Track CreateTrack(
        string filePath,
        string title,
        string artist,
        string album,
        uint trackNumber = 1,
        double durationSeconds = 240.0)
    {
        return new Track
        {
            FilePath = filePath,
            Title = title,
            Artist = artist,
            Album = album,
            TrackNumber = trackNumber,
            Duration = TimeSpan.FromSeconds(durationSeconds)
        };
    }

    /// <summary>
    /// 音楽ファイルを別フォルダへ移動・リネームしても、(Title, Artist, Album)一致でお気に入りが維持されLastKnownFilePathが自動更新されることを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_フォルダ移動およびリネーム後_メタデータ一致でお気に入り維持かつパスが自動更新されること()
    {
        // Arrange
        var addedAt = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Local);
        var entries = new List<FavoriteTrackEntry>
        {
            new("Midnight City", "M83", "Hurry Up, We're Dreaming", 2, 243.5, @"C:\Music\OldFolder\02_Midnight_City.mp3", addedAt)
        };
        var movedTrack = CreateTrack(
            @"D:\Archive\M83\MidnightCity_Renamed.mp3",
            "  midnight city  ",
            "m83",
            "Hurry Up, We're Dreaming",
            trackNumber: 2,
            durationSeconds: 243.5);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, [movedTrack]);

        // Assert
        Assert.True(movedTrack.IsFavorite);
        Assert.Single(sut.MatchedTracks);
        Assert.Same(movedTrack, sut.MatchedTracks[0]);
        Assert.True(sut.HasEntryChanges);
        Assert.Equal(@"D:\Archive\M83\MidnightCity_Renamed.mp3", sut.UpdatedEntries[0].LastKnownFilePath);
        Assert.Equal(addedAt, sut.UpdatedEntries[0].AddedAt);
    }

    /// <summary>
    /// MP3からFLACへ差し替えて拡張子や軽微な再生時間差が発生しても、メタデータ一致でお気に入りが引き継がれることを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_MP3からFLACへ差し替え後_軽微な再生時間差を許容しお気に入りが引き継がれること()
    {
        // Arrange
        var entries = new List<FavoriteTrackEntry>
        {
            new("Get Lucky", "Daft Punk", "Random Access Memories", 8, 369.2, @"D:\Music\DaftPunk\08_Get_Lucky.mp3", DateTime.Now)
        };
        var flacTrack = CreateTrack(
            @"D:\HiRes\DaftPunk\08_Get_Lucky.flac",
            "Get Lucky",
            "Daft Punk",
            "Random Access Memories",
            trackNumber: 8,
            durationSeconds: 369.8);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, [flacTrack]);

        // Assert
        Assert.True(flacTrack.IsFavorite);
        Assert.True(sut.HasEntryChanges);
        Assert.Equal(@"D:\HiRes\DaftPunk\08_Get_Lucky.flac", sut.UpdatedEntries[0].LastKnownFilePath);
    }

    /// <summary>
    /// 同一アルバム内に同名曲が複数存在する場合、TrackNumberとDurationSecondsで対象トラックのみを正確に判別できることを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_同一アルバム内の同名曲複数存在_トラック番号と再生時間で対象曲のみお気に入りになること()
    {
        // Arrange
        var entries = new List<FavoriteTrackEntry>
        {
            new("Interlude", "Artist A", "Album X", 7, 180.0, @"D:\Old\07_Interlude.flac", DateTime.Now)
        };
        var track1 = CreateTrack(@"D:\New\01_Interlude.flac", "Interlude", "Artist A", "Album X", trackNumber: 1, durationSeconds: 45.0);
        var track7 = CreateTrack(@"D:\New\07_Interlude.flac", "Interlude", "Artist A", "Album X", trackNumber: 7, durationSeconds: 180.2);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, [track1, track7]);

        // Assert
        Assert.False(track1.IsFavorite);
        Assert.True(track7.IsFavorite);
        Assert.Single(sut.MatchedTracks);
        Assert.Same(track7, sut.MatchedTracks[0]);
    }

    /// <summary>
    /// タグ未設定曲の場合、LastKnownFilePath一致が優先され別フォルダの同名未タグ曲へ誤マッチしないことを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_タグ未設定曲が別フォルダに同名存在_パス一致する曲のみがお気に入りになること()
    {
        // Arrange
        var entries = new List<FavoriteTrackEntry>
        {
            new("Track01", "Unknown Artist", "Unknown Album", 0, 200.0, @"D:\Music\FolderB\Track01.mp3", DateTime.Now)
        };
        var otherUntagged = CreateTrack(@"D:\Music\FolderA\Track01.mp3", "Track01", "Unknown Artist", "Unknown Album", trackNumber: 0, durationSeconds: 200.0);
        var targetUntagged = CreateTrack(@"D:\Music\FolderB\Track01.mp3", "Track01", "Unknown Artist", "Unknown Album", trackNumber: 0, durationSeconds: 200.0);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, [otherUntagged, targetUntagged]);

        // Assert
        Assert.False(otherUntagged.IsFavorite);
        Assert.True(targetUntagged.IsFavorite);
        Assert.Single(sut.MatchedTracks);
        Assert.Same(targetUntagged, sut.MatchedTracks[0]);
    }

    /// <summary>
    /// タグ編集直後で曲名やアルバム名が変わった場合でも、LastKnownFilePathフォールバックでお気に入りが維持されメタデータが最新化されることを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_タグ編集直後_パス一致フォールバックでお気に入り維持かつメタデータが更新されること()
    {
        // Arrange
        var entries = new List<FavoriteTrackEntry>
        {
            new("Old Title", "Old Artist", "Old Album", 1, 210.0, @"D:\Music\Song.flac", DateTime.Now)
        };
        var editedTrack = CreateTrack(@"D:\Music\Song.flac", "New Title", "New Artist", "New Album", trackNumber: 3, durationSeconds: 210.0);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, [editedTrack]);

        // Assert
        Assert.True(editedTrack.IsFavorite);
        Assert.True(sut.HasEntryChanges);
        Assert.Equal("New Title", sut.UpdatedEntries[0].Title);
        Assert.Equal("New Artist", sut.UpdatedEntries[0].Artist);
        Assert.Equal("New Album", sut.UpdatedEntries[0].Album);
        Assert.Equal(3u, sut.UpdatedEntries[0].TrackNumber);
    }

    /// <summary>
    /// 旧形式（パスのみ）エントリがライブラリ照合された際、メタデータが自動補完されることを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_旧形式パスのみエントリ_ライブラリ照合でメタデータが自動補完されること()
    {
        // Arrange
        var entries = new List<FavoriteTrackEntry>
        {
            FavoriteTrackEntry.FromLegacyFilePath(@"D:\Music\Legacy.mp3")
        };
        var track = CreateTrack(@"D:\Music\Legacy.mp3", "Legacy Song", "Legacy Artist", "Legacy Album", trackNumber: 4, durationSeconds: 195.4);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, [track]);

        // Assert
        Assert.True(track.IsFavorite);
        Assert.True(sut.HasEntryChanges);
        Assert.Equal("Legacy Song", sut.UpdatedEntries[0].Title);
        Assert.Equal("Legacy Artist", sut.UpdatedEntries[0].Artist);
        Assert.Equal("Legacy Album", sut.UpdatedEntries[0].Album);
        Assert.Equal(4u, sut.UpdatedEntries[0].TrackNumber);
        Assert.Equal(195.4, sut.UpdatedEntries[0].DurationSeconds);
    }

    /// <summary>
    /// ライブラリに含まれないお気に入りエントリがfallbackTrackLoader経由で補完解決されることを検証します。
    /// </summary>
    [Fact]
    public void Reconcile_ライブラリ外のお気に入りエントリ_フォールバックローダーで解決されること()
    {
        // Arrange
        var entries = new List<FavoriteTrackEntry>
        {
            new("Standalone", "Solo Artist", "Single", 1, 180.0, @"E:\External\Standalone.flac", DateTime.Now)
        };
        var loadedTrack = CreateTrack(@"E:\External\Standalone.flac", "Standalone", "Solo Artist", "Single", trackNumber: 1, durationSeconds: 180.0);

        // Act
        var sut = FavoriteTrackMatcher.Reconcile(entries, Array.Empty<Track>(), path => path == @"E:\External\Standalone.flac" ? loadedTrack : null);

        // Assert
        Assert.Single(sut.MatchedTracks);
        Assert.Same(loadedTrack, sut.MatchedTracks[0]);
        Assert.True(loadedTrack.IsFavorite);
    }

    /// <summary>
    /// 別フォルダに存在する同名・同再生時間のタグ未設定曲に対して、FindBestMatchingEntry・IsMatchForRemoval・AreSameEntryがいずれも不一致と判定することを検証します。
    /// </summary>
    [Fact]
    public void 未タグ曲判定_別フォルダの同名未タグ曲_FindBestMatchingEntryとIsMatchForRemovalとAreSameEntryが誤マッチしないこと()
    {
        // Arrange
        var folderBTrack = CreateTrack(@"D:\Music\FolderB\Track01.mp3", "Track01", "Unknown Artist", "Unknown Album", trackNumber: 0, durationSeconds: 200.0);
        var folderATrack = CreateTrack(@"D:\Music\FolderA\Track01.mp3", "Track01", "Unknown Artist", "Unknown Album", trackNumber: 0, durationSeconds: 200.0);
        var entryB = FavoriteTrackEntry.FromTrack(folderBTrack);
        var entryA = FavoriteTrackEntry.FromTrack(folderATrack);

        // Act
        var matchedEntry = FavoriteTrackMatcher.FindBestMatchingEntry(folderATrack, [entryB]);
        bool matchForRemoval = FavoriteTrackMatcher.IsMatchForRemoval(entryB, folderATrack);
        bool sameEntry = FavoriteTrackMatcher.AreSameEntry(entryB, entryA);

        // Assert
        Assert.Null(matchedEntry);
        Assert.False(matchForRemoval);
        Assert.False(sameEntry);
    }

    /// <summary>
    /// 再生時間許容誤差の境界値（5.0秒以内は一致、5.001秒は不一致）が正確に判定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(200.0, 205.0, true)]
    [InlineData(200.0, 195.0, true)]
    [InlineData(200.0, 205.001, false)]
    [InlineData(200.0, 194.999, false)]
    public void FindBestMatchingEntry_再生時間差の境界値_許容誤差5秒以内で一致し超過で不一致となること(
        double entryDurationSeconds,
        double trackDurationSeconds,
        bool expectedMatch)
    {
        // Arrange
        var entry = new FavoriteTrackEntry("Midnight City", "M83", "Hurry Up, We're Dreaming", 2, entryDurationSeconds, @"C:\Old\song.mp3", DateTime.Now);
        var movedTrack = CreateTrack(@"D:\New\song.flac", "Midnight City", "M83", "Hurry Up, We're Dreaming", trackNumber: 2, durationSeconds: trackDurationSeconds);

        // Act
        var actual = FavoriteTrackMatcher.FindBestMatchingEntry(movedTrack, [entry]);

        // Assert
        Assert.Equal(expectedMatch, actual != null);
    }
}
