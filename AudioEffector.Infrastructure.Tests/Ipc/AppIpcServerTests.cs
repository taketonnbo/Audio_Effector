using System.IO.Pipes;
using AudioEffector.Infrastructure.Ipc;

namespace AudioEffector.Infrastructure.Tests.Ipc;

public sealed class AppIpcServerTests
{
    /// <summary>
    /// IpcPipeNameResolver が同一ユーザー識別子・同一セッション ID から決定的なバージョン付きパイプ名を生成することを検証します。
    /// </summary>
    [Fact]
    public void CreatePipeName_同一識別子と同一セッションID指定時_決定的なパイプ名を返す()
    {
        // Arrange
        const string sid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        string name1 = IpcPipeNameResolver.CreatePipeName(sid, sessionId: 1);
        string name2 = IpcPipeNameResolver.CreatePipeName(sid, sessionId: 1);
        string otherUser = IpcPipeNameResolver.CreatePipeName("S-1-5-21-999999999-1002", sessionId: 1);

        // Assert
        Assert.StartsWith(IpcPipeNameResolver.PIPE_NAME_PREFIX, name1, StringComparison.Ordinal);
        Assert.EndsWith(".s1", name1, StringComparison.Ordinal);
        Assert.Equal(name1, name2);
        Assert.NotEqual(name1, otherUser);
    }

    /// <summary>
    /// 同じユーザー SID であっても Windows セッション ID が異なる場合、別のパイプ名が生成されることを検証します。
    /// </summary>
    [Fact]
    public void CreatePipeName_同一SIDで別セッションID指定時_異なるパイプ名を返す()
    {
        // Arrange
        const string sid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        string session1Pipe = IpcPipeNameResolver.CreatePipeName(sid, sessionId: 1);
        string session2Pipe = IpcPipeNameResolver.CreatePipeName(sid, sessionId: 2);

        // Assert
        Assert.NotEqual(session1Pipe, session2Pipe);
        Assert.EndsWith(".s1", session1Pipe, StringComparison.Ordinal);
        Assert.EndsWith(".s2", session2Pipe, StringComparison.Ordinal);
    }

    /// <summary>
    /// AppIpcServer の起動前・起動中・停止後に対して NamedPipeClientStream での接続可否がそれぞれ false・true・false となることを検証します。
    /// </summary>
    [Fact]
    public async Task StartおよびDispose_AppIpcServer起動前後と停止後_NamedPipe接続可否が期待通りに遷移する()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.Test.{Guid.NewGuid():N}";

        // Act 1: サーバ未起動
        bool beforeStart = await TryConnectPipeAsync(pipeName);

        // Act 2: サーバ起動中
        bool whileRunning;
        using (var sut = new AppIpcServer(pipeName))
        {
            sut.Start();
            whileRunning = await TryConnectPipeAsync(pipeName);
        }

        // Act 3: サーバ停止後
        bool afterDispose = await TryConnectPipeAsync(pipeName);

        // Assert
        Assert.False(beforeStart);
        Assert.True(whileRunning);
        Assert.False(afterDispose);
    }

    private static async Task<bool> TryConnectPipeAsync(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await client.ConnectAsync(cts.Token);
            return client.IsConnected;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
