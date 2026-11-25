using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Protocol;
using AudioEffector.McpServer.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;

namespace AudioEffector.Tests.McpServer.Tools;

public sealed class PlaybackToolsTests
{
    /// <summary>
    /// trackId を指定して play_track を呼び出した場合、ゲートウェイへ TrackId が渡され統一 JSON 応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_有効なtrackId指定時_再生開始結果JSONを返す()
    {
        // Arrange
        var trackGuid = Guid.NewGuid();
        var expectedResponse = new PlayTrackResponse(
            PlaybackState: "playing",
            Track: new PlayedTrackDto(
                Id: trackGuid,
                Title: "Blue Note Morning",
                Artist: "Miles Trio",
                Album: "Midnight Sessions",
                DurationSeconds: 225.5,
                DurationString: "03:45",
                FilePath: @"C:\Music\Jazz\BlueNote.flac"));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(
                It.Is<TrackId?>(id => id.HasValue && id.Value.Value == trackGuid),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Success(expectedResponse));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: trackGuid.ToString("D"));

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("playing", data.GetProperty("playbackState").GetString());
        JsonElement track = data.GetProperty("track");
        Assert.Equal(trackGuid.ToString("D"), track.GetProperty("id").GetString());
        Assert.Equal("Blue Note Morning", track.GetProperty("title").GetString());
        Assert.Equal("Miles Trio", track.GetProperty("artist").GetString());
        Assert.Equal("Midnight Sessions", track.GetProperty("album").GetString());
        Assert.Equal(225.5, track.GetProperty("durationSeconds").GetDouble());
        Assert.Equal("03:45", track.GetProperty("durationString").GetString());
        Assert.Equal(@"C:\Music\Jazz\BlueNote.flac", track.GetProperty("filePath").GetString());
    }

    /// <summary>
    /// filePath を指定して play_track を呼び出した場合、前後空白がトリムされてゲートウェイへ渡され正常応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_filePath指定時_トリム済みパスで再生開始結果JSONを返す()
    {
        // Arrange
        const string filePath = @"C:\Music\Rock\Track01.mp3";
        var expectedResponse = new PlayTrackResponse(
            PlaybackState: "playing",
            Track: new PlayedTrackDto(
                Id: Guid.NewGuid(),
                Title: "Track01",
                Artist: "Rock Band",
                Album: "First Album",
                DurationSeconds: 180.0,
                DurationString: "03:00",
                FilePath: filePath));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(null, filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Success(expectedResponse));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(filePath: $"  {filePath}  ");

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(filePath, doc.RootElement.GetProperty("data").GetProperty("track").GetProperty("filePath").GetString());
        gateway.Verify(g => g.PlayTrackAsync(null, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// trackId と filePath の両方が未指定または空白の場合に INVALID_PARAMETER エラーを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    public async Task PlayTrackAsync_trackIdとfilePath両方未指定時_INVALID_PARAMETERを返す(string? trackId, string? filePath)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: trackId, filePath: filePath);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// trackId に不正な GUID 形式または空 GUID を指定した場合に INVALID_PARAMETER エラーを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("invalid-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task PlayTrackAsync_不正なtrackId指定時_INVALID_PARAMETERを返す(string invalidTrackId)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: invalidTrackId);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// filePath に相対パスを指定した場合に送信前で INVALID_PARAMETER エラーを返しゲートウェイを呼び出さないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("song.wav")]
    [InlineData(@".\music\song.flac")]
    [InlineData(@"..\song.mp3")]
    public async Task PlayTrackAsync_filePathに相対パス指定時_INVALID_PARAMETERを返す(string relativeFilePath)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(filePath: relativeFilePath);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// WPF 本体が未起動の場合に WPF_APP_NOT_RUNNING エラーおよび launch_app 案内メッセージが返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_WPF未起動時_WPF_APP_NOT_RUNNINGとlaunch_app案内を返す()
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Failure(
                McpErrorCodes.WPF_APP_NOT_RUNNING,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.WPF_APP_NOT_RUNNING)));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: Guid.NewGuid().ToString("D"));

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, error.GetProperty("code").GetString());
        Assert.Contains("launch_app", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// ゲートウェイが NOT_FOUND / FILE_NOT_FOUND / IPC_CONNECTION_FAILED を返した場合に対応するエラーコードが返ることを検証します。
    /// </summary>
    [Theory]
    [InlineData(McpErrorCodes.NOT_FOUND)]
    [InlineData(McpErrorCodes.FILE_NOT_FOUND)]
    [InlineData(McpErrorCodes.IPC_CONNECTION_FAILED)]
    public async Task PlayTrackAsync_ゲートウェイ失敗結果時_対応するエラーコードを返す(string errorCode)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Failure(
                errorCode,
                McpErrorMapper.GetPublicMessage(errorCode)));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: Guid.NewGuid().ToString("D"));

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.Equal(errorCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// ゲートウェイ実行中にキャンセル例外または予期しない例外が発生した場合に統一エラー応答へ変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_予期しない例外発生時_INTERNAL_ERRORを返し機密詳細を露出しない()
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(@"secret internal path C:\Users\secret\file.flac"));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: Guid.NewGuid().ToString("D"));

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, error.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// play_track ツールの Description 属性に launch_app による事前起動案内が明記されていることを検証します。
    /// </summary>
    [Fact]
    public void PlayTrackAsync_ツール説明属性_launch_app案内が含まれる()
    {
        // Arrange
        MethodInfo? method = typeof(PlaybackTools).GetMethod(nameof(PlaybackTools.PlayTrackAsync));
        Assert.NotNull(method);

        // Act
        DescriptionAttribute? description = method.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        Assert.NotNull(description);
        Assert.Contains("launch_app", description.Description, StringComparison.Ordinal);
    }

    private static JsonDocument ParsePayload(CallToolResult result)
    {
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return JsonDocument.Parse(textBlock.Text);
    }
}
