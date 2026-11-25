using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Ipc;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AudioEffector.Tests.McpServer.Gateways;

public sealed class IpcPlaybackControlGatewayTests
{
    /// <summary>
    /// 起動中の AppIpcServer へ trackId を送信し、IUiDispatcher 経由の再生開始と PlayTrackResponse の返却が行われることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_IPCサーバ起動中でtrackId指定時_再生開始されPlayTrackResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.PlayTest.{Guid.NewGuid():N}";
        var trackId = TrackId.New();
        var track = new Track(
            id: trackId,
            filePath: AudioPath.Create(@"C:\Music\TestTrack.flac"),
            title: "Night Drive",
            artist: "Synth Wave",
            album: "Neon City",
            duration: TimeSpan.FromSeconds(185.25),
            year: 2025,
            trackNumber: 2,
            bitrate: 1000,
            sampleRate: 48000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Electronic");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);

        var audioService = new Mock<IAudioService>();
        int dispatchCount = 0;
        var uiDispatcher = new Mock<IUiDispatcher>();
        uiDispatcher
            .Setup(d => d.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
                dispatchCount++;
                action();
            });

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            uiDispatcher.Object,
            fileExists: _ => true);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(trackId, filePath: null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("playing", result.Value.PlaybackState);
        Assert.Equal(trackId.Value, result.Value.Track.Id);
        Assert.Equal("Night Drive", result.Value.Track.Title);
        Assert.Equal("Synth Wave", result.Value.Track.Artist);
        Assert.Equal("Neon City", result.Value.Track.Album);
        Assert.Equal(185.25, result.Value.Track.DurationSeconds);
        Assert.Equal("03:05", result.Value.Track.DurationString);
        Assert.Equal(@"C:\Music\TestTrack.flac", result.Value.Track.FilePath);
        Assert.Equal(1, dispatchCount);
        audioService.Verify(a => a.PlayTrack(track), Times.Once);
    }

    /// <summary>
    /// WPF 本体の IPC サーバが未起動の場合に WPF_APP_NOT_RUNNING エラーおよび launch_app 案内メッセージが返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_IPCサーバ未起動時_WPF_APP_NOT_RUNNINGとlaunch_app案内を返す()
    {
        // Arrange
        string missingPipeName = $"AudioEffector.Ipc.Missing.{Guid.NewGuid():N}";
        var sut = new IpcPlaybackControlGateway(
            missingPipeName,
            commandTimeout: TimeSpan.FromMilliseconds(300),
            connectTimeout: TimeSpan.FromMilliseconds(100),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(TrackId.New(), filePath: null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, result.ErrorCode);
        Assert.Contains("launch_app", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// IPC サーバ側が NOT_FOUND / FILE_NOT_FOUND エラーを返した場合に対応する標準エラーコードへ変換されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(AppIpcErrorCodes.NOT_FOUND, McpErrorCodes.NOT_FOUND)]
    [InlineData(AppIpcErrorCodes.FILE_NOT_FOUND, McpErrorCodes.FILE_NOT_FOUND)]
    public async Task PlayTrackAsync_IPCサーバがエラー応答を返す場合_対応する標準エラーコードを返す(
        string serverErrorCode,
        string expectedMcpErrorCode)
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.ErrTest.{Guid.NewGuid():N}";
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppIpcResponse.Failure(serverErrorCode, "internal diagnostic detail C:\\secret\\path.flac"));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(TrackId.New(), filePath: null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedMcpErrorCode, result.ErrorCode);
        Assert.DoesNotContain(@"C:\secret", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// IPC サーバへの接続後に応答がタイムアウトした場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_IPC応答タイムアウト時_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.TimeoutTest.{Guid.NewGuid():N}";
        var slowHandler = new Mock<IAppIpcCommandHandler>();
        slowHandler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (AppIpcRequest _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                return AppIpcResponse.SuccessPing();
            });

        using var server = new AppIpcServer(pipeName, slowHandler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromMilliseconds(150),
            connectTimeout: TimeSpan.FromMilliseconds(150),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(TrackId.New(), filePath: null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// 呼び出し元の CancellationToken がすでにキャンセル済みの場合に OperationCanceledException を送出することを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_キャンセル済みトークン指定時_OperationCanceledExceptionを送出する()
    {
        // Arrange
        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.CancelTest.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromSeconds(1),
            connectTimeout: TimeSpan.FromMilliseconds(200),
            NullLogger<IpcPlaybackControlGateway>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.PlayTrackAsync(TrackId.New(), filePath: null, cts.Token));
    }
}
