using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using AudioEffector.Presentation.Services;
using Xunit;

namespace AudioEffector.Tests.Presentation.Services;

/// <summary>
/// <see cref="WpfUiDispatcher"/> のUIスレッドマーシャリング・シャットダウン保護・ヘッドレスフォールバック動作を検証するテストクラス
/// </summary>
public sealed class WpfUiDispatcherTests
{
    /// <summary>
    /// UIスレッドと同一スレッドから Invoke を呼び出した場合、同一スレッドで直接実行されることを検証します。
    /// </summary>
    [Fact]
    public void Invoke_同一UIスレッドから呼び出した場合_同一スレッドで直接実行されること()
    {
        // Arrange
        Exception? threadEx = null;
        int uiThreadId = -1;
        int executedThreadId = -1;

        var thread = new Thread(() =>
        {
            try
            {
                uiThreadId = Environment.CurrentManagedThreadId;
                var dispatcher = Dispatcher.CurrentDispatcher;
                var sut = new WpfUiDispatcher(() => dispatcher);

                // Act
                sut.Invoke(() => executedThreadId = Environment.CurrentManagedThreadId);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Assert
        Assert.Null(threadEx);
        Assert.Equal(uiThreadId, executedThreadId);
    }

    /// <summary>
    /// バックグラウンドスレッドから Invoke および BeginInvoke を呼び出した場合、UIスレッド（Dispatcherスレッド）へマーシャリングされて実行されることを検証します。
    /// </summary>
    [Fact]
    public async Task InvokeとBeginInvoke_別スレッドから呼び出した場合_UIスレッドへマーシャリング実行されること()
    {
        // Arrange
        var readyTcs = new TaskCompletionSource<(Dispatcher Dispatcher, int ThreadId)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            readyTcs.TrySetResult((dispatcher, Environment.CurrentManagedThreadId));
            Dispatcher.Run();
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        var (uiDispatcher, uiThreadId) = await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var sut = new WpfUiDispatcher(() => uiDispatcher);
            int invokeExecutedThreadId = -1;
            var beginInvokeThreadTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Act
            await Task.Run(() =>
            {
                sut.Invoke(() => invokeExecutedThreadId = Environment.CurrentManagedThreadId);
                sut.BeginInvoke(() => beginInvokeThreadTcs.TrySetResult(Environment.CurrentManagedThreadId));
            });

            int beginInvokeExecutedThreadId = await beginInvokeThreadTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.Equal(uiThreadId, invokeExecutedThreadId);
            Assert.Equal(uiThreadId, beginInvokeExecutedThreadId);
        }
        finally
        {
            uiDispatcher.InvokeShutdown();
            uiThread.Join(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Dispatcher が null（ヘッドレス・単体テスト環境）の場合、Invoke は直接実行され BeginInvoke は Task.Run へフォールバックして非同期実行されることを検証します。
    /// </summary>
    [Fact]
    public async Task InvokeとBeginInvoke_Dispatcherがnullの場合_直接実行およびTaskRunフォールバックすること()
    {
        // Arrange
        var sut = new WpfUiDispatcher(() => null);
        bool invokeExecuted = false;
        using var gate = new ManualResetEventSlim(false);
        var beginInvokeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        sut.Invoke(() => invokeExecuted = true);
        sut.BeginInvoke(() =>
        {
            bool signaled = gate.Wait(TimeSpan.FromSeconds(5));
            beginInvokeTcs.TrySetResult(signaled);
        });

        gate.Set();
        bool beginInvokeResult = await beginInvokeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(invokeExecuted);
        Assert.True(beginInvokeResult);
    }

    /// <summary>
    /// Dispatcher がシャットダウン済みの場合、BeginInvoke は安全に破棄（no-op）され、Invoke は直接実行されることを検証します。
    /// </summary>
    [Fact]
    public void BeginInvokeとInvoke_Dispatcherシャットダウン済みの場合_BeginInvokeは破棄されInvokeは直接実行されること()
    {
        // Arrange
        Dispatcher? shutdownDispatcher = null;
        var thread = new Thread(() =>
        {
            shutdownDispatcher = Dispatcher.CurrentDispatcher;
            shutdownDispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.NotNull(shutdownDispatcher);
        Assert.True(shutdownDispatcher!.HasShutdownStarted || shutdownDispatcher.HasShutdownFinished);

        var sut = new WpfUiDispatcher(() => shutdownDispatcher);
        bool beginInvokeExecuted = false;
        bool invokeExecuted = false;

        // Act
        sut.BeginInvoke(() => beginInvokeExecuted = true);
        sut.Invoke(() => invokeExecuted = true);

        // Assert
        Assert.False(beginInvokeExecuted);
        Assert.True(invokeExecuted);
    }
}
