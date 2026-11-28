using System;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Events;

/// <summary>
/// 楽曲に対応する歌詞データ（外部 .lrc ファイルの保存・更新・キャッシュ無効化等）が更新された際に発行されるドメインイベントです
/// </summary>
/// <param name="TrackFilePath">対象楽曲の音声ファイルパス</param>
/// <param name="Document">更新後の歌詞ドキュメント（キャッシュ無効化のみの場合は <see langword="null"/>）</param>
public sealed record LyricsUpdatedEvent(string TrackFilePath, LyricsDocument? Document = null) : IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）を取得します
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
