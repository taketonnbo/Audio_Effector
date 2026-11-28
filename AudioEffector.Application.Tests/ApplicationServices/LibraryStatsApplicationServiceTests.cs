using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Application.Tests.ApplicationServices;

/// <summary>
/// <see cref="LibraryStatsApplicationService"/> の期間別集計・TOP 10 抽出・推移/傾向分析・エラーフォールバックを検証するテストクラス
/// </summary>
public sealed class LibraryStatsApplicationServiceTests
{
    private sealed class InMemoryListeningStatsRepository : IListeningStatsRepository
    {
        private ListeningStatsData _data;

        public InMemoryListeningStatsRepository(ListeningStatsData? initialData = null)
        {
            _data = initialData?.Clone() ?? new ListeningStatsData();
        }

        public ListeningStatsData Load() => _data.Clone();

        public void Save(ListeningStatsData data) => _data = data.Clone();

        public ListeningStatsData Update(Action<ListeningStatsData> update)
        {
            ListeningStatsData working = _data.Clone();
            update(working);
            _data = working.Clone();
            return _data.Clone();
        }
    }

    private sealed class ThrowingListeningStatsRepository : IListeningStatsRepository
    {
        public ListeningStatsData Load() => throw new IOException("disk unavailable");

        public void Save(ListeningStatsData data) => throw new IOException("disk unavailable");

        public ListeningStatsData Update(Action<ListeningStatsData> update) => throw new IOException("disk unavailable");
    }

    /// <summary>
    /// 期間フィルタ（今週／今月／今年／全期間）および任意期間指定で日時毎再生履歴が正しく絞り込み集計されることを検証します。
    /// </summary>
    [Fact]
    public void GetStats_期間フィルタおよび任意期間指定時_対象期間内のイベントのみ集計すること()
    {
        // Arrange（基準日: 2026/05/13 水曜）
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        var data = new ListeningStatsData();
        // 今週（5/11〜5/17）: 2回
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 12, 21, 10, 0), @"C:\music\a.mp3", "Song A", "Artist 1", "Album 1", TimeSpan.FromMinutes(3)));
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 13, 21, 20, 0), @"C:\music\a.mp3", "Song A", "Artist 1", "Album 1", TimeSpan.FromMinutes(3)));
        // 今月（5/1〜5/10）: 1回
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 2, 10, 0, 0), @"C:\music\b.mp3", "Song B", "Artist 2", "Album 2", TimeSpan.FromMinutes(4)));
        // 今年（2月）: 1回
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 2, 15, 8, 0, 0), @"C:\music\c.mp3", "Song C", "Artist 3", "Album 3", TimeSpan.FromMinutes(5)));
        // 昨年（2025年）: 1回
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2025, 11, 20, 22, 0, 0), @"C:\music\d.mp3", "Song D", "Artist 4", "Album 4", TimeSpan.FromMinutes(6)));

        var repo = new InMemoryListeningStatsRepository(data);
        var sut = new LibraryStatsApplicationService(repo, () => now);

        // Act
        LibraryStatsSnapshot week = sut.GetStats(StatsPeriodFilter.ThisWeek);
        LibraryStatsSnapshot month = sut.GetStats(StatsPeriodFilter.ThisMonth);
        LibraryStatsSnapshot year = sut.GetStats(StatsPeriodFilter.ThisYear);
        LibraryStatsSnapshot allTime = sut.GetStats(StatsPeriodFilter.AllTime);
        LibraryStatsSnapshot feb2026 = sut.GetStatsForRange(StatsDateRange.ForSpecificMonth(2026, 2));

        // Assert
        Assert.Equal(2, week.Summary.TotalPlayCount);
        Assert.Equal(TimeSpan.FromMinutes(6), week.Summary.TotalListeningDuration);
        Assert.Equal(1, week.Summary.UniqueTrackCount);

        Assert.Equal(3, month.Summary.TotalPlayCount);
        Assert.Equal(2, month.Summary.UniqueTrackCount);

        Assert.Equal(4, year.Summary.TotalPlayCount);
        Assert.Equal(3, year.Summary.UniqueTrackCount);

        Assert.Equal(5, allTime.Summary.TotalPlayCount);
        Assert.Equal(4, allTime.Summary.UniqueTrackCount);
        Assert.Equal(4, allTime.Summary.UniqueArtistCount);
        Assert.Equal(4, allTime.Summary.UniqueAlbumCount);
        Assert.Equal("Artist 1", allTime.Summary.TopArtistName);
        Assert.Equal(2, allTime.Summary.TopArtistPlayCount);

        Assert.Equal(1, feb2026.Summary.TotalPlayCount);
        Assert.Equal("Song C", feb2026.TopTracks[0].Title);
    }

    /// <summary>
    /// 12 曲以上のデータがある場合に既定で TOP 10 のみが再生回数降順および同率時の安定順序で抽出されることを検証します。
    /// </summary>
    [Fact]
    public void GetStats_12曲存在する場合_既定TOP10が再生回数降順と同率安定順序で抽出されること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 12, 0, 0);
        var data = new ListeningStatsData();
        for (int i = 1; i <= 12; i++)
        {
            int plays = i <= 2 ? 5 : (13 - i); // Track 1 と Track 2 は同率 5 回、再生時間で Track 2 が上
            TimeSpan duration = i == 2 ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(3);
            for (int p = 0; p < plays; p++)
            {
                data.PlayEvents.Add(new TrackPlayEvent(
                    now.AddMinutes(-(i * 10) - p),
                    $@"C:\music\track_{i:00}.mp3",
                    $"Track {i:00}",
                    $"Artist {i:00}",
                    $"Album {i:00}",
                    duration));
            }
        }

        var sut = new LibraryStatsApplicationService(new InMemoryListeningStatsRepository(data), () => now);

        // Act
        LibraryStatsSnapshot snapshot = sut.GetStats(StatsPeriodFilter.AllTime);

        // Assert
        Assert.Equal(10, snapshot.TopTracks.Count);
        Assert.Equal(10, snapshot.TopArtists.Count);
        Assert.Equal(10, snapshot.TopAlbums.Count);
        Assert.Equal("Track 03", snapshot.TopTracks[0].Title);
        Assert.Equal(10, snapshot.TopTracks[0].PlayCount);
        Assert.Equal(100.0, snapshot.TopTracks[0].RatioPercent, precision: 2);

        // 同率 5 回同士（Track 02: 25分 vs Track 01: 15分）では累計時間が長い Track 02 が先
        int idxTrack02 = snapshot.TopTracks.ToList().FindIndex(t => t.Title == "Track 02");
        int idxTrack01 = snapshot.TopTracks.ToList().FindIndex(t => t.Title == "Track 01");
        Assert.True(idxTrack02 >= 0 && idxTrack01 > idxTrack02);
    }

    /// <summary>
    /// 日別・月別推移および時間帯別・曜日別傾向とピークインサイトが正しく集計されることを検証します。
    /// </summary>
    [Fact]
    public void GetStats_複数日時イベント存在時_日別月別推移と時間帯曜日別ピークインサイトを正しく算出すること()
    {
        // Arrange（2026/05/16 は土曜日）
        var now = new DateTime(2026, 5, 16, 23, 0, 0);
        var data = new ListeningStatsData();
        // 土曜 21時台に 3 回
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 16, 21, 5, 0), @"C:\m\1.mp3", "T1", "A1", "Al1", TimeSpan.FromMinutes(3)));
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 16, 21, 20, 0), @"C:\m\1.mp3", "T1", "A1", "Al1", TimeSpan.FromMinutes(3)));
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 16, 21, 45, 0), @"C:\m\2.mp3", "T2", "A1", "Al1", TimeSpan.FromMinutes(4)));
        // 水曜 9時台に 1 回
        data.PlayEvents.Add(new TrackPlayEvent(new DateTime(2026, 5, 13, 9, 10, 0), @"C:\m\2.mp3", "T2", "A1", "Al1", TimeSpan.FromMinutes(4)));

        var sut = new LibraryStatsApplicationService(new InMemoryListeningStatsRepository(data), () => now);

        // Act
        LibraryStatsSnapshot week = sut.GetStats(StatsPeriodFilter.ThisWeek);
        LibraryStatsSnapshot year = sut.GetStats(StatsPeriodFilter.ThisYear);

        // Assert
        Assert.Equal(7, week.DailyTrends.Count);
        Assert.Equal(3, week.DailyTrends.Single(d => d.BucketStart == new DateTime(2026, 5, 16)).PlayCount);
        Assert.Equal(1, week.DailyTrends.Single(d => d.BucketStart == new DateTime(2026, 5, 13)).PlayCount);

        Assert.Equal(12, year.MonthlyTrends.Count);
        Assert.Equal(4, year.MonthlyTrends.Single(m => m.BucketStart == new DateTime(2026, 5, 1)).PlayCount);

        Assert.Equal(24, week.Habits.HourlyBuckets.Count);
        Assert.Equal(7, week.Habits.DayOfWeekBuckets.Count);
        Assert.Equal(21, week.Habits.PeakHour);
        Assert.Equal(3, week.Habits.PeakHourPlayCount);
        Assert.True(week.Habits.HourlyBuckets[21].IsPeak);
        Assert.Contains("21:00 台", week.Habits.HourlyPeakInsight);

        Assert.Equal(DayOfWeek.Saturday, week.Habits.PeakDayOfWeek);
        Assert.Equal(3, week.Habits.PeakDayOfWeekPlayCount);
        Assert.Contains("土曜日", week.Habits.DayOfWeekPeakInsight);
    }

    /// <summary>
    /// ライブラリ楽曲一覧が渡された場合にファイルパス突き合わせで最新メタデータが優先解決されることを検証します。
    /// </summary>
    [Fact]
    public void GetStats_ライブラリ実体指定時_最新メタデータへ解決して集計すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 12, 0, 0);
        var data = new ListeningStatsData();
        data.PlayEvents.Add(new TrackPlayEvent(now.AddHours(-1), @"C:\music\song.flac", "Old Title", "Old Artist", "Old Album", TimeSpan.FromMinutes(4)));

        var libraryTracks = new List<Track>
        {
            new Track
            {
                FilePath = @"C:\music\song.flac",
                Title = "Updated Title",
                Artist = "Updated Artist",
                Album = "Updated Album",
                Duration = TimeSpan.FromMinutes(4),
            },
        };

        var sut = new LibraryStatsApplicationService(new InMemoryListeningStatsRepository(data), () => now);

        // Act
        LibraryStatsSnapshot snapshot = sut.GetStats(StatsPeriodFilter.AllTime, libraryTracks: libraryTracks);

        // Assert
        Assert.Single(snapshot.TopTracks);
        Assert.Equal("Updated Title", snapshot.TopTracks[0].Title);
        Assert.Equal("Updated Artist", snapshot.TopTracks[0].Artist);
        Assert.Equal("Updated Album", snapshot.TopTracks[0].Album);
        Assert.Equal("Updated Artist", snapshot.TopArtists[0].ArtistName);
        Assert.Equal("Updated Album", snapshot.TopAlbums[0].AlbumTitle);
    }

    /// <summary>
    /// 再生データ 0 件時およびリポジトリ読み込み例外時にクラッシュせず空スナップショットを返すことを検証します。
    /// </summary>
    [Fact]
    public void GetStats_データ0件およびストレージ例外時_クラッシュせず空スナップショットを返すこと()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 12, 0, 0);
        var emptySut = new LibraryStatsApplicationService(new InMemoryListeningStatsRepository(), () => now);
        var errorSut = new LibraryStatsApplicationService(new ThrowingListeningStatsRepository(), () => now);

        // Act
        LibraryStatsSnapshot emptySnapshot = emptySut.GetStats(StatsPeriodFilter.AllTime);
        LibraryStatsSnapshot errorSnapshot = errorSut.GetStats(StatsPeriodFilter.ThisMonth);

        // Assert
        Assert.False(emptySnapshot.HasData);
        Assert.Equal(0, emptySnapshot.Summary.TotalPlayCount);
        Assert.Empty(emptySnapshot.TopTracks);
        Assert.Equal(24, emptySnapshot.Habits.HourlyBuckets.Count);
        Assert.Equal(7, emptySnapshot.Habits.DayOfWeekBuckets.Count);
        Assert.Null(emptySnapshot.Habits.PeakHour);
        Assert.Null(emptySnapshot.Habits.PeakDayOfWeek);

        Assert.False(errorSnapshot.HasData);
        Assert.Equal(0, errorSnapshot.Summary.TotalPlayCount);
        Assert.Empty(errorSnapshot.TopTracks);
    }
}
