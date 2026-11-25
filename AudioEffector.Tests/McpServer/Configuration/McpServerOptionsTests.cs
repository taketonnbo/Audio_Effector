using AudioEffector.McpServer.Configuration;
using Microsoft.Extensions.Logging;

namespace AudioEffector.Tests.McpServer.Configuration;

public sealed class McpServerOptionsTests
{
    /// <summary>
    /// CLI 引数も環境変数も未指定の場合に期待される既定値が設定されることを検証します。
    /// </summary>
    [Fact]
    public void TryParse_引数および環境変数が未指定の場合_既定値のオプションを返す()
    {
        // Arrange
        var args = Array.Empty<string>();
        static string? EmptyEnv(string _) => null;

        // Act
        bool succeeded = McpServerOptions.TryParse(args, out McpServerOptions? sut, out string? error, EmptyEnv);

        // Assert
        Assert.True(succeeded);
        Assert.Null(error);
        Assert.NotNull(sut);
        Assert.Equal(McpTransportMode.Stdio, sut.Transport);
        Assert.Equal(McpServerOptions.DEFAULT_HOST, sut.Host);
        Assert.Equal(McpServerOptions.DEFAULT_PORT, sut.Port);
        Assert.Null(sut.DataDirectory);
        Assert.Equal(LogLevel.Information, sut.MinLogLevel);
        Assert.Null(sut.WpfAppPath);
    }

    /// <summary>
    /// すべての CLI 引数をスペース区切りで指定した場合に各プロパティが上書きされることを検証します。
    /// </summary>
    [Fact]
    public void TryParse_全CLIオプション指定時_指定値でオプションを構築する()
    {
        // Arrange
        string[] args =
        [
            "--transport", "http",
            "--host", "127.0.0.2",
            "--port", "52000",
            "--data-dir", @"C:\Temp\AudioEffectorMcp",
            "--log-level", "Debug",
            "--wpf-app-path", @"C:\Apps\AudioEffector.exe",
        ];
        static string? EmptyEnv(string _) => null;

        // Act
        bool succeeded = McpServerOptions.TryParse(args, out McpServerOptions? sut, out string? error, EmptyEnv);

        // Assert
        Assert.True(succeeded);
        Assert.Null(error);
        Assert.NotNull(sut);
        Assert.Equal(McpTransportMode.Http, sut.Transport);
        Assert.Equal("127.0.0.2", sut.Host);
        Assert.Equal(52000, sut.Port);
        Assert.Equal(@"C:\Temp\AudioEffectorMcp", sut.DataDirectory);
        Assert.Equal(LogLevel.Debug, sut.MinLogLevel);
        Assert.Equal(@"C:\Apps\AudioEffector.exe", sut.WpfAppPath);
    }

    /// <summary>
    /// イコール区切り（--key=value）の CLI 引数形式を正しく解析できることを検証します。
    /// </summary>
    [Fact]
    public void TryParse_イコール区切りオプション指定時_正しく解析される()
    {
        // Arrange
        string[] args =
        [
            "--transport=stdio",
            "--port=51900",
            "--log-level=Warning",
        ];
        static string? EmptyEnv(string _) => null;

        // Act
        bool succeeded = McpServerOptions.TryParse(args, out McpServerOptions? sut, out string? error, EmptyEnv);

        // Assert
        Assert.True(succeeded);
        Assert.Null(error);
        Assert.NotNull(sut);
        Assert.Equal(McpTransportMode.Stdio, sut.Transport);
        Assert.Equal(51900, sut.Port);
        Assert.Equal(LogLevel.Warning, sut.MinLogLevel);
    }

    /// <summary>
    /// 環境変数から各オプションが読み込まれ、CLI 引数が指定された場合は CLI 引数が優先されることを検証します。
    /// </summary>
    [Fact]
    public void TryParse_環境変数とCLI引数が併存する場合_CLI引数が優先される()
    {
        // Arrange
        var envVars = new Dictionary<string, string?>
        {
            [McpServerOptions.ENV_TRANSPORT] = "http",
            [McpServerOptions.ENV_HOST] = "127.0.0.5",
            [McpServerOptions.ENV_PORT] = "53000",
            [McpServerOptions.ENV_DATA_DIRECTORY] = @"C:\EnvData",
            [McpServerOptions.ENV_MIN_LOG_LEVEL] = "Error",
            [McpServerOptions.ENV_WPF_APP_PATH] = @"C:\EnvApps\AudioEffector.exe",
        };
        string[] args = ["--transport", "stdio", "--port", "54000"];

        // Act
        bool succeeded = McpServerOptions.TryParse(
            args,
            out McpServerOptions? sut,
            out string? error,
            key => envVars.TryGetValue(key, out string? value) ? value : null);

        // Assert
        Assert.True(succeeded);
        Assert.Null(error);
        Assert.NotNull(sut);
        Assert.Equal(McpTransportMode.Stdio, sut.Transport);
        Assert.Equal("127.0.0.5", sut.Host);
        Assert.Equal(54000, sut.Port);
        Assert.Equal(@"C:\EnvData", sut.DataDirectory);
        Assert.Equal(LogLevel.Error, sut.MinLogLevel);
        Assert.Equal(@"C:\EnvApps\AudioEffector.exe", sut.WpfAppPath);
    }

    /// <summary>
    /// 不正なオプション値・未知のオプション・値欠落に対して解析が失敗しエラーメッセージを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("--transport", "websocket")]
    [InlineData("--port", "0")]
    [InlineData("--port", "70000")]
    [InlineData("--port", "abc")]
    [InlineData("--log-level", "Verbose")]
    [InlineData("--unknown", "value")]
    [InlineData("--transport")]
    [InlineData("--data-dir", "--transport=http")]
    [InlineData("--host", "--port", "51900")]
    [InlineData("--data-dir", "--log-level", "Debug")]
    public void TryParse_不正な引数指定時_Falseとエラーメッセージを返す(params string[] args)
    {
        // Arrange
        static string? EmptyEnv(string _) => null;

        // Act
        bool succeeded = McpServerOptions.TryParse(args, out McpServerOptions? sut, out string? error, EmptyEnv);

        // Assert
        Assert.False(succeeded);
        Assert.Null(sut);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// イコール区切りで明示指定した場合はオプション名と同じ文字列であっても値として受理されることを検証します。
    /// </summary>
    [Fact]
    public void TryParse_イコール区切りでオプション名と同名の値を指定した場合_値として受理される()
    {
        // Arrange
        string[] args = ["--data-dir=--transport=http"];
        static string? EmptyEnv(string _) => null;

        // Act
        bool succeeded = McpServerOptions.TryParse(args, out McpServerOptions? sut, out string? error, EmptyEnv);

        // Assert
        Assert.True(succeeded);
        Assert.Null(error);
        Assert.NotNull(sut);
        Assert.Equal("--transport=http", sut.DataDirectory);
    }
}
