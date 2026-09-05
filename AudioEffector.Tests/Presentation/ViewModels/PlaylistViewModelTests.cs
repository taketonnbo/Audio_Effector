using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Library;
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
        _favoriteRepoMock.Setup(r => r.GetFavoriteIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<TrackId>());

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
}
