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
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Infrastructure.Repository;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="PlaylistViewModel"/> の選択、非同期競合制御、およびコマンド処理を検証するテストクラス。
/// </summary>
public sealed class PlaylistViewModelTests
{
    private readonly Mock<ITrackRepository> _trackRepoMock = new();
    private readonly Mock<IPlaylistRepository> _playlistRepoMock = new();
    private readonly Mock<IFavoriteRepository> _favoriteRepoMock = new();
    private readonly TagLibMetadataExtractor _metadataExtractor = new();
    private readonly Mock<IAudioService> _audioServiceMock = new();
    private readonly InMemoryEventBus _eventBus = new();

    private readonly PlaylistApplicationService _playlistAppService;
    private readonly LibraryApplicationService _libraryAppService;

    public PlaylistViewModelTests()
    {
        _playlistRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserPlaylist>());
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        _playlistAppService = new PlaylistApplicationService(_playlistRepoMock.Object, _trackRepoMock.Object, _eventBus);
        _libraryAppService = new LibraryApplicationService(
            _trackRepoMock.Object,
            _favoriteRepoMock.Object,
            _metadataExtractor,
            _eventBus);
    }

    /// <summary>
    /// 古いプレイリストの読み込みが遅れて完了しても、最新の選択内容が維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task SelectPlaylistAsync_古い読み込みが後から完了する_最新プレイリストの楽曲を維持する()
    {
        // Arrange
        var firstTrack = new Track { FilePath = @"C:\Music\first.mp3", Title = "First" };
        var secondTrack = new Track { FilePath = @"C:\Music\second.mp3", Title = "Second" };

        var firstRequestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource<Track?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _trackRepoMock.Setup(r => r.GetByPathAsync(
                It.Is<AudioPath>(p => p.Value == firstTrack.FilePath),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                firstRequestStarted.TrySetResult(true);
                return firstResult.Task;
            });

        _trackRepoMock.Setup(r => r.GetByPathAsync(
                It.Is<AudioPath>(p => p.Value == secondTrack.FilePath),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondTrack);

        using var sut = new PlaylistViewModel(
            _playlistAppService,
            _libraryAppService,
            _audioServiceMock.Object,
            _eventBus);

        var firstPlaylist = new UserPlaylist { Name = "First", TrackPaths = [firstTrack.FilePath] };
        var secondPlaylist = new UserPlaylist { Name = "Second", TrackPaths = [secondTrack.FilePath] };

        // Act
        Task firstLoad = sut.SelectPlaylistAsync(firstPlaylist);
        await firstRequestStarted.Task;

        Task secondLoad = sut.SelectPlaylistAsync(secondPlaylist);
        await secondLoad;

        firstResult.TrySetResult(firstTrack);
        await firstLoad;

        // Assert
        Assert.Same(secondPlaylist, sut.SelectedPlaylist);
        Assert.Equal("Second", sut.CurrentPlaylistName);
        Assert.Collection(sut.PlaylistTracks, track => Assert.Same(secondTrack, track));
    }

    /// <summary>
    /// CreatePlaylistCommandにプレイリスト名を渡して実行時、UserPlaylistsコレクションに追加されることを検証します。
    /// </summary>
    [Fact]
    public void CreatePlaylistCommand_名前指定実行_UserPlaylistsに追加される()
    {
        // Arrange
        using var sut = new PlaylistViewModel(
            _playlistAppService,
            _libraryAppService,
            _audioServiceMock.Object,
            _eventBus);

        // Act
        sut.CreatePlaylistCommand.Execute("My New Playlist");

        // Assert
        Assert.Contains(sut.UserPlaylists, p => p.Name == "My New Playlist");
    }

    /// <summary>
    /// ShowFavorites呼び出し時、お気に入りビュー状態になり指定トラックが表示されることを検証します。
    /// </summary>
    [Fact]
    public void ShowFavorites_呼び出し時_お気に入りビューになり指定トラックが表示される()
    {
        // Arrange
        using var sut = new PlaylistViewModel(
            _playlistAppService,
            _libraryAppService,
            _audioServiceMock.Object,
            _eventBus);

        var favoriteTrack = new Track { FilePath = @"C:\Music\fav.mp3", Title = "Fav Song" };

        // Act
        sut.ShowFavorites(new[] { favoriteTrack }, null);

        // Assert
        Assert.True(sut.IsFavoritesView);
        Assert.Equal("Favorites", sut.CurrentPlaylistName);
        Assert.Contains(sut.PlaylistTracks, t => t.FilePath == favoriteTrack.FilePath);
    }

    /// <summary>
    /// プレイリスト読み込み失敗時、HasLoadError と LastErrorMessage が設定され既存の表示一覧が消去されないことを検証します。
    /// </summary>
    [Fact]
    public void LoadPlaylists_読み込み失敗時_エラー状態が設定され既存コレクションを維持する()
    {
        // Arrange
        var existing = new UserPlaylist(PlaylistId.New(), "既存");
        _playlistRepoMock.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing])
            .ThrowsAsync(new IOException("Lock timeout"));

        using var sut = new PlaylistViewModel(
            _playlistAppService,
            _libraryAppService,
            _audioServiceMock.Object,
            _eventBus);
        sut.LoadPlaylists();

        // Act
        sut.LoadPlaylists();

        // Assert
        Assert.True(sut.HasLoadError);
        Assert.False(string.IsNullOrWhiteSpace(sut.LastErrorMessage));
        Assert.Single(sut.UserPlaylists);
        Assert.Equal(existing.Id, sut.UserPlaylists[0].Id);
    }

    /// <summary>
    /// CreatePlaylistCommand 実行時にリポジトリ保存が失敗した場合、LastErrorMessage が設定され UserPlaylists に追加されないことを検証します。
    /// </summary>
    [Fact]
    public void CreatePlaylistCommand_保存失敗時_エラーメッセージが設定されコレクションに追加されない()
    {
        // Arrange
        _playlistRepoMock.Setup(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk locked"));

        using var sut = new PlaylistViewModel(
            _playlistAppService,
            _libraryAppService,
            _audioServiceMock.Object,
            _eventBus);

        // Act
        sut.CreatePlaylistCommand.Execute("Failed Playlist");

        // Assert
        Assert.Empty(sut.UserPlaylists);
        Assert.False(string.IsNullOrWhiteSpace(sut.LastErrorMessage));
    }

    /// <summary>
    /// ID 形式のプレイリスト [A, X]（X は未登録）に対して B の追記、X の再登録、および別曲 A の削除を行った場合、WPF（SelectPlaylistAsync）と MCP（ResolvePlaylistTracksAsync）の双方で曲順が完全に一致することを検証します。
    /// </summary>
    [Fact]
    public async Task SelectPlaylistAsync_ID形式で未登録曲を含む追記と削除と再登録_WPFとMCPの曲順が一致する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorWpfMcpIdParity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            string favoritesPath = Path.Combine(tempDir, "favorites.json");

            var trackA = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "a.flac")), title: "Track A", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
            var trackX = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "x.flac")), title: "Track X", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(190));
            var trackB = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "b.flac")), title: "Track B", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(200));

            // ライブラリには A, B のみ登録（X は未登録）、プレイリストは ID 形式で [A, X]
            using (var seedTrackRepo = new JsonTrackRepository(tracksPath))
            using (var seedPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                await seedTrackRepo.SaveRangeAsync([trackA, trackB]);
                await seedPlaylistRepo.SaveAsync(new UserPlaylist(PlaylistId.New(), "ID形式リスト", [trackA.Id, trackX.Id]));
            }

            // Act 1: X 未登録の間に B を追記して保存
            using (var editTrackRepo = new JsonTrackRepository(tracksPath))
            using (var editPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                var editService = new PlaylistApplicationService(editPlaylistRepo, editTrackRepo, new InMemoryEventBus());
                UserPlaylist loaded = Assert.Single(editService.LoadPlaylists());
                Result<IReadOnlyList<Track>> addResult = editService.TryAddTracksPreservingOrder(loaded, [trackB]);
                Assert.True(addResult.IsSuccess);
                Assert.True(editService.TrySavePlaylist(loaded).IsSuccess);
            }

            // Act 2: X を同じ ID で再登録し、別リポジトリから WPF と MCP の両方で曲順を確認
            using (var reregisterTrackRepo = new JsonTrackRepository(tracksPath))
            {
                await reregisterTrackRepo.SaveAsync(trackX);
            }

            using (var verifyTrackRepo = new JsonTrackRepository(tracksPath))
            using (var verifyPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            using (var verifyFavRepo = new JsonFavoriteRepository(favoritesPath))
            {
                var eventBus = new InMemoryEventBus();
                var playlistService = new PlaylistApplicationService(verifyPlaylistRepo, verifyTrackRepo, eventBus, verifyFavRepo);
                var libraryService = new LibraryApplicationService(verifyTrackRepo, verifyFavRepo, _metadataExtractor, eventBus);
                using var sut = new PlaylistViewModel(playlistService, libraryService, _audioServiceMock.Object, eventBus);

                sut.LoadPlaylists();
                UserPlaylist reloaded = Assert.Single(sut.UserPlaylists);
                await sut.SelectPlaylistAsync(reloaded);
                IReadOnlyList<Track> mcpResolved = await playlistService.ResolvePlaylistTracksAsync(reloaded);

                // Assert 1: WPF・MCP ともに [A, X, B] で完全一致
                Assert.Equal([trackA.Id, trackX.Id, trackB.Id], sut.PlaylistTracks.Select(t => t.Id).ToArray());
                Assert.Equal([trackA.Id, trackX.Id, trackB.Id], mcpResolved.Select(t => t.Id).ToArray());
            }

            // Act 3: 再度 X が未登録の状態を想定し、A のみ削除しても X の ID が保持されることを検証
            using (var deleteTrackRepo = new JsonTrackRepository(tracksPath))
            using (var deletePlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                var deleteService = new PlaylistApplicationService(deletePlaylistRepo, deleteTrackRepo, new InMemoryEventBus());
                UserPlaylist loadedForDelete = Assert.Single(deleteService.LoadPlaylists());
                Assert.True(PlaylistApplicationService.RemoveTrackPreservingUnresolved(loadedForDelete, trackA));
                Assert.True(deleteService.TrySavePlaylist(loadedForDelete).IsSuccess);
            }

            using (var finalTrackRepo = new JsonTrackRepository(tracksPath))
            using (var finalPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            using (var finalFavRepo = new JsonFavoriteRepository(favoritesPath))
            {
                var eventBus = new InMemoryEventBus();
                var playlistService = new PlaylistApplicationService(finalPlaylistRepo, finalTrackRepo, eventBus, finalFavRepo);
                var libraryService = new LibraryApplicationService(finalTrackRepo, finalFavRepo, _metadataExtractor, eventBus);
                using var sut = new PlaylistViewModel(playlistService, libraryService, _audioServiceMock.Object, eventBus);

                sut.LoadPlaylists();
                UserPlaylist finalPlaylist = Assert.Single(sut.UserPlaylists);
                await sut.SelectPlaylistAsync(finalPlaylist);
                IReadOnlyList<Track> finalMcpResolved = await playlistService.ResolvePlaylistTracksAsync(finalPlaylist);

                // Assert 2: 削除後も WPF・MCP ともに [X, B] で完全一致
                Assert.Equal([trackX.Id, trackB.Id], sut.PlaylistTracks.Select(t => t.Id).ToArray());
                Assert.Equal([trackX.Id, trackB.Id], finalMcpResolved.Select(t => t.Id).ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
