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
        audioService
            .Setup(a => a.PlayTrackAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
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
        audioService.Verify(a => a.PlayTrackAsync(track, It.IsAny<CancellationToken>()), Times.Once);
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

    /// <summary>
    /// カスタム --data-dir 使用時に trackId が MCP 側リポジトリで filePath へ解決されてから WPF 側へ送信されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_カスタムデータディレクトリ使用時のtrackId指定_ローカルでfilePath解決して送信する()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.CustomDirTest.{Guid.NewGuid():N}";
        var trackId = TrackId.New();
        const string expectedPath = @"C:\CustomMusic\CustomTrack.flac";
        var localTrack = new Track(
            id: trackId,
            filePath: AudioPath.Create(expectedPath),
            title: "Custom Track",
            artist: "Custom Artist",
            album: "Custom Album",
            duration: TimeSpan.FromSeconds(120),
            year: 2025,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "FLAC",
            genre: "Jazz");

        var localRepo = new Mock<ITrackRepository>();
        localRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localTrack);

        var dataDirProvider = new Mock<IDataDirectoryProvider>();
        dataDirProvider.SetupGet(p => p.IsCustomDirectory).Returns(true);

        AppIpcRequest? capturedRequest = null;
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppIpcRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(AppIpcResponse.SuccessPlayback(
                PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING,
                new AppIpcTrackInfo(trackId.Value, "Custom Track", "Custom Artist", "Custom Album", 120, "02:00", expectedPath)));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance,
            dataDirProvider.Object,
            localRepo.Object);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(trackId, filePath: null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.TrackId);
        Assert.Equal(expectedPath, capturedRequest.FilePath);
        Assert.NotNull(capturedRequest.DeadlineUtc);
    }

    /// <summary>
    /// カスタム --data-dir 使用時に MCP 側リポジトリに存在しない trackId を指定した場合、NOT_FOUND を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_カスタムデータディレクトリに存在しないtrackId指定時_NOT_FOUNDを返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var localRepo = new Mock<ITrackRepository>();
        localRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Track?)null);

        var dataDirProvider = new Mock<IDataDirectoryProvider>();
        dataDirProvider.SetupGet(p => p.IsCustomDirectory).Returns(true);

        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.Unused.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromSeconds(1),
            connectTimeout: TimeSpan.FromMilliseconds(200),
            NullLogger<IpcPlaybackControlGateway>.Instance,
            dataDirProvider.Object,
            localRepo.Object);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(trackId, filePath: null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.NOT_FOUND, result.ErrorCode);
    }

    /// <summary>
    /// IPC 成功応答で track または playbackState が欠損している場合、いずれも IPC_CONNECTION_FAILED として分類されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlayTrackAsync_IPC成功応答の必須項目欠損時_IPC_CONNECTION_FAILEDを返す(bool includeTrackWithoutPlaybackState)
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.MalformedResp.{Guid.NewGuid():N}";
        AppIpcResponse malformedResponse = includeTrackWithoutPlaybackState
            ? new AppIpcResponse(
                Ok: true,
                PlaybackState: null,
                Track: new AppIpcTrackInfo(Guid.NewGuid(), "Title", "Artist", "Album", 100, "01:40", @"C:\Music\a.flac"))
            : AppIpcResponse.SuccessPing();

        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(malformedResponse);

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
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// トラック解決がクライアント期限を超過した場合、クライアントが IPC_CONNECTION_FAILED を受け取った後に WPF 側で再生が開始されないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_トラック解決がクライアント期限超過時_タイムアウト後に再生が開始されない()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.DeadlineTest.{Guid.NewGuid():N}";
        var trackId = TrackId.New();
        var track = new Track(
            id: trackId,
            filePath: AudioPath.Create(@"C:\Music\SlowResolve.flac"),
            title: "Slow",
            artist: "Artist",
            album: "Album",
            duration: TimeSpan.FromSeconds(100),
            year: 2025,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "FLAC",
            genre: "Pop");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .Returns(async (TrackId _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), ct);
                return track;
            });

        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);

        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayTrackAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromMilliseconds(150),
            connectTimeout: TimeSpan.FromMilliseconds(150),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayTrackResponse> result = await sut.PlayTrackAsync(trackId, filePath: null);
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
        audioService.Verify(a => a.PlayTrackAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
