using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Events;

namespace AudioEffector.Application.Common;

/// <summary>
/// メモリ内（インプロセス）でドメインイベントのディスパッチを行うイベントバス具象クラス
/// </summary>
public class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();
    private readonly object _lock = new();

    /// <summary>
    /// 指定されたドメインイベントを発行し、登録されたすべてのハンドラーを非同期で実行します
    /// </summary>
    /// <typeparam name="TEvent">発行するイベントの型</typeparam>
    /// <param name="domainEvent">ドメインイベントインスタンス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        List<Delegate> handlers;
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0)
            {
                return;
            }

            handlers = new List<Delegate>(list);
        }

        var tasks = handlers
            .OfType<Func<TEvent, CancellationToken, Task>>()
            .Select(h => SafeExecuteHandlerAsync(h, domainEvent, cancellationToken));

        await Task.WhenAll(tasks);
    }

    private static async Task SafeExecuteHandlerAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        TEvent domainEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler(domainEvent, cancellationToken);
        }
        catch
        {
            // 個々のハンドラーの例外で他のハンドラーの実行が阻害されないよう保護
        }
    }

    /// <summary>
    /// 指定されたイベント型の通知を購読します
    /// </summary>
    /// <typeparam name="TEvent">購読するイベントの型</typeparam>
    /// <param name="handler">イベント受信時の非同期コールバック</param>
    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            var list = _subscribers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
            if (!list.Contains(handler))
            {
                list.Add(handler);
            }
        }
    }

    /// <summary>
    /// 指定されたイベント型の購読を解除します
    /// </summary>
    /// <typeparam name="TEvent">購読解除するイベントの型</typeparam>
    /// <param name="handler">解除対象のハンドラーコールバック</param>
    public void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            if (_subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                list.Remove(handler);
            }
        }
    }
}
