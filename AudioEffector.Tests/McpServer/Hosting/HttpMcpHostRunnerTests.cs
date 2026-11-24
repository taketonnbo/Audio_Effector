using System.IO;
using AudioEffector.McpServer;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Hosting;

namespace AudioEffector.Tests.McpServer.Hosting;

public sealed class HttpMcpHostRunnerTests
{
    /// <summary>
    /// HttpMcpHostRunner 実行時に未実装エラーメッセージが stderr へ出力され終了コード 2 を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task RunAsync_HTTPトランスポート指定時_stderrへ未実装エラーを出力し終了コード2を返す()
    {
        // Arrange
        using var errorWriter = new StringWriter();
        var sut = new HttpMcpHostRunner(errorWriter);
        var options = new McpServerOptions
        {
            Transport = McpTransportMode.Http,
            Host = "127.0.0.1",
            Port = 51890,
        };

        // Act
        int exitCode = await sut.RunAsync(options, CancellationToken.None);
        string output = errorWriter.ToString();

        // Assert
        Assert.Equal(HttpMcpHostRunner.EXIT_CODE_NOT_IMPLEMENTED, exitCode);
        Assert.Contains("未実装", output, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:51890", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// CLI 引数で --transport http を指定した場合に Program 経由で終了コード 2 が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task RunWithArgsAsync_TransportHttp指定時_終了コード2を返す()
    {
        // Arrange
        using var errorWriter = new StringWriter();
        string[] args = ["--transport", "http"];

        // Act
        int exitCode = await Program.RunWithArgsAsync(args, errorWriter, CancellationToken.None, _ => null);

        // Assert
        Assert.Equal(2, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(errorWriter.ToString()));
    }

    /// <summary>
    /// 不正な CLI 引数を指定した場合に Program 経由で終了コード 1 が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task RunWithArgsAsync_不正引数指定時_終了コード1を返す()
    {
        // Arrange
        using var errorWriter = new StringWriter();
        string[] args = ["--transport", "invalid"];

        // Act
        int exitCode = await Program.RunWithArgsAsync(args, errorWriter, CancellationToken.None, _ => null);

        // Assert
        Assert.Equal(Program.EXIT_CODE_INVALID_ARGUMENTS, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(errorWriter.ToString()));
    }
}
