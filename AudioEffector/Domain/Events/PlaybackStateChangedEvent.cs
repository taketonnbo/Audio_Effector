using System;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Events;

/// <summary>
/// 音声再生エンジンの状態（再生・一時停止・停止）が変更された際に発行されるドメインイベント
/// </summary>
/// <param name="State">変更後の再生状態</param>
public record PlaybackStateChangedEvent(PlaybackState State) : IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
