using System;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using Xunit;

namespace AudioEffector.Tests.Application.Common;

/// <summary>
/// <see cref="ImmediateUiDispatcher"/> の同期・非同期ディスパッチ動作を検証するテストクラス
/// </summary>
public sealed class ImmediateUiDispatcherTests
{
    /// <summary>
    /// Invoke を呼び出した場合、同一スレッド上で即座に同期実行されることを検証します。
    /// </summary>
    [Fact]
    public void Invoke_アクションを指定した場合_同一スレッド上で同期実行されること()
    {
        // Arrange
        var sut = ImmediateUiDispatcher.Instance;
        int callerThreadId = Environment.CurrentManagedThreadId;
        int executedThreadId = -1;

        // Act
        sut.Invoke(() => executedThreadId = Environment.CurrentManagedThreadId);

        // Assert
        Assert.Equal(callerThreadId, executedThreadId);
    }

    /// <summary>
    /// BeginInvoke を呼び出した場合、呼び出し元スレッドをブロックせずにスレッドプール上で実行されることを検証します。
    /// </summary>
    [Fact]
    public async Task BeginInvoke_アクションを指定した場合_呼び出し元をブロックせず非同期実行されること()
    {
        // Arrange
        var sut = ImmediateUiDispatcher.Instance;
        using var gate = new ManualResetEventSlim(false);
        var executedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        sut.BeginInvoke(() =>
        {
            // 呼び出し元がゲートを開くまで待機することで、非同期（非ブロッキング）実行であることを検証する
            bool signaled = gate.Wait(TimeSpan.FromSeconds(5));
            executedTcs.TrySetResult(signaled);
        });

        // BeginInvoke がブロックせずここへ到達した後にゲートを開放する
        gate.Set();
        bool result = await executedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// null のアクションを渡した場合、Invoke および BeginInvoke のいずれも ArgumentNullException を送出することを検証します。
    /// </summary>
    [Fact]
    public void InvokeとBeginInvoke_アクションがnullの場合_ArgumentNullExceptionを送出すること()
    {
        // Arrange
        var sut = ImmediateUiDispatcher.Instance;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.Invoke(null!));
        Assert.Throws<ArgumentNullException>(() => sut.BeginInvoke(null!));
    }
}
