using System;
using System.Collections.Generic;
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
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// RecentViewModelの履歴記録・重複制御・上限トリミング・検索・削除・再生・ライブラリ同期を検証するテストクラス
/// </summary>
public sealed class RecentViewModelTests
{
    private static Track CreateTrack(string title, string artist = "Artist A", string album = "Album A")
    {
        return new Track
        {
            FilePath = $@"C:\music\{artist}\{album}\{title}.flac",
            Title = title,
            Artist = artist,
            Album = album,
            Duration = TimeSpan.FromMinutes(3),
        };
    }

    private static (RecentViewModel Sut, InMemoryRecentRepository Repo, StubAudioService Audio, LibraryViewModel Library) CreateSut(
        Func<DateTime>? clock = null,
        IEnumerable<RecentTrackEntry>? initialEntries = null)
    {
        var libraryService = new LibraryApplicationService(
            new InMemoryTrackRepository(),
            new InMemoryFavoriteRepository(),
            new TagLibMetadataExtractor(),
            new InMemoryEventBus());
        var audio = new StubAudioService();
        var settings = new StubSettingsService();
        var library = new LibraryViewModel(libraryService, audio, settings);
        var repo = new InMemoryRecentRepository();
        if (initialEntries != null)
        {
            repo.Save(initialEntries.ToList());
        }

        var sut = new RecentViewModel(repo, library, audio, clock ?? (() => new DateTime(2026, 9, 11, 21, 0, 0)));
        return (sut, repo, audio, library);
    }

    /// <summary>
    /// 楽曲再生イベントが発生した際、履歴先頭に追加され同一曲の再再生時は重複せず先頭へ移動することを検証します。
    /// </summary>
    [Fact]
    public void RecordTrack_同一曲を再再生した場合_重複せず先頭へ更新される()
    {
        // Arrange
        var (sut, repo, audio, library) = CreateSut();
        try
        {
            var t1 = CreateTrack("Track 1");
            var t2 = CreateTrack("Track 2");

            // Act
            audio.RaiseTrackChanged(t1);
            audio.RaiseTrackChanged(t2);
            audio.RaiseTrackChanged(t1);

            // Assert
            Assert.Equal(2, sut.FilteredEntries.Count);
            Assert.Equal("Track 1", sut.FilteredEntries[0].Title);
            Assert.Equal("Track 2", sut.FilteredEntries[1].Title);
            Assert.Equal(2, repo.SavedEntries.Count);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// 履歴件数が上限100件を超えた場合、古い履歴が削除され100件に維持されることを検証します。
    /// </summary>
    [Fact]
    public void RecordTrack_上限100件を超えた場合_古い履歴が削除され100件に維持される()
    {
        // Arrange
        var (sut, _, _, library) = CreateSut();
        try
        {
            // Act
            for (int i = 1; i <= 105; i++)
            {
                sut.RecordTrack(CreateTrack($"Track {i}"));
            }

            // Assert
            Assert.Equal(RecentViewModel.MaxHistoryCount, sut.FilteredEntries.Count);
            Assert.Equal("Track 105", sut.FilteredEntries[0].Title);
            Assert.Equal("Track 6", sut.FilteredEntries[^1].Title);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// 検索キーワードを入力した際、該当する履歴エントリのみに絞り込まれることを検証します。
    /// </summary>
    [Fact]
    public void SearchKeyword_キーワード入力時_該当する履歴のみに絞り込まれる()
    {
        // Arrange
        var (sut, _, _, library) = CreateSut();
        try
        {
            sut.RecordTrack(CreateTrack("Get Lucky", "Daft Punk", "RAM"));
            sut.RecordTrack(CreateTrack("Midnight City", "M83", "Hurry Up"));

            // Act
            sut.SearchKeyword = "midnight";

            // Assert
            Assert.Single(sut.FilteredEntries);
            Assert.Equal("Midnight City", sut.FilteredEntries[0].Title);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// 個別削除および全クリアを実行した際、表示と永続化データの双方から削除され選択状態もクリアされることを検証します。
    /// </summary>
    [Fact]
    public void RemoveEntryAndClearHistory_削除実行時_一覧と保存先から削除される()
    {
        // Arrange
        var (sut, repo, _, library) = CreateSut();
        try
        {
            sut.RecordTrack(CreateTrack("Track 1"));
            sut.RecordTrack(CreateTrack("Track 2"));
            sut.SelectedEntry = sut.FilteredEntries[0];

            // Act
            sut.RemoveEntry(sut.FilteredEntries[0]);
            int afterSingleRemove = sut.FilteredEntries.Count;
            RecentTrackEntry? selectedAfterRemove = sut.SelectedEntry;
            sut.ClearHistory();

            // Assert
            Assert.Equal(1, afterSingleRemove);
            Assert.Null(selectedAfterRemove);
            Assert.Empty(sut.FilteredEntries);
            Assert.Empty(repo.SavedEntries);
            Assert.True(sut.IsEmpty);
            Assert.False(sut.HasHistory);
            Assert.Null(sut.SelectedEntry);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// 検索フィルタで選択中エントリが表示対象外になった場合、SelectedEntryがnullへクリアされることを検証します。
    /// </summary>
    [Fact]
    public void SearchKeyword_選択中エントリが絞り込み対象外になった場合_SelectedEntryがnullになる()
    {
        // Arrange
        var (sut, _, _, library) = CreateSut();
        try
        {
            sut.RecordTrack(CreateTrack("Get Lucky", "Daft Punk", "RAM"));
            sut.RecordTrack(CreateTrack("Midnight City", "M83", "Hurry Up"));
            sut.SelectedEntry = sut.FilteredEntries[0];

            // Act
            sut.SearchKeyword = "Daft";

            // Assert
            Assert.Single(sut.FilteredEntries);
            Assert.Null(sut.SelectedEntry);
            Assert.True(sut.HasHistory);
            Assert.False(sut.IsEmpty);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// PlayFromHistoryを実行した際、SetPlaylist→PlaybackRequested→PlayTrackの順に表示履歴先頭から再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void PlayFromHistory_履歴存在時_先頭曲から規定の順序で再生が開始される()
    {
        // Arrange
        var (sut, _, audio, library) = CreateSut();
        try
        {
            var t1 = CreateTrack("Track 1");
            var t2 = CreateTrack("Track 2");
            sut.RecordTrack(t1);
            sut.RecordTrack(t2);

            var callOrder = new List<string>();
            audio.OnSetPlaylist = () => callOrder.Add("SetPlaylist");
            audio.OnPlayTrack = () => callOrder.Add("PlayTrack");

            IReadOnlyList<Track>? requestedTracks = null;
            Track? requestedStart = null;
            string? requestedName = null;
            string? requestedSubtitle = null;
            sut.PlaybackRequested += (tracks, start, name, subtitle) =>
            {
                callOrder.Add("PlaybackRequested");
                requestedTracks = tracks;
                requestedStart = start;
                requestedName = name;
                requestedSubtitle = subtitle;
            };

            // Act
            sut.PlayFromHistory();

            // Assert
            Assert.Equal(new[] { "SetPlaylist", "PlaybackRequested", "PlayTrack" }, callOrder);
            Assert.NotNull(requestedTracks);
            Assert.Equal(2, requestedTracks!.Count);
            Assert.Same(t2, requestedStart);
            Assert.Equal("最近再生した曲", requestedName);
            Assert.Equal("2 曲", requestedSubtitle);
            Assert.Same(t2, audio.LastStartTrack);
            Assert.True(audio.PlayCalled);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// PlayEntryで履歴の2曲目を指定した際、SetPlaylist→PlaybackRequested→PlayTrackの順に指定曲から再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void PlayEntry_2曲目の履歴エントリ指定時_指定曲を開始位置として規定の順序で再生される()
    {
        // Arrange
        var (sut, _, audio, library) = CreateSut();
        try
        {
            var t1 = CreateTrack("Track 1");
            var t2 = CreateTrack("Track 2");
            sut.RecordTrack(t1);
            sut.RecordTrack(t2);
            var secondEntry = sut.FilteredEntries[1]; // Track 1

            var callOrder = new List<string>();
            audio.OnSetPlaylist = () => callOrder.Add("SetPlaylist");
            audio.OnPlayTrack = () => callOrder.Add("PlayTrack");

            Track? requestedStart = null;
            sut.PlaybackRequested += (_, start, _, _) =>
            {
                callOrder.Add("PlaybackRequested");
                requestedStart = start;
            };

            // Act
            sut.PlayEntry(secondEntry);

            // Assert
            Assert.Equal(new[] { "SetPlaylist", "PlaybackRequested", "PlayTrack" }, callOrder);
            Assert.Same(t1, requestedStart);
            Assert.Same(t1, audio.LastStartTrack);
            Assert.Equal(new[] { t2, t1 }, audio.LastPlaylist);
            Assert.True(audio.PlayCalled);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>
    /// 履歴読み込み後にライブラリへ同一ファイルパスのトラックが追加された際、履歴エントリが新しいイミュータブルインスタンスでライブラリ実体へ差し替わることを検証します。
    /// </summary>
    [Fact]
    public void OnLibraryTracksChanged_ライブラリへ同一ファイルパスの曲追加時_新しいエントリでTrack実体が同期される()
    {
        // Arrange
        var persistedTrack = CreateTrack("Midnight City", "M83", "Hurry Up");
        var playedAt = new DateTime(2026, 9, 11, 21, 15, 0);
        var initialEntry = new RecentTrackEntry(persistedTrack, playedAt);
        var (sut, _, _, library) = CreateSut(initialEntries: new[] { initialEntry });
        try
        {
            var beforeEntry = sut.FilteredEntries[0];
            sut.SelectedEntry = beforeEntry;

            var libraryTrack = CreateTrack("Midnight City", "M83", "Hurry Up");
            libraryTrack.IsFavorite = true;

            // Act
            library.Tracks.Add(libraryTrack);

            // Assert
            Assert.Single(sut.FilteredEntries);
            var afterEntry = sut.FilteredEntries[0];
            Assert.NotSame(beforeEntry, afterEntry);
            Assert.Same(libraryTrack, afterEntry.Track);
            Assert.True(afterEntry.Track.IsFavorite);
            Assert.Equal(playedAt, afterEntry.PlayedAt);
            Assert.Same(afterEntry, sut.SelectedEntry);
        }
        finally
        {
            sut.Dispose();
            library.Dispose();
        }
    }

    /// <summary>別画面で追加された履歴を曲の追加と削除で保持し、全消去で削除することを検証します</summary>
    [Fact]
    public void 履歴の更新_画面の読み込み後に別インスタンスが追加した曲を保持する()
    {
        // Arrange
        var (owner, _, audio, library) = CreateSut();
        using var ownedLibrary = library;
        using var ownedView = owner;
        var directory = System.IO.Directory.CreateTempSubdirectory();
        try
        {
            var path = System.IO.Path.Combine(directory.FullName, "RecentTracks.json");
            var repository = new AudioEffector.Infrastructure.Repository.JsonRecentTrackRepository(path);
            using var sut = new RecentViewModel(repository, library, audio);
            var other = new AudioEffector.Infrastructure.Repository.JsonRecentTrackRepository(path);
            var external = new RecentTrackEntry(CreateTrack("External"), DateTime.Now);
            other.Save([external]);

            // Act
            var local = CreateTrack("Local");
            sut.RecordTrack(local);
            sut.RemoveEntry(local);

            // Assert
            Assert.Equal(external.FilePath, Assert.Single(other.Load()).FilePath);
            sut.ClearHistory();
            Assert.Empty(other.Load());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    /// <summary>ロック作成が拒否されても画面を初期化し再生を継続できることを検証します</summary>
    [Fact]
    public void 履歴の読込_アクセス拒否でも表示を初期化できる()
    {
        // Arrange
        var (owner, _, audio, library) = CreateSut();
        using var ownedLibrary = library;
        using var ownedView = owner;
        var repository = new Moq.Mock<IRecentTrackRepository>();
        repository.Setup(r => r.Load()).Throws<UnauthorizedAccessException>();

        // Act
        using var sut = new RecentViewModel(repository.Object, library, audio);

        // Assert
        Assert.True(sut.IsEmpty);
    }

    private sealed class InMemoryRecentRepository : IRecentTrackRepository
    {
        public List<RecentTrackEntry> SavedEntries { get; private set; } = new();
        public IReadOnlyList<RecentTrackEntry> Load() => SavedEntries.ToList();
        public IReadOnlyList<RecentTrackEntry> Update(Action<List<RecentTrackEntry>> update)
        {
            update(SavedEntries);
            return SavedEntries.ToList();
        }
        public void Save(IReadOnlyList<RecentTrackEntry> entries) => SavedEntries = entries.ToList();
    }

    private sealed class InMemoryTrackRepository : ITrackRepository
    {
        private readonly List<Track> _tracks = new();
        public Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Track>>(_tracks);
        public Task<Track?> GetByIdAsync(TrackId id, CancellationToken cancellationToken = default) => Task.FromResult(_tracks.FirstOrDefault(t => t.Id.Equals(id)));
        public Task<Track?> GetByPathAsync(AudioPath path, CancellationToken cancellationToken = default) => Task.FromResult(_tracks.FirstOrDefault(t => string.Equals(t.FilePath, path.Value, StringComparison.OrdinalIgnoreCase)));
        public Task SaveAsync(Track track, CancellationToken cancellationToken = default) { _tracks.Add(track); return Task.CompletedTask; }
        public Task SaveRangeAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default) { _tracks.AddRange(tracks); return Task.CompletedTask; }
        public Task ReplaceAllAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default) { _tracks.Clear(); _tracks.AddRange(tracks); return Task.CompletedTask; }
        public Task DeleteAsync(TrackId id, CancellationToken cancellationToken = default) { _tracks.RemoveAll(t => t.Id.Equals(id)); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { _tracks.Clear(); return Task.CompletedTask; }
        public Task<IReadOnlyList<Track>> SearchAsync(string keyword, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Track>>(_tracks);
    }

    private sealed class InMemoryFavoriteRepository : IFavoriteRepository
    {
        private readonly List<FavoriteTrackEntry> _entries = new();
        public Task<IReadOnlyList<FavoriteTrackEntry>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FavoriteTrackEntry>>(_entries.ToList());
        public Task AddOrUpdateAsync(FavoriteTrackEntry entry, CancellationToken cancellationToken = default) { _entries.Add(entry); return Task.CompletedTask; }
        public Task RemoveAsync(Track track, CancellationToken cancellationToken = default) { _entries.RemoveAll(e => AudioEffector.Domain.Services.FavoriteTrackMatcher.IsMatchForRemoval(e, track)); return Task.CompletedTask; }
        public Task UpdateMetadataAsync(IReadOnlyList<FavoriteMetadataChange> changes, CancellationToken cancellationToken = default)
        {
            FavoriteMetadataChange.ApplyTo(_entries, changes);
            return Task.CompletedTask;
        }
        public Task SaveAllAsync(IReadOnlyList<FavoriteTrackEntry> entries, CancellationToken cancellationToken = default) { _entries.Clear(); _entries.AddRange(entries); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(Track track, CancellationToken cancellationToken = default) => Task.FromResult(AudioEffector.Domain.Services.FavoriteTrackMatcher.FindBestMatchingEntry(track, _entries) != null);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        private AppSettings _settings = new();
        public AppSettings LoadSettings() => _settings;
        public void UpdateSettings(System.Action<AppSettings> update) => update(_settings);
        public Task UpdateSettingsAsync(System.Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(_settings);
            return Task.CompletedTask;
        }
        public void SaveSettings(AppSettings settings) => _settings = settings;
        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) { _settings = settings; return Task.CompletedTask; }
        public List<EqualizerPreset> LoadPresets() => new();
        public void SavePresets(List<EqualizerPreset> presets) { }
    }

    private sealed class StubAudioService : IAudioService
    {
        public List<Track> LastPlaylist { get; private set; } = new();
        public Track? LastStartTrack { get; private set; }
        public bool PlayCalled { get; private set; }
        public Action? OnSetPlaylist { get; set; }
        public Action? OnPlayTrack { get; set; }

        public event Action<Track?>? TrackChanged;
        public event Action<bool>? PlaybackStateChanged { add { } remove { } }
        public event Action? PlaybackStopped { add { } remove { } }
        public event EventHandler? PlaylistEnded { add { } remove { } }
        public event Action<Track>? TrackPlaybackEnded { add { } remove { } }
        public event EventHandler<FftEventArgs>? FftCalculated { add { } remove { } }
        public event Action<List<Track>>? PlaylistChanged { add { } remove { } }
        public event Action<float>? VolumeChanged { add { } remove { } }
        public event Action<bool>? ShuffleChanged { add { } remove { } }
        public event Action<bool>? RepeatChanged { add { } remove { } }
        public event Action<string>? QueueSourceNameChanged { add { } remove { } }

        public void RaiseTrackChanged(Track? track) => TrackChanged?.Invoke(track);

        public IReadOnlyList<Track> UserQueue => Array.Empty<Track>();
        public IReadOnlyList<Track> AlbumQueue => LastPlaylist;
        public Track? CurrentTrack => LastStartTrack;
        public bool IsPlaying => PlayCalled;
        public bool IsPaused => false;
        public bool IsShuffleEnabled { get; set; }
        public bool IsRepeatEnabled { get; set; }
        public TimeSpan CurrentTime => TimeSpan.Zero;
        public TimeSpan TotalTime => TimeSpan.Zero;
        public float Volume { get; set; } = 0.5f;
        public float[] Frequencies { get; } = new float[10];

        public void ClearUserQueue() { }
        public void MoveUserQueueItem(int fromIndex, int toIndex) { }
        public void MoveAlbumQueueItem(int fromIndex, int toIndex) { }
        public void MoveAlbumQueueItemToUserQueue(int fromAlbumIndex, int toUserIndex) { }
        public void SetPlaylist(List<Track> tracks, Track? startTrack = null)
        {
            OnSetPlaylist?.Invoke();
            LastPlaylist = tracks.ToList();
            LastStartTrack = startTrack ?? LastPlaylist.FirstOrDefault();
        }
        public void EnqueueTracks(IReadOnlyList<Track> tracks, bool playNext) { }
        public void RemoveTrack(Track track) { }
        public void PlayTrack(Track track)
        {
            OnPlayTrack?.Invoke();
            LastStartTrack = track;
            PlayCalled = true;
        }
        public void PlayTrack(Track track, bool insertAtBeginning)
        {
            OnPlayTrack?.Invoke();
            LastStartTrack = track;
            PlayCalled = true;
        }
        public Task<bool> PlayTrackAsync(Track track, CancellationToken cancellationToken = default)
        {
            PlayTrack(track);
            return Task.FromResult(true);
        }
        public Task<Track?> PlayPlaylistAsync(IReadOnlyList<Track> tracks, Track? startTrack = null, bool? shuffle = null, bool? repeat = null, string? sourceName = null, int? startTrackIndex = null, CancellationToken cancellationToken = default)
        {
            if (shuffle.HasValue) IsShuffleEnabled = shuffle.Value;
            if (repeat.HasValue) IsRepeatEnabled = repeat.Value;
            SetPlaylist(tracks.ToList(), startTrack);
            Track? started = startTrack ?? (tracks.Count > 0 ? tracks[0] : null);
            if (started != null) PlayTrack(started);
            return Task.FromResult(started);
        }
        public void TogglePlayPause() => PlayCalled = !PlayCalled;
        public void Next() { }
        public void Previous() { }
        public void Stop(bool internalStop = false) => PlayCalled = false;
        public void SeekTo(double percentage) { }
        public void PauseForSeek() { }
        public void ResumeAfterSeek() { }
        public void SetGain(int bandIndex, float gain) { }
        public void UpdateAudioProperties(int sampleRate, int bufferSizeMs) { }
        public void Dispose() { }
    }
}
