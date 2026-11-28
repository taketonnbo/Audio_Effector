using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// ライブラリ全体のリスニング統計 KPI サマリモデル
/// </summary>
public sealed class LibraryStatsSummary
{
    /// <summary>
    /// 空のサマリインスタンス
    /// </summary>
    public static LibraryStatsSummary Empty { get; } = new(0, 0.0, TimeSpan.Zero, 0, 0, 0, string.Empty, 0);

    /// <summary>
    /// 対象期間内の総再生回数
    /// </summary>
    public int TotalPlayCount { get; }

    /// <summary>
    /// 対象期間内の日平均再生回数
    /// </summary>
    public double DailyAveragePlayCount { get; }

    /// <summary>
    /// 対象期間内の累計再生時間
    /// </summary>
    public TimeSpan TotalListeningDuration { get; }

    /// <summary>
    /// 対象期間内に再生されたユニーク曲数
    /// </summary>
    public int UniqueTrackCount { get; }

    /// <summary>
    /// 対象期間内に再生されたユニークアーティスト数
    /// </summary>
    public int UniqueArtistCount { get; }

    /// <summary>
    /// 対象期間内に再生されたユニークアルバム数
    /// </summary>
    public int UniqueAlbumCount { get; }

    /// <summary>
    /// 対象期間内で最も再生回数が多いトップアーティスト名（データなし時は空文字）
    /// </summary>
    public string TopArtistName { get; }

    /// <summary>
    /// トップアーティストの再生回数
    /// </summary>
    public int TopArtistPlayCount { get; }

    /// <summary>
    /// <see cref="LibraryStatsSummary"/> を初期化します
    /// </summary>
    /// <param name="totalPlayCount">総再生回数</param>
    /// <param name="dailyAveragePlayCount">日平均再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="uniqueTrackCount">ユニーク再生曲数</param>
    /// <param name="uniqueArtistCount">ユニーク再生アーティスト数</param>
    /// <param name="uniqueAlbumCount">ユニーク再生アルバム数</param>
    /// <param name="topArtistName">トップアーティスト名</param>
    /// <param name="topArtistPlayCount">トップアーティスト再生回数</param>
    public LibraryStatsSummary(
        int totalPlayCount,
        double dailyAveragePlayCount,
        TimeSpan totalListeningDuration,
        int uniqueTrackCount,
        int uniqueArtistCount,
        int uniqueAlbumCount,
        string topArtistName,
        int topArtistPlayCount)
    {
        TotalPlayCount = Math.Max(0, totalPlayCount);
        DailyAveragePlayCount = double.IsNaN(dailyAveragePlayCount) || double.IsInfinity(dailyAveragePlayCount) || dailyAveragePlayCount < 0
            ? 0.0
            : dailyAveragePlayCount;
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        UniqueTrackCount = Math.Max(0, uniqueTrackCount);
        UniqueArtistCount = Math.Max(0, uniqueArtistCount);
        UniqueAlbumCount = Math.Max(0, uniqueAlbumCount);
        TopArtistName = topArtistName ?? string.Empty;
        TopArtistPlayCount = Math.Max(0, topArtistPlayCount);
    }
}

/// <summary>
/// よく聴く曲ランキングの 1 項目を表す集計モデル
/// </summary>
public sealed class RankedTrackStat
{
    /// <summary>順位（1 始まり）</summary>
    public int Rank { get; }

    /// <summary>音声ファイルパス</summary>
    public string FilePath { get; }

    /// <summary>楽曲タイトル</summary>
    public string Title { get; }

    /// <summary>アーティスト名</summary>
    public string Artist { get; }

    /// <summary>アルバム名</summary>
    public string Album { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>累計再生時間</summary>
    public TimeSpan TotalListeningDuration { get; }

    /// <summary>対象期間内の最終再生日時</summary>
    public DateTime LastPlayedAt { get; }

    /// <summary>ランキング 1 位に対する再生回数比率（0〜100）</summary>
    public double RatioPercent { get; }

    /// <summary>
    /// <see cref="RankedTrackStat"/> を初期化します
    /// </summary>
    /// <param name="rank">順位（1 始まり）</param>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="title">楽曲タイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="playCount">再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="lastPlayedAt">最終再生日時</param>
    /// <param name="ratioPercent">1 位対比の再生回数比率（0〜100）</param>
    public RankedTrackStat(
        int rank,
        string filePath,
        string title,
        string artist,
        string album,
        int playCount,
        TimeSpan totalListeningDuration,
        DateTime lastPlayedAt,
        double ratioPercent)
    {
        Rank = Math.Max(1, rank);
        FilePath = filePath ?? string.Empty;
        Title = title ?? string.Empty;
        Artist = artist ?? string.Empty;
        Album = album ?? string.Empty;
        PlayCount = Math.Max(0, playCount);
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        LastPlayedAt = lastPlayedAt;
        RatioPercent = Math.Clamp(ratioPercent, 0.0, 100.0);
    }
}

/// <summary>
/// よく聴くアーティストランキングの 1 項目を表す集計モデル
/// </summary>
public sealed class RankedArtistStat
{
    /// <summary>順位（1 始まり）</summary>
    public int Rank { get; }

    /// <summary>アーティスト名</summary>
    public string ArtistName { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>累計再生時間</summary>
    public TimeSpan TotalListeningDuration { get; }

    /// <summary>再生されたユニーク曲数</summary>
    public int UniqueTrackCount { get; }

    /// <summary>アートワーク表示用の代表楽曲ファイルパス</summary>
    public string? RepresentativeFilePath { get; }

    /// <summary>ランキング 1 位に対する再生回数比率（0〜100）</summary>
    public double RatioPercent { get; }

    /// <summary>
    /// <see cref="RankedArtistStat"/> を初期化します
    /// </summary>
    /// <param name="rank">順位（1 始まり）</param>
    /// <param name="artistName">アーティスト名</param>
    /// <param name="playCount">再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="uniqueTrackCount">ユニーク再生曲数</param>
    /// <param name="representativeFilePath">代表楽曲ファイルパス</param>
    /// <param name="ratioPercent">1 位対比の再生回数比率（0〜100）</param>
    public RankedArtistStat(
        int rank,
        string artistName,
        int playCount,
        TimeSpan totalListeningDuration,
        int uniqueTrackCount,
        string? representativeFilePath,
        double ratioPercent)
    {
        Rank = Math.Max(1, rank);
        ArtistName = artistName ?? string.Empty;
        PlayCount = Math.Max(0, playCount);
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        UniqueTrackCount = Math.Max(0, uniqueTrackCount);
        RepresentativeFilePath = representativeFilePath;
        RatioPercent = Math.Clamp(ratioPercent, 0.0, 100.0);
    }
}

/// <summary>
/// よく聴くアルバムランキングの 1 項目を表す集計モデル
/// </summary>
public sealed class RankedAlbumStat
{
    /// <summary>順位（1 始まり）</summary>
    public int Rank { get; }

    /// <summary>アルバムタイトル</summary>
    public string AlbumTitle { get; }

    /// <summary>アーティスト名</summary>
    public string ArtistName { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>累計再生時間</summary>
    public TimeSpan TotalListeningDuration { get; }

    /// <summary>再生されたユニーク曲数</summary>
    public int UniqueTrackCount { get; }

    /// <summary>カバーアート表示用の代表楽曲ファイルパス</summary>
    public string? RepresentativeFilePath { get; }

    /// <summary>ランキング 1 位に対する再生回数比率（0〜100）</summary>
    public double RatioPercent { get; }

    /// <summary>
    /// <see cref="RankedAlbumStat"/> を初期化します
    /// </summary>
    /// <param name="rank">順位（1 始まり）</param>
    /// <param name="albumTitle">アルバムタイトル</param>
    /// <param name="artistName">アーティスト名</param>
    /// <param name="playCount">再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="uniqueTrackCount">ユニーク再生曲数</param>
    /// <param name="representativeFilePath">代表楽曲ファイルパス</param>
    /// <param name="ratioPercent">1 位対比の再生回数比率（0〜100）</param>
    public RankedAlbumStat(
        int rank,
        string albumTitle,
        string artistName,
        int playCount,
        TimeSpan totalListeningDuration,
        int uniqueTrackCount,
        string? representativeFilePath,
        double ratioPercent)
    {
        Rank = Math.Max(1, rank);
        AlbumTitle = albumTitle ?? string.Empty;
        ArtistName = artistName ?? string.Empty;
        PlayCount = Math.Max(0, playCount);
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        UniqueTrackCount = Math.Max(0, uniqueTrackCount);
        RepresentativeFilePath = representativeFilePath;
        RatioPercent = Math.Clamp(ratioPercent, 0.0, 100.0);
    }
}

/// <summary>
/// 日別・月別再生推移グラフの 1 データ点を表す集計モデル
/// </summary>
public sealed class ListeningTrendPoint
{
    /// <summary>横軸表示ラベル（例: "05/01", "5月"）</summary>
    public string Label { get; }

    /// <summary>ツールチップ等に使用する詳細ラベル（例: "2026/05/01"）</summary>
    public string DetailLabel { get; }

    /// <summary>バケット開始日時</summary>
    public DateTime BucketStart { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>累計再生時間</summary>
    public TimeSpan TotalListeningDuration { get; }

    /// <summary>最大再生回数に対するバー高さ比率（0.0〜1.0）</summary>
    public double HeightRatio { get; }

    /// <summary>
    /// <see cref="ListeningTrendPoint"/> を初期化します
    /// </summary>
    /// <param name="label">横軸表示ラベル</param>
    /// <param name="detailLabel">詳細ラベル</param>
    /// <param name="bucketStart">バケット開始日時</param>
    /// <param name="playCount">再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="heightRatio">最大値対比比率（0.0〜1.0）</param>
    public ListeningTrendPoint(
        string label,
        string detailLabel,
        DateTime bucketStart,
        int playCount,
        TimeSpan totalListeningDuration,
        double heightRatio)
    {
        Label = label ?? string.Empty;
        DetailLabel = detailLabel ?? string.Empty;
        BucketStart = bucketStart;
        PlayCount = Math.Max(0, playCount);
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        HeightRatio = Math.Clamp(heightRatio, 0.0, 1.0);
    }
}

/// <summary>
/// 時間帯別・曜日別のリスニング傾向グラフの 1 バケットを表す集計モデル
/// </summary>
public sealed class ListeningHabitBucket
{
    /// <summary>バケットキー（時間帯は 0〜23、曜日は月曜=0〜日曜=6）</summary>
    public int Key { get; }

    /// <summary>横軸表示ラベル</summary>
    public string Label { get; }

    /// <summary>詳細表示ラベル</summary>
    public string DetailLabel { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>累計再生時間</summary>
    public TimeSpan TotalListeningDuration { get; }

    /// <summary>最大再生回数に対するバー高さ比率（0.0〜1.0）</summary>
    public double HeightRatio { get; }

    /// <summary>最も再生回数が多いピークバケットかどうか</summary>
    public bool IsPeak { get; }

    /// <summary>
    /// <see cref="ListeningHabitBucket"/> を初期化します
    /// </summary>
    /// <param name="key">バケットキー</param>
    /// <param name="label">横軸表示ラベル</param>
    /// <param name="detailLabel">詳細ラベル</param>
    /// <param name="playCount">再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="heightRatio">最大値対比比率（0.0〜1.0）</param>
    /// <param name="isPeak">ピークバケットかどうか</param>
    public ListeningHabitBucket(
        int key,
        string label,
        string detailLabel,
        int playCount,
        TimeSpan totalListeningDuration,
        double heightRatio,
        bool isPeak)
    {
        Key = key;
        Label = label ?? string.Empty;
        DetailLabel = detailLabel ?? string.Empty;
        PlayCount = Math.Max(0, playCount);
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        HeightRatio = Math.Clamp(heightRatio, 0.0, 1.0);
        IsPeak = isPeak;
    }
}

/// <summary>
/// 時間帯別および曜日別のリスニング傾向分布とピークインサイトを表す集計モデル
/// </summary>
public sealed class ListeningHabitDistribution
{
    /// <summary>時間帯別（0時〜23時）の分布バケット一覧</summary>
    public IReadOnlyList<ListeningHabitBucket> HourlyBuckets { get; }

    /// <summary>曜日別（月曜〜日曜）の分布バケット一覧</summary>
    public IReadOnlyList<ListeningHabitBucket> DayOfWeekBuckets { get; }

    /// <summary>最も再生回数が多いピーク時間帯（0〜23、データなし時は null）</summary>
    public int? PeakHour { get; }

    /// <summary>ピーク時間帯の再生回数</summary>
    public int PeakHourPlayCount { get; }

    /// <summary>最も再生回数が多いピーク曜日（データなし時は null）</summary>
    public DayOfWeek? PeakDayOfWeek { get; }

    /// <summary>ピーク曜日の再生回数</summary>
    public int PeakDayOfWeekPlayCount { get; }

    /// <summary>時間帯別傾向のピークインサイト文言</summary>
    public string HourlyPeakInsight { get; }

    /// <summary>曜日別傾向のピークインサイト文言</summary>
    public string DayOfWeekPeakInsight { get; }

    /// <summary>
    /// <see cref="ListeningHabitDistribution"/> を初期化します
    /// </summary>
    /// <param name="hourlyBuckets">時間帯別バケット一覧</param>
    /// <param name="dayOfWeekBuckets">曜日別バケット一覧</param>
    /// <param name="peakHour">ピーク時間帯</param>
    /// <param name="peakHourPlayCount">ピーク時間帯の再生回数</param>
    /// <param name="peakDayOfWeek">ピーク曜日</param>
    /// <param name="peakDayOfWeekPlayCount">ピーク曜日の再生回数</param>
    /// <param name="hourlyPeakInsight">時間帯別インサイト文言</param>
    /// <param name="dayOfWeekPeakInsight">曜日別インサイト文言</param>
    public ListeningHabitDistribution(
        IEnumerable<ListeningHabitBucket> hourlyBuckets,
        IEnumerable<ListeningHabitBucket> dayOfWeekBuckets,
        int? peakHour,
        int peakHourPlayCount,
        DayOfWeek? peakDayOfWeek,
        int peakDayOfWeekPlayCount,
        string hourlyPeakInsight,
        string dayOfWeekPeakInsight)
    {
        HourlyBuckets = hourlyBuckets?.ToList() ?? new List<ListeningHabitBucket>();
        DayOfWeekBuckets = dayOfWeekBuckets?.ToList() ?? new List<ListeningHabitBucket>();
        PeakHour = peakHour;
        PeakHourPlayCount = Math.Max(0, peakHourPlayCount);
        PeakDayOfWeek = peakDayOfWeek;
        PeakDayOfWeekPlayCount = Math.Max(0, peakDayOfWeekPlayCount);
        HourlyPeakInsight = hourlyPeakInsight ?? string.Empty;
        DayOfWeekPeakInsight = dayOfWeekPeakInsight ?? string.Empty;
    }
}

/// <summary>
/// ライブラリ全体のリスニング統計・ランキング・推移・傾向をまとめた不変スナップショット
/// </summary>
public sealed class LibraryStatsSnapshot
{
    /// <summary>集計に適用された期間範囲</summary>
    public StatsDateRange DateRange { get; }

    /// <summary>KPI サマリ</summary>
    public LibraryStatsSummary Summary { get; }

    /// <summary>よく聴く曲ランキング（再生回数降順）</summary>
    public IReadOnlyList<RankedTrackStat> TopTracks { get; }

    /// <summary>よく聴くアーティストランキング（再生回数降順）</summary>
    public IReadOnlyList<RankedArtistStat> TopArtists { get; }

    /// <summary>よく聴くアルバムランキング（再生回数降順）</summary>
    public IReadOnlyList<RankedAlbumStat> TopAlbums { get; }

    /// <summary>日別の再生推移データ一覧</summary>
    public IReadOnlyList<ListeningTrendPoint> DailyTrends { get; }

    /// <summary>月別の再生推移データ一覧</summary>
    public IReadOnlyList<ListeningTrendPoint> MonthlyTrends { get; }

    /// <summary>時間帯別・曜日別のリスニング傾向分布</summary>
    public ListeningHabitDistribution Habits { get; }

    /// <summary>集計対象期間内に 1 件以上の再生データが存在するかどうか</summary>
    public bool HasData => Summary.TotalPlayCount > 0;

    /// <summary>
    /// <see cref="LibraryStatsSnapshot"/> を初期化します
    /// </summary>
    /// <param name="dateRange">集計期間</param>
    /// <param name="summary">KPI サマリ</param>
    /// <param name="topTracks">よく聴く曲ランキング</param>
    /// <param name="topArtists">よく聴くアーティストランキング</param>
    /// <param name="topAlbums">よく聴くアルバムランキング</param>
    /// <param name="dailyTrends">日別推移</param>
    /// <param name="monthlyTrends">月別推移</param>
    /// <param name="habits">時間帯別・曜日別傾向</param>
    public LibraryStatsSnapshot(
        StatsDateRange dateRange,
        LibraryStatsSummary summary,
        IEnumerable<RankedTrackStat> topTracks,
        IEnumerable<RankedArtistStat> topArtists,
        IEnumerable<RankedAlbumStat> topAlbums,
        IEnumerable<ListeningTrendPoint> dailyTrends,
        IEnumerable<ListeningTrendPoint> monthlyTrends,
        ListeningHabitDistribution habits)
    {
        DateRange = dateRange ?? StatsDateRange.AllTime;
        Summary = summary ?? LibraryStatsSummary.Empty;
        TopTracks = topTracks?.ToList() ?? new List<RankedTrackStat>();
        TopArtists = topArtists?.ToList() ?? new List<RankedArtistStat>();
        TopAlbums = topAlbums?.ToList() ?? new List<RankedAlbumStat>();
        DailyTrends = dailyTrends?.ToList() ?? new List<ListeningTrendPoint>();
        MonthlyTrends = monthlyTrends?.ToList() ?? new List<ListeningTrendPoint>();
        Habits = habits ?? throw new ArgumentNullException(nameof(habits));
    }
}
