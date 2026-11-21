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
/// AllSongsViewModelのフィルタリング・ソート・サマリー計算・再生コマンドを検証するテストクラス
/// </summary>
public sealed class AllSongsViewModelTests
{
    private static Track CreateTrack(
        string title,
        string artist = "Artist A",
        string album = "Album A",
        TimeSpan? duration = null,
        string genre = "Pop",
        string format = "FLAC")
    {
        return new Track
        {
            FilePath = $@"C:\music\{artist}\{album}\{title}.flac",
            Title = title,
            Artist = artist,
            Album = album,
            Duration = duration ?? TimeSpan.FromMinutes(3),
            Genre = genre,
            Format = format,
        };
    }

    private static (AllSongsViewModel Sut, LibraryViewModel Library, StubAudioService Audio) CreateSut()
    {
        var libraryService = new LibraryApplicationService(
            new InMemoryTrackRepository(),
            new InMemoryFavoriteRepository(),
            new TagLibMetadataExtractor(),
            new InMemoryEventBus());
        var audio = new StubAudioService();
        var settings = new StubSettingsService();
        var library = new LibraryViewModel(libraryService, audio, settings);
        var sut = new AllSongsViewModel(library, audio);
        return (sut, library, audio);
    }

    /// <summary>
    /// ライブラリに楽曲が追加された際、FilteredTracksに自動反映されサマリーが更新されることを検証します。
    /// </summary>
    [Fact]
    public void RefreshTracks_ライブラリへ楽曲追加時_FilteredTracksとサマリーが更新される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        var track1 = CreateTrack("Beta", duration: TimeSpan.FromMinutes(4));
        var track2 = CreateTrack("Alpha", duration: TimeSpan.FromMinutes(2));

        // Act
        library.Tracks.Add(track1);
        library.Tracks.Add(track2);

        // Assert
        Assert.Equal(2, sut.FilteredTracks.Count);
        Assert.Equal("Alpha", sut.FilteredTracks[0].Title);
        Assert.Equal("Beta", sut.FilteredTracks[1].Title);
        Assert.False(sut.IsEmpty);
        Assert.Contains("全 2 曲", sut.SummaryText);
        sut.Dispose();
        library.Dispose();
    }

    /// <summary>
    /// 検索キーワードを設定した際、曲名・アーティスト・アルバム・ジャンルで絞り込まれることを検証します。
    /// </summary>
    [Fact]
    public void SearchKeyword_キーワード入力時_該当楽曲のみに絞り込まれる()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        library.Tracks.Add(CreateTrack("Get Lucky", artist: "Daft Punk", album: "RAM", genre: "Disco"));
        library.Tracks.Add(CreateTrack("Midnight City", artist: "M83", album: "Hurry Up", genre: "Synthpop"));

        // Act
        sut.SearchKeyword = "daft";

        // Assert
        Assert.Single(sut.FilteredTracks);
        Assert.Equal("Get Lucky", sut.FilteredTracks[0].Title);
        sut.Dispose();
        library.Dispose();
    }

    /// <summary>
    /// ソート基準と昇降順を変更した際、楽曲リストが正しく並び替えられることを検証します。
    /// </summary>
    [Fact]
    public void SelectedSortOption_再生時間降順を指定した場合_長い曲順に並ぶ()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        library.Tracks.Add(CreateTrack("Short", duration: TimeSpan.FromMinutes(2)));
        library.Tracks.Add(CreateTrack("Long", duration: TimeSpan.FromMinutes(6)));
        library.Tracks.Add(CreateTrack("Medium", duration: TimeSpan.FromMinutes(4)));

        // Act
        sut.SelectedSortOption = "Duration";
        sut.IsAscending = false;

        // Assert
        Assert.Equal(new[] { "Long", "Medium", "Short" }, sut.FilteredTracks.Select(t => t.Title).ToArray());
        sut.Dispose();
        library.Dispose();
    }

    /// <summary>
    /// PlayAllを実行した際、表示中の全楽曲がAudioServiceのプレイリストにセットされ再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void PlayAll_楽曲が存在する場合_全曲をプレイリストにセットし再生開始する()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        library.Tracks.Add(CreateTrack("Track 1"));
        library.Tracks.Add(CreateTrack("Track 2"));
        IReadOnlyList<Track>? requestedTracks = null;
        sut.PlaybackRequested += (tracks, _, _, _) => requestedTracks = tracks;

        // Act
        sut.PlayAll();

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal(2, requestedTracks!.Count);
        Assert.Equal(2, audio.LastPlaylist.Count);
        Assert.True(audio.PlayCalled);
        sut.Dispose();
        library.Dispose();
    }

    /// <summary>
    /// PlayTrackを実行した際、選択された楽曲を開始トラックとして再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void PlayTrack_特定トラックを指定した場合_対象トラックを開始曲として再生する()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        var track1 = CreateTrack("A Track");
        var track2 = CreateTrack("B Track");
        library.Tracks.Add(track1);
        library.Tracks.Add(track2);

        // Act
        sut.PlayTrack(track2);

        // Assert
        Assert.Equal(track2, audio.LastStartTrack);
        Assert.True(audio.PlayCalled);
        sut.Dispose();
        library.Dispose();
    }

    private sealed class InMemoryTrackRepository : ITrackRepository
    {
        private readonly List<Track> _tracks = new();
        public Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Track>>(_tracks);
        public Task<Track?> GetByIdAsync(TrackId id, CancellationToken cancellationToken = default) => Task.FromResult(_tracks.FirstOrDefault(t => t.Id.Equals(id)));
        public Task<Track?> GetByPathAsync(AudioPath path, CancellationToken cancellationToken = default) => Task.FromResult(_tracks.FirstOrDefault(t => string.Equals(t.FilePath, path.Value, StringComparison.OrdinalIgnoreCase)));
        public Task SaveAsync(Track track, CancellationToken cancellationToken = default) { _tracks.Add(track); return Task.CompletedTask; }
        public Task SaveRangeAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default) { _tracks.AddRange(tracks); return Task.CompletedTask; }
        public Task DeleteAsync(TrackId id, CancellationToken cancellationToken = default) { _tracks.RemoveAll(t => t.Id.Equals(id)); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { _tracks.Clear(); return Task.CompletedTask; }
        public Task<IReadOnlyList<Track>> SearchAsync(string keyword, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Track>>(_tracks);
    }

    private sealed class InMemoryFavoriteRepository : IFavoriteRepository
    {
        private readonly HashSet<TrackId> _ids = new();
        public Task<IReadOnlySet<TrackId>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<TrackId>>(_ids);
        public Task AddAsync(TrackId trackId, CancellationToken cancellationToken = default) { _ids.Add(trackId); return Task.CompletedTask; }
        public Task RemoveAsync(TrackId trackId, CancellationToken cancellationToken = default) { _ids.Remove(trackId); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(TrackId trackId, CancellationToken cancellationToken = default) => Task.FromResult(_ids.Contains(trackId));
    }

    private sealed class StubSettingsService : ISettingsService
    {
        private AppSettings _settings = new();
        public AppSettings LoadSettings() => _settings;
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

        public event Action<Track?>? TrackChanged { add { } remove { } }
        public event Action<bool>? PlaybackStateChanged { add { } remove { } }
        public event Action? PlaybackStopped { add { } remove { } }
        public event EventHandler? PlaylistEnded { add { } remove { } }
        public event Action<Track>? TrackPlaybackEnded { add { } remove { } }
        public event EventHandler<FftEventArgs>? FftCalculated { add { } remove { } }
        public event Action<List<Track>>? PlaylistChanged { add { } remove { } }
        public event Action<float>? VolumeChanged { add { } remove { } }

        public IReadOnlyList<Track> UserQueue => Array.Empty<Track>();
        public IReadOnlyList<Track> AlbumQueue => LastPlaylist;
        public bool IsPlaying => PlayCalled;
        public bool IsShuffleEnabled { get; set; }
        public bool IsRepeatEnabled { get; set; }
        public TimeSpan CurrentTime => TimeSpan.Zero;
        public TimeSpan TotalTime => TimeSpan.Zero;
        public float Volume { get; set; } = 0.5f;
        public float[] Frequencies { get; } = new float[10];

        public void ClearUserQueue() { }
        public void SetPlaylist(List<Track> tracks, Track? startTrack = null)
        {
            LastPlaylist = tracks.ToList();
            LastStartTrack = startTrack ?? LastPlaylist.FirstOrDefault();
        }
        public void EnqueueTracks(IReadOnlyList<Track> tracks, bool playNext) { }
        public void RemoveTrack(Track track) { }
        public void PlayTrack(Track track) { LastStartTrack = track; PlayCalled = true; }
        public void PlayTrack(Track track, bool insertAtBeginning) { LastStartTrack = track; PlayCalled = true; }
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
