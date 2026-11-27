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

namespace AudioEffector.McpServer.Tests.Gateways;

public sealed class IpcPlaybackControlGatewayTests : IDisposable
{
    private readonly string _tempDir;

    public IpcPlaybackControlGatewayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorIpcGatewayTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // クリーンアップエラーは無視
        }
    }

    private string CreateExistingAudioFile(string relativePath)
    {
        string fullPath = Path.Combine(_tempDir, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, [0x01]);
        return fullPath;
    }

    /// <summary>
    /// 起動中の AppIpcServer へ trackId を送信し、IUiDispatcher 経由の再生開始と PlayTrackResponse の返却が行われることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_IPCサーバ起動中でtrackId指定時_再生開始されPlayTrackResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.PlayTest.{Guid.NewGuid():N}";
        string audioFilePath = CreateExistingAudioFile("TestTrack.flac");
        var trackId = TrackId.New();
        var track = new Track(
            id: trackId,
            filePath: AudioPath.Create(audioFilePath),
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
            uiDispatcher.Object);

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
        Assert.Equal(audioFilePath, result.Value.Track.FilePath);
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

        var wpfGeneratedId = Guid.NewGuid();
        AppIpcRequest? capturedRequest = null;
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppIpcRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(AppIpcResponse.SuccessPlayback(
                PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING,
                new AppIpcTrackInfo(wpfGeneratedId, "Custom Track", "Custom Artist", "Custom Album", 120, "02:00", expectedPath)));

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
        Assert.NotNull(result.Value);
        Assert.Equal(trackId.Value, result.Value.Track.Id);
        Assert.NotEqual(wpfGeneratedId, result.Value.Track.Id);
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
            filePath: AudioPath.Create(CreateExistingAudioFile("SlowResolve.flac")),
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
            ImmediateUiDispatcher.Instance);

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

    /// <summary>
    /// 起動中の AppIpcServer へ play_album コマンドを送信し、PlayCollectionResponse が返却されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayAlbumAsync_IPCサーバ起動中でalbumId指定時_再生開始されPlayCollectionResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.PlayAlbumTest.{Guid.NewGuid():N}";
        var albumId = AlbumId.New();
        var track1 = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(CreateExistingAudioFile(@"Album\01.flac")),
            title: "Track 1",
            artist: "Miles Trio",
            album: "Midnight Sessions",
            duration: TimeSpan.FromSeconds(200),
            year: 2024,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 48000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Jazz",
            albumId: albumId);

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1]);

        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);

        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                null,
                true,
                false,
                "Midnight Sessions",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(track1);
        audioService.SetupGet(a => a.IsShuffleEnabled).Returns(true);
        audioService.SetupGet(a => a.IsRepeatEnabled).Returns(false);

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            ImmediateUiDispatcher.Instance);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayCollectionResponse> result = await sut.PlayAlbumAsync(
            albumId: albumId,
            albumName: null,
            artist: null,
            startTrackId: null,
            startTrackIndex: null,
            shuffle: true,
            repeat: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("album", result.Value.SourceType);
        Assert.Equal(albumId.Value, result.Value.SourceId);
        Assert.Equal("Midnight Sessions", result.Value.SourceName);
        Assert.Equal("playing", result.Value.PlaybackState);
        Assert.Equal(1, result.Value.TotalTracks);
        Assert.True(result.Value.IsShuffleEnabled);
        Assert.False(result.Value.IsRepeatEnabled);
        Assert.Equal(track1.Id.Value, result.Value.Track.Id);
    }

    /// <summary>
    /// 起動中の AppIpcServer へ play_playlist コマンドを送信し、PlayCollectionResponse が返却されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_IPCサーバ起動中でplaylistId指定時_再生開始されPlayCollectionResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.PlayPlaylistTest.{Guid.NewGuid():N}";
        var track1 = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(CreateExistingAudioFile(@"Playlist\01.flac")),
            title: "Drive Song",
            artist: "Synth Wave",
            album: "Highway",
            duration: TimeSpan.FromSeconds(240),
            year: 2025,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 48000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Electronic");

        var playlist = new UserPlaylist(PlaylistId.New(), "Drive Mix", [track1.Id]);

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1]);

        var playlistRepo = new Mock<IPlaylistRepository>();
        playlistRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([playlist]);

        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);
        var playlistService = new PlaylistApplicationService(
            playlistRepo.Object,
            trackRepo.Object,
            new Mock<IEventBus>().Object);

        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                null,
                false,
                true,
                "Drive Mix",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(track1);
        audioService.SetupGet(a => a.IsShuffleEnabled).Returns(false);
        audioService.SetupGet(a => a.IsRepeatEnabled).Returns(true);

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            playlistService: playlistService);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayCollectionResponse> result = await sut.PlayPlaylistAsync(
            playlistId: playlist.Id,
            playlistName: null,
            startTrackId: null,
            startTrackIndex: null,
            shuffle: false,
            repeat: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("playlist", result.Value.SourceType);
        Assert.Equal(playlist.Id.Value, result.Value.SourceId);
        Assert.Equal("Drive Mix", result.Value.SourceName);
        Assert.Equal("playing", result.Value.PlaybackState);
        Assert.Equal(1, result.Value.TotalTracks);
        Assert.False(result.Value.IsShuffleEnabled);
        Assert.True(result.Value.IsRepeatEnabled);
        Assert.Equal(track1.Id.Value, result.Value.Track.Id);
    }

    /// <summary>
    /// PlayAlbumAsync で IPC サーバ未起動の場合に WPF_APP_NOT_RUNNING を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayAlbumAsync_IPCサーバ未起動時_WPF_APP_NOT_RUNNINGを返す()
    {
        // Arrange
        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.MissingAlbum.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromMilliseconds(300),
            connectTimeout: TimeSpan.FromMilliseconds(100),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayCollectionResponse> result = await sut.PlayAlbumAsync(
            AlbumId.New(), null, null, null, null, null, null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, result.ErrorCode);
    }

    /// <summary>
    /// カスタム --data-dir 使用時、WPF 既定ライブラリと同名で内容が異なるプレイリストが存在しても、カスタム側で曲順・開始位置を解決して WPF へ再生させることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_カスタムデータディレクトリと既定側に同名別内容のプレイリストが存在する場合_カスタム側の曲順と開始位置で再生する()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.CustomPlaylist.{Guid.NewGuid():N}";

        // カスタム側プレイリスト（[C1, C2] を 2 曲目 C2 から開始）
        var customTrack1 = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(CreateExistingAudioFile(@"CustomMusic\C1.flac")),
            title: "Custom 1",
            artist: "Custom Artist",
            album: "Custom Album",
            duration: TimeSpan.FromSeconds(100),
            year: 2025,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "FLAC",
            genre: "Jazz");
        var customTrack2 = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(CreateExistingAudioFile(@"CustomMusic\C2.flac")),
            title: "Custom 2",
            artist: "Custom Artist",
            album: "Custom Album",
            duration: TimeSpan.FromSeconds(120),
            year: 2025,
            trackNumber: 2,
            bitrate: 1000,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "FLAC",
            genre: "Jazz");
        var customPlaylist = new UserPlaylist(PlaylistId.New(), "Drive Mix", [customTrack1.Id, customTrack2.Id]);

        var customTrackRepo = new Mock<ITrackRepository>();
        customTrackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([customTrack1, customTrack2]);
        var customPlaylistRepo = new Mock<IPlaylistRepository>();
        customPlaylistRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([customPlaylist]);
        var customLibraryService = new LibraryApplicationService(
            customTrackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);
        var customPlaylistService = new PlaylistApplicationService(
            customPlaylistRepo.Object,
            customTrackRepo.Object,
            new Mock<IEventBus>().Object);

        // WPF 既定側には同名の別プレイリスト（[Default1]）が存在する
        var defaultTrack = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\DefaultMusic\D1.flac"),
            title: "Default 1",
            artist: "Default Artist",
            album: "Default Album",
            duration: TimeSpan.FromSeconds(200),
            year: 2024,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "FLAC",
            genre: "Rock");
        var defaultPlaylist = new UserPlaylist(PlaylistId.New(), "Drive Mix", [defaultTrack.Id]);

        var wpfTrackRepo = new Mock<ITrackRepository>();
        wpfTrackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([defaultTrack]);
        wpfTrackRepo
            .Setup(r => r.GetByPathAsync(It.IsAny<AudioPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Track?)null);
        var wpfMetadataExtractor = new Mock<IMetadataExtractor>();
        wpfMetadataExtractor
            .Setup(m => m.ExtractMetadataAsync(It.Is<AudioPath>(p => p.Value == customTrack1.FilePath), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Track { FilePath = customTrack1.FilePath, Title = customTrack1.Title, Artist = customTrack1.Artist, Album = customTrack1.Album });
        wpfMetadataExtractor
            .Setup(m => m.ExtractMetadataAsync(It.Is<AudioPath>(p => p.Value == customTrack2.FilePath), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Track { FilePath = customTrack2.FilePath, Title = customTrack2.Title, Artist = customTrack2.Artist, Album = customTrack2.Album });
        var wpfPlaylistRepo = new Mock<IPlaylistRepository>();
        wpfPlaylistRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([defaultPlaylist]);
        var wpfLibraryService = new LibraryApplicationService(
            wpfTrackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            wpfMetadataExtractor.Object,
            new Mock<IEventBus>().Object);
        var wpfPlaylistService = new PlaylistApplicationService(
            wpfPlaylistRepo.Object,
            wpfTrackRepo.Object,
            new Mock<IEventBus>().Object);

        IReadOnlyList<Track>? capturedTracks = null;
        int? capturedStartIndex = null;
        string? capturedSourceName = null;
        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                It.IsAny<Track?>(),
                false,
                false,
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Track>, Track?, bool?, bool?, string?, int?, CancellationToken>((tracks, _, _, _, src, idx, _) =>
            {
                capturedTracks = tracks;
                capturedSourceName = src;
                capturedStartIndex = idx;
            })
            .ReturnsAsync((IReadOnlyList<Track> tracks, Track? start, bool? _, bool? _, string? _, int? idx, CancellationToken _) =>
                start ?? tracks[idx ?? 0]);

        var handler = new PlaybackIpcCommandHandler(
            wpfTrackRepo.Object,
            wpfLibraryService,
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            playlistService: wpfPlaylistService);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var dataDirProvider = new Mock<IDataDirectoryProvider>();
        dataDirProvider.SetupGet(p => p.IsCustomDirectory).Returns(true);

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance,
            dataDirProvider.Object,
            customTrackRepo.Object,
            customLibraryService,
            customPlaylistService,
            fileExists: _ => true);

        // Act
        PlaybackControlResult<PlayCollectionResponse> result = await sut.PlayPlaylistAsync(
            playlistId: null,
            playlistName: "Drive Mix",
            startTrackId: null,
            startTrackIndex: 1,
            shuffle: false,
            repeat: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(customPlaylist.Id.Value, result.Value.SourceId);
        Assert.Equal("Drive Mix", result.Value.SourceName);
        Assert.Equal(2, result.Value.TotalTracks);
        Assert.Equal(customTrack2.Id.Value, result.Value.Track.Id);
        Assert.Equal(customTrack2.FilePath, result.Value.Track.FilePath);
        Assert.NotNull(capturedTracks);
        Assert.Equal([customTrack1.FilePath, customTrack2.FilePath], capturedTracks.Select(t => t.FilePath).ToArray());
        Assert.Equal(1, capturedStartIndex);
        Assert.Equal("Drive Mix", capturedSourceName);
    }

    /// <summary>
    /// 同名アルバムの曖昧一致エラー（AMBIGUOUS_ALBUM）が IPC 経由で返却された場合、公開エラーメッセージに albumId と artist の絞り込み案内が含まれることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayAlbumAsync_同名アルバム曖昧一致時_albumIdとartistの絞り込み案内を返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.AmbiguousAlbum.{Guid.NewGuid():N}";
        var trackA = new Track
        {
            FilePath = @"C:\Music\A\01.flac",
            Title = "Song A",
            Artist = "Artist A",
            Album = "Greatest Hits",
            TrackNumber = 1,
        };
        var trackB = new Track
        {
            FilePath = @"C:\Music\B\01.flac",
            Title = "Song B",
            Artist = "Artist B",
            Album = "Greatest Hits",
            TrackNumber = 1,
        };

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trackA, trackB]);
        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            new Mock<IAudioService>().Object,
            ImmediateUiDispatcher.Instance);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlayCollectionResponse> result = await sut.PlayAlbumAsync(
            albumId: null,
            albumName: "Greatest Hits",
            artist: null,
            startTrackId: null,
            startTrackIndex: null,
            shuffle: null,
            repeat: null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, result.ErrorCode);
        Assert.Contains("albumId", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("artist", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(result.Details);
    }

    /// <summary>
    /// 起動中の AppIpcServer へ get_playback_state を送信し、再生中・一時停止中・停止中の 3 状態が PlaybackStateResponse として正確に取得できることを検証します。
    /// </summary>
    [Theory]
    [InlineData(true, false, "playing")]
    [InlineData(false, true, "paused")]
    [InlineData(false, false, "stopped")]
    public async Task GetPlaybackStateAsync_IPCサーバ起動中で各状態時_正確なPlaybackStateResponseを返す(
        bool isPlaying,
        bool isPaused,
        string expectedState)
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.GetState.{expectedState}.{Guid.NewGuid():N}";
        var trackId = TrackId.New();
        var track = new Track(
            id: trackId,
            filePath: AudioPath.Create(@"C:\Music\Jazz\BlueNote.flac"),
            title: "Blue Note Morning",
            artist: "Miles Trio",
            album: "Midnight Sessions",
            duration: TimeSpan.FromSeconds(225.5),
            year: 2024,
            trackNumber: 1,
            bitrate: 1411,
            sampleRate: 96000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Jazz");

        var trackRepo = new Mock<ITrackRepository>();
        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);

        var audioService = new Mock<IAudioService>();
        audioService.SetupGet(a => a.IsPlaying).Returns(isPlaying);
        audioService.SetupGet(a => a.IsPaused).Returns(isPaused);
        audioService.SetupGet(a => a.CurrentTrack).Returns(isPlaying || isPaused ? track : null);
        audioService.SetupGet(a => a.CurrentTime).Returns(isPlaying || isPaused ? TimeSpan.FromSeconds(45.5) : TimeSpan.Zero);
        audioService.SetupGet(a => a.TotalTime).Returns(isPlaying || isPaused ? TimeSpan.FromSeconds(225.5) : TimeSpan.Zero);
        audioService.SetupGet(a => a.Volume).Returns(0.65f);
        audioService.SetupGet(a => a.IsShuffleEnabled).Returns(true);
        audioService.SetupGet(a => a.IsRepeatEnabled).Returns(true);
        audioService.SetupGet(a => a.UserQueue).Returns([track]);
        audioService.SetupGet(a => a.AlbumQueue).Returns([track, track]);

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            ImmediateUiDispatcher.Instance);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackStateResponse> result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expectedState, result.Value.PlaybackState);
        Assert.Equal(0.65, result.Value.Volume);
        Assert.True(result.Value.IsShuffleEnabled);
        Assert.True(result.Value.IsRepeatEnabled);
        Assert.Equal(1, result.Value.UserQueueCount);
        Assert.Equal(2, result.Value.AlbumQueueCount);

        if (expectedState == "stopped")
        {
            Assert.Null(result.Value.Track);
            Assert.Equal(0.0, result.Value.PositionSeconds);
            Assert.Equal("00:00", result.Value.PositionString);
            Assert.Equal(0.0, result.Value.DurationSeconds);
            Assert.Equal("00:00", result.Value.DurationString);
        }
        else
        {
            Assert.NotNull(result.Value.Track);
            Assert.Equal(trackId.Value, result.Value.Track.Id);
            Assert.Equal("Blue Note Morning", result.Value.Track.Title);
            Assert.Equal(45.5, result.Value.PositionSeconds);
            Assert.Equal("00:45", result.Value.PositionString);
            Assert.Equal(225.5, result.Value.DurationSeconds);
            Assert.Equal("03:45", result.Value.DurationString);
        }
    }

    /// <summary>
    /// WPF 本体の IPC サーバが未起動の状態で get_playback_state を呼び出した場合、WPF_APP_NOT_RUNNING を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackStateAsync_IPCサーバ未起動時_WPF_APP_NOT_RUNNINGを返す()
    {
        // Arrange
        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.MissingState.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromMilliseconds(300),
            connectTimeout: TimeSpan.FromMilliseconds(100),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackStateResponse> result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, result.ErrorCode);
        Assert.Contains("launch_app", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// get_playback_state の IPC 成功応答で必須フィールドが欠損または不正な playbackState の場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("invalid_state", false)]
    [InlineData("playing", true)]
    [InlineData("stopped", false)]
    public async Task GetPlaybackStateAsync_IPC成功応答の必須項目欠損または不正状態時_IPC_CONNECTION_FAILEDを返す(
        string playbackState,
        bool missingTrackOnly)
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.MalformedState.{Guid.NewGuid():N}";
        AppIpcResponse malformedResponse = missingTrackOnly
            ? AppIpcResponse.SuccessPlaybackState(
                playbackState: playbackState,
                track: null,
                positionSeconds: 10,
                positionString: "00:10",
                durationSeconds: 100,
                durationString: "01:40",
                volume: 0.8,
                isShuffleEnabled: false,
                isRepeatEnabled: false,
                userQueueCount: 0,
                albumQueueCount: 0)
            : new AppIpcResponse(Ok: true, PlaybackState: playbackState);

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
        PlaybackControlResult<PlaybackStateResponse> result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// 起動中の AppIpcServer へ基本再生制御の 5 コマンドを送信し、各コマンドに対応する PlaybackActionResponse が返却されることを検証します。
    /// </summary>
    [Fact]
    public async Task TransportCommands_IPCサーバ起動中_5コマンドが送受信されPlaybackActionResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.Transport.{Guid.NewGuid():N}";
        var track = new AppIpcTrackInfo(
            Id: Guid.NewGuid(),
            Title: "Blue Note Morning",
            Artist: "Miles Trio",
            Album: "Midnight Sessions",
            DurationSeconds: 225.5,
            DurationString: "03:45",
            FilePath: @"C:\Music\Jazz\BlueNote.flac");

        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppIpcRequest req, CancellationToken _) => req.Command switch
            {
                AppIpcServer.COMMAND_PAUSE_PLAYBACK => AppIpcResponse.SuccessPlaybackControl(PlaybackIpcCommandHandler.PLAYBACK_STATE_PAUSED, track),
                AppIpcServer.COMMAND_RESUME_PLAYBACK => AppIpcResponse.SuccessPlaybackControl(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, track),
                AppIpcServer.COMMAND_STOP_PLAYBACK => AppIpcResponse.SuccessPlaybackControl(PlaybackIpcCommandHandler.PLAYBACK_STATE_STOPPED, null),
                AppIpcServer.COMMAND_NEXT_TRACK => AppIpcResponse.SuccessPlaybackControl(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, track),
                AppIpcServer.COMMAND_PREVIOUS_TRACK => AppIpcResponse.SuccessPlaybackControl(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, track),
                _ => AppIpcResponse.Failure(AppIpcErrorCodes.UNSUPPORTED_COMMAND, "unsupported"),
            });

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackActionResponse> pauseRes = await sut.PausePlaybackAsync();
        PlaybackControlResult<PlaybackActionResponse> resumeRes = await sut.ResumePlaybackAsync();
        PlaybackControlResult<PlaybackActionResponse> stopRes = await sut.StopPlaybackAsync();
        PlaybackControlResult<PlaybackActionResponse> nextRes = await sut.NextTrackAsync();
        PlaybackControlResult<PlaybackActionResponse> prevRes = await sut.PreviousTrackAsync();

        // Assert
        Assert.True(pauseRes.IsSuccess);
        Assert.Equal(AppIpcServer.COMMAND_PAUSE_PLAYBACK, pauseRes.Value!.Action);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PAUSED, pauseRes.Value.PlaybackState);
        Assert.Equal(track.Id, pauseRes.Value.Track!.Id);

        Assert.True(resumeRes.IsSuccess);
        Assert.Equal(AppIpcServer.COMMAND_RESUME_PLAYBACK, resumeRes.Value!.Action);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, resumeRes.Value.PlaybackState);

        Assert.True(stopRes.IsSuccess);
        Assert.Equal(AppIpcServer.COMMAND_STOP_PLAYBACK, stopRes.Value!.Action);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_STOPPED, stopRes.Value.PlaybackState);
        Assert.Null(stopRes.Value.Track);

        Assert.True(nextRes.IsSuccess);
        Assert.Equal(AppIpcServer.COMMAND_NEXT_TRACK, nextRes.Value!.Action);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, nextRes.Value.PlaybackState);

        Assert.True(prevRes.IsSuccess);
        Assert.Equal(AppIpcServer.COMMAND_PREVIOUS_TRACK, prevRes.Value!.Action);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, prevRes.Value.PlaybackState);
    }

    /// <summary>
    /// WPF 本体の IPC サーバが未起動の状態で基本再生制御コマンドを呼び出した場合、いずれも WPF_APP_NOT_RUNNING を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(AppIpcServer.COMMAND_PAUSE_PLAYBACK)]
    [InlineData(AppIpcServer.COMMAND_RESUME_PLAYBACK)]
    [InlineData(AppIpcServer.COMMAND_STOP_PLAYBACK)]
    [InlineData(AppIpcServer.COMMAND_NEXT_TRACK)]
    [InlineData(AppIpcServer.COMMAND_PREVIOUS_TRACK)]
    public async Task TransportCommands_IPCサーバ未起動時_WPF_APP_NOT_RUNNINGを返す(string command)
    {
        // Arrange
        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.MissingTransport.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromMilliseconds(300),
            connectTimeout: TimeSpan.FromMilliseconds(100),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackActionResponse> result = command switch
        {
            AppIpcServer.COMMAND_PAUSE_PLAYBACK => await sut.PausePlaybackAsync(),
            AppIpcServer.COMMAND_RESUME_PLAYBACK => await sut.ResumePlaybackAsync(),
            AppIpcServer.COMMAND_STOP_PLAYBACK => await sut.StopPlaybackAsync(),
            AppIpcServer.COMMAND_NEXT_TRACK => await sut.NextTrackAsync(),
            AppIpcServer.COMMAND_PREVIOUS_TRACK => await sut.PreviousTrackAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, result.ErrorCode);
        Assert.Contains("launch_app", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// IPC サーバから NO_ACTIVE_TRACK 理由コード付きの NOT_FOUND 応答を受信した場合、play_track 案内メッセージと reason 付き details を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task PausePlaybackAsync_NO_ACTIVE_TRACK受信時_play_track案内付きのNOT_FOUNDを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.NoActiveTrack.{Guid.NewGuid():N}";
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppIpcResponse.Failure(AppIpcErrorCodes.NOT_FOUND, "no active track", AppIpcErrorReasons.NO_ACTIVE_TRACK));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackActionResponse> result = await sut.PausePlaybackAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.NOT_FOUND, result.ErrorCode);
        Assert.Contains("play_track", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(result.Details);
    }

    /// <summary>
    /// 基本再生制御の IPC 成功応答で不正な playbackState または playing/paused 時の track 欠損がある場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("invalid_state")]
    [InlineData("playing")]
    [InlineData("paused")]
    public async Task PausePlaybackAsync_IPC成功応答の不正状態またはトラック欠損時_IPC_CONNECTION_FAILEDを返す(string playbackState)
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.MalformedAction.{Guid.NewGuid():N}";
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppIpcResponse(Ok: true, PlaybackState: playbackState, Track: null));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackActionResponse> result = await sut.PausePlaybackAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// 起動中の AppIpcServer へ get_playback_queue を送信し、現在トラック・予約キュー・アルバム残り曲キューが順序保持および limit 適用で取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_IPCサーバ起動中で両キュー存在時_順序保持とlimit適用されたPlaybackQueueResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.QueueTest.{Guid.NewGuid():N}";
        var currentTrack = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\Music\00.flac"),
            title: "Now Playing",
            artist: "Main Artist",
            album: "Main Album",
            duration: TimeSpan.FromSeconds(210.0),
            year: 2025,
            trackNumber: 1,
            bitrate: 1411,
            sampleRate: 48000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Jazz");
        var userTracks = Enumerable.Range(1, 3)
            .Select(i => new Track(
                id: TrackId.New(),
                filePath: AudioPath.Create($@"C:\Music\user_{i}.flac"),
                title: $"User Track {i}",
                artist: "User Artist",
                album: "User Album",
                duration: TimeSpan.FromSeconds(180 + i),
                year: 2025,
                trackNumber: (uint)i,
                bitrate: 1000,
                sampleRate: 44100,
                bitsPerSample: 16,
                format: "FLAC",
                genre: "Pop"))
            .ToList();
        var albumTracks = Enumerable.Range(1, 3)
            .Select(i => new Track(
                id: TrackId.New(),
                filePath: AudioPath.Create($@"C:\Music\album_{i}.flac"),
                title: $"Album Track {i}",
                artist: "Main Artist",
                album: "Main Album",
                duration: TimeSpan.FromSeconds(200 + i),
                year: 2025,
                trackNumber: (uint)(i + 1),
                bitrate: 1000,
                sampleRate: 44100,
                bitsPerSample: 16,
                format: "FLAC",
                genre: "Jazz"))
            .ToList();

        var trackRepo = new Mock<ITrackRepository>();
        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);
        var audioService = new Mock<IAudioService>();
        audioService.SetupGet(a => a.CurrentTrack).Returns(currentTrack);
        audioService.SetupGet(a => a.UserQueue).Returns(userTracks);
        audioService.SetupGet(a => a.AlbumQueue).Returns(albumTracks);

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            ImmediateUiDispatcher.Instance);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackQueueResponse> result = await sut.GetPlaybackQueueAsync(limit: 2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.CurrentTrack);
        Assert.Equal(currentTrack.Id.Value, result.Value.CurrentTrack.Id);
        Assert.Equal(2, result.Value.Limit);
        Assert.Equal(3, result.Value.UserQueueCount);
        Assert.Equal(3, result.Value.AlbumQueueCount);
        Assert.Equal(new[] { "User Track 1", "User Track 2" }, result.Value.UserQueue.Select(t => t.Title).ToArray());
        Assert.Equal(new[] { "Album Track 1", "Album Track 2" }, result.Value.AlbumQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// get_playback_queue 実行時に IPC サーバが未起動の場合、WPF_APP_NOT_RUNNING と launch_app 案内を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_IPCサーバ未起動時_WPF_APP_NOT_RUNNINGとlaunch_app案内を返す()
    {
        // Arrange
        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.MissingQueue.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromMilliseconds(300),
            connectTimeout: TimeSpan.FromMilliseconds(100),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackQueueResponse> result = await sut.GetPlaybackQueueAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, result.ErrorCode);
        Assert.Contains("launch_app", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// get_playback_queue の IPC 応答がタイムアウトした場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_IPC応答タイムアウト時_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.QueueTimeout.{Guid.NewGuid():N}";
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
        PlaybackControlResult<PlaybackQueueResponse> result = await sut.GetPlaybackQueueAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// get_playback_queue の IPC 成功応答で必須フィールド（userQueue / albumQueue）が欠損している場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_IPC成功応答の必須フィールド欠損時_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.MalformedQueue.{Guid.NewGuid():N}";
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppIpcResponse(Ok: true, UserQueueCount: 1, AlbumQueueCount: 0, UserQueue: null, AlbumQueue: null));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<PlaybackQueueResponse> result = await sut.GetPlaybackQueueAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// 起動中の AppIpcServer へ enqueue_tracks（trackIds + playNext: true）を送信し、EnqueueTracksResponse が返却されることを検証します。
    /// </summary>
    [Fact]
    public async Task EnqueueTracksAsync_IPCサーバ起動中でtrackIds指定時_キューに追加されEnqueueTracksResponseを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.EnqueueTest.{Guid.NewGuid():N}";
        string audioPath1 = CreateExistingAudioFile("Enqueue1.flac");
        string audioPath2 = CreateExistingAudioFile("Enqueue2.flac");
        var trackId1 = TrackId.New();
        var trackId2 = TrackId.New();
        var track1 = new Track(trackId1, AudioPath.Create(audioPath1), "Enqueue 1", "Artist A", "Album A", TimeSpan.FromSeconds(180), 2025, 1, 1000, 48000, 24, "FLAC", "Jazz");
        var track2 = new Track(trackId2, AudioPath.Create(audioPath2), "Enqueue 2", "Artist A", "Album A", TimeSpan.FromSeconds(200), 2025, 2, 1000, 48000, 24, "FLAC", "Jazz");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo.Setup(r => r.GetByIdAsync(trackId1, It.IsAny<CancellationToken>())).ReturnsAsync(track1);
        trackRepo.Setup(r => r.GetByIdAsync(trackId2, It.IsAny<CancellationToken>())).ReturnsAsync(track2);

        var libraryService = new LibraryApplicationService(
            trackRepo.Object,
            new Mock<IFavoriteRepository>().Object,
            new Mock<IMetadataExtractor>().Object,
            new Mock<IEventBus>().Object);

        var userQueue = new List<Track>();
        var audioService = new Mock<IAudioService>();
        audioService.SetupGet(a => a.UserQueue).Returns(() => userQueue);
        audioService.SetupGet(a => a.AlbumQueue).Returns(Array.Empty<Track>());
        audioService
            .Setup(a => a.EnqueueTracks(It.IsAny<IReadOnlyList<Track>>(), true))
            .Callback<IReadOnlyList<Track>, bool>((tracks, _) => userQueue.InsertRange(0, tracks));

        var uiDispatcher = new Mock<IUiDispatcher>();
        uiDispatcher.Setup(d => d.Invoke(It.IsAny<Action>())).Callback<Action>(action => action());

        var handler = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            uiDispatcher.Object);

        using var server = new AppIpcServer(pipeName, handler);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<EnqueueTracksResponse> result = await sut.EnqueueTracksAsync(
            trackIds: [trackId1, trackId2],
            filePaths: null,
            albumId: null,
            playNext: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("next", result.Value.Position);
        Assert.Equal(2, result.Value.AddedCount);
        Assert.Equal(2, result.Value.UserQueueCount);
        Assert.Equal(0, result.Value.AlbumQueueCount);
        Assert.Equal(new[] { "Enqueue 1", "Enqueue 2" }, result.Value.AddedTracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// --data-dir カスタムディレクトリ使用時に trackIds を MCP 側リポジトリで filePaths へ解決して送信し、応答の TrackId にローカル ID が復元されることを検証します。
    /// </summary>
    [Fact]
    public async Task EnqueueTracksAsync_カスタムデータディレクトリでtrackIds指定時_filePathsへ解決して送信しローカルTrackIdを維持する()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.CustomEnqueue.{Guid.NewGuid():N}";
        string audioPath = CreateExistingAudioFile("CustomTrack.flac");
        var localTrackId = TrackId.New();
        var wpfTrackId = TrackId.New();
        var localTrack = new Track(localTrackId, AudioPath.Create(audioPath), "Custom Song", "Custom Artist", "Custom Album", TimeSpan.FromSeconds(150), 2025, 1, 1000, 48000, 24, "FLAC", "Pop");

        var dataDirProvider = new Mock<IDataDirectoryProvider>();
        dataDirProvider.SetupGet(d => d.IsCustomDirectory).Returns(true);

        var localTrackRepo = new Mock<ITrackRepository>();
        localTrackRepo.Setup(r => r.GetByIdAsync(localTrackId, It.IsAny<CancellationToken>())).ReturnsAsync(localTrack);

        AppIpcRequest? capturedRequest = null;
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppIpcRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(AppIpcResponse.SuccessEnqueueTracks(
                position: "last",
                addedCount: 1,
                userQueueCount: 1,
                albumQueueCount: 0,
                addedTracks: [new AppIpcTrackInfo(wpfTrackId.Value, "Custom Song", "Custom Artist", "Custom Album", 150.0, "02:30", audioPath)]));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance,
            dataDirectoryProvider: dataDirProvider.Object,
            trackRepository: localTrackRepo.Object);

        // Act
        PlaybackControlResult<EnqueueTracksResponse> result = await sut.EnqueueTracksAsync(
            trackIds: [localTrackId],
            filePaths: null,
            albumId: null,
            playNext: false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.TrackIds);
        Assert.NotNull(capturedRequest.FilePaths);
        Assert.Equal(audioPath, Assert.Single(capturedRequest.FilePaths));
        Assert.Equal(localTrackId.Value, Assert.Single(result.Value.AddedTracks).Id);
    }

    /// <summary>
    /// enqueue_tracks 実行時に IPC サーバが未起動の場合、WPF_APP_NOT_RUNNING と launch_app 案内を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task EnqueueTracksAsync_IPCサーバ未起動時_WPF_APP_NOT_RUNNINGとlaunch_app案内を返す()
    {
        // Arrange
        var sut = new IpcPlaybackControlGateway(
            $"AudioEffector.Ipc.MissingEnqueue.{Guid.NewGuid():N}",
            commandTimeout: TimeSpan.FromMilliseconds(300),
            connectTimeout: TimeSpan.FromMilliseconds(100),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<EnqueueTracksResponse> result = await sut.EnqueueTracksAsync(
            trackIds: [TrackId.New()],
            filePaths: null,
            albumId: null,
            playNext: false);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, result.ErrorCode);
        Assert.Contains("launch_app", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// enqueue_tracks の IPC 応答がタイムアウトした場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task EnqueueTracksAsync_IPC応答タイムアウト時_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.EnqueueTimeout.{Guid.NewGuid():N}";
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
        PlaybackControlResult<EnqueueTracksResponse> result = await sut.EnqueueTracksAsync(
            trackIds: [TrackId.New()],
            filePaths: null,
            albumId: null,
            playNext: false);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }

    /// <summary>
    /// enqueue_tracks の IPC 成功応答で必須フィールド（addedTracks / position 等）が欠損している場合に IPC_CONNECTION_FAILED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task EnqueueTracksAsync_IPC成功応答の必須フィールド欠損時_IPC_CONNECTION_FAILEDを返す()
    {
        // Arrange
        string pipeName = $"AudioEffector.Ipc.MalformedEnqueue.{Guid.NewGuid():N}";
        var handler = new Mock<IAppIpcCommandHandler>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<AppIpcRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppIpcResponse(Ok: true, Position: "last", AddedCount: 1, UserQueueCount: 1, AlbumQueueCount: 0, AddedTracks: null));

        using var server = new AppIpcServer(pipeName, handler.Object);
        server.Start();

        var sut = new IpcPlaybackControlGateway(
            pipeName,
            commandTimeout: TimeSpan.FromSeconds(2),
            connectTimeout: TimeSpan.FromMilliseconds(500),
            NullLogger<IpcPlaybackControlGateway>.Instance);

        // Act
        PlaybackControlResult<EnqueueTracksResponse> result = await sut.EnqueueTracksAsync(
            trackIds: [TrackId.New()],
            filePaths: null,
            albumId: null,
            playNext: false);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(McpErrorCodes.IPC_CONNECTION_FAILED, result.ErrorCode);
    }
}
