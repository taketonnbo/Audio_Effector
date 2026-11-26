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

    /// <summary>
    /// play_album で albumId・shuffle・repeat を指定した場合、トラック番号順かつ存在するファイルのみを PlayPlaylistAsync へ渡しコレクション再生結果を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_play_albumでalbumId指定時_欠損ファイルをスキップしトラック番号順で再生開始する()
    {
        // Arrange
        var albumId = AlbumId.New();
        var track2 = CreateTrack(TrackId.New(), @"C:\Music\Alb\02.flac", "Track 02", "Miles Trio", "Midnight Sessions", trackNumber: 2, albumId: albumId);
        var track1 = CreateTrack(TrackId.New(), @"C:\Music\Alb\01.flac", "Track 01", "Miles Trio", "Midnight Sessions", trackNumber: 1, albumId: albumId);
        var missingTrack = CreateTrack(TrackId.New(), @"C:\Music\Alb\03_missing.flac", "Track 03", "Miles Trio", "Midnight Sessions", trackNumber: 3, albumId: albumId);

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track2, missingTrack, track1]);

        var audioService = new Mock<IAudioService>();
        IReadOnlyList<Track>? capturedTracks = null;
        audioService
            .Setup(a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                null,
                true,
                true,
                "Midnight Sessions",
                null,
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Track>, Track?, bool?, bool?, string?, int?, CancellationToken>((tracks, _, _, _, _, _, _) => capturedTracks = tracks)
            .ReturnsAsync(track1);
        audioService.SetupGet(a => a.IsShuffleEnabled).Returns(true);
        audioService.SetupGet(a => a.IsRepeatEnabled).Returns(true);

        var recordingDispatcher = new RecordingUiDispatcher();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            recordingDispatcher,
            fileExists: p => !p.Contains("missing", StringComparison.OrdinalIgnoreCase));

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_ALBUM,
            AlbumId: albumId.Value.ToString("D"),
            Shuffle: true,
            Repeat: true);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(PlaybackIpcCommandHandler.SOURCE_TYPE_ALBUM, response.SourceType);
        Assert.Equal(albumId.Value, response.SourceId);
        Assert.Equal("Midnight Sessions", response.SourceName);
        Assert.Equal(PlaybackIpcCommandHandler.PLAYBACK_STATE_PLAYING, response.PlaybackState);
        Assert.Equal(2, response.TotalTracks);
        Assert.True(response.IsShuffleEnabled);
        Assert.True(response.IsRepeatEnabled);
        Assert.NotNull(response.Track);
        Assert.Equal(track1.Id.Value, response.Track.Id);
        Assert.Equal(1, recordingDispatcher.InvokeCount);
        Assert.NotNull(capturedTracks);
        Assert.Equal([track1.Id, track2.Id], capturedTracks.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// play_album で albumName のみ指定し同名アルバムが複数アーティストに跨る場合、INVALID_PARAMETER（曖昧性エラー）を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_play_albumで同名アルバムが複数存在する場合_INVALID_PARAMETERを返す()
    {
        // Arrange
        var trackA = CreateTrack(TrackId.New(), @"C:\Music\A\01.flac", "Song A", "Artist A", "Greatest Hits", trackNumber: 1, albumId: AlbumId.New());
        var trackB = CreateTrack(TrackId.New(), @"C:\Music\B\01.flac", "Song B", "Artist B", "Greatest Hits", trackNumber: 1, albumId: AlbumId.New());

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trackA, trackB]);

        var audioService = new Mock<IAudioService>();
        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_ALBUM,
            AlbumName: "greatest hits");

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.INVALID_PARAMETER, response.ErrorCode);
        Assert.Equal(AppIpcErrorReasons.AMBIGUOUS_ALBUM, response.ErrorReason);
        Assert.Contains("artist", response.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// play_album で albumName と artist および startTrackIndex を指定した場合、一意に特定されたアルバムの指定曲から再生開始されることを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_play_albumでalbumNameとartistおよびstartTrackIndex指定時_一意に特定して開始曲から再生する()
    {
        // Arrange
        var albumIdA = AlbumId.New();
        var albumIdB = AlbumId.New();
        var trackA1 = CreateTrack(TrackId.New(), @"C:\Music\A\01.flac", "A1", "Artist A", "Greatest Hits", trackNumber: 1, albumId: albumIdA);
        var trackB1 = CreateTrack(TrackId.New(), @"C:\Music\B\01.flac", "B1", "Artist B", "Greatest Hits", trackNumber: 1, albumId: albumIdB);
        var trackB2 = CreateTrack(TrackId.New(), @"C:\Music\B\02.flac", "B2", "Artist B", "Greatest Hits", trackNumber: 2, albumId: albumIdB);

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trackA1, trackB1, trackB2]);

        var audioService = new Mock<IAudioService>();
        audioService
            .Setup(a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                trackB2,
                null,
                null,
                "Greatest Hits",
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackB2);

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true);

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_ALBUM,
            AlbumName: "Greatest Hits",
            Artist: "artist b",
            StartTrackIndex: 1);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(albumIdB.Value, response.SourceId);
        Assert.Equal(trackB2.Id.Value, response.Track!.Id);
        Assert.Equal(2, response.TotalTracks);
    }

    /// <summary>
    /// play_playlist で playlistName を指定した場合、プレイリストの収録順を保ち欠損ファイルをスキップして再生開始することを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_play_playlistでplaylistName指定時_欠損ファイルをスキップして再生開始する()
    {
        // Arrange
        var track1 = CreateTrack(TrackId.New(), @"C:\Music\P\01.flac", "Drive 1", "Artist", "Album");
        var missingTrack = CreateTrack(TrackId.New(), @"C:\Music\P\missing.flac", "Drive Missing", "Artist", "Album");
        var track2 = CreateTrack(TrackId.New(), @"C:\Music\P\02.flac", "Drive 2", "Artist", "Album");

        var playlist = new UserPlaylist(PlaylistId.New(), "Drive Mix", [track2.Id, missingTrack.Id, track1.Id]);

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, missingTrack, track2]);

        var playlistRepo = new Mock<IPlaylistRepository>();
        playlistRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([playlist]);

        var playlistService = new PlaylistApplicationService(playlistRepo.Object, trackRepo.Object, new Mock<IEventBus>().Object);
        var audioService = new Mock<IAudioService>();
        IReadOnlyList<Track>? capturedTracks = null;
        audioService
            .Setup(a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                null,
                false,
                true,
                "Drive Mix",
                null,
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Track>, Track?, bool?, bool?, string?, int?, CancellationToken>((tracks, _, _, _, _, _, _) => capturedTracks = tracks)
            .ReturnsAsync(track2);
        audioService.SetupGet(a => a.IsShuffleEnabled).Returns(false);
        audioService.SetupGet(a => a.IsRepeatEnabled).Returns(true);

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: p => !p.Contains("missing", StringComparison.OrdinalIgnoreCase),
            playlistService: playlistService);

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_PLAYLIST,
            PlaylistName: "drive mix",
            Shuffle: false,
            Repeat: true);

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.True(response.Ok);
        Assert.Equal(PlaybackIpcCommandHandler.SOURCE_TYPE_PLAYLIST, response.SourceType);
        Assert.Equal(playlist.Id.Value, response.SourceId);
        Assert.Equal("Drive Mix", response.SourceName);
        Assert.Equal(2, response.TotalTracks);
        Assert.False(response.IsShuffleEnabled);
        Assert.True(response.IsRepeatEnabled);
        Assert.Equal(track2.Id.Value, response.Track!.Id);
        Assert.NotNull(capturedTracks);
        Assert.Equal([track2.Id, track1.Id], capturedTracks.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// play_playlist で空のプレイリストを指定した場合に NOT_FOUND を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_play_playlistで空プレイリスト指定時_NOT_FOUNDを返す()
    {
        // Arrange
        var emptyPlaylist = new UserPlaylist(PlaylistId.New(), "Empty List");
        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Track>());

        var playlistRepo = new Mock<IPlaylistRepository>();
        playlistRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([emptyPlaylist]);

        var playlistService = new PlaylistApplicationService(playlistRepo.Object, trackRepo.Object, new Mock<IEventBus>().Object);
        var audioService = new Mock<IAudioService>();

        var sut = new PlaybackIpcCommandHandler(
            trackRepo.Object,
            CreateLibraryService(trackRepo.Object),
            audioService.Object,
            ImmediateUiDispatcher.Instance,
            fileExists: _ => true,
            playlistService: playlistService);

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_PLAYLIST,
            PlaylistId: emptyPlaylist.Id.Value.ToString("D"));

        // Act
        AppIpcResponse response = await sut.HandleAsync(request);

        // Assert
        Assert.False(response.Ok);
        Assert.Equal(AppIpcErrorCodes.NOT_FOUND, response.ErrorCode);
    }

    /// <summary>
    /// play_album で Invoke 待機中に DeadlineUtc を超過した場合、再生を開始せず OperationCanceledException を送出することを検証します。
    /// </summary>
    [Fact]
    public async Task HandleAsync_play_albumでInvoke待機中に期限超過時_再生を開始せずキャンセルされる()
    {
        // Arrange
        var albumId = AlbumId.New();
        var track = CreateTrack(TrackId.New(), @"C:\Music\LateAlb\01.flac", "Late 1", "Artist", "Late Album", trackNumber: 1, albumId: albumId);

        var trackRepo = new Mock<ITrackRepository>();
        trackRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track]);

        var audioService = new Mock<IAudioService>();
        DateTimeOffset now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = now.AddSeconds(3);

        var dispatcher = new Mock<IUiDispatcher>();
        dispatcher
            .Setup(d => d.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
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
            Command: AppIpcServer.COMMAND_PLAY_ALBUM,
            AlbumId: albumId.Value.ToString("D"),
            DeadlineUtc: deadline);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.HandleAsync(request));
        audioService.Verify(
            a => a.PlayPlaylistAsync(
                It.IsAny<IReadOnlyList<Track>>(),
                It.IsAny<Track?>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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

    private static Track CreateTrack(
        TrackId id,
        string filePath,
        string title,
        string artist,
        string album,
        uint trackNumber = 1,
        AlbumId? albumId = null)
    {
        return new Track(
            id: id,
            filePath: AudioPath.Create(filePath),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromSeconds(225.5),
            year: 2024,
            trackNumber: trackNumber,
            bitrate: 1411,
            sampleRate: 96000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Jazz",
            albumId: albumId);
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
