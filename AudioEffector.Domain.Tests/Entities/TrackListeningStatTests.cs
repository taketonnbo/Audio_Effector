using System;
using AudioEffector.Domain.Entities;
using Xunit;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// <see cref="TrackListeningStat"/>・<see cref="TrackPlayEvent"/>・<see cref="ListeningStatsData"/> のドメイン振る舞いを検証するテストクラス
/// </summary>
public sealed class TrackListeningStatTests
{
    /// <summary>
    /// 再生完了記録を複数回呼び出した際、累計再生回数・累計再生時間・最終再生日時・最近再生日時が正しく更新されることを検証します。
    /// </summary>
    [Fact]
    public void RecordPlaybackCompleted_複数回呼び出した場合_累計統計と最終再生日時が加算更新されること()
    {
        // Arrange
        var track = new Track
        {
            FilePath = @"C:\Music\ArtistA\Song1.flac",
            Title = "Song 1",
            Artist = "Artist A",
            Album = "Album A",
            Duration = TimeSpan.FromSeconds(180),
        };
        var sut = TrackListeningStat.FromTrack(track);
        var firstPlayedAt = new DateTime(2026, 11, 29, 10, 0, 0);
        var secondPlayedAt = new DateTime(2026, 11, 29, 11, 30, 0);

        // Act
        sut.RecordPlaybackCompleted(track, firstPlayedAt, TimeSpan.FromSeconds(180));
        sut.RecordPlaybackCompleted(track, secondPlayedAt, TimeSpan.FromSeconds(180));

        // Assert
        Assert.Equal(2, sut.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(360), sut.TotalListeningDuration);
        Assert.Equal(secondPlayedAt, sut.LastPlayedAt);
        Assert.Equal(secondPlayedAt, sut.RecentPlayedAt);
    }

    /// <summary>
    /// 最近再生日時のみを記録した場合、累計再生回数や累計再生時間に影響を与えずに RecentPlayedAt だけ更新されることを検証します。
    /// </summary>
    [Fact]
    public void RecordRecentPlayed_呼び出した場合_累計統計を変更せずRecentPlayedAtのみ更新されること()
    {
        // Arrange
        var track = new Track
        {
            FilePath = @"C:\Music\ArtistA\Song2.flac",
            Title = "Song 2",
            Artist = "Artist A",
            Album = "Album A",
            Duration = TimeSpan.FromSeconds(210),
        };
        var sut = TrackListeningStat.FromTrack(track);
        var playedAt = new DateTime(2026, 11, 29, 12, 0, 0);

        // Act
        sut.RecordRecentPlayed(track, playedAt);

        // Assert
        Assert.Equal(0, sut.PlayCount);
        Assert.Equal(TimeSpan.Zero, sut.TotalListeningDuration);
        Assert.Equal(DateTime.MinValue, sut.LastPlayedAt);
        Assert.Equal(playedAt, sut.RecentPlayedAt);
        RecentTrackEntry? recentEntry = sut.ToRecentTrackEntry();
        Assert.NotNull(recentEntry);
        Assert.Equal(playedAt, recentEntry!.PlayedAt);
    }

    /// <summary>
    /// ListeningStatsData の FindTrackStat が大文字小文字を区別せず一致し、Clone が独立コピーを生成することを検証します。
    /// </summary>
    [Fact]
    public void ListeningStatsData_FindTrackStatおよびClone_大文字小文字非区別検索と独立複製が行われること()
    {
        // Arrange
        var track = new Track
        {
            FilePath = @"C:\Music\CaseTest.flac",
            Title = "CaseTest",
            Artist = "Artist",
            Album = "Album",
            Duration = TimeSpan.FromSeconds(200),
        };
        var stat = TrackListeningStat.FromTrack(track);
        var playedAt = new DateTime(2026, 11, 29, 13, 0, 0);
        stat.RecordPlaybackCompleted(track, playedAt, track.Duration);
        var playEvent = TrackPlayEvent.FromTrack(track, playedAt);
        var sut = new ListeningStatsData([stat], [playEvent]);

        // Act
        TrackListeningStat? found = sut.FindTrackStat(@"c:\music\casetest.flac");
        ListeningStatsData cloned = sut.Clone();
        cloned.TrackStats[0].PlayCount = 99;

        // Assert
        Assert.NotNull(found);
        Assert.Equal(1, found!.PlayCount);
        Assert.Equal(1, sut.TrackStats[0].PlayCount);
        Assert.Single(cloned.PlayEvents);
        Assert.Equal(playedAt, cloned.PlayEvents[0].PlayedAt);
    }
}
