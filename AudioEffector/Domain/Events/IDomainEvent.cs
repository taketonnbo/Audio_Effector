using System;

namespace AudioEffector.Domain.Events;

/// <summary>
/// ドメイン内で発生した出来事を表すドメインイベントの基本インターフェース
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// イベント発生日時（UTC）
    /// </summary>
    DateTime OccurredOn { get; }
}
