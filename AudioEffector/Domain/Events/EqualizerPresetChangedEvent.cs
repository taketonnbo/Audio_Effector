using System;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Events;

/// <summary>
/// イコライザープリセットまたはバンドゲインが変更された際に発行されるドメインイベント
/// </summary>
/// <param name="Preset">適用されたイコライザープリセット</param>
public record EqualizerPresetChangedEvent(EqualizerPreset Preset) : IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
