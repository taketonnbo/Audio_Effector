using System.IO;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Ipc;
using Moq;

namespace AudioEffector.Tests.Infrastructure.Ipc;

public sealed class PlaybackIpcCommandHandlerTests
{
    /// <summary>
    /// trackId 指定でリポジトリからトラックを解決し、IUiDispatcher 経由で IAudioService.PlayTrack を実行して成功応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_有効なtrackId指定時_UiDispatcher経由でPlayTrackを実行し再生結果を返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var track = CreateTrack(trackId, @"C:\Music\Jazz\BlueNote.flac", "Blue Note Morning", "Miles Trio", "Midnight Sessions");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var libraryService = CreateLibraryService(trackRepo.Object);
        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayTrackAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var recordingDispatcher = new RecordingUiDispatcher();

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            recordingDispatcher,
            fileExists: _ => true);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, TrackId: trackId.Value.ToString("D"));

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, response.PlaybackState);
        Assert.NotNull(response.Track);
        Assert.Equal(trackId.Value, response.Track.Id);
        Assert.Equal("Blue Note Morning", response.Track.Title);
        Assert.Equal("Miles Trio", response.Track.Artist);
        Assert.Equal("Midnight Sessions", response.Track.Album);
        Assert.Equal(225.5, response.Track.DurationSeconds);
        Assert.Equal("03:45", response.Track.DurationString);
        Assert.Equal(@"C:\Music\Jazz\BlueNote.flac", response.Track.FilePath);
        Assert.Equal(1, recordingDispatcher.InvokeCount);
        audioService.Verify(a => a.PlayTrackAsync(track, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// filePath 指定時にライブラリ未登録ファイルであっても LibraryApplicationService.GetTrackByPathAsync のメタデータ抽出経由で再生開始できることを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_filePath指定でメタデータ解決成功時_PlayTrackを実行し成功応答を返す()
    {
        // Arrange
        const string filePath = @"C:\Music\New\Sample.flac";
        var extractedTrack = CreateTrack(TrackId.New(), filePath, "Sample Song", "New Artist", "Single");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByPathAsync(It.IsAny<AudioPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Track?)null);

        var metadataExtractor = new Mock<IMetadataExtractor>();
        metadataExtractor
            .Setup(m => m.ExtractMetadataAsync(It.Is<AudioPath>(p => p.Value == filePath), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedTrack);

        var libraryService = CreateLibraryService(trackRepo.Object, metadataExtractor.Object);
        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayTrackAsync(extractedTrack, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var recordingDispatcher = new RecordingUiDispatcher();

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            libraryService,
            audioService.Object,
            recordingDispatcher,
            fileExists: p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, FilePath: $"  {filePath}  ");

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, response.PlaybackState);
        Assert.NotNull(response.Track);
        Assert.Equal("Sample Song", response.Track.Title);
        Assert.Equal(1, recordingDispatcher.InvokeCount);
        audioService.Verify(a => a.PlayTrackAsync(extractedTrack, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// trackId と filePath の両方が未指定の場合に INVALID_PARAMETER を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_trackIdとfilePath両方未指定時_INVALID_PARAMETERを返す()
    {
        // Arrange
        var trackRepo = new Mock<ITrackRepository>();
        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.INVALID_PARAMETER, response.ErrorCode);
        audioService.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
    }

    /// <summary>
    /// trackId が無効な GUID または空 GUID の場合に INVALID_PARAMETER を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task HandleAsync_不正なtrackId形式指定時_INVALID_PARAMETERを返す(string invalidTrackId)
    {
        // Arrange
        var trackRepo = new Mock<ITrackRepository>();
        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, TrackId: invalidTrackId);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.INVALID_PARAMETER, response.ErrorCode);
        audioService.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
    }

    /// <summary>
    /// 指定された trackId に一致するトラックがリポジトリに存在しない場合に NOT_FOUND を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_存在しないtrackId指定時_NOT_FOUNDを返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Track?)null);

        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, TrackId: trackId.Value.ToString("D"));

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.NOT_FOUND, response.ErrorCode);
        audioService.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
    }

    /// <summary>
    /// trackId で解決されたトラックの音声ファイルがディスク上に存在しない場合に FILE_NOT_FOUND を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_trackIdの音声ファイル欠損時_FILE_NOT_FOUNDを返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var track = CreateTrack(trackId, @"C:\Music\Missing.flac", "Missing", "Artist", "Album");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => false);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, TrackId: trackId.Value.ToString("D"));

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.FILE_NOT_FOUND, response.ErrorCode);
        audioService.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
    }

    /// <summary>
    /// filePath で指定された音声ファイルが存在しない場合に FILE_NOT_FOUND を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_存在しないfilePath指定時_FILE_NOT_FOUNDを返す()
    {
        // Arrange
        var trackRepo = new Mock<ITrackRepository>();
        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => false);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, FilePath: @"C:\Music\DoesNotExist.mp3");

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.FILE_NOT_FOUND, response.ErrorCode);
        audioService.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
    }

    /// <summary>
    /// 未対応コマンドを受信した場合に UNSUPPORTED_COMMAND を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_未対応コマンド受信時_UNSUPPORTED_COMMANDを返す()
    {
        // Arrange
        var trackRepo = new Mock<ITrackRepository>();
        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest("unknown_command");

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.UNSUPPORTED_COMMAND, response.ErrorCode);
    }

    /// <summary>
    /// AudioService.PlayTrackAsync が再生開始失敗（false）を返した場合に playing を返さず INTERNAL_ERROR を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_再生開始失敗時_INTERNAL_ERRORを返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var track = CreateTrack(trackId, @"C:\Music\Corrupted.flac", "Corrupted", "Artist", "Album");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayTrackAsync(track, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, TrackId: trackId.Value.ToString("D"));

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.INTERNAL_ERROR, response.ErrorCode);
        Assert.Null(response.PlaybackState);
    }

    /// <summary>
    /// 実体の AudioService へ破損した音声ファイルを渡した場合、デコード失敗を検知して INTERNAL_ERROR を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_破損音声ファイルを実体AudioServiceで再生時_INTERNAL_ERRORを返す()
    {
        // Arrange
        string tempFile = Path.Combine(Path.GetTempPath(), $"AudioEffectorCorrupt_{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(tempFile, [0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]);

        try
        {
            var trackId = TrackId.New();
            var track = CreateTrack(trackId, tempFile, "Broken", "Artist", "Album");

            var trackRepo = new Mock<ITrackRepository>();
            trackRepo
                .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(track);

            using var audioService = new AudioService(ImmediateUiDispatcher.Instance);
            var sut = new PlaybackIpcCommandHandler(
                trackRepo.Object,
                CreateLibraryService(trackRepo.Object),
                audioService,
                ImmediateUiDispatcher.Instance);

            var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, TrackId: trackId.Value.ToString("D"));

            // Act
            AppIpcResponse response = await sut.HandleAsync(request);

            // Assert
            Assert.False(response.Ok);
            Assert.Equal(AppIpcErrorCodes.INTERNAL_ERROR, response.ErrorCode);
            Assert.False(audioService.IsPlaying);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// UI スレッドの Invoke 待機中に要求の有効期限が切れた場合、再生を開始せず OperationCanceledException を送出することを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_Invoke待機中に期限超過時_再生を開始せずキャンセルされる()
    {
        // Arrange
        var trackId = TrackId.New();
        var track = CreateTrack(trackId, @"C:\Music\Late.flac", "Late", "Artist", "Album");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var audioService = new Mock<IAudioService>();
        DateTimeOffset now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = now.AddSeconds(3);

        var dispatcher = new Mock<IUiDispatcher>();
        dispatcher
            .Setup(d => d.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
                // Invoke の順番待ち中に期限が超過した状態を模擬する
                now = deadline.AddMilliseconds(10);
                action();
            });

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            dispatcher.Object,
            fileExists: _ => true,
            utcNow: () => now);

        var request = new AppIpcRequest(
            AppIpcServer.COMMAND_PLAY_TRACK,
            TrackId: trackId.Value.ToString("D"),
            DeadlineUtc: deadline);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.HandleAsync(request));
        audioService.Verify(a => a.PlayTrackAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 再生開始が確定した直後に有効期限が経過した場合でも、開始済み再生と整合させるため成功応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_再生開始確定直後に期限超過時_成功応答を返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var track = CreateTrack(trackId, @"C:\Music\JustInTime.flac", "Just In Time", "Artist", "Album");

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetByIdAsync(trackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        DateTimeOffset now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = now.AddSeconds(3);

        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayTrackAsync(track, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                now = deadline.AddMilliseconds(5);
                return Task.FromResult(true);
            });

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true,
            utcNow: () => now);

        var request = new AppIpcRequest(
            AppIpcServer.COMMAND_PLAY_TRACK,
            TrackId: trackId.Value.ToString("D"),
            DeadlineUtc: deadline);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, response.PlaybackState);
    }

    /// <summary>
    /// filePath に相対パスを指定した場合に INVALID_PARAMETER を返し再生を実行しないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("song.wav")]
    [InlineData(@"music\song.flac")]
    public async Task HandleAsync_filePathに相対パス指定時_INVALID_PARAMETERを返す(string relativePath)
    {
        // Arrange
        var trackRepo = new Mock<ITrackRepository>();
        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(AppIpcServer.COMMAND_PLAY_TRACK, FilePath: relativePath);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.INVALID_PARAMETER, response.ErrorCode);
        audioService.Verify(a => a.PlayTrackAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static LibraryApplicationService CreateLibraryService(
        ITrackRepository trackRepository,
        IMetadataExtractor? metadataExtractor = null)
    {
        var favoriteRepo = new Mock<IFavoriteRepository>();
        var extractor = metadataExtractor ?? new Mock<IMetadataExtractor>().Object;
        var eventBus = new Mock<IEventBus>();
        return new LibraryApplicationService(trackRepository, favoriteRepo.Object, extractor, eventBus.Object);
    }

    private static Track CreateTrack(TrackId id, string filePath, string title, string artist, string album)
    {
        return new Track(
            id: id,
            filePath: AudioPath.Create(filePath),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromSeconds(225.5),
            year: 2024,
            trackNumber: 1,
            bitrate: 1411,
            sampleRate: 96000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Jazz");
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int InvokeCount { get; private set; }

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }

        public void BeginInvoke(Action action)
        {
            action();
        }
    }
}
