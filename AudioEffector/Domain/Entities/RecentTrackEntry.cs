using System;
using System.Globalization;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// 「最近再生した曲」の1件分の再生履歴エントリを表すエンティティ
/// </summary>
public sealed class RecentTrackEntry
{
    /// <summary>
    /// 再生された楽曲エンティティ
    /// </summary>
    public Track Track { get; }

    /// <summary>
    /// 音声ファイルパス
    /// </summary>
    public string FilePath => Track.FilePath;

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title => Track.Title;

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist => Track.Artist;

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album => Track.Album;

    /// <summary>
    /// 再生時間
    /// </summary>
    public TimeSpan Duration => Track.Duration;

    /// <summary>
    /// 再生時間のフォーマット済み文字列
    /// </summary>
    public string DurationString => Track.DurationString;

    /// <summary>
    /// 再生日時（ローカル時刻）
    /// </summary>
    public DateTime PlayedAt { get; }

    /// <summary>
    /// 事前整形済みの再生日時表示文字列（yyyy/MM/dd HH:mm）
    /// </summary>
    public string PlayedAtDisplay { get; }

    /// <summary>
    /// RecentTrackEntryを初期化します
    /// </summary>
    /// <param name="track">再生されたトラック</param>
    /// <param name="playedAt">再生日時</param>
    public RecentTrackEntry(Track track, DateTime playedAt)
    {
        Track = track ?? throw new ArgumentNullException(nameof(track));
        PlayedAt = playedAt;
        PlayedAtDisplay = playedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ライブラリ上の最新トラックインスタンスへ差し替えた新しい履歴エントリを生成します
    /// </summary>
    /// <param name="libraryTrack">ライブラリ上のトラック実体</param>
    /// <returns>トラック参照を差し替えた新しいRecentTrackEntry</returns>
    public RecentTrackEntry WithTrack(Track libraryTrack)
    {
        ArgumentNullException.ThrowIfNull(libraryTrack);
        return new RecentTrackEntry(libraryTrack, PlayedAt);
    }
}
