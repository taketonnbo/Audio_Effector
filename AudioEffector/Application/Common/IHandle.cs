using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Events;

namespace AudioEffector.Application.Common;

/// <summary>
/// ドメインイベントのハンドラーインターフェース
/// </summary>
/// <typeparam name="TEvent">処理対象のドメインイベント型</typeparam>
public interface IHandle<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// ドメインイベントを非同期で処理します
    /// </summary>
    /// <param name="domainEvent">受信したドメインイベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
