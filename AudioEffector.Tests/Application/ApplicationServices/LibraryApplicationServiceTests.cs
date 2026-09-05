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
/// LibraryApplicationServiceのフォルダスキャン、検索、アルバム集約、およびお気に入り管理を検証するテストクラス
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

    private static Track CreateSampleTrack(string title = "Song 1", string artist = "Artist 1", string album = "Album 1", bool isFavorite = false)
    {
        var track = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create($@"C:\Music\{title}.mp3"),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromMinutes(3),
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
        mockFavRepo.Setup(r => r.GetFavoriteIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<TrackId>());

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
        mockFavRepo.Setup(r => r.GetFavoriteIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<TrackId>());

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
    /// GetAllAlbumsAsync呼び出し時、全楽曲がアルバム単位にグルーピングされ、お気に入り状態が反映されるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAlbumsAsync_複数楽曲存在_アルバム名とアーティストでグルーピングされお気に入りフラグが反映されること()
    {
        // Arrange
        var t1 = CreateSampleTrack("Track 1", "Artist A", "Album X");
        var t2 = CreateSampleTrack("Track 2", "Artist A", "Album X");
        var t3 = CreateSampleTrack("Track 3", "Artist B", "Album Y");

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t1, t2, t3]);

        var mockFavRepo = new Mock<IFavoriteRepository>();
        mockFavRepo.Setup(r => r.GetFavoriteIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<TrackId> { t1.Id });

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
    }

    /// <summary>
    /// お気に入り未登録のトラックに対してToggleFavoriteAsyncを呼び出した際、お気に入り追加されリポジトリに保存されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ToggleFavoriteAsync_お気に入り未登録トラック_お気に入り追加されリポジトリへ保存されること()
    {
        // Arrange
        var track = CreateSampleTrack("Song A", isFavorite: false);
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
        mockFavRepo.Verify(r => r.AddAsync(track.Id, It.IsAny<CancellationToken>()), Times.Once);
        mockTrackRepo.Verify(r => r.SaveAsync(track, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// お気に入り登録済みのトラックに対してToggleFavoriteAsyncを呼び出した際、お気に入り解除されリポジトリに保存されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ToggleFavoriteAsync_お気に入り登録済みトラック_お気に入り解除されリポジトリへ保存されること()
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
        mockFavRepo.Verify(r => r.RemoveAsync(track.Id, It.IsAny<CancellationToken>()), Times.Once);
        mockTrackRepo.Verify(r => r.SaveAsync(track, It.IsAny<CancellationToken>()), Times.Once);
    }
}
