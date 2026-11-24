using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using AudioEffector.Application.Common;

namespace AudioEffector.Presentation.Services;

/// <summary>
/// WPF の <see cref="Dispatcher"/> へ処理をマーシャリングする <see cref="IUiDispatcher"/> 具象実装
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Func<Dispatcher?> _dispatcherProvider;

    /// <summary>
    /// 現在の WPF アプリケーションの <see cref="Dispatcher"/> を使用するインスタンスを初期化します
    /// </summary>
    public WpfUiDispatcher()
        : this(() => System.Windows.Application.Current?.Dispatcher)
    {
    }

    /// <summary>
    /// 任意の <see cref="Dispatcher"/> 取得デリゲートを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="dispatcherProvider">ディスパッチャー取得関数</param>
    public WpfUiDispatcher(Func<Dispatcher?> dispatcherProvider)
    {
        _dispatcherProvider = dispatcherProvider ?? throw new ArgumentNullException(nameof(dispatcherProvider));
    }

    /// <inheritdoc />
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = _dispatcherProvider();
        if (dispatcher == null ||
            dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished ||
            dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.Invoke(action);
        }
        catch (TaskCanceledException)
        {
            // アプリケーション終了処理中のキャンセルは安全に無視する
        }
    }

    /// <inheritdoc />
    public void BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = _dispatcherProvider();
        if (dispatcher == null)
        {
            // ヘッドレス・単体テスト環境ではオーディオ再生スレッドからの離脱を維持する
            _ = Task.Run(action);
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            // WPF 終了処理中・終了済みは安全に破棄（no-op）する
            return;
        }

        _ = dispatcher.BeginInvoke(action);
    }
}
