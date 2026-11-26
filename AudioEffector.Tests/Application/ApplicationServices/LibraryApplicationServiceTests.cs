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
using Moq;
using Xunit;

namespace AudioEffector.Tests.Application.ApplicationServices;

/// <summary>
/// LibraryApplicationServiceのフォルダスキャン、検索、アルバム集約、およびメタデータベースお気に入り管理を検証するテストクラス
/// </summary>
public sealed class LibraryApplicationServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public LibraryApplicationServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_LibraryAppService", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // クリーンアップエラーは無視
        }
    }

    private static Track CreateSampleTrack(
        string title = "Song 1",
        string artist = "Artist 1",
        string album = "Album 1",
        bool isFavorite = false,
        string? filePath = null,
        uint trackNumber = 1,
        double durationSeconds = 180.0)
    {
        var track = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(filePath ?? $@"C:\Music\{title}.mp3"),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromSeconds(durationSeconds),
            trackNumber: trackNumber,
            isFavorite: isFavorite);
        return track;
    }

    /// <summary>
    /// コンストラクタ引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockFavRepo = new Mock<IFavoriteRepository>();
        var extractor = new TagLibMetadataExtractor();
        var mockEventBus = new Mock<IEventBus>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LibraryApplicationService(null!, mockFavRepo.Object, extractor, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new LibraryApplicationService(mockTrackRepo.Object, null!, extractor, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, null!, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, extractor, null!));
    }

    /// <summary>
    /// 存在しないフォルダパスを指定してScanFolderAsyncを呼び出した際、空のリストが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task ScanFolderAsync_存在しないフォルダ指定_空のリストを返すこと()
    {
        // Arrange
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockFavRepo = new Mock<IFavoriteRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.ScanFolderAsync(Path.Combine(_tempDirectory, "non_existent"));

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// 音声ファイルが存在するフォルダを指定した際、メタデータ抽出とお気に入り判定が行われ一括保存されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ScanFolderAsync_音声ファイルが存在するフォルダ_メタデータ抽出とお気に入り判定が行われ一括保存されること()
    {
        // Arrange
        string file1 = Path.Combine(_tempDirectory, "song1.mp3");
        string file2 = Path.Combine(_tempDirectory, "song2.flac");
        File.WriteAllBytes(file1, [1, 2, 3]);
        File.WriteAllBytes(file2, [4, 5, 6]);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.ScanFolderAsync(_tempDirectory);

        // Assert
        Assert.Equal(2, actual.Count);
        mockTrackRepo.Verify(r => r.SaveRangeAsync(It.Is<IEnumerable<Track>>(ts => ts.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// スキャン中にIProgressに進捗率（0.0〜1.0）が通知されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ScanFolderAsync_進捗通知_IProgressに進捗率が通知されること()
    {
        // Arrange
        string file1 = Path.Combine(_tempDirectory, "a.mp3");
        string file2 = Path.Combine(_tempDirectory, "b.mp3");
        File.WriteAllBytes(file1, [1]);
        File.WriteAllBytes(file2, [2]);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        var mockProgress = new Mock<IProgress<double>>();

        // Act
        await sut.ScanFolderAsync(_tempDirectory, mockProgress.Object);

        // Assert
        mockProgress.Verify(p => p.Report(0.5), Times.Once);
        mockProgress.Verify(p => p.Report(1.0), Times.Once);
    }

    /// <summary>
    /// SearchTracksAsync呼び出し時、ITrackRepositoryのSearchAsyncが呼ばれ検索結果が返るかを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_キーワード指定_ITrackRepositoryのSearchAsyncが呼ばれ結果を返すこと()
    {
        // Arrange
        var tracks = new List<Track> { CreateSampleTrack("Jazz Morning") };
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.SearchAsync("jazz", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracks);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.SearchTracksAsync("jazz");

        // Assert
        Assert.Single(actual);
        Assert.Equal("Jazz Morning", actual[0].Title);
    }

    /// <summary>
    /// GetTrackByPathAsync呼び出し時、リポジトリに未登録の実ファイルパスであればメタデータ抽出器からトラックが取得されるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetTrackByPathAsync_未登録かつ実ファイルパス指定_メタデータ抽出器からトラックを取得すること()
    {
        // Arrange
        string file = Path.Combine(_tempDirectory, "fresh_track.wav");
        File.WriteAllBytes(file, [1, 2, 3]);

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetByPathAsync(It.IsAny<AudioPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Track?)null);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.GetTrackByPathAsync(file);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("fresh_track.wav", actual.Title);
    }

    /// <summary>
    /// GetAllAlbumsAsync呼び出し時、全楽曲がアルバム単位にグルーピングされ、フォルダ移動・拡張子変更後でもメタデータ一致でお気に入りフラグが反映されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAlbumsAsync_フォルダ移動および拡張子変更後_メタデータ一致でお気に入り反映かつパスが自動更新されること()
    {
        // Arrange
        var t1 = CreateSampleTrack("Track 1", "Artist A", "Album X", filePath: @"D:\Moved\Track1.flac");
        var t2 = CreateSampleTrack("Track 2", "Artist A", "Album X", filePath: @"D:\Moved\Track2.flac");
        var t3 = CreateSampleTrack("Track 3", "Artist B", "Album Y", filePath: @"D:\Moved\Track3.flac");

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t1, t2, t3]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FavoriteTrackEntry("Track 1", "Artist A", "Album X", 1, 180.0, @"C:\Old\Track1.mp3", DateTime.Now)
            });

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var albums = await sut.GetAllAlbumsAsync();

        // Assert
        Assert.Equal(2, albums.Count);
        var albumX = albums.First(a => a.Name == "Album X");
        Assert.Equal(2, albumX.Tracks.Count);
        Assert.True(albumX.Tracks.First(t => t.Id == t1.Id).IsFavorite);
        Assert.False(albumX.Tracks.First(t => t.Id == t2.Id).IsFavorite);
        mockFavRepo.Verify(
            r => r.UpdateMetadataAsync(
                It.Is<IReadOnlyList<FavoriteMetadataChange>>(list => list.Count == 1 && list[0].Updated.LastKnownFilePath == @"D:\Moved\Track1.flac"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// お気に入り未登録のトラックに対してToggleFavoriteAsyncを呼び出した際、メタデータ付きでお気に入り追加されリポジトリに保存されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ToggleFavoriteAsync_お気に入り未登録トラック_お気に入り追加されリポジトリへ保存されること()
    {
        // Arrange
        var track = CreateSampleTrack("Song A", "Artist A", "Album A", isFavorite: false);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetByIdAsync(track.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.ToggleFavoriteAsync(track.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.True(actual.IsFavorite);
        mockFavRepo.Verify(
            r => r.AddOrUpdateAsync(
                It.Is<FavoriteTrackEntry>(e => e.Title == "Song A" && e.Artist == "Artist A" && e.Album == "Album A" && e.LastKnownFilePath == track.FilePath),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mockTrackRepo.Verify(r => r.SaveAsync(track, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// お気に入り登録済みのトラックに対してToggleFavoriteAsyncを呼び出した際、お気に入り解除されリポジトリから削除されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ToggleFavoriteAsync_お気に入り登録済みトラック_お気に入り解除されリポジトリから削除されること()
    {
        // Arrange
        var track = CreateSampleTrack("Song B", isFavorite: true);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetByIdAsync(track.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.ToggleFavoriteAsync(track.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.False(actual.IsFavorite);
        mockFavRepo.Verify(r => r.RemoveAsync(track, It.IsAny<CancellationToken>()), Times.Once);
        mockTrackRepo.Verify(r => r.SaveAsync(track, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// GetFavoritesAsync呼び出し時、タグ編集直後でもLastKnownFilePathフォールバックでお気に入り楽曲が解決されメタデータが最新化されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetFavoritesAsync_タグ編集直後の楽曲_パス一致フォールバックで解決されメタデータが更新されること()
    {
        // Arrange
        var editedTrack = CreateSampleTrack("Updated Title", "Updated Artist", "Updated Album", filePath: @"D:\Music\Track.flac");
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([editedTrack]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FavoriteTrackEntry("Old Title", "Old Artist", "Old Album", 1, 180.0, @"D:\Music\Track.flac", DateTime.Now)
            });

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var favorites = await sut.GetFavoritesAsync();

        // Assert
        Assert.Single(favorites);
        Assert.Same(editedTrack, favorites[0]);
        Assert.True(editedTrack.IsFavorite);
        mockFavRepo.Verify(
            r => r.UpdateMetadataAsync(
                It.Is<IReadOnlyList<FavoriteMetadataChange>>(list => list.Count == 1 && list[0].Updated.Title == "Updated Title"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// SearchTracksAsync(TrackSearchQuery) がお気に入りリポジトリと読み取り専用照合を行い、複合条件フィルタおよびページネーションを適用することを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_複合条件クエリ指定_お気に入り照合と絞り込みおよびページネーションが適用され書き込み副作用がないこと()
    {
        // Arrange
        var track1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\1.flac"), "Track 1", "Artist A", "Album X", TimeSpan.FromSeconds(200), trackNumber: 1, genre: "Jazz");
        var track2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\2.flac"), "Track 2", "Artist A", "Album X", TimeSpan.FromSeconds(210), trackNumber: 2, genre: "Jazz");
        var track3 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\3.flac"), "Track 3", "Artist A", "Album X", TimeSpan.FromSeconds(220), trackNumber: 3, genre: "Rock");

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track2, track3, track1]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new FavoriteTrackEntry("Track 1", "Artist A", "Album X", 1, 200.0, @"D:\OldPath\1.mp3", DateTime.UtcNow),
                FavoriteTrackEntry.FromTrack(track2),
            ]);

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        var query = new TrackSearchQuery(
            Keyword: "Artist",
            Artist: "artist a",
            Album: "album x",
            Genre: "jazz",
            IsFavoriteOnly: true,
            Limit: 1,
            Offset: 1);

        // Act
        TrackSearchResult actual = await sut.SearchTracksAsync(query);

        // Assert
        Assert.Equal(2, actual.TotalCount);
        Assert.Equal(1, actual.Offset);
        Assert.Equal(1, actual.Limit);
        Track returned = Assert.Single(actual.Tracks);
        Assert.Equal("Track 2", returned.Title);
        Assert.True(returned.IsFavorite);
        mockFavRepo.Verify(
            r => r.UpdateMetadataAsync(It.IsAny<IReadOnlyList<FavoriteMetadataChange>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// SearchTracksAsync(TrackSearchQuery) に範囲外の limit または負の offset を指定した場合、ArgumentOutOfRangeException がスローされることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(201, 0)]
    [InlineData(50, -1)]
    public async Task SearchTracksAsync_ページネーション範囲外指定_ArgumentOutOfRangeExceptionをスローすること(int limit, int offset)
    {
        // Arrange
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockFavRepo = new Mock<IFavoriteRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);
        var query = new TrackSearchQuery(Limit: limit, Offset: offset);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.SearchTracksAsync(query));
    }

    /// <summary>
    /// お気に入りエントリの元ファイルパスの曲が改題され、別パスに旧メタデータ一致の曲が存在する場合でも、キーワード付きお気に入り限定検索で改題曲へ誤フォールバックしないことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_元パス改題曲と別パス旧メタデータ一致曲が共存_改題曲のお気に入り限定検索が0件になること()
    {
        // Arrange
        var renamedTrack = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\Music\original_path.flac"),
            title: "Renamed",
            artist: "Shared Artist",
            album: "Shared Album",
            duration: TimeSpan.FromSeconds(200),
            trackNumber: 1,
            genre: "Rock");
        var originalTrack = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\Music\another_path.flac"),
            title: "Original",
            artist: "Shared Artist",
            album: "Shared Album",
            duration: TimeSpan.FromSeconds(200),
            trackNumber: 1,
            genre: "Rock");

        var favoriteEntry = new FavoriteTrackEntry(
            title: "Original",
            artist: "Shared Artist",
            album: "Shared Album",
            trackNumber: 1,
            durationSeconds: 200.0,
            lastKnownFilePath: @"C:\Music\original_path.flac",
            addedAt: DateTime.UtcNow);

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([renamedTrack, originalTrack]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([favoriteEntry]);

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        TrackSearchResult favoritesOnly = await sut.SearchTracksAsync(new TrackSearchQuery(IsFavoriteOnly: true));
        TrackSearchResult renamedFavorites = await sut.SearchTracksAsync(new TrackSearchQuery(Keyword: "Renamed", IsFavoriteOnly: true));
        TrackSearchResult renamedAll = await sut.SearchTracksAsync(new TrackSearchQuery(Keyword: "Renamed", IsFavoriteOnly: false));

        // Assert
        Track favoriteTrack = Assert.Single(favoritesOnly.Tracks);
        Assert.Equal(originalTrack.Id, favoriteTrack.Id);
        Assert.True(favoriteTrack.IsFavorite);

        Assert.Equal(0, renamedFavorites.TotalCount);
        Assert.Empty(renamedFavorites.Tracks);

        Track nonFavoriteRenamed = Assert.Single(renamedAll.Tracks);
        Assert.Equal(renamedTrack.Id, nonFavoriteRenamed.Id);
        Assert.False(nonFavoriteRenamed.IsFavorite);
    }

    /// <summary>
    /// ScanFolderAsync が共通の SupportedAudioExtensions を用いて .mp4 を含む対応形式を取りこぼさずスキャンすることを検証します。
    /// </summary>
    [Fact]
    public async Task ScanFolderAsync_MP4ファイルを含むフォルダ_MP4を含めてスキャンおよび保存されること()
    {
        // Arrange
        string mp4File = Path.Combine(_tempDirectory, "sample.mp4");
        string opusFile = Path.Combine(_tempDirectory, "sample.opus");
        File.WriteAllBytes(mp4File, [1, 2, 3]);
        File.WriteAllBytes(opusFile, [4, 5, 6]);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        var mockEventBus = new Mock<IEventBus>();
        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), mockEventBus.Object);

        // Act
        var actual = await sut.ScanFolderAsync(_tempDirectory);

        // Assert
        Assert.Equal(2, actual.Count);
        Assert.Contains(actual, t => string.Equals(t.FilePath, mp4File, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(actual, t => string.Equals(t.FilePath, opusFile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// SyncLibraryTracksAsync が同一パスの既存 TrackId を維持したうえで、更新・新規追加・削除を ReplaceAllAsync で一括同期することを検証します。
    /// </summary>
    [Fact]
    public async Task SyncLibraryTracksAsync_既存トラック更新と追加削除_既存IDを維持してReplaceAllAsyncへ渡されること()
    {
        // Arrange
        var existingId = TrackId.New();
        var existingTrack = new Track(existingId, AudioPath.Create(@"C:\Music\keep.mp4"), "Old Title", "Old Artist", "Album", TimeSpan.FromSeconds(180));
        var removedTrack = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\removed.mp3"), "Removed", "Artist", "Album", TimeSpan.FromSeconds(120));

        var updatedKeepTrack = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\keep.mp4"), "New Title", "New Artist", "Album", TimeSpan.FromSeconds(180));
        var addedTrack = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\added.flac"), "Added", "Artist B", "Album B", TimeSpan.FromSeconds(200));

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingTrack, removedTrack]);

        List<Track>? replacedTracks = null;
        mockTrackRepo.Setup(r => r.ReplaceAllAsync(It.IsAny<IEnumerable<Track>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Track>, CancellationToken>((ts, _) => replacedTracks = ts.ToList())
            .Returns(Task.CompletedTask);

        var sut = new LibraryApplicationService(mockTrackRepo.Object, new Mock<IFavoriteRepository>().Object, new TagLibMetadataExtractor(), new Mock<IEventBus>().Object);

        // Act
        await sut.SyncLibraryTracksAsync([updatedKeepTrack, addedTrack]);

        // Assert
        Assert.NotNull(replacedTracks);
        Assert.Equal(2, replacedTracks.Count);
        Track syncedKeep = Assert.Single(replacedTracks, t => t.FilePath == @"C:\Music\keep.mp4");
        Assert.Equal(existingId, syncedKeep.Id);
        Assert.Equal("New Title", syncedKeep.Title);
        Assert.Contains(replacedTracks, t => t.Id == addedTrack.Id && t.Title == "Added");
        Assert.DoesNotContain(replacedTracks, t => t.FilePath == @"C:\Music\removed.mp3");
    }

    /// <summary>
    /// GetAllAlbumsAsyncが同名アルバム・別アーティストの楽曲を別個のAlbumとして集約し、それぞれに一意なAlbumIdを付与することを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAlbumsAsync_同名アルバム別アーティスト混在時_それぞれ異なる一意のAlbumIdが付与されること()
    {
        // Arrange
        var t1 = CreateSampleTrack("Song 1", "Artist A", "Greatest Hits", trackNumber: 1, filePath: @"C:\Music\ArtistA\a1.mp3");
        var t2 = CreateSampleTrack("Song 2", "Artist A", "greatest hits", trackNumber: 2, filePath: @"C:\Music\ArtistA\a2.mp3");
        var t3 = CreateSampleTrack("Song 1", "Artist B", "Greatest Hits", trackNumber: 1, filePath: @"C:\Music\ArtistB\b1.mp3");

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t2, t3, t1]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), new Mock<IEventBus>().Object);

        // Act
        var albums = await sut.GetAllAlbumsAsync();

        // Assert
        Assert.Equal(2, albums.Count);
        var albumA = Assert.Single(albums, a => a.Artist == "Artist A");
        var albumB = Assert.Single(albums, a => a.Artist == "Artist B");
        Assert.NotEqual(albumA.Id, albumB.Id);
        Assert.Equal(2, albumA.Tracks.Count);
        Assert.Equal([t1.Id, t2.Id], albumA.Tracks.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// GetAlbumByIdAsyncが存在するAlbumIdを指定された場合、該当アルバムとトラック番号順の収録曲を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumByIdAsync_存在するAlbumId指定_該当アルバムとトラック番号順の収録曲を返すこと()
    {
        // Arrange
        var targetAlbumId = AlbumId.New();
        var t1 = CreateSampleTrack("Track 1", "Artist A", "Same Title", trackNumber: 1, filePath: @"C:\Music\ArtistA\a1.flac");
        var t2 = CreateSampleTrack("Track 2", "Artist A", "Same Title", trackNumber: 2, filePath: @"C:\Music\ArtistA\a2.flac");
        t1.AlbumId = targetAlbumId;
        t2.AlbumId = targetAlbumId;
        var other = CreateSampleTrack("Track 1", "Artist B", "Same Title", trackNumber: 1, filePath: @"C:\Music\ArtistB\b1.flac");

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t2, other, t1]);

        var sut = new LibraryApplicationService(mockTrackRepo.Object, new Mock<IFavoriteRepository>().Object, new TagLibMetadataExtractor(), new Mock<IEventBus>().Object);

        // Act
        var actual = await sut.GetAlbumByIdAsync(targetAlbumId);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(targetAlbumId, actual.Id);
        Assert.Equal("Same Title", actual.Name);
        Assert.Equal("Artist A", actual.Artist);
        Assert.Equal([t1.Id, t2.Id], actual.Tracks.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// GetAlbumByIdAsyncが存在しないAlbumIdを指定された場合、nullを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumByIdAsync_存在しないAlbumId指定_nullを返すこと()
    {
        // Arrange
        var track = CreateSampleTrack("Track 1", "Artist A", "Album A");
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track]);

        var sut = new LibraryApplicationService(mockTrackRepo.Object, new Mock<IFavoriteRepository>().Object, new TagLibMetadataExtractor(), new Mock<IEventBus>().Object);
        var unknownAlbumId = AlbumId.New();

        // Act
        var actual = await sut.GetAlbumByIdAsync(unknownAlbumId);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// GetAlbumByIdAsyncがfavorites.jsonのお気に入り正本と読み取り専用照合を行い、tracks.json側がfalseでも最新のお気に入り状態を反映することを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumByIdAsync_お気に入り登録済みかつトラック側falseの場合_最新のお気に入り状態が反映され書き込み副作用がないこと()
    {
        // Arrange
        var targetAlbumId = AlbumId.New();
        var t1 = CreateSampleTrack("Track 1", "Artist A", "Album Fav", isFavorite: false, trackNumber: 1, filePath: @"C:\Music\fav1.flac");
        var t2 = CreateSampleTrack("Track 2", "Artist A", "Album Fav", isFavorite: false, trackNumber: 2, filePath: @"C:\Music\fav2.flac");
        t1.AlbumId = targetAlbumId;
        t2.AlbumId = targetAlbumId;

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t1, t2]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new FavoriteTrackEntry("Track 1", "Artist A", "Album Fav", 1, 180.0, @"D:\OldPath\fav1.mp3", DateTime.UtcNow)
            ]);

        var sut = new LibraryApplicationService(mockTrackRepo.Object, mockFavRepo.Object, new TagLibMetadataExtractor(), new Mock<IEventBus>().Object);

        // Act
        var actual = await sut.GetAlbumByIdAsync(targetAlbumId);

        // Assert
        Assert.NotNull(actual);
        Assert.True(Assert.Single(actual.Tracks, t => t.Id == t1.Id).IsFavorite);
        Assert.False(Assert.Single(actual.Tracks, t => t.Id == t2.Id).IsFavorite);
        mockFavRepo.Verify(
            r => r.UpdateMetadataAsync(It.IsAny<IReadOnlyList<FavoriteMetadataChange>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// SearchAlbumsAsync で複合条件絞り込み・代表年算出・安定ソート・ページネーションが正しく適用されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchAlbumsAsync_複合条件とページネーション指定_代表年算出と絞り込みおよび安定ソート結果を返すこと()
    {
        // Arrange
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\a1.flac"), title: "T1", artist: "Alpha Trio", album: "Zeta Album", duration: TimeSpan.FromSeconds(180), year: 2024, trackNumber: 1, genre: "Jazz");
        var t2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\a2.flac"), title: "T2", artist: "Alpha Trio", album: "Zeta Album", duration: TimeSpan.FromSeconds(180), year: 2024, trackNumber: 2, genre: "Jazz");
        var t3 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\a3.flac"), title: "T3", artist: "Alpha Trio", album: "Zeta Album", duration: TimeSpan.FromSeconds(180), year: 0, trackNumber: 3, genre: "Fusion");
        var t4 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\b1.flac"), title: "B1", artist: "Alpha Trio", album: "Beta Album", duration: TimeSpan.FromSeconds(180), year: 2024, trackNumber: 1, genre: "Jazz");
        var t5 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\c1.flac"), title: "C1", artist: "Alpha Trio", album: "Old Album", duration: TimeSpan.FromSeconds(180), year: 2020, trackNumber: 1, genre: "Jazz");
        var t6 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\d1.flac"), title: "D1", artist: "Other Artist", album: "Rock Album", duration: TimeSpan.FromSeconds(180), year: 2024, trackNumber: 1, genre: "Rock");

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t1, t2, t3, t4, t5, t6]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([FavoriteTrackEntry.FromTrack(t2)]);

        var sut = new LibraryApplicationService(
            mockTrackRepo.Object,
            mockFavRepo.Object,
            new TagLibMetadataExtractor(),
            new Mock<IEventBus>().Object);

        // Act
        AlbumSearchResult result = await sut.SearchAlbumsAsync(new AlbumSearchQuery(
            Keyword: "alpha",
            Artist: "ALPHA",
            Genre: "jazz",
            Year: 2024,
            IncludeTracks: true,
            Limit: 1,
            Offset: 1));

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.Offset);
        Assert.Equal(1, result.Limit);
        Album album = Assert.Single(result.Albums);
        Assert.Equal("Zeta Album", album.Name);
        Assert.Equal("Alpha Trio", album.Artist);
        Assert.Equal(2024u, album.Year);
        Assert.Equal(3, album.TrackCount);
        Assert.True(Assert.Single(album.Tracks, t => t.Id == t2.Id).IsFavorite);
        mockFavRepo.Verify(
            r => r.UpdateMetadataAsync(It.IsAny<IReadOnlyList<FavoriteMetadataChange>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// SearchAlbumsAsync で範囲外の limit・offset・year を指定した場合、ArgumentOutOfRangeException がスローされることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(201, 0, null)]
    [InlineData(50, -1, null)]
    [InlineData(50, 0, -1)]
    public async Task SearchAlbumsAsync_範囲外パラメータ指定_ArgumentOutOfRangeExceptionをスローすること(int limit, int offset, int? year)
    {
        // Arrange
        var sut = new LibraryApplicationService(
            new Mock<ITrackRepository>().Object,
            new Mock<IFavoriteRepository>().Object,
            new TagLibMetadataExtractor(),
            new Mock<IEventBus>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.SearchAlbumsAsync(new AlbumSearchQuery(Year: year, Limit: limit, Offset: offset)));
    }

    /// <summary>
    /// SearchAlbumsAsync で IncludeTracks=false（既定値）を指定した場合、IFavoriteRepository へアクセスせずにアルバム概要を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchAlbumsAsync_IncludeTracks無効指定_お気に入りリポジトリを参照せずアルバム概要を返すこと()
    {
        // Arrange
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\only.flac"), title: "T1", artist: "Artist A", album: "Album A", duration: TimeSpan.FromSeconds(180), year: 2024, trackNumber: 1, genre: "Jazz");
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t1]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        var sut = new LibraryApplicationService(
            mockTrackRepo.Object,
            mockFavRepo.Object,
            new TagLibMetadataExtractor(),
            new Mock<IEventBus>().Object);

        // Act
        AlbumSearchResult result = await sut.SearchAlbumsAsync(new AlbumSearchQuery(IncludeTracks: false));

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Albums);
        mockFavRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
