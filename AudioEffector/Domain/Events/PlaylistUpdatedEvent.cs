using System;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Events;

/// <summary>
/// プレイリストの内容（楽曲追加・削除・並び替え等）が変更された際に発行されるドメインイベント
/// </summary>
/// <param name="Playlist">更新されたプレイリストエンティティ</param>
public record PlaylistUpdatedEvent(UserPlaylist Playlist) : IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
