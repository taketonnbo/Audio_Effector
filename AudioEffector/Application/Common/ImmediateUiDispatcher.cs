using System;
using System.Threading.Tasks;

namespace AudioEffector.Application.Common;

/// <summary>
/// ヘッドレス実行・MCPサーバ・単体テスト向けの既定 <see cref="IUiDispatcher"/> 実装
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <summary>
    /// シングルトンインスタンスを取得します
    /// </summary>
    public static ImmediateUiDispatcher Instance { get; } = new();

    /// <summary>
    /// 指定された処理を現在のスレッドで即座に同期実行します
    /// </summary>
    /// <param name="action">実行する処理</param>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }

    /// <summary>
    /// オーディオ再生スレッドからのデッドロックを防止するため、指定された処理をスレッドプール上で非同期実行します
    /// </summary>
    /// <param name="action">実行する処理</param>
    public void BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = Task.Run(action);
    }
}
