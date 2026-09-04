using System;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Events;

/// <summary>
/// 再生対象の楽曲が変更された際に発行されるドメインイベント
/// </summary>
/// <param name="Track">新しくセットされたトラック（停止・アンロード時はnull）</param>
/// <param name="InitialPosition">再生開始位置</param>
public record TrackChangedEvent(Track? Track, TimeSpan InitialPosition) : IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
