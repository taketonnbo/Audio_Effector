using System;

namespace AudioEffector.Domain.Events;

/// <summary>
/// 音量またはミュート状態が変更された際に発行されるドメインイベント
/// </summary>
/// <param name="Volume">変更後の音量値（0.0〜1.0）</param>
/// <param name="IsMuted">ミュート状態</param>
public record VolumeChangedEvent(float Volume, bool IsMuted) : IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
