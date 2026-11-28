using System;
using System.Globalization;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// ライブラリ全体リスニング統計画面「よく聴く曲 TOP 10」の 1 行表示モデル
/// </summary>
public sealed class LibraryStatsTrackItemViewModel
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

    /// <summary>再生回数表示文字列（例: "18 回"）</summary>
    public string PlayCountDisplay { get; }

    /// <summary>累計再生時間表示文字列</summary>
    public string TotalListeningTimeDisplay { get; }

    /// <summary>1 位対比の再生回数比率（0〜100）</summary>
    public double RatioPercent { get; }

    /// <summary>元のランキング集計モデル</summary>
    public RankedTrackStat Stat { get; }

    /// <summary>
    /// <see cref="LibraryStatsTrackItemViewModel"/> を初期化します
    /// </summary>
    /// <param name="stat">楽曲ランキング集計モデル</param>
    public LibraryStatsTrackItemViewModel(RankedTrackStat stat)
    {
        ArgumentNullException.ThrowIfNull(stat);

        Stat = stat;
        Rank = stat.Rank;
        FilePath = stat.FilePath;
        Title = stat.Title;
        Artist = stat.Artist;
        Album = stat.Album;
        PlayCount = stat.PlayCount;
        PlayCountDisplay = string.Format(CultureInfo.InvariantCulture, "{0:N0} 回", stat.PlayCount);
        TotalListeningTimeDisplay = ListeningStatsViewModel.FormatDuration(stat.TotalListeningDuration);
        RatioPercent = stat.RatioPercent;
    }
}

/// <summary>
/// ライブラリ全体リスニング統計画面「よく聴くアーティスト TOP 10」の 1 行表示モデル
/// </summary>
public sealed class LibraryStatsArtistItemViewModel
{
    /// <summary>順位（1 始まり）</summary>
    public int Rank { get; }

    /// <summary>アーティスト名</summary>
    public string ArtistName { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>再生回数表示文字列</summary>
    public string PlayCountDisplay { get; }

    /// <summary>サブ情報表示文字列（ユニーク曲数・累計時間）</summary>
    public string SubInfoDisplay { get; }

    /// <summary>サムネイル表示用の代表楽曲ファイルパス</summary>
    public string? RepresentativeFilePath { get; }

    /// <summary>1 位対比の再生回数比率（0〜100）</summary>
    public double RatioPercent { get; }

    /// <summary>元のランキング集計モデル</summary>
    public RankedArtistStat Stat { get; }

    /// <summary>
    /// <see cref="LibraryStatsArtistItemViewModel"/> を初期化します
    /// </summary>
    /// <param name="stat">アーティストランキング集計モデル</param>
    public LibraryStatsArtistItemViewModel(RankedArtistStat stat)
    {
        ArgumentNullException.ThrowIfNull(stat);

        Stat = stat;
        Rank = stat.Rank;
        ArtistName = stat.ArtistName;
        PlayCount = stat.PlayCount;
        PlayCountDisplay = string.Format(CultureInfo.InvariantCulture, "{0:N0} 回", stat.PlayCount);
        SubInfoDisplay = string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} 曲 • {1}",
            stat.UniqueTrackCount,
            ListeningStatsViewModel.FormatDuration(stat.TotalListeningDuration));
        RepresentativeFilePath = stat.RepresentativeFilePath;
        RatioPercent = stat.RatioPercent;
    }
}

/// <summary>
/// ライブラリ全体リスニング統計画面「よく聴くアルバム TOP 10」の 1 行表示モデル
/// </summary>
public sealed class LibraryStatsAlbumItemViewModel
{
    /// <summary>順位（1 始まり）</summary>
    public int Rank { get; }

    /// <summary>アルバムタイトル</summary>
    public string AlbumTitle { get; }

    /// <summary>アーティスト名</summary>
    public string ArtistName { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>再生回数表示文字列</summary>
    public string PlayCountDisplay { get; }

    /// <summary>サブ情報表示文字列（アーティスト名・再生曲数）</summary>
    public string SubInfoDisplay { get; }

    /// <summary>カバーアート表示用の代表楽曲ファイルパス</summary>
    public string? RepresentativeFilePath { get; }

    /// <summary>1 位対比の再生回数比率（0〜100）</summary>
    public double RatioPercent { get; }

    /// <summary>元のランキング集計モデル</summary>
    public RankedAlbumStat Stat { get; }

    /// <summary>
    /// <see cref="LibraryStatsAlbumItemViewModel"/> を初期化します
    /// </summary>
    /// <param name="stat">アルバムランキング集計モデル</param>
    public LibraryStatsAlbumItemViewModel(RankedAlbumStat stat)
    {
        ArgumentNullException.ThrowIfNull(stat);

        Stat = stat;
        Rank = stat.Rank;
        AlbumTitle = stat.AlbumTitle;
        ArtistName = stat.ArtistName;
        PlayCount = stat.PlayCount;
        PlayCountDisplay = string.Format(CultureInfo.InvariantCulture, "{0:N0} 回", stat.PlayCount);
        SubInfoDisplay = string.Format(
            CultureInfo.InvariantCulture,
            "{0} • {1:N0} 曲",
            stat.ArtistName,
            stat.UniqueTrackCount);
        RepresentativeFilePath = stat.RepresentativeFilePath;
        RatioPercent = stat.RatioPercent;
    }
}

/// <summary>
/// 再生推移グラフおよび時間帯別・曜日別傾向グラフの 1 本のバー表示モデル
/// </summary>
public sealed class LibraryStatsChartBarViewModel
{
    /// <summary>グラフバーの最大描画高さ（px）</summary>
    public const double MaxBarHeight = 110.0;

    /// <summary>1 回以上の再生がある場合の最小視認高さ（px）</summary>
    public const double MinNonZeroBarHeight = 4.0;

    /// <summary>横軸表示ラベル</summary>
    public string Label { get; }

    /// <summary>ツールチップ表示文字列</summary>
    public string ToolTipText { get; }

    /// <summary>再生回数</summary>
    public int PlayCount { get; }

    /// <summary>バー描画高さ（px）</summary>
    public double BarHeight { get; }

    /// <summary>ピーク強調表示対象かどうか</summary>
    public bool IsPeak { get; }

    /// <summary>
    /// <see cref="LibraryStatsChartBarViewModel"/> を初期化します
    /// </summary>
    /// <param name="label">横軸表示ラベル</param>
    /// <param name="detailLabel">詳細ラベル（ツールチップ用）</param>
    /// <param name="playCount">再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="heightRatio">最大値対比比率（0.0〜1.0）</param>
    /// <param name="isPeak">ピーク強調対象かどうか</param>
    public LibraryStatsChartBarViewModel(
        string label,
        string detailLabel,
        int playCount,
        TimeSpan totalListeningDuration,
        double heightRatio,
        bool isPeak)
    {
        Label = label ?? string.Empty;
        PlayCount = Math.Max(0, playCount);
        IsPeak = isPeak;

        double clampedRatio = Math.Clamp(heightRatio, 0.0, 1.0);
        BarHeight = PlayCount > 0
            ? Math.Max(MinNonZeroBarHeight, Math.Round(clampedRatio * MaxBarHeight, 1))
            : 2.0;

        ToolTipText = string.Format(
            CultureInfo.InvariantCulture,
            "{0}\n{1:N0} 回（{2}）",
            detailLabel ?? label ?? string.Empty,
            PlayCount,
            ListeningStatsViewModel.FormatDuration(totalListeningDuration));
    }
}
