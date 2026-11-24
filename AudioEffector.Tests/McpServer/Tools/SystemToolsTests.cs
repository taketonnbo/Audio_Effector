using System.Globalization;
using System.Text.Json;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.DependencyInjection;
using AudioEffector.McpServer.Protocol;
using AudioEffector.McpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;

namespace AudioEffector.Tests.McpServer.Tools;

public sealed class SystemToolsTests
{
    /// <summary>
    /// ping ツールに message を指定した場合、正常エンベロープ内にエコー・サーバ情報・UTC タイムスタンプが含まれることを検証します。
    /// </summary>
    [Fact]
    public void Ping_メッセージ指定あり_エコーとサーバ情報を含む正常応答を返す()
    {
        // Arrange
        var sut = CreateSut(out _, out _);

        // Act
        CallToolResult result = sut.Ping("こんにちはMCP");
        string text = GetSingleText(result);

        // Assert
        Assert.False(result.IsError ?? false);
        Assert.Contains("こんにちはMCP", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("ok", data.GetProperty("status").GetString());
        Assert.Equal(McpServiceCollectionExtensions.SERVER_NAME, data.GetProperty("serverName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("version").GetString()));
        Assert.Equal("stdio", data.GetProperty("transport").GetString());
        Assert.Equal("こんにちはMCP", data.GetProperty("echo").GetString());

        string? timestamp = data.GetProperty("timestampUtc").GetString();
        Assert.False(string.IsNullOrWhiteSpace(timestamp));
        Assert.EndsWith("Z", timestamp, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
    }

    /// <summary>
    /// ping ツールに message を省略した場合、echo プロパティが JSON から省略されることを検証します。
    /// </summary>
    [Fact]
    public void Ping_メッセージ未指定_echoプロパティが省略された正常応答を返す()
    {
        // Arrange
        var sut = CreateSut(out _, out _);

        // Act
        CallToolResult result = sut.Ping();

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("ok", data.GetProperty("status").GetString());
        Assert.False(data.TryGetProperty("echo", out _));
    }

    /// <summary>
    /// get_server_info ツールがリポジトリから楽曲件数を取得し、構成情報および WPF IPC 状態を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetServerInfoAsync_正常取得時_構成情報と楽曲件数を返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, dataDirectory: @"C:\TestData\AudioEffector");
        var tracks = new List<Track>
        {
            new() { Title = "Track 1", FilePath = @"C:\Music\1.mp3" },
            new() { Title = "Track 2", FilePath = @"C:\Music\2.flac" },
            new() { Title = "Track 3", FilePath = @"C:\Music\3.wav" },
        };
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracks);

        // Act
        CallToolResult result = await sut.GetServerInfoAsync();

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(McpServiceCollectionExtensions.SERVER_NAME, data.GetProperty("serverName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("version").GetString()));
        Assert.Equal("stdio", data.GetProperty("transport").GetString());
        Assert.Equal(@"C:\TestData\AudioEffector", data.GetProperty("dataDirectory").GetString());
        Assert.Equal(3, data.GetProperty("libraryTrackCount").GetInt32());
        Assert.Equal(SystemTools.WPF_IPC_STATUS_NOT_CONFIGURED, data.GetProperty("wpfIpcStatus").GetString());
    }

    /// <summary>
    /// get_server_info ツールが呼び出し元の CancellationToken をリポジトリへ伝播し、キャンセル時は OPERATION_CANCELLED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetServerInfoAsync_キャンセル要求時_CancellationTokenを伝播しOPERATION_CANCELLEDを返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _);
        using var cts = new CancellationTokenSource();
        CancellationToken passedToken = default;

        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => passedToken = ct)
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act
        CallToolResult result = await sut.GetServerInfoAsync(cts.Token);

        // Assert
        Assert.Equal(cts.Token, passedToken);
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.OPERATION_CANCELLED, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// get_server_info 実行中にリポジトリが未捕捉例外を送出した場合、INTERNAL_ERROR へ変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task GetServerInfoAsync_リポジトリ例外発生時_INTERNAL_ERRORを返し内部詳細を露出しない()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out Mock<ILogger<SystemTools>> loggerMock);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET_REPO_INTERNAL_ERROR"));

        // Act
        CallToolResult result = await sut.GetServerInfoAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SECRET_REPO_INTERNAL_ERROR", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, doc.RootElement.GetProperty("error").GetProperty("code").GetString());

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// get_server_info 実行中にファイルロックタイムアウトが発生した場合、CONFLICT_LOCK_TIMEOUT へ変換されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetServerInfoAsync_ファイルロックタイムアウト発生時_CONFLICT_LOCK_TIMEOUTを返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileLockTimeoutException(@"C:\SecretPath\tracks.json", TimeSpan.FromSeconds(5)));

        // Act
        CallToolResult result = await sut.GetServerInfoAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SecretPath", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.Equal(McpErrorCodes.CONFLICT_LOCK_TIMEOUT, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static SystemTools CreateSut(
        out Mock<ITrackRepository> trackRepoMock,
        out Mock<ILogger<SystemTools>> loggerMock,
        string dataDirectory = @"C:\DefaultData\AudioEffector",
        McpTransportMode transport = McpTransportMode.Stdio)
    {
        var options = new McpServerOptions
        {
            Transport = transport,
            DataDirectory = dataDirectory,
        };

        var dataDirProviderMock = new Mock<IDataDirectoryProvider>();
        dataDirProviderMock.SetupGet(p => p.DataDirectory).Returns(dataDirectory);
        dataDirProviderMock.SetupGet(p => p.IsCustomDirectory).Returns(true);

        trackRepoMock = new Mock<ITrackRepository>();
        loggerMock = new Mock<ILogger<SystemTools>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        return new SystemTools(options, dataDirProviderMock.Object, trackRepoMock.Object, loggerMock.Object);
    }

    private static string GetSingleText(CallToolResult result)
    {
        TextContentBlock block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }
}
