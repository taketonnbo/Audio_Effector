using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="AlbumDetailViewModel"/> のメタデータ集計・コマンド操作・高解像度アートワーク非同期読込・ライブラリ同期を検証するテストクラス
/// </summary>
public sealed class AlbumDetailViewModelTests
{
    // 1x1 白ピクセルの有効な最小 PNG バイナリ
    private static readonly byte[] ValidPngBytesA = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    // 1x1 黒ピクセルの有効な最小 PNG バイナリ
    private static readonly byte[] ValidPngBytesB = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static Track CreateTrack(
        string title,
        string artist = "Daft Punk",
        string album = "Random Access Memories",
        uint year = 2013,
        string genre = "Electronic",
        string format = "FLAC",
        bool isHiRes = false,
        bool isLossless = false,
        TimeSpan? duration = null,
        string? filePath = null)
    {
        return new Track
        {
            FilePath = filePath ?? $@"C:\music\{artist}\{album}\{title}.{format.ToLowerInvariant()}",
            Title = title,
            Artist = artist,
            Album = album,
            Year = year,
            Genre = genre,
            Format = format,
            IsHiRes = isHiRes,
            IsLossless = isLossless,
            Duration = duration ?? TimeSpan.FromMinutes(4),
        };
    }

    private static (AlbumDetailViewModel Sut, LibraryViewModel Library, Mock<IAudioService> AudioMock) CreateSut(
        Func<string, CancellationToken, Task<byte[]?>>? artworkLoader = null,
        Func<string, Track>? trackMetadataLoader = null)
    {
        var trackRepoMock = new Mock<ITrackRepository>();
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        var metadataExtractor = new TagLibMetadataExtractor();
        var eventBus = new InMemoryEventBus();
        var libraryService = new LibraryApplicationService(
            trackRepoMock.Object,
            favoriteRepoMock.Object,
            metadataExtractor,
            eventBus);

        var audioMock = new Mock<IAudioService>();
        audioMock.SetupProperty(a => a.IsShuffleEnabled, false);
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var library = new LibraryViewModel(
            libraryService,
            audioMock.Object,
            settingsMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: trackMetadataLoader);

        var loader = artworkLoader ?? ((_, _) => Task.FromResult<byte[]?>(null));
        var sut = new AlbumDetailViewModel(library, audioMock.Object, playlistViewModel: null, artworkBytesLoader: loader);
        return (sut, library, audioMock);
    }

    /// <summary>
    /// アルバム設定時にタイトル・アーティスト・年・最頻ジャンル・最頻フォーマット・総時間・音質ラベルが正しく集計されることを検証します
    /// </summary>
    [Fact]
    public void SetAlbum_複数トラックを持つアルバムを設定_メタデータと最頻値が正しく集計される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var tracks = new List<Track>
        {
            CreateTrack("Track 1", year: 2013, genre: "Pop", format: "MP3", isLossless: false, duration: TimeSpan.FromMinutes(30)),
            CreateTrack("Track 2", year: 2013, genre: "Electronic", format: "FLAC", isLossless: true, isHiRes: true, duration: TimeSpan.FromMinutes(25)),
            CreateTrack("Track 3", year: 2013, genre: "Electronic", format: "FLAC", isLossless: true, duration: TimeSpan.FromMinutes(20)),
        };
        var album = new Album("Random Access Memories", "Daft Punk", 2013, tracks);

        // Act
        sut.SetAlbum(album);

        // Assert
        Assert.Same(album, sut.CurrentAlbum);
        Assert.Equal("Random Access Memories", sut.AlbumTitle);
        Assert.Equal("Daft Punk", sut.Artist);
        Assert.Equal(2013u, sut.Year);
        Assert.Equal("2013", sut.YearDisplay);
        Assert.Equal("Electronic", sut.GenreDisplay);
        Assert.Equal(3, sut.TrackCount);
        Assert.True(sut.HasTracks);
        Assert.Equal("1:15:00", sut.TotalDurationString);
        Assert.Equal("2013 • Electronic • 3 曲 • 1:15:00", sut.MetadataSummary);
        Assert.Equal("FLAC", sut.AudioFormatSummary);
        Assert.True(sut.IsHiRes);
        Assert.True(sut.IsLossless);
        Assert.Equal("Hi-Res", sut.QualityLabel);
    }

    /// <summary>
    /// Album.Yearが0の場合に収録曲先頭の非0年がフォールバック採用され、LosslessのみのアルバムではLosslessラベルが返ることを検証します
    /// </summary>
    [Fact]
    public void SetAlbum_AlbumYearが0でLosslessのみの場合_トラック年からフォールバックしLosslessラベルを返す()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var tracks = new List<Track>
        {
            CreateTrack("Track 1", year: 0, genre: "", format: "WAV", isLossless: true, isHiRes: false, duration: TimeSpan.FromSeconds(185)),
            CreateTrack("Track 2", year: 2001, genre: "", format: "WAV", isLossless: true, isHiRes: false, duration: TimeSpan.FromSeconds(200)),
        };
        var album = new Album("Discovery", "Daft Punk", year: 0, tracks);

        // Act
        sut.SetAlbum(album);

        // Assert
        Assert.Equal(2001u, sut.Year);
        Assert.Equal("2001", sut.YearDisplay);
        Assert.Equal(string.Empty, sut.GenreDisplay);
        Assert.Equal("06:25", sut.TotalDurationString);
        Assert.Equal("2001 • 2 曲 • 06:25", sut.MetadataSummary);
        Assert.Equal("WAV", sut.AudioFormatSummary);
        Assert.False(sut.IsHiRes);
        Assert.True(sut.IsLossless);
        Assert.Equal("Lossless", sut.QualityLabel);
    }

    /// <summary>
    /// 空アルバム設定時に再生系コマンドのCanExecuteがfalseになり、メタデータが安全な既定値となることを検証します
    /// </summary>
    [Fact]
    public void SetAlbum_収録曲が0件の空アルバムを設定_再生系コマンドが無効となり安全な既定値を返す()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var emptyAlbum = new Album("Empty Album", "Unknown Artist", year: 0);

        // Act
        sut.SetAlbum(emptyAlbum);

        // Assert
        Assert.Equal(0, sut.TrackCount);
        Assert.False(sut.HasTracks);
        Assert.Equal(0u, sut.Year);
        Assert.Equal(string.Empty, sut.YearDisplay);
        Assert.Equal(string.Empty, sut.GenreDisplay);
        Assert.Equal("00:00", sut.TotalDurationString);
        Assert.Equal("0 曲 • 00:00", sut.MetadataSummary);
        Assert.Equal(string.Empty, sut.AudioFormatSummary);
        Assert.Equal(string.Empty, sut.QualityLabel);
        Assert.False(sut.PlayAllCommand.CanExecute(null));
        Assert.False(sut.ShufflePlayCommand.CanExecute(null));
        Assert.False(sut.PlayNextAlbumCommand.CanExecute(null));
        Assert.False(sut.EnqueueAlbumCommand.CanExecute(null));
        Assert.True(sut.DeleteAlbumCommand.CanExecute(null));
    }

    /// <summary>
    /// PlayAllCommand実行時に全曲がトラック順でAudioServiceへ設定され、先頭曲から再生されることを検証します
    /// </summary>
    [Fact]
    public void PlayAllCommand_収録曲ありで実行_全曲が再生キューに設定され先頭曲から再生される()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var album = new Album("RAM", "Daft Punk", 2013, [t1, t2]);
        sut.SetAlbum(album);

        IReadOnlyList<Track>? requestedTracks = null;
        Track? requestedStart = null;
        string? requestedTitle = null;
        sut.PlaybackRequested += (tracks, start, title, _) =>
        {
            requestedTracks = tracks;
            requestedStart = start;
            requestedTitle = title;
        };

        // Act
        sut.PlayAllCommand.Execute(null);

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal(2, requestedTracks!.Count);
        Assert.Same(t1, requestedStart);
        Assert.Equal("RAM", requestedTitle);
        audioMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(list => list.SequenceEqual(new[] { t1, t2 })), t1), Times.Once);
        audioMock.Verify(a => a.PlayTrack(t1), Times.Once);
    }

    /// <summary>
    /// ShufflePlayCommand実行時にIsShuffleEnabledを変更せずワンショットシャッフルリストが再生キューとPlaybackRequestedへ渡されることを検証します
    /// </summary>
    [Fact]
    public void ShufflePlayCommand_収録曲ありで実行_IsShuffleEnabledを変更せずシャッフル済みリストで再生開始される()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var t3 = CreateTrack("Track 3");
        var album = new Album("RAM", "Daft Punk", 2013, [t1, t2, t3]);
        sut.SetAlbum(album);

        IReadOnlyList<Track>? requestedTracks = null;
        Track? startedTrack = null;
        sut.PlaybackRequested += (tracks, start, _, _) =>
        {
            requestedTracks = tracks;
            startedTrack = start;
        };

        List<Track>? setPlaylistTracks = null;
        audioMock
            .Setup(a => a.SetPlaylist(It.IsAny<List<Track>>(), It.IsAny<Track?>()))
            .Callback<List<Track>, Track?>((list, _) => setPlaylistTracks = list);

        // Act
        sut.ShufflePlayCommand.Execute(null);

        // Assert
        Assert.False(audioMock.Object.IsShuffleEnabled);
        Assert.NotNull(startedTrack);
        Assert.NotNull(setPlaylistTracks);
        Assert.NotNull(requestedTracks);
        Assert.Equal(3, setPlaylistTracks!.Count);
        Assert.Same(setPlaylistTracks[0], startedTrack);
        Assert.Equal(setPlaylistTracks, requestedTracks);
        Assert.Contains(startedTrack, album.Tracks);
        audioMock.Verify(a => a.PlayTrack(startedTrack!), Times.Once);
    }

    /// <summary>
    /// 途中曲からPlayTrackCommandを実行した際、PlaybackRequestedにはアルバム全収録曲が渡され、
    /// AudioService.SetPlaylistには#276仕様に従い選択曲以降の楽曲のみが渡されることを検証します
    /// </summary>
    [Fact]
    public void PlayTrackCommand_途中曲を指定して実行_PlaybackRequestedには全収録曲が渡され再生キューには選択曲以降のみが渡される()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var t3 = CreateTrack("Track 3");
        var album = new Album("RAM", "Daft Punk", 2013, [t1, t2, t3]);
        sut.SetAlbum(album);

        IReadOnlyList<Track>? requestedTracks = null;
        Track? requestedStartTrack = null;
        sut.PlaybackRequested += (tracks, start, _, _) =>
        {
            requestedTracks = tracks;
            requestedStartTrack = start;
        };

        // Act
        sut.PlayTrackCommand.Execute(t2);

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal([t1, t2, t3], requestedTracks);
        Assert.Same(t2, requestedStartTrack);
        audioMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(list => list.SequenceEqual(new[] { t2, t3 })), t2), Times.Once);
        audioMock.Verify(a => a.PlayTrack(t2), Times.Once);
    }

    /// <summary>
    /// 現在のアルバムTracksに含まれないトラックでPlayTrackCommandを実行した際、
    /// PlaybackRequestedおよびSetPlaylistの両方が単曲フォールバック([track])になることを検証します
    /// </summary>
    [Fact]
    public void PlayTrackCommand_収録曲外のトラックを指定して実行_PlaybackRequestedと再生キューの両方が単曲へフォールバックする()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var externalTrack = CreateTrack("External Track", album: "Other Album");
        var album = new Album("RAM", "Daft Punk", 2013, [t1, t2]);
        sut.SetAlbum(album);

        IReadOnlyList<Track>? requestedTracks = null;
        Track? requestedStartTrack = null;
        sut.PlaybackRequested += (tracks, start, _, _) =>
        {
            requestedTracks = tracks;
            requestedStartTrack = start;
        };

        // Act
        sut.PlayTrackCommand.Execute(externalTrack);

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal([externalTrack], requestedTracks);
        Assert.Same(externalTrack, requestedStartTrack);
        audioMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(list => list.SequenceEqual(new[] { externalTrack })), externalTrack), Times.Once);
        audioMock.Verify(a => a.PlayTrack(externalTrack), Times.Once);
    }

    /// <summary>
    /// 次に再生・最後に再生・お気に入り切替・戻るの各コマンドが期待通りに動作することを検証します
    /// </summary>
    [Fact]
    public void キュー追加およびお気に入りおよび戻るコマンド_実行時_期待されるイベントと状態変更が行われる()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var album = new Album("RAM", "Daft Punk", 2013, [t1, t2]);
        sut.SetAlbum(album);

        var enqueuedEvents = new List<(IReadOnlyList<Track> Tracks, bool PlayNext)>();
        sut.EnqueueRequested += (tracks, playNext) => enqueuedEvents.Add((tracks, playNext));

        bool backFired = false;
        sut.BackRequested += () => backFired = true;

        // Act
        sut.PlayNextAlbumCommand.Execute(null);
        sut.EnqueueAlbumCommand.Execute(null);
        sut.PlayNextTrackCommand.Execute(t1);
        sut.EnqueueTrackCommand.Execute(t2);
        sut.ToggleFavoriteTrackCommand.Execute(t1);
        sut.BackToAlbumsCommand.Execute(null);

        // Assert
        Assert.Equal(4, enqueuedEvents.Count);
        Assert.True(enqueuedEvents[0].PlayNext);
        Assert.Equal(2, enqueuedEvents[0].Tracks.Count);
        Assert.False(enqueuedEvents[1].PlayNext);
        Assert.Equal(2, enqueuedEvents[1].Tracks.Count);
        Assert.True(enqueuedEvents[2].PlayNext);
        Assert.Same(t1, enqueuedEvents[2].Tracks[0]);
        Assert.False(enqueuedEvents[3].PlayNext);
        Assert.Same(t2, enqueuedEvents[3].Tracks[0]);
        Assert.True(t1.IsFavorite);
        Assert.True(backFired);
    }

    /// <summary>
    /// 高解像度アートワークが非同期でデコード・FreezeされてCoverImageへ反映され、SetAlbum(null)で解放されることを検証します
    /// </summary>
    [Fact]
    public async Task SetAlbum_有効なアートワークがある場合_Freeze済みBitmapImageが非同期反映されnull設定で解放される()
    {
        // Arrange
        var (sut, library, _) = CreateSut(artworkLoader: (_, _) => Task.FromResult<byte[]?>(ValidPngBytesA));
        using var _lib = library;
        using var _sut = sut;

        var album = new Album("RAM", "Daft Punk", 2013, [CreateTrack("Track 1")]);

        // Act
        sut.SetAlbum(album);
        await sut.CurrentArtLoadTask;

        // Assert
        Assert.NotNull(sut.CoverImage);
        Assert.True(sut.CoverImage!.IsFrozen);

        // Act 2: null 設定で参照解放
        sut.SetAlbum(null);
        await sut.CurrentArtLoadTask;

        // Assert 2
        Assert.Null(sut.CoverImage);
        Assert.Null(sut.CurrentAlbum);
        Assert.Empty(sut.Tracks);
    }

    /// <summary>
    /// アルバムを高速切替した際、古い読み込みがキャンセルまたは後から完了しても最新アルバムの画像が維持されることを検証します
    /// </summary>
    [Fact]
    public async Task SetAlbum_古いアートワーク読込が後から完了する_世代番号とキャンセルにより最新アルバム画像を維持する()
    {
        // Arrange
        var firstTcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = false;
        var secondTcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (sut, library, _) = CreateSut(artworkLoader: (path, ct) =>
        {
            if (path.Contains("AlbumA", StringComparison.Ordinal))
            {
                ct.Register(() => firstCancelled = true);
                return firstTcs.Task;
            }

            return secondTcs.Task;
        });
        using var _lib = library;
        using var _sut = sut;

        var albumA = new Album("AlbumA", "Artist", 2020, [CreateTrack("T1", album: "AlbumA", filePath: @"C:\music\AlbumA\t1.flac")]);
        var albumB = new Album("AlbumB", "Artist", 2021, [CreateTrack("T2", album: "AlbumB", filePath: @"C:\music\AlbumB\t2.flac")]);

        // Act: AlbumA の読み込み中に AlbumB へ切り替え
        sut.SetAlbum(albumA);
        var taskA = sut.CurrentArtLoadTask;

        sut.SetAlbum(albumB);
        var taskB = sut.CurrentArtLoadTask;

        // 先に AlbumB を完了させ、その後から古い AlbumA を完了させる
        secondTcs.SetResult(ValidPngBytesB);
        await taskB;
        var imageAfterB = sut.CoverImage;

        firstTcs.SetResult(ValidPngBytesA);
        await taskA;

        // Assert: 古いタスクはキャンセル通知を受け、最新画像が上書きされない
        Assert.True(firstCancelled);
        Assert.NotNull(imageAfterB);
        Assert.Same(imageAfterB, sut.CoverImage);
    }

    /// <summary>
    /// ライブラリ差分同期で表示中アルバムが消滅した場合に自動的に表示をクリアしBackRequestedが発火することを検証します
    /// </summary>
    [Fact]
    public async Task LibrarySynced_表示中アルバムがライブラリから消滅した場合_自動的にクリアされBackRequestedが発火する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffector_AlbumDetailTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string fileA = Path.Combine(tempDir, "songA.mp3");
            File.WriteAllBytes(fileA, [0x00]);

            var (sut, library, _) = CreateSut(
                trackMetadataLoader: path => CreateTrack(
                    Path.GetFileNameWithoutExtension(path),
                    artist: "ArtistA",
                    album: "AlbumA",
                    filePath: path));
            using var _lib = library;
            using var _sut = sut;

            await library.LoadLibraryAsync(tempDir);
            var targetAlbum = Assert.Single(library.Albums);
            sut.SetAlbum(targetAlbum);

            bool backFired = false;
            sut.BackRequested += () => backFired = true;

            // Act: ファイルを削除して再スキャン（差分同期でアルバム消滅）
            File.Delete(fileA);
            await library.LoadLibraryAsync(tempDir);

            // Assert
            Assert.True(backFired);
            Assert.Null(sut.CurrentAlbum);
            Assert.Empty(sut.Tracks);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// ライブラリ差分同期で表示中アルバムに楽曲が追加された場合、Tracksとメタデータが自動追従することを検証します
    /// </summary>
    [Fact]
    public async Task LibrarySynced_表示中アルバムに楽曲が追加された場合_Tracksとメタデータが自動更新される()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffector_AlbumDetailSyncTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string file1 = Path.Combine(tempDir, "song1.flac");
            File.WriteAllBytes(file1, [0x00]);

            var (sut, library, _) = CreateSut(
                trackMetadataLoader: path => CreateTrack(
                    Path.GetFileNameWithoutExtension(path),
                    artist: "ArtistA",
                    album: "AlbumA",
                    format: "FLAC",
                    isLossless: true,
                    duration: TimeSpan.FromMinutes(3),
                    filePath: path));
            using var _lib = library;
            using var _sut = sut;

            await library.LoadLibraryAsync(tempDir);
            sut.SetAlbum(Assert.Single(library.Albums));
            Assert.Equal(1, sut.TrackCount);

            // Act: 2曲目を追加して再スキャン
            string file2 = Path.Combine(tempDir, "song2.flac");
            File.WriteAllBytes(file2, [0x00]);
            await library.LoadLibraryAsync(tempDir);

            // Assert
            Assert.Equal(2, sut.TrackCount);
            Assert.Equal("06:00", sut.TotalDurationString);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// OnLibraryTrackRemovedで途中曲が削除された場合にメタデータ再集計と代表アート再読込が行われ、最終曲削除時にBackRequestedが発火することを検証します
    /// </summary>
    [Fact]
    public async Task OnLibraryTrackRemoved_途中曲と最終曲の削除_メタデータ再集計と代表アート再読込後に自動復帰する()
    {
        // Arrange
        var loadedPaths = new List<string>();
        var (sut, library, _) = CreateSut(artworkLoader: (path, _) =>
        {
            loadedPaths.Add(path);
            return Task.FromResult<byte[]?>(ValidPngBytesA);
        });
        using var _lib = library;
        using var _sut = sut;

        var t1 = CreateTrack("Track 1", format: "FLAC", isHiRes: true, duration: TimeSpan.FromMinutes(3), filePath: @"C:\music\t1.flac");
        var t2 = CreateTrack("Track 2", format: "MP3", isHiRes: false, duration: TimeSpan.FromMinutes(4), filePath: @"C:\music\t2.mp3");
        var album = new Album("RAM", "Daft Punk", 2013, [t1, t2]);
        sut.SetAlbum(album);
        await sut.CurrentArtLoadTask;

        bool backFired = false;
        sut.BackRequested += () => backFired = true;

        // Act 1: 代表トラック（先頭曲）を削除
        sut.OnLibraryTrackRemoved(t1);
        await sut.CurrentArtLoadTask;

        // Assert 1: 残り1曲でメタデータ再集計され、代表アートが t2 で再読込される
        Assert.False(backFired);
        Assert.Equal(1, sut.TrackCount);
        Assert.Equal("04:00", sut.TotalDurationString);
        Assert.False(sut.IsHiRes);
        Assert.Equal("MP3", sut.AudioFormatSummary);
        Assert.Equal([@"C:\music\t1.flac", @"C:\music\t2.mp3"], loadedPaths);

        // Act 2: 最終曲を削除
        sut.OnLibraryTrackRemoved(t2);

        // Assert 2: クリアされ BackRequested が発火
        Assert.True(backFired);
        Assert.Null(sut.CurrentAlbum);
        Assert.Empty(sut.Tracks);
    }

    /// <summary>
    /// OnLibraryAlbumRemovedで表示中アルバムが削除された場合にクリアされBackRequestedが発火することを検証します
    /// </summary>
    [Fact]
    public void OnLibraryAlbumRemoved_表示中アルバム削除時_クリアされBackRequestedが発火する()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        var album = new Album("RAM", "Daft Punk", 2013, [CreateTrack("Track 1")]);
        sut.SetAlbum(album);

        bool backFired = false;
        sut.BackRequested += () => backFired = true;

        // Act
        sut.OnLibraryAlbumRemoved(album);

        // Assert
        Assert.True(backFired);
        Assert.Null(sut.CurrentAlbum);
        Assert.Empty(sut.Tracks);
    }

    /// <summary>
    /// Dispose呼び出し後にSetAlbumやPlaybackStateChangedが発火しても状態が変化せず例外も発生しないことを検証します
    /// </summary>
    [Fact]
    public void Dispose_破棄後にSetAlbumやイベント発火があっても_状態変化せず例外が発生しない()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        var album = new Album("RAM", "Daft Punk", 2013, [CreateTrack("Track 1")]);
        sut.SetAlbum(album);

        // Act
        sut.Dispose();
        var exception = Record.Exception(() =>
        {
            sut.SetAlbum(album);
            audioMock.Raise(a => a.PlaybackStateChanged += null, true);
            sut.OnLibraryAlbumRemoved(album);
            sut.OnLibraryTrackRemoved(album.Tracks[0]);
        });

        // Assert
        Assert.Null(exception);
        Assert.Null(sut.CurrentAlbum);
        Assert.Null(sut.CoverImage);
        Assert.False(sut.IsPlaying);
    }

    /// <summary>
    /// TogglePlayTrackCommand で再生中トラックを指定した場合、再再生せず TogglePlayPause が呼び出されることを検証します
    /// </summary>
    [Fact]
    public void TogglePlayTrackCommand_再生中トラック指定時_TogglePlayPauseが呼ばれ再再生しない()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        audioMock.Setup(a => a.IsPlaying).Returns(true);
        var playingTrack = CreateTrack("Playing Track");
        playingTrack.IsPlaying = true;
        var album = new Album("RAM", "Daft Punk", 2013, [playingTrack]);
        sut.SetAlbum(album);

        bool playbackRequested = false;
        sut.PlaybackRequested += (_, _, _, _) => playbackRequested = true;

        // Act
        sut.TogglePlayTrackCommand.Execute(playingTrack);

        // Assert
        audioMock.Verify(a => a.TogglePlayPause(), Times.Once);
        audioMock.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
        Assert.False(playbackRequested);
    }

    /// <summary>
    /// TogglePlayTrackCommand で未再生トラックを指定した場合、通常の再生開始（SetPlaylist および PlayTrack）が実行されることを検証します
    /// </summary>
    [Fact]
    public void TogglePlayTrackCommand_未再生トラック指定時_PlayTrackが呼ばれ再生開始される()
    {
        // Arrange
        var (sut, library, audioMock) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        audioMock.Setup(a => a.IsPlaying).Returns(true);
        var idleTrack = CreateTrack("Idle Track");
        idleTrack.IsPlaying = false;
        var album = new Album("RAM", "Daft Punk", 2013, [idleTrack]);
        sut.SetAlbum(album);

        Track? startedTrack = null;
        sut.PlaybackRequested += (_, startTrack, _, _) => startedTrack = startTrack;

        // Act
        sut.TogglePlayTrackCommand.Execute(idleTrack);

        // Assert
        audioMock.Verify(a => a.TogglePlayPause(), Times.Never);
        audioMock.Verify(a => a.PlayTrack(idleTrack), Times.Once);
        Assert.Same(idleTrack, startedTrack);
    }
}
