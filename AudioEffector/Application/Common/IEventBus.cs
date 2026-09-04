using System;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Events;

namespace AudioEffector.Application.Common;

/// <summary>
/// アプリケーション全体のドメインイベントの発行および購読を仲介するイベントバスインターフェース
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 指定されたドメインイベントを非同期で発行し、すべての購読者に通知します
    /// </summary>
    /// <typeparam name="TEvent">発行するイベントの型</typeparam>
    /// <param name="domainEvent">ドメインイベントインスタンス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>
    /// 指定されたイベント型の通知を購読します
    /// </summary>
    /// <typeparam name="TEvent">購読するイベントの型</typeparam>
    /// <param name="handler">イベント受信時の非同期コールバック</param>
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent;

    /// <summary>
    /// 指定されたイベント型の購読を解除します
    /// </summary>
    /// <typeparam name="TEvent">購読解除するイベントの型</typeparam>
    /// <param name="handler">解除対象のハンドラーコールバック</param>
    void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent;
}
