using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// ライブラリ全体のリスニング統計（KPI サマリ・曲/アーティスト/アルバム TOP 10・日別/月別推移・時間帯/曜日別傾向）を集計するアプリケーションサービスです。
/// </summary>
public sealed class LibraryStatsApplicationService
{
    /// <summary>
    /// 各ランキングの既定表示件数（TOP 10）
    /// </summary>
    public const int DefaultTopCount = 10;

    private const int MaxDailyTrendBuckets = 31;
    private const int DefaultRecentDailyDays = 14;
    private const int DefaultRecentMonthlyMonths = 12;
    private const int MaxMonthlyTrendBuckets = 24;
    private const string UnknownArtistName = "Unknown Artist";
    private const string UnknownAlbumName = "Unknown Album";

    private static readonly (DayOfWeek Day, string ShortLabel, string FullLabel)[] OrderedDaysOfWeek =
    [
        (DayOfWeek.Monday, "月", "月曜日"),
        (DayOfWeek.Tuesday, "火", "火曜日"),
        (DayOfWeek.Wednesday, "水", "水曜日"),
        (DayOfWeek.Thursday, "木", "木曜日"),
        (DayOfWeek.Friday, "金", "金曜日"),
        (DayOfWeek.Saturday, "土", "土曜日"),
        (DayOfWeek.Sunday, "日", "日曜日"),
    ];

    private readonly IListeningStatsRepository _repository;
    private readonly Func<DateTime> _clock;

    /// <summary>
    /// <see cref="LibraryStatsApplicationService"/> を初期化します
    /// </summary>
    /// <param name="repository">リスニング統計リポジトリ</param>
    /// <param name="clock">現在時刻プロバイダ（省略時は <see cref="DateTime.Now"/>）</param>
    public LibraryStatsApplicationService(
        IListeningStatsRepository repository,
        Func<DateTime>? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>
    /// 指定された期間フィルタに対するライブラリ全体のリスニング統計を集計します
    /// </summary>
    /// <param name="periodFilter">期間フィルタ（今週／今月／今年／全期間）</param>
    /// <param name="topCount">各ランキングの最大件数（既定 10 件）</param>
    /// <param name="libraryTracks">最新メタデータ解決に利用するライブラリ楽曲一覧（任意）</param>
    /// <returns>集計結果スナップショット</returns>
    public LibraryStatsSnapshot GetStats(
        StatsPeriodFilter periodFilter,
        int topCount = DefaultTopCount,
        IReadOnlyCollection<Track>? libraryTracks = null)
    {
        DateTime now = _clock();
        StatsDateRange dateRange = StatsDateRange.FromPeriodFilter(periodFilter, now);
        return GetStatsForRangeCore(dateRange, topCount, libraryTracks, now);
    }

    /// <summary>
    /// 任意の期間範囲（<see cref="StatsDateRange"/>）に対するライブラリ全体のリスニング統計を集計します。
    /// 将来の特定週・月・年単位ランキング拡張にも対応する共通集計エントリポイントです。
    /// </summary>
    /// <param name="dateRange">集計対象期間（null 指定時は全期間）</param>
    /// <param name="topCount">各ランキングの最大件数（既定 10 件）</param>
    /// <param name="libraryTracks">最新メタデータ解決に利用するライブラリ楽曲一覧（任意）</param>
    /// <returns>集計結果スナップショット</returns>
    public LibraryStatsSnapshot GetStatsForRange(
        StatsDateRange? dateRange,
        int topCount = DefaultTopCount,
        IReadOnlyCollection<Track>? libraryTracks = null)
    {
        DateTime now = _clock();
        return GetStatsForRangeCore(dateRange ?? StatsDateRange.AllTime, topCount, libraryTracks, now);
    }

    private LibraryStatsSnapshot GetStatsForRangeCore(
        StatsDateRange dateRange,
        int topCount,
        IReadOnlyCollection<Track>? libraryTracks,
        DateTime now)
    {
        int effectiveTopCount = Math.Max(0, topCount);
        ListeningStatsData data = LoadDataSafe();
        Dictionary<string, Track> libraryTrackMap = BuildLibraryTrackMap(libraryTracks);

        List<NormalizedPlayRecord> records = data.PlayEvents
            .Where(ev => dateRange.Contains(ev.PlayedAt))
            .Select(ev => NormalizePlayEvent(ev, libraryTrackMap))
            .ToList();

        // 全期間かつ PlayEvents が未記録の旧データのみ存在する場合の安全なフォールバック
        if (records.Count == 0 && dateRange.IsUnbounded && data.PlayEvents.Count == 0)
        {
            List<TrackListeningStat> positiveStats = data.TrackStats
                .Where(stat => stat.PlayCount > 0)
                .ToList();
            if (positiveStats.Count > 0)
            {
                return BuildFallbackSnapshotFromTrackStats(dateRange, positiveStats, libraryTrackMap, effectiveTopCount, now);
            }
        }

        List<RankedTrackStat> topTracks = BuildTopTracks(records, effectiveTopCount);
        List<RankedArtistStat> topArtists = BuildTopArtists(records, effectiveTopCount);
        List<RankedAlbumStat> topAlbums = BuildTopAlbums(records, effectiveTopCount);
        LibraryStatsSummary summary = BuildSummary(records, dateRange, now, topArtists);
        List<ListeningTrendPoint> dailyTrends = BuildDailyTrends(records, dateRange, now);
        List<ListeningTrendPoint> monthlyTrends = BuildMonthlyTrends(records, dateRange, now);
        ListeningHabitDistribution habits = BuildHabits(records);

        return new LibraryStatsSnapshot(
            dateRange,
            summary,
            topTracks,
            topArtists,
            topAlbums,
            dailyTrends,
            monthlyTrends,
            habits);
    }

    private ListeningStatsData LoadDataSafe()
    {
        try
        {
            return _repository.Load();
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"ライブラリ全体リスニング統計の読み込みに失敗しました: {ex.Message}");
            return new ListeningStatsData();
        }
    }

    private static Dictionary<string, Track> BuildLibraryTrackMap(IReadOnlyCollection<Track>? libraryTracks)
    {
        var map = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        if (libraryTracks == null)
        {
            return map;
        }

        foreach (Track track in libraryTracks)
        {
            if (track != null && !string.IsNullOrWhiteSpace(track.FilePath))
            {
                map.TryAdd(track.FilePath, track);
            }
        }

        return map;
    }

    private static NormalizedPlayRecord NormalizePlayEvent(
        TrackPlayEvent ev,
        Dictionary<string, Track> libraryTrackMap)
    {
        libraryTrackMap.TryGetValue(ev.FilePath, out Track? libraryTrack);

        string title = ChooseNonEmpty(libraryTrack?.Title, ev.Title, Path.GetFileNameWithoutExtension(ev.FilePath));
        string artist = ChooseNonEmpty(libraryTrack?.Artist, ev.Artist, UnknownArtistName);
        string album = ChooseNonEmpty(libraryTrack?.Album, ev.Album, UnknownAlbumName);
        TimeSpan duration = ev.PlayedDuration > TimeSpan.Zero
            ? ev.PlayedDuration
            : (libraryTrack != null && libraryTrack.Duration > TimeSpan.Zero ? libraryTrack.Duration : TimeSpan.Zero);

        return new NormalizedPlayRecord(ev.PlayedAt, ev.FilePath, title, artist, album, duration);
    }

    private static LibraryStatsSnapshot BuildFallbackSnapshotFromTrackStats(
        StatsDateRange dateRange,
        List<TrackListeningStat> positiveStats,
        Dictionary<string, Track> libraryTrackMap,
        int topCount,
        DateTime now)
    {
        var normalizedStats = positiveStats.Select(stat =>
        {
            libraryTrackMap.TryGetValue(stat.FilePath, out Track? libraryTrack);
            string title = ChooseNonEmpty(libraryTrack?.Title, stat.Title, Path.GetFileNameWithoutExtension(stat.FilePath));
            string artist = ChooseNonEmpty(libraryTrack?.Artist, stat.Artist, UnknownArtistName);
            string album = ChooseNonEmpty(libraryTrack?.Album, stat.Album, UnknownAlbumName);
            return (stat.FilePath, Title: title, Artist: artist, Album: album, stat.PlayCount, stat.TotalListeningDuration, stat.LastPlayedAt);
        }).ToList();

        var orderedTracks = normalizedStats
            .OrderByDescending(x => x.PlayCount)
            .ThenByDescending(x => x.TotalListeningDuration)
            .ThenByDescending(x => x.LastPlayedAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .ToList();

        int maxTrackPlays = orderedTracks.Count > 0 ? orderedTracks[0].PlayCount : 0;
        var topTracks = orderedTracks
            .Select((item, idx) => new RankedTrackStat(
                idx + 1,
                item.FilePath,
                item.Title,
                item.Artist,
                item.Album,
                item.PlayCount,
                item.TotalListeningDuration,
                item.LastPlayedAt,
                CalculatePercent(item.PlayCount, maxTrackPlays)))
            .ToList();

        var artistGroups = normalizedStats
            .GroupBy(x => x.Artist, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var bestTrack = g
                    .OrderByDescending(x => x.PlayCount)
                    .ThenByDescending(x => x.TotalListeningDuration)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .First();
                return new
                {
                    ArtistName = bestTrack.Artist,
                    PlayCount = g.Sum(x => x.PlayCount),
                    TotalDuration = SumDurations(g.Select(x => x.TotalListeningDuration)),
                    UniqueTrackCount = g.Count(),
                    RepresentativeFilePath = bestTrack.FilePath,
                };
            })
            .OrderByDescending(x => x.PlayCount)
            .ThenByDescending(x => x.TotalDuration)
            .ThenBy(x => x.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int maxArtistPlays = artistGroups.Count > 0 ? artistGroups[0].PlayCount : 0;
        var topArtists = artistGroups
            .Take(topCount)
            .Select((item, idx) => new RankedArtistStat(
                idx + 1,
                item.ArtistName,
                item.PlayCount,
                item.TotalDuration,
                item.UniqueTrackCount,
                item.RepresentativeFilePath,
                CalculatePercent(item.PlayCount, maxArtistPlays)))
            .ToList();

        var albumGroups = normalizedStats
            .GroupBy(x => new AlbumGroupKey(x.Album, x.Artist))
            .Select(g =>
            {
                var bestTrack = g
                    .OrderByDescending(x => x.PlayCount)
                    .ThenByDescending(x => x.TotalListeningDuration)
                    .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                    .First();
                return new
                {
                    AlbumTitle = bestTrack.Album,
                    ArtistName = bestTrack.Artist,
                    PlayCount = g.Sum(x => x.PlayCount),
                    TotalDuration = SumDurations(g.Select(x => x.TotalListeningDuration)),
                    UniqueTrackCount = g.Count(),
                    RepresentativeFilePath = bestTrack.FilePath,
                };
            })
            .OrderByDescending(x => x.PlayCount)
            .ThenByDescending(x => x.TotalDuration)
            .ThenBy(x => x.AlbumTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int maxAlbumPlays = albumGroups.Count > 0 ? albumGroups[0].PlayCount : 0;
        var topAlbums = albumGroups
            .Take(topCount)
            .Select((item, idx) => new RankedAlbumStat(
                idx + 1,
                item.AlbumTitle,
                item.ArtistName,
                item.PlayCount,
                item.TotalDuration,
                item.UniqueTrackCount,
                item.RepresentativeFilePath,
                CalculatePercent(item.PlayCount, maxAlbumPlays)))
            .ToList();

        int totalPlays = normalizedStats.Sum(x => x.PlayCount);
        TimeSpan totalDuration = SumDurations(normalizedStats.Select(x => x.TotalListeningDuration));
        var validDates = normalizedStats
            .Where(x => x.LastPlayedAt != DateTime.MinValue)
            .Select(x => x.LastPlayedAt.Date)
            .ToList();
        int elapsedDays = validDates.Count > 0
            ? Math.Max(1, (MaxDate(now.Date, validDates.Max()) - validDates.Min()).Days + 1)
            : 1;

        var summary = new LibraryStatsSummary(
            totalPlays,
            totalPlays > 0 ? (double)totalPlays / elapsedDays : 0.0,
            totalDuration,
            normalizedStats.Count,
            artistGroups.Count,
            albumGroups.Count,
            topArtists.Count > 0 ? topArtists[0].ArtistName : string.Empty,
            topArtists.Count > 0 ? topArtists[0].PlayCount : 0);

        return new LibraryStatsSnapshot(
            dateRange,
            summary,
            topTracks,
            topArtists,
            topAlbums,
            BuildDailyTrends(Array.Empty<NormalizedPlayRecord>(), dateRange, now),
            BuildMonthlyTrends(Array.Empty<NormalizedPlayRecord>(), dateRange, now),
            BuildHabits(Array.Empty<NormalizedPlayRecord>()));
    }

    private static List<RankedTrackStat> BuildTopTracks(
        IReadOnlyList<NormalizedPlayRecord> records,
        int topCount)
    {
        if (records.Count == 0 || topCount <= 0)
        {
            return new List<RankedTrackStat>();
        }

        var grouped = records
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                NormalizedPlayRecord latest = g.OrderByDescending(r => r.PlayedAt).First();
                return new
                {
                    latest.FilePath,
                    latest.Title,
                    latest.Artist,
                    latest.Album,
                    PlayCount = g.Count(),
                    TotalDuration = SumDurations(g.Select(r => r.Duration)),
                    LastPlayedAt = latest.PlayedAt,
                };
            })
            .OrderByDescending(x => x.PlayCount)
            .ThenByDescending(x => x.TotalDuration)
            .ThenByDescending(x => x.LastPlayedAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .ToList();

        int maxPlays = grouped.Count > 0 ? grouped[0].PlayCount : 0;
        return grouped
            .Select((item, idx) => new RankedTrackStat(
                idx + 1,
                item.FilePath,
                item.Title,
                item.Artist,
                item.Album,
                item.PlayCount,
                item.TotalDuration,
                item.LastPlayedAt,
                CalculatePercent(item.PlayCount, maxPlays)))
            .ToList();
    }

    private static List<RankedArtistStat> BuildTopArtists(
        IReadOnlyList<NormalizedPlayRecord> records,
        int topCount)
    {
        if (records.Count == 0 || topCount <= 0)
        {
            return new List<RankedArtistStat>();
        }

        var grouped = records
            .GroupBy(r => r.Artist, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var repTrack = g
                    .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(tg => new
                    {
                        FilePath = tg.First().FilePath,
                        Count = tg.Count(),
                        Latest = tg.Max(r => r.PlayedAt),
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.Latest)
                    .ThenBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
                    .First();

                string displayArtist = g.OrderByDescending(r => r.PlayedAt).First().Artist;
                return new
                {
                    ArtistName = displayArtist,
                    PlayCount = g.Count(),
                    TotalDuration = SumDurations(g.Select(r => r.Duration)),
                    UniqueTrackCount = g.Select(r => r.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    RepresentativeFilePath = repTrack.FilePath,
                };
            })
            .OrderByDescending(x => x.PlayCount)
            .ThenByDescending(x => x.TotalDuration)
            .ThenBy(x => x.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .ToList();

        int maxPlays = grouped.Count > 0 ? grouped[0].PlayCount : 0;
        return grouped
            .Select((item, idx) => new RankedArtistStat(
                idx + 1,
                item.ArtistName,
                item.PlayCount,
                item.TotalDuration,
                item.UniqueTrackCount,
                item.RepresentativeFilePath,
                CalculatePercent(item.PlayCount, maxPlays)))
            .ToList();
    }

    private static List<RankedAlbumStat> BuildTopAlbums(
        IReadOnlyList<NormalizedPlayRecord> records,
        int topCount)
    {
        if (records.Count == 0 || topCount <= 0)
        {
            return new List<RankedAlbumStat>();
        }

        var grouped = records
            .GroupBy(r => new AlbumGroupKey(r.Album, r.Artist))
            .Select(g =>
            {
                var repTrack = g
                    .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(tg => new
                    {
                        FilePath = tg.First().FilePath,
                        Count = tg.Count(),
                        Latest = tg.Max(r => r.PlayedAt),
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.Latest)
                    .ThenBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
                    .First();

                NormalizedPlayRecord latest = g.OrderByDescending(r => r.PlayedAt).First();
                return new
                {
                    AlbumTitle = latest.Album,
                    ArtistName = latest.Artist,
                    PlayCount = g.Count(),
                    TotalDuration = SumDurations(g.Select(r => r.Duration)),
                    UniqueTrackCount = g.Select(r => r.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    RepresentativeFilePath = repTrack.FilePath,
                };
            })
            .OrderByDescending(x => x.PlayCount)
            .ThenByDescending(x => x.TotalDuration)
            .ThenBy(x => x.AlbumTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Take(topCount)
            .ToList();

        int maxPlays = grouped.Count > 0 ? grouped[0].PlayCount : 0;
        return grouped
            .Select((item, idx) => new RankedAlbumStat(
                idx + 1,
                item.AlbumTitle,
                item.ArtistName,
                item.PlayCount,
                item.TotalDuration,
                item.UniqueTrackCount,
                item.RepresentativeFilePath,
                CalculatePercent(item.PlayCount, maxPlays)))
            .ToList();
    }

    private static LibraryStatsSummary BuildSummary(
        IReadOnlyList<NormalizedPlayRecord> records,
        StatsDateRange dateRange,
        DateTime now,
        List<RankedArtistStat> topArtists)
    {
        if (records.Count == 0)
        {
            return LibraryStatsSummary.Empty;
        }

        int totalPlays = records.Count;
        TimeSpan totalDuration = SumDurations(records.Select(r => r.Duration));
        int uniqueTracks = records.Select(r => r.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int uniqueArtists = records.Select(r => r.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int uniqueAlbums = records.Select(r => new AlbumGroupKey(r.Album, r.Artist)).Distinct().Count();

        int elapsedDays = CalculateElapsedDays(records, dateRange, now);
        double dailyAverage = (double)totalPlays / elapsedDays;

        string topArtistName = topArtists.Count > 0 ? topArtists[0].ArtistName : string.Empty;
        int topArtistPlayCount = topArtists.Count > 0 ? topArtists[0].PlayCount : 0;

        return new LibraryStatsSummary(
            totalPlays,
            dailyAverage,
            totalDuration,
            uniqueTracks,
            uniqueArtists,
            uniqueAlbums,
            topArtistName,
            topArtistPlayCount);
    }

    private static int CalculateElapsedDays(
        IReadOnlyList<NormalizedPlayRecord> records,
        StatsDateRange dateRange,
        DateTime now)
    {
        DateTime todayEndExclusive = now.Date.AddDays(1);

        if (dateRange.StartInclusive.HasValue && dateRange.EndExclusive.HasValue)
        {
            DateTime effectiveEnd = dateRange.EndExclusive.Value < todayEndExclusive
                ? dateRange.EndExclusive.Value
                : todayEndExclusive;

            if (effectiveEnd <= dateRange.StartInclusive.Value)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Ceiling((effectiveEnd - dateRange.StartInclusive.Value).TotalDays));
        }

        DateTime earliestDate = dateRange.StartInclusive?.Date ?? records.Min(r => r.PlayedAt.Date);
        DateTime latestRecordDate = records.Max(r => r.PlayedAt.Date);
        DateTime latestDate = dateRange.EndExclusive.HasValue
            ? dateRange.EndExclusive.Value.AddTicks(-1).Date
            : MaxDate(now.Date, latestRecordDate);

        if (latestDate < earliestDate)
        {
            return 1;
        }

        return Math.Max(1, (latestDate - earliestDate).Days + 1);
    }

    private static List<ListeningTrendPoint> BuildDailyTrends(
        IReadOnlyList<NormalizedPlayRecord> records,
        StatsDateRange dateRange,
        DateTime now)
    {
        DateTime endDateInclusive;
        DateTime startDateInclusive;

        if (dateRange.StartInclusive.HasValue && dateRange.EndExclusive.HasValue)
        {
            DateTime rangeStart = dateRange.StartInclusive.Value.Date;
            DateTime rangeEnd = dateRange.EndExclusive.Value.AddTicks(-1).Date;
            int totalRangeDays = (rangeEnd - rangeStart).Days + 1;

            if (totalRangeDays <= MaxDailyTrendBuckets)
            {
                startDateInclusive = rangeStart;
                endDateInclusive = rangeEnd;
            }
            else
            {
                DateTime anchorEnd = now.Date < rangeEnd ? MaxDate(rangeStart, now.Date) : rangeEnd;
                endDateInclusive = anchorEnd;
                startDateInclusive = MaxDate(rangeStart, anchorEnd.AddDays(-(DefaultRecentDailyDays - 1)));
            }
        }
        else if (records.Count > 0)
        {
            DateTime latestRecordDate = records.Max(r => r.PlayedAt.Date);
            endDateInclusive = MaxDate(now.Date, latestRecordDate);
            DateTime earliestRecordDate = records.Min(r => r.PlayedAt.Date);
            DateTime windowStart = endDateInclusive.AddDays(-(DefaultRecentDailyDays - 1));
            startDateInclusive = earliestRecordDate > windowStart ? earliestRecordDate : windowStart;
        }
        else
        {
            endDateInclusive = now.Date;
            startDateInclusive = endDateInclusive.AddDays(-(DefaultRecentDailyDays - 1));
        }

        var bucketMap = records
            .GroupBy(r => r.PlayedAt.Date)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(), Duration: SumDurations(g.Select(r => r.Duration))));

        var rawBuckets = new List<(DateTime Date, int Count, TimeSpan Duration)>();
        for (DateTime day = startDateInclusive; day <= endDateInclusive; day = day.AddDays(1))
        {
            bucketMap.TryGetValue(day, out var metrics);
            rawBuckets.Add((day, metrics.Count, metrics.Duration));
        }

        int maxPlays = rawBuckets.Count > 0 ? rawBuckets.Max(b => b.Count) : 0;
        bool useShortDayLabel = rawBuckets.Count <= 7;

        return rawBuckets
            .Select(b =>
            {
                string label = useShortDayLabel
                    ? string.Format(CultureInfo.InvariantCulture, "{0:MM/dd}", b.Date)
                    : string.Format(CultureInfo.InvariantCulture, "{0:M/d}", b.Date);
                string detailLabel = string.Format(CultureInfo.InvariantCulture, "{0:yyyy/MM/dd}", b.Date);
                return new ListeningTrendPoint(
                    label,
                    detailLabel,
                    b.Date,
                    b.Count,
                    b.Duration,
                    CalculateRatio(b.Count, maxPlays));
            })
            .ToList();
    }

    private static List<ListeningTrendPoint> BuildMonthlyTrends(
        IReadOnlyList<NormalizedPlayRecord> records,
        StatsDateRange dateRange,
        DateTime now)
    {
        DateTime startMonth;
        DateTime endMonth;

        if (dateRange.StartInclusive.HasValue && dateRange.EndExclusive.HasValue)
        {
            startMonth = new DateTime(dateRange.StartInclusive.Value.Year, dateRange.StartInclusive.Value.Month, 1);
            DateTime rangeEnd = dateRange.EndExclusive.Value.AddTicks(-1);
            endMonth = new DateTime(rangeEnd.Year, rangeEnd.Month, 1);

            int monthsSpan = ((endMonth.Year - startMonth.Year) * 12) + (endMonth.Month - startMonth.Month) + 1;
            if (monthsSpan > MaxMonthlyTrendBuckets)
            {
                startMonth = endMonth.AddMonths(-(MaxMonthlyTrendBuckets - 1));
            }
        }
        else if (records.Count > 0)
        {
            DateTime latest = MaxDate(now.Date, records.Max(r => r.PlayedAt.Date));
            endMonth = new DateTime(latest.Year, latest.Month, 1);
            startMonth = endMonth.AddMonths(-(DefaultRecentMonthlyMonths - 1));
        }
        else
        {
            endMonth = new DateTime(now.Year, now.Month, 1);
            startMonth = endMonth.AddMonths(-(DefaultRecentMonthlyMonths - 1));
        }

        var bucketMap = records
            .GroupBy(r => new DateTime(r.PlayedAt.Year, r.PlayedAt.Month, 1))
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Count(), Duration: SumDurations(g.Select(r => r.Duration))));

        var rawBuckets = new List<(DateTime MonthStart, int Count, TimeSpan Duration)>();
        for (DateTime m = startMonth; m <= endMonth; m = m.AddMonths(1))
        {
            bucketMap.TryGetValue(m, out var metrics);
            rawBuckets.Add((m, metrics.Count, metrics.Duration));
        }

        int maxPlays = rawBuckets.Count > 0 ? rawBuckets.Max(b => b.Count) : 0;
        return rawBuckets
            .Select(b => new ListeningTrendPoint(
                string.Format(CultureInfo.InvariantCulture, "{0}月", b.MonthStart.Month),
                string.Format(CultureInfo.InvariantCulture, "{0:yyyy年M月}", b.MonthStart),
                b.MonthStart,
                b.Count,
                b.Duration,
                CalculateRatio(b.Count, maxPlays)))
            .ToList();
    }

    private static ListeningHabitDistribution BuildHabits(IReadOnlyList<NormalizedPlayRecord> records)
    {
        var hourlyCounts = new int[24];
        var hourlyDurations = new TimeSpan[24];
        var dowCounts = new int[7];
        var dowDurations = new TimeSpan[7];

        foreach (NormalizedPlayRecord record in records)
        {
            int hour = record.PlayedAt.Hour;
            hourlyCounts[hour]++;
            hourlyDurations[hour] += record.Duration;

            int dowIdx = ((int)record.PlayedAt.DayOfWeek + 6) % 7;
            dowCounts[dowIdx]++;
            dowDurations[dowIdx] += record.Duration;
        }

        int maxHourly = hourlyCounts.Max();
        int? peakHour = maxHourly > 0 ? Array.IndexOf(hourlyCounts, maxHourly) : null;
        var hourlyBuckets = new List<ListeningHabitBucket>(24);
        for (int hour = 0; hour < 24; hour++)
        {
            string label = hour.ToString(CultureInfo.InvariantCulture);
            string detail = string.Format(CultureInfo.InvariantCulture, "{0:00}:00〜{0:00}:59", hour);
            hourlyBuckets.Add(new ListeningHabitBucket(
                hour,
                label,
                detail,
                hourlyCounts[hour],
                hourlyDurations[hour],
                CalculateRatio(hourlyCounts[hour], maxHourly),
                peakHour.HasValue && peakHour.Value == hour));
        }

        int maxDow = dowCounts.Max();
        int peakDowIdx = maxDow > 0 ? Array.IndexOf(dowCounts, maxDow) : -1;
        DayOfWeek? peakDayOfWeek = peakDowIdx >= 0 ? OrderedDaysOfWeek[peakDowIdx].Day : null;
        var dowBuckets = new List<ListeningHabitBucket>(7);
        for (int i = 0; i < OrderedDaysOfWeek.Length; i++)
        {
            var (_, shortLabel, fullLabel) = OrderedDaysOfWeek[i];
            dowBuckets.Add(new ListeningHabitBucket(
                i,
                shortLabel,
                fullLabel,
                dowCounts[i],
                dowDurations[i],
                CalculateRatio(dowCounts[i], maxDow),
                peakDowIdx == i));
        }

        string hourlyInsight = peakHour.HasValue
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:00 台が最もよく聴かれています（{1:N0} 回）",
                peakHour.Value,
                maxHourly)
            : "再生データが蓄積されると、よく聴く時間帯の傾向が表示されます。";

        string dowInsight = peakDowIdx >= 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0}が最もよく聴かれています（{1:N0} 回）",
                OrderedDaysOfWeek[peakDowIdx].FullLabel,
                maxDow)
            : "再生データが蓄積されると、よく聴く曜日の傾向が表示されます。";

        return new ListeningHabitDistribution(
            hourlyBuckets,
            dowBuckets,
            peakHour,
            maxHourly,
            peakDayOfWeek,
            maxDow,
            hourlyInsight,
            dowInsight);
    }

    private static double CalculatePercent(int count, int maxCount) =>
        maxCount > 0 ? Math.Clamp(count * 100.0 / maxCount, 0.0, 100.0) : 0.0;

    private static double CalculateRatio(int count, int maxCount) =>
        maxCount > 0 ? Math.Clamp((double)count / maxCount, 0.0, 1.0) : 0.0;

    private static TimeSpan SumDurations(IEnumerable<TimeSpan> durations)
    {
        long ticks = 0;
        foreach (TimeSpan d in durations)
        {
            if (d > TimeSpan.Zero)
            {
                ticks += d.Ticks;
            }
        }

        return TimeSpan.FromTicks(ticks);
    }

    private static DateTime MaxDate(DateTime left, DateTime right) => left >= right ? left : right;

    private static string ChooseNonEmpty(string? preferred, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            return secondary.Trim();
        }

        return fallback;
    }

    private static bool IsStorageUnavailable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or TimeoutException or JsonException or OverflowException;

    private readonly record struct NormalizedPlayRecord(
        DateTime PlayedAt,
        string FilePath,
        string Title,
        string Artist,
        string Album,
        TimeSpan Duration);

    private readonly struct AlbumGroupKey : IEquatable<AlbumGroupKey>
    {
        public string Album { get; }

        public string Artist { get; }

        public AlbumGroupKey(string album, string artist)
        {
            Album = album ?? UnknownAlbumName;
            Artist = artist ?? UnknownArtistName;
        }

        public bool Equals(AlbumGroupKey other) =>
            string.Equals(Album, other.Album, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Artist, other.Artist, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is AlbumGroupKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = default(HashCode);
            hash.Add(Album, StringComparer.OrdinalIgnoreCase);
            hash.Add(Artist, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
