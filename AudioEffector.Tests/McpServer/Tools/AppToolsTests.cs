using System.IO;
using System.Text.Json;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Protocol;
using AudioEffector.McpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;

namespace AudioEffector.Tests.McpServer.Tools;

public sealed class AppToolsTests
{
    /// <summary>
    /// launch_app ツールで mode="mini_player"（既定値）を指定した場合、MiniPlayer モードでランチャーを呼び出し正常応答 JSON を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_mini_player指定時_MiniPlayerモードで起動しlaunchedTrueのJSONを返す()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        launcherMock
            .Setup(l => l.LaunchAsync(AppLaunchMode.MiniPlayer, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppLaunchResult(
                Launched: true,
                AlreadyRunning: false,
                Mode: WpfAppLauncher.MODE_MINI_PLAYER,
                IpcReady: true));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("launched").GetBoolean());
        Assert.False(data.GetProperty("alreadyRunning").GetBoolean());
        Assert.Equal("mini_player", data.GetProperty("mode").GetString());
        Assert.True(data.GetProperty("ipcReady").GetBoolean());

        launcherMock.Verify(
            l => l.LaunchAsync(AppLaunchMode.MiniPlayer, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// launch_app ツールで mode="normal"（大文字小文字・前後空白含む）および waitForReady=false を指定した場合、Normal モードで起動することを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_normal指定時_Normalモードで起動し正常応答JSONを返す()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        launcherMock
            .Setup(l => l.LaunchAsync(AppLaunchMode.Normal, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppLaunchResult(
                Launched: true,
                AlreadyRunning: false,
                Mode: WpfAppLauncher.MODE_NORMAL,
                IpcReady: false));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync(mode: "  NORMAL  ", waitForReady: false);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("launched").GetBoolean());
        Assert.False(data.GetProperty("alreadyRunning").GetBoolean());
        Assert.Equal("normal", data.GetProperty("mode").GetString());
        Assert.False(data.GetProperty("ipcReady").GetBoolean());
    }

    /// <summary>
    /// Audio Effector 本体がすでに起動済みの場合、launched=false・alreadyRunning=true・mode="existing" の正常応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_すでに起動済みの場合_alreadyRunningTrueの正常応答を返す()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        launcherMock
            .Setup(l => l.LaunchAsync(AppLaunchMode.MiniPlayer, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppLaunchResult(
                Launched: false,
                AlreadyRunning: true,
                Mode: WpfAppLauncher.MODE_EXISTING,
                IpcReady: false));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync(mode: "mini_player");

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("launched").GetBoolean());
        Assert.True(data.GetProperty("alreadyRunning").GetBoolean());
        Assert.Equal("existing", data.GetProperty("mode").GetString());
        Assert.False(data.GetProperty("ipcReady").GetBoolean());
    }

    /// <summary>
    /// launch_app ツールで不正な mode 文字列を指定した場合、INVALID_PARAMETER エラーを返しランチャーを呼び出さないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("minimized")]
    [InlineData("fullscreen")]
    public async Task LaunchAppAsync_不正なmode指定時_INVALID_PARAMETERエラーを返す(string invalidMode)
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync(mode: invalidMode);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, error.GetProperty("code").GetString());
        JsonElement details = error.GetProperty("details");
        Assert.Equal("mode", details.GetProperty("parameter").GetString());

        launcherMock.Verify(
            l => l.LaunchAsync(It.IsAny<AppLaunchMode>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// AudioEffector.exe が見つからず FileNotFoundException が発生した場合、FILE_NOT_FOUND エラーへ変換しファイルパスを露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_実行ファイル未存在時_FILE_NOT_FOUNDを返しパスを露出しない()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        launcherMock
            .Setup(l => l.LaunchAsync(It.IsAny<AppLaunchMode>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("Not found", @"C:\SecretPath\AudioEffector.exe"));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SecretPath", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.FILE_NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// 起動待機がタイムアウトして TimeoutException が発生した場合、IPC_CONNECTION_FAILED エラーへ変換されることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_起動待機タイムアウト時_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        launcherMock
            .Setup(l => l.LaunchAsync(It.IsAny<AppLaunchMode>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("SECRET_TIMEOUT_DETAIL"));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SECRET_TIMEOUT_DETAIL", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// プロセス起動失敗等で InvalidOperationException が発生した場合、INTERNAL_ERROR へ変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_プロセス起動失敗時_INTERNAL_ERRORを返し内部詳細を露出しない()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        launcherMock
            .Setup(l => l.LaunchAsync(It.IsAny<AppLaunchMode>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET_PROCESS_START_ERROR"));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SECRET_PROCESS_START_ERROR", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// キャンセル要求時に CancellationToken がランチャーへ伝播され OPERATION_CANCELLED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_キャンセル要求時_CancellationTokenを伝播しOPERATION_CANCELLEDを返す()
    {
        // Arrange
        var launcherMock = new Mock<IAppLauncher>();
        var loggerMock = new Mock<ILogger<AppTools>>();
        using var cts = new CancellationTokenSource();
        CancellationToken passedToken = default;

        launcherMock
            .Setup(l => l.LaunchAsync(It.IsAny<AppLaunchMode>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AppLaunchMode, bool, CancellationToken>((_, _, ct) => passedToken = ct)
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = new AppTools(launcherMock.Object, loggerMock.Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync(cancellationToken: cts.Token);

        // Assert
        Assert.Equal(cts.Token, passedToken);
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.Equal(McpErrorCodes.OPERATION_CANCELLED, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// 実 WpfAppLauncher を経由し、Mutex が確立された後も IPC が応答しない状態で waitForReady=true を指定した場合、IPC_CONNECTION_FAILED エラーを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAppAsync_実ランチャーでMutex確立後もIPC未応答の場合_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        bool mutexCreated = false;
        using var launcher = new WpfAppLauncher(
            new McpServerOptions(),
            new Mock<ILogger<WpfAppLauncher>>().Object,
            @"C:\Apps\AudioEffector",
            _ => true,
            () => mutexCreated,
            _ =>
            {
                mutexCreated = true;
                return new DummyLaunchedProcess();
            },
            _ => Task.FromResult(false),
            readyTimeout: TimeSpan.FromMilliseconds(60),
            pollInterval: TimeSpan.FromMilliseconds(10));

        var sut = new AppTools(launcher, new Mock<ILogger<AppTools>>().Object);

        // Act
        CallToolResult result = await sut.LaunchAppAsync(mode: "mini_player", waitForReady: true);

        // Assert
        Assert.True(mutexCreated);
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private sealed class DummyLaunchedProcess : WpfAppLauncher.ILaunchedProcess
    {
        public bool HasExited => false;

        public int ExitCode => 0;

        public void Dispose()
        {
        }
    }

    private static string GetSingleText(CallToolResult result)
    {
        TextContentBlock block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }
}
