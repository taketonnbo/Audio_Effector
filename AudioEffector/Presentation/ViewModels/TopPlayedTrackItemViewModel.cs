using System;
using System.Globalization;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// ダッシュボード上の「よく聴く曲 TOP N」の 1 行分の表示モデル
/// </summary>
public sealed class TopPlayedTrackItemViewModel
{
    /// <summary>
    /// 順位（1 始まり）
    /// </summary>
    public int Rank { get; }

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist { get; }

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album { get; }

    /// <summary>
    /// 音声ファイルパス
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 累計再生回数
    /// </summary>
    public int PlayCount { get; }

    /// <summary>
    /// 累計再生回数の表示文字列（例: "12 回"）
    /// </summary>
    public string PlayCountDisplay { get; }

    /// <summary>
    /// 累計再生時間の表示文字列（例: "45分 12秒"）
    /// </summary>
    public string TotalListeningTimeDisplay { get; }

    /// <summary>
    /// ランキング 1 位に対する再生回数比率（0〜100、プログレスバー表示用）
    /// </summary>
    public double PlayCountPercent { get; }

    /// <summary>
    /// 元の統計エンティティ
    /// </summary>
    public TrackListeningStat Stat { get; }

    /// <summary>
    /// <see cref="TopPlayedTrackItemViewModel"/> を初期化します
    /// </summary>
    /// <param name="rank">順位（1 始まり）</param>
    /// <param name="stat">楽曲統計エンティティ</param>
    /// <param name="maxPlayCount">ランキング内の最大再生回数</param>
    public TopPlayedTrackItemViewModel(int rank, TrackListeningStat stat, int maxPlayCount)
    {
        ArgumentNullException.ThrowIfNull(stat);

        Rank = rank;
        Stat = stat;
        Title = stat.Title;
        Artist = stat.Artist;
        Album = stat.Album;
        FilePath = stat.FilePath;
        PlayCount = stat.PlayCount;
        PlayCountDisplay = string.Format(CultureInfo.InvariantCulture, "{0:N0} 回", stat.PlayCount);
        TotalListeningTimeDisplay = ListeningStatsViewModel.FormatDuration(stat.TotalListeningDuration);
        PlayCountPercent = maxPlayCount > 0
            ? Math.Clamp(stat.PlayCount * 100.0 / maxPlayCount, 0.0, 100.0)
            : 0.0;
    }
}
