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
}
