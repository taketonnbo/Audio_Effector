using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Tests;

/// <summary>
/// PlaylistViewModelの非同期選択処理を検証します。
/// </summary>
public sealed class PlaylistViewModelTests
{
    /// <summary>
    /// 古いプレイリストの読み込みが遅れて完了しても、最新の選択内容が維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task SelectPlaylistAsync_古い読み込みが後から完了する_最新プレイリストの楽曲を維持する()
    {
        var firstTrack = new Track { FilePath = @"C:\Music\first.mp3", Title = "First" };
        var secondTrack = new Track { FilePath = @"C:\Music\second.mp3", Title = "Second" };
        var trackRepository = new DelayedTrackRepository(firstTrack, secondTrack);
        var eventBus = new InMemoryEventBus();
        var playlistService = new PlaylistApplicationService(new EmptyPlaylistRepository(), trackRepository, eventBus);
        var libraryService = new LibraryApplicationService(
            trackRepository,
            new EmptyFavoriteRepository(),
            new TagLibMetadataExtractor(),
            eventBus);
        using var audioService = new AudioService();
        using var viewModel = new PlaylistViewModel(playlistService, libraryService, audioService, eventBus);
        var firstPlaylist = new UserPlaylist { Name = "First", TrackPaths = [firstTrack.FilePath] };
        var secondPlaylist = new UserPlaylist { Name = "Second", TrackPaths = [secondTrack.FilePath] };

        Task firstLoad = viewModel.SelectPlaylistAsync(firstPlaylist);
        await trackRepository.FirstRequestStarted;
        Task secondLoad = viewModel.SelectPlaylistAsync(secondPlaylist);
        await secondLoad;
        trackRepository.CompleteFirstRequest();
        await firstLoad;

        Assert.Same(secondPlaylist, viewModel.SelectedPlaylist);
        Assert.Equal("Second", viewModel.CurrentPlaylistName);
        Assert.Collection(viewModel.PlaylistTracks, track => Assert.Same(secondTrack, track));
    }

    private sealed class DelayedTrackRepository : ITrackRepository
    {
        private readonly Track _firstTrack;
        private readonly Track _secondTrack;
        private readonly TaskCompletionSource<bool> _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Track?> _firstResult = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DelayedTrackRepository(Track firstTrack, Track secondTrack)
        {
            _firstTrack = firstTrack;
            _secondTrack = secondTrack;
        }

        public Task FirstRequestStarted => _firstRequestStarted.Task;

        public void CompleteFirstRequest() => _firstResult.TrySetResult(_firstTrack);

        public Task<Track?> GetByIdAsync(TrackId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Track?>(null);

        public Task<Track?> GetByPathAsync(AudioPath filePath, CancellationToken cancellationToken = default)
        {
            if (string.Equals(filePath.Value, _firstTrack.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _firstRequestStarted.TrySetResult(true);
                return _firstResult.Task;
            }

            return Task.FromResult<Track?>(_secondTrack);
        }

        public Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Track>>([]);

        public Task<IReadOnlyList<Track>> SearchAsync(string keyword, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Track>>([]);

        public Task SaveAsync(Track track, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveRangeAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(TrackId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyPlaylistRepository : IPlaylistRepository
    {
        public Task<UserPlaylist?> GetByIdAsync(PlaylistId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserPlaylist?>(null);

        public Task<IReadOnlyList<UserPlaylist>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserPlaylist>>([]);

        public Task SaveAsync(UserPlaylist playlist, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(PlaylistId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyFavoriteRepository : IFavoriteRepository
    {
        public Task<IReadOnlySet<TrackId>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<TrackId>>(new HashSet<TrackId>());

        public Task AddAsync(TrackId trackId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(TrackId trackId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ContainsAsync(TrackId trackId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
