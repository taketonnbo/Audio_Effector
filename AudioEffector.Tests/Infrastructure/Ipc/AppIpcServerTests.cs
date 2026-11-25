using AudioEffector.Infrastructure.Ipc;
using AudioEffector.McpServer.Gateways;

namespace AudioEffector.Tests.Infrastructure.Ipc;

public sealed class AppIpcServerTests
{
    /// <summary>
    /// IpcPipeNameResolver が同一ユーザー識別子から決定的なバージョン付きパイプ名を生成することを検証します。
    /// </summary>
    [Fact]
    public void CreatePipeNameFromUserIdentifier_同一識別子指定時_決定的なパイプ名を返す()
    {
        // Arrange
        const string sid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        string name1 = IpcPipeNameResolver.CreatePipeNameFromUserIdentifier(sid);
        string name2 = IpcPipeNameResolver.CreatePipeNameFromUserIdentifier(sid);
        string other = IpcPipeNameResolver.CreatePipeNameFromUserIdentifier("S-1-5-21-999999999-1002");

        // Assert
        Assert.StartsWith(IpcPipeNameResolver.PIPE_NAME_PREFIX, name1, StringComparison.Ordinal);
        Assert.Equal(IpcPipeNameResolver.PIPE_NAME_PREFIX.Length + 16, name1.Length);
        Assert.Equal(name1, name2);
        Assert.NotEqual(name1, other);
    }

    /// <summary>
    /// AppIpcServer の起動前・起動中・停止後に対して NamedPipeIpcReadinessProbe がそれぞれ false・true・false を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_AppIpcServer起動前後と停止後_期待される準備完了状態を返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.Test.{Guid.NewGuid():N}";
        var probe = new NamedPipeIpcReadinessProbe(pipeName, TimeSpan.FromMilliseconds(150));

        // Act 1: サーバ未起動
        bool beforeStart = await probe.IsReadyAsync();

        // Act 2: サーバ起動中
        bool whileRunning;
        using (var sut = new AppIpcServer(pipeName))
        {
            sut.Start();
            whileRunning = await probe.IsReadyAsync();
        }

        // Act 3: サーバ停止後
        bool afterDispose = await probe.IsReadyAsync();

        // Assert
        Assert.False(beforeStart);
        Assert.True(whileRunning);
        Assert.False(afterDispose);
    }
}
