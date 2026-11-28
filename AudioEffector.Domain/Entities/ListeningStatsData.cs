using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// リスニング統計永続化ファイル（ListeningStats.json）全体の集約データモデル。
/// 楽曲別の累計統計と日時毎の再生イベント履歴を一元保持します。
/// </summary>
public sealed class ListeningStatsData
{
    /// <summary>
    /// 現在のスキーマバージョン
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// データスキーマバージョン
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 楽曲別の累計統計リスト
    /// </summary>
    public List<TrackListeningStat> TrackStats { get; }

    /// <summary>
    /// 日時毎の再生履歴イベントリスト
    /// </summary>
    public List<TrackPlayEvent> PlayEvents { get; }

    /// <summary>
    /// 空の <see cref="ListeningStatsData"/> を初期化します
    /// </summary>
    public ListeningStatsData()
        : this(new List<TrackListeningStat>(), new List<TrackPlayEvent>(), CurrentSchemaVersion)
    {
    }

    /// <summary>
    /// 指定された統計および再生イベント一覧で <see cref="ListeningStatsData"/> を初期化します
    /// </summary>
    /// <param name="trackStats">楽曲別の累計統計一覧</param>
    /// <param name="playEvents">日時毎の再生履歴イベント一覧</param>
    /// <param name="schemaVersion">スキーマバージョン</param>
    public ListeningStatsData(
        IEnumerable<TrackListeningStat>? trackStats,
        IEnumerable<TrackPlayEvent>? playEvents,
        int schemaVersion = CurrentSchemaVersion)
    {
        SchemaVersion = schemaVersion > 0 ? schemaVersion : CurrentSchemaVersion;
        TrackStats = trackStats?.ToList() ?? new List<TrackListeningStat>();
        PlayEvents = playEvents?.ToList() ?? new List<TrackPlayEvent>();
    }

    /// <summary>
    /// ファイルパスに一致する楽曲統計エントリを検索します（大文字・小文字を区別しません）
    /// </summary>
    /// <param name="filePath">検索対象のファイルパス</param>
    /// <returns>一致した <see cref="TrackListeningStat"/>（未登録の場合は null）</returns>
    public TrackListeningStat? FindTrackStat(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return TrackStats.FirstOrDefault(
            stat => string.Equals(stat.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 本データのディープコピーを生成します
    /// </summary>
    /// <returns>独立した <see cref="ListeningStatsData"/> の複製</returns>
    public ListeningStatsData Clone()
    {
        return new ListeningStatsData(
            TrackStats.Select(stat => stat.Clone()),
            PlayEvents.Select(ev => ev.Clone()),
            SchemaVersion);
    }
}
