using System.Diagnostics;
using AudioEffector.Infrastructure.Ipc;
using AudioEffector.McpServer.Gateways;

namespace AudioEffector.McpServer.Tests.Gateways;

/// <summary>
/// <see cref="NamedPipeIpcReadinessProbe"/> と <see cref="AppIpcServer"/> の Named Pipe 準備完了プローブ結合動作を検証するテストクラス
/// </summary>
public sealed class NamedPipeIpcReadinessProbeTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StateTransitionTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// AppIpcServer の起動前・起動中・停止後に対して NamedPipeIpcReadinessProbe がそれぞれ false・true・false を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_AppIpcServer起動前後と停止後_期待される準備完了状態を返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.Test.{Guid.NewGuid():N}";
        var sut = new NamedPipeIpcReadinessProbe(pipeName, ProbeTimeout);

        // Act 1: サーバ未起動
        bool beforeStart = await sut.IsReadyAsync();

        // Act 2: サーバ起動中（バックグラウンド待受開始のスケジューリング遅延を吸収して準備完了遷移を待機する）
        bool whileRunning;
        using (var server = new AppIpcServer(pipeName))
        {
            server.Start();
            whileRunning = await WaitForReadinessAsync(sut, expected: true, StateTransitionTimeout);
        }

        // Act 3: サーバ停止後（OS のパイプハンドル解放遅延を吸収して未準備遷移を待機する）
        bool afterDispose = await WaitForReadinessAsync(sut, expected: false, StateTransitionTimeout);

        // Assert
        Assert.False(beforeStart);
        Assert.True(whileRunning);
        Assert.False(afterDispose);
    }

    private static async Task<bool> WaitForReadinessAsync(
        NamedPipeIpcReadinessProbe probe,
        bool expected,
        TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        bool lastResult;
        do
        {
            lastResult = await probe.IsReadyAsync();
            if (lastResult == expected)
            {
                return lastResult;
            }

            if (sw.Elapsed >= timeout)
            {
                break;
            }

            await Task.Delay(PollInterval);
        }
        while (sw.Elapsed < timeout);

        return lastResult;
    }
}
