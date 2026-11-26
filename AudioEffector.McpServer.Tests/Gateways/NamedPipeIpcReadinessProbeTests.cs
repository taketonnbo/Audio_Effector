using AudioEffector.Infrastructure.Ipc;
using AudioEffector.McpServer.Gateways;

namespace AudioEffector.McpServer.Tests.Gateways;

/// <summary>
/// <see cref="NamedPipeIpcReadinessProbe"/> と <see cref="AppIpcServer"/> の Named Pipe 準備完了プローブ結合動作を検証するテストクラス
/// </summary>
public sealed class NamedPipeIpcReadinessProbeTests
{
    /// <summary>
    /// AppIpcServer の起動前・起動中・停止後に対して NamedPipeIpcReadinessProbe がそれぞれ false・true・false を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_AppIpcServer起動前後と停止後_期待される準備完了状態を返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.Test.{Guid.NewGuid():N}";
        var sut = new NamedPipeIpcReadinessProbe(pipeName, TimeSpan.FromMilliseconds(150));

        // Act 1: サーバ未起動
        bool beforeStart = await sut.IsReadyAsync();

        // Act 2: サーバ起動中
        bool whileRunning;
        using (var server = new AppIpcServer(pipeName))
        {
            server.Start();
            whileRunning = await sut.IsReadyAsync();
        }

        // Act 3: サーバ停止後
        bool afterDispose = await sut.IsReadyAsync();

        // Assert
        Assert.False(beforeStart);
        Assert.True(whileRunning);
        Assert.False(afterDispose);
    }
}
