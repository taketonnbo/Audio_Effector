using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="LibraryStatsViewModel"/> の期間フィルタ切替・TOP 10 再生・プレイリスト化・画面遷移・遅延更新コアレッシング・0 件フォールバックを検証するテストクラス
/// </summary>
public sealed class LibraryStatsViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
    }

    private sealed class QueuedDispatcherStub : IUiDispatcher
    {
        private readonly Queue<Action> _pending = new();

        public int PendingCount => _pending.Count;

        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => _pending.Enqueue(action);

        public void FlushAll()
        {
            while (_pending.Count > 0)
            {
                _pending.Dequeue().Invoke();
            }
        }
    }

    private sealed class InMemoryListeningStatsRepository : IListeningStatsRepository
    {
        private ListeningStatsData _data;

        public InMemoryListeningStatsRepository(ListeningStatsData? initialData = null)
        {
            _data = initialData?.Clone() ?? new ListeningStatsData();
        }

        public ListeningStatsData Load() => _data.Clone();

        public void Save(ListeningStatsData data) => _data = data.Clone();

        public ListeningStatsData Update(Action<ListeningStatsData> update)
        {
            ListeningStatsData working = _data.Clone();
            update(working);
            _data = working.Clone();
            return _data.Clone();
        }

        public void ClearAll() => _data = new ListeningStatsData();
    }

    private static Track CreateTrack(string title, string artist, string album, string filePath, int seconds = 200)
    {
        return new Track
        {
            FilePath = filePath,
            Title = title,
            Artist = artist,
            Album = album,
            Duration = TimeSpan.FromSeconds(seconds),
        };
    }

    private static (LibraryViewModel LibraryVm, PlaylistViewModel PlaylistVm, List<UserPlaylist> SavedPlaylists) CreateLibraryAndPlaylist(
        IAudioService audioService,
        IReadOnlyList<Track> tracks,
        IReadOnlyList<Album>? albums = null)
    {
        var trackRepoMock = new Mock<ITrackRepository>();
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        var playlistRepoMock = new Mock<IPlaylistRepository>();
        var metadataMock = new Mock<IMetadataExtractor>();
        var settingsMock = new Mock<ISettingsService>();
        var eventBus = new InMemoryEventBus();

        var savedPlaylists = new List<UserPlaylist>();
        favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        playlistRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => savedPlaylists.ToList());
        playlistRepoMock.Setup(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()))
            .Callback<UserPlaylist, CancellationToken>((pl, _) => savedPlaylists.Add(pl))
            .Returns(System.Threading.Tasks.Task.CompletedTask);
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var libraryService = new LibraryApplicationService(trackRepoMock.Object, favoriteRepoMock.Object, metadataMock.Object, eventBus);
        var playlistService = new PlaylistApplicationService(playlistRepoMock.Object, trackRepoMock.Object, eventBus);

        var playlistVm = new PlaylistViewModel(playlistService, libraryService, audioService, eventBus);
        var libraryVm = new LibraryViewModel(libraryService, audioService, settingsMock.Object, playlistVm);
        foreach (Track track in tracks)
        {
            libraryVm.Tracks.Add(track);
        }

        if (albums != null)
        {
            foreach (Album album in albums)
            {
                libraryVm.Albums.Add(album);
            }
        }

        return (libraryVm, playlistVm, savedPlaylists);
    }

    /// <summary>
    /// 期間フィルタ切替に応じて KPI サマリ・ランキング・推移グラフ粒度および傾向インサイトが正しく再集計されることを検証します。
    /// </summary>
    [Fact]
    public void SelectedPeriodFilter_今週と全期間を切替時_KPIとグラフ粒度が連動更新されること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track t1 = CreateTrack("Song A", "Artist 1", "Album 1", @"C:\music\a.mp3", 180);
        Track t2 = CreateTrack("Song B", "Artist 2", "Album 2", @"C:\music\b.mp3", 240);

        var data = new ListeningStatsData();
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, new DateTime(2026, 5, 12, 21, 0, 0)));
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, new DateTime(2026, 5, 13, 21, 30, 0)));
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t2, new DateTime(2025, 10, 5, 9, 0, 0)));

        var repo = new InMemoryListeningStatsRepository(data);
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, [t1, t2]);
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: name => name,
            messageNotifier: (_, _, _) => { });

        // Act
        int allTimePlays = sut.TotalPlayCount;
        bool allTimeMonthlyDefault = sut.IsMonthlyTrendSelected;
        sut.SelectPeriodFilterCommand.Execute(StatsPeriodFilter.ThisWeek);

        // Assert
        Assert.Equal(3, allTimePlays);
        Assert.True(allTimeMonthlyDefault);
        Assert.Equal(StatsPeriodFilter.ThisWeek, sut.SelectedPeriodFilter);
        Assert.Equal(2, sut.TotalPlayCount);
        Assert.True(sut.IsDailyTrendSelected);
        Assert.Single(sut.TopTracks);
        Assert.Equal("Song A", sut.TopTracks[0].Title);
        Assert.Contains("21:00 台", sut.HabitPeakInsight);
    }

    /// <summary>
    /// 曲クリック再生・TOP 10 一括再生・キュー追加・新規プレイリスト作成の各アクションが期待通り実行されることを検証します。
    /// </summary>
    [Fact]
    public void Actions_再生キュー追加およびプレイリスト化実行時_音声サービスとプレイリスト永続化が連携すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track t1 = CreateTrack("Top 1", "Artist 1", "Album 1", @"C:\music\top1.mp3");
        Track t2 = CreateTrack("Top 2", "Artist 2", "Album 2", @"C:\music\top2.mp3");

        var data = new ListeningStatsData();
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, now.AddHours(-3)));
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, now.AddHours(-2)));
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t2, now.AddHours(-1)));

        var repo = new InMemoryListeningStatsRepository(data);
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, savedPlaylists) = CreateLibraryAndPlaylist(audioMock.Object, [t1, t2]);
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        IReadOnlyList<Track>? requestedPlaybackList = null;
        Track? requestedStartTrack = null;
        IReadOnlyList<Track>? requestedEnqueue = null;
        bool? requestedPlayNext = null;
        string? notifiedTitle = null;

        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: _ => "マイTOP10",
            messageNotifier: (_, title, _) => notifiedTitle = title);

        sut.PlaybackRequested += (tracks, start, _, _) =>
        {
            requestedPlaybackList = tracks;
            requestedStartTrack = start;
        };
        sut.EnqueueRequested += (tracks, playNext) =>
        {
            requestedEnqueue = tracks;
            requestedPlayNext = playNext;
        };

        // Act
        sut.PlayTopTracksCommand.Execute(null);
        sut.PlayNextTrackCommand.Execute(sut.TopTracks[1]);
        UserPlaylist? created = sut.CreateTopTracksPlaylist();

        // Assert
        Assert.NotNull(requestedPlaybackList);
        Assert.Equal(2, requestedPlaybackList!.Count);
        Assert.Same(t1, requestedStartTrack);
        audioMock.Verify(a => a.PlayTrack(t1), Times.Once);

        Assert.NotNull(requestedEnqueue);
        Assert.Same(t2, requestedEnqueue![0]);
        Assert.True(requestedPlayNext);

        Assert.NotNull(created);
        Assert.Equal("マイTOP10", created!.Name);
        Assert.Single(savedPlaylists);
        Assert.Equal(2, created.TrackPaths.Count);
        Assert.Equal("プレイリスト作成完了", notifiedTitle);
    }

    /// <summary>
    /// アーティストおよびアルバムランキング項目クリック時に各遷移イベントが正確な対象で発火することを検証します。
    /// </summary>
    [Fact]
    public void NavigationCommands_アーティストおよびアルバム項目実行時_該当遷移イベントが発火すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track t1 = CreateTrack("Track 1", "Aimer", "Midnight Sun", @"C:\music\ms1.flac");
        var album = new Album("Midnight Sun", "Aimer", 2014, new List<Track> { t1 });

        var data = new ListeningStatsData();
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, now.AddHours(-1)));

        var repo = new InMemoryListeningStatsRepository(data);
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, [t1], [album]);
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        string? navigatedArtist = null;
        Album? navigatedAlbum = null;

        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: _ => null,
            messageNotifier: (_, _, _) => { });

        sut.ArtistNavigationRequested += name => navigatedArtist = name;
        sut.AlbumNavigationRequested += alb => navigatedAlbum = alb;

        // Act
        sut.NavigateToArtistCommand.Execute(sut.TopArtists[0]);
        sut.NavigateToAlbumCommand.Execute(sut.TopAlbums[0]);

        // Assert
        Assert.Equal("Aimer", navigatedArtist);
        Assert.Same(album, navigatedAlbum);
    }

    /// <summary>
    /// 非表示状態（IsActiveView = false）での統計更新は遅延され、表示復帰時に最新データへ再集計されることを検証します。
    /// </summary>
    [Fact]
    public void IsActiveView_非表示中の統計更新時_表示復帰まで再集計を遅延し復帰時に最新化すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track t1 = CreateTrack("Song 1", "Artist 1", "Album 1", @"C:\music\s1.mp3");
        var repo = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var dispatcher = new QueuedDispatcherStub();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, [t1]);
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            dispatcher,
            playlistVm,
            playlistNamePrompt: _ => null,
            messageNotifier: (_, _, _) => { });

        // Act
        sut.IsActiveView = false;
        listeningSvc.RecordPlaybackCompleted(t1);
        listeningSvc.RecordPlaybackCompleted(t1);
        int queuedCount = dispatcher.PendingCount;
        dispatcher.FlushAll();
        int countWhileInactive = sut.TotalPlayCount;

        sut.IsActiveView = true;

        // Assert
        Assert.Equal(1, queuedCount); // 2 回の更新が 1 回の遅延タスクへコアレッシングされる
        Assert.Equal(0, countWhileInactive);
        Assert.Equal(2, sut.TotalPlayCount);
        Assert.True(sut.HasData);
    }

    /// <summary>
    /// 再生データ 0 件時にエンプティ状態となり、TOP 10 再生・プレイリスト化コマンドが非活性であることを検証します。
    /// </summary>
    [Fact]
    public void EmptyState_再生データ0件時_エンプティ表示となりアクションコマンドが非活性であること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        var repo = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, Array.Empty<Track>());
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        // Act
        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: _ => null,
            messageNotifier: (_, _, _) => { });

        // Assert
        Assert.True(sut.IsEmpty);
        Assert.False(sut.HasData);
        Assert.False(sut.PlayTopTracksCommand.CanExecute(null));
        Assert.False(sut.CreateTopTracksPlaylistCommand.CanExecute(null));
        Assert.Equal("—", sut.TopArtistNameDisplay);
        Assert.Equal(24, sut.HabitBars.Count);
    }

    /// <summary>
    /// ライブラリに存在しない曲を含む TOP 10 をプレイリスト化・追加する際、架空 TrackId を永続化せずライブラリ実体曲のみを登録すること、および全件未解決時に案内通知して中断することを検証します。
    /// </summary>
    [Fact]
    public void CreateTopTracksPlaylist_未解決曲混在および全件未解決時_実ライブラリ曲のみ登録し全件未解決時は作成しないこと()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track existingTrack = CreateTrack("Existing", "Artist 1", "Album 1", @"C:\music\existing.mp3");
        Track removedTrack = CreateTrack("Removed", "Artist 2", "Album 2", @"C:\music\removed.mp3");

        var mixedData = new ListeningStatsData();
        mixedData.PlayEvents.Add(TrackPlayEvent.FromTrack(existingTrack, now.AddHours(-2)));
        mixedData.PlayEvents.Add(TrackPlayEvent.FromTrack(removedTrack, now.AddHours(-1)));

        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, savedPlaylists) = CreateLibraryAndPlaylist(audioMock.Object, [existingTrack]);
        using var _playlistDisposable = playlistVm;
        var repo = new InMemoryListeningStatsRepository(mixedData);
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        var notifications = new List<(string Message, string Title, bool IsError)>();
        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: _ => "解決曲のみプレイリスト",
            messageNotifier: (m, t, e) => notifications.Add((m, t, e)));

        // Act 1: 混在状態でのプレイリスト化と未解決曲の右クリック追加
        UserPlaylist? created = sut.CreateTopTracksPlaylist();
        LibraryStatsTrackItemViewModel removedItem = sut.TopTracks.Single(t => t.Title == "Removed");
        sut.AddTrackToPlaylistCommand.Execute(removedItem);

        // Act 2: ライブラリから全曲除外された状態でのプレイリスト化
        libraryVm.Tracks.Clear();
        UserPlaylist? failed = sut.CreateTopTracksPlaylist();

        // Assert
        Assert.NotNull(created);
        Assert.Single(created!.TrackIds);
        Assert.Equal(existingTrack.Id, created.TrackIds[0]);
        Assert.Single(savedPlaylists);
        Assert.Contains(notifications, n => n.Title == "プレイリスト作成完了" && n.Message.Contains("1 曲"));
        Assert.Contains(notifications, n => n.Title == "プレイリスト追加不可" && n.IsError);
        Assert.Null(failed);
        Assert.Contains(notifications, n => n.Title == "プレイリスト作成不可" && n.IsError);
    }

    /// <summary>
    /// 統計上のアルバムと同名・別アーティストのアルバムしかライブラリに存在しない場合、誤遷移せず案内通知することを検証します。
    /// </summary>
    [Fact]
    public void NavigateToAlbumCommand_同名別アーティストアルバムのみライブラリ存在時_誤遷移せず未検出案内すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track libraryTrack = CreateTrack("Hit A", "Artist A", "Greatest Hits", @"C:\music\a_hit.mp3");
        var libraryAlbum = new Album("Greatest Hits", "Artist A", 2020, new List<Track> { libraryTrack });
        Track deletedArtistBTrack = CreateTrack("Hit B", "Artist B", "Greatest Hits", @"C:\music\b_hit.mp3");

        var data = new ListeningStatsData();
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(deletedArtistBTrack, now.AddHours(-1)));

        var repo = new InMemoryListeningStatsRepository(data);
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, [libraryTrack], [libraryAlbum]);
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        Album? navigated = null;
        string? notifyTitle = null;
        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: _ => null,
            messageNotifier: (_, title, _) => notifyTitle = title);
        sut.AlbumNavigationRequested += alb => navigated = alb;

        // Act
        sut.NavigateToAlbumCommand.Execute(sut.TopAlbums[0]);

        // Assert
        Assert.Null(navigated);
        Assert.Equal("アルバム未検出", notifyTitle);
    }

    /// <summary>
    /// 単曲クリック再生・キュー末尾追加・31日および24時間軸ラベル間引き・Dispose後の更新イベント無害性を検証します。
    /// </summary>
    [Fact]
    public void PlaybackAndDispose_単曲再生とキュー追加とラベル間引きおよびDispose後通知時_期待通り動作すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track t1 = CreateTrack("Track 1", "Artist 1", "Album 1", @"C:\music\t1.mp3");
        Track t2 = CreateTrack("Track 2", "Artist 2", "Album 2", @"C:\music\t2.mp3");

        var data = new ListeningStatsData();
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, new DateTime(2026, 5, 1, 21, 0, 0)));
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t2, new DateTime(2026, 5, 10, 9, 0, 0)));

        var repo = new InMemoryListeningStatsRepository(data);
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, [t1, t2]);
        using var _playlistDisposable = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);

        bool? lastPlayNext = null;
        var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            playlistVm,
            playlistNamePrompt: _ => null,
            messageNotifier: (_, _, _) => { });
        sut.EnqueueRequested += (_, playNext) => lastPlayNext = playNext;

        // Act
        sut.PlayTrackCommand.Execute(sut.TopTracks[1]);
        sut.EnqueueTrackCommand.Execute(sut.TopTracks[0]);
        sut.SelectPeriodFilterCommand.Execute(StatsPeriodFilter.ThisMonth);

        int monthDays = sut.TrendBars.Count;
        int visibleTrendLabels = sut.TrendBars.Count(b => !string.IsNullOrEmpty(b.Label));
        int visibleHourlyLabels = sut.HabitBars.Count(b => !string.IsNullOrEmpty(b.Label));
        bool allTooltipsPopulated = sut.TrendBars.All(b => !string.IsNullOrWhiteSpace(b.ToolTipText))
            && sut.HabitBars.All(b => !string.IsNullOrWhiteSpace(b.ToolTipText));

        sut.Dispose();
        listeningSvc.RecordPlaybackCompleted(t1);

        // Assert
        audioMock.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Once);
        Assert.False(lastPlayNext);
        Assert.Equal(31, monthDays);
        Assert.True(visibleTrendLabels is >= 6 and <= 9);
        Assert.Equal(8, visibleHourlyLabels); // 0, 3, 6, 9, 12, 15, 18, 21
        Assert.True(allTooltipsPopulated);
        Assert.Equal(2, sut.TotalPlayCount); // Dispose 後の統計更新で変化しない
    }

    /// <summary>
    /// ListeningStatsApplicationService.ResetAllStats 実行時、非表示状態（IsActiveView = false）であっても
    /// StatsReset イベントにより即座に空状態へ同期され、Dispose 後は通知を無視することを検証します。
    /// </summary>
    [Fact]
    public void StatsResetイベント受信時_非表示中でも即座に空状態へ同期されDispose後は通知を無視すること()
    {
        // Arrange
        var now = new DateTime(2026, 5, 13, 18, 0, 0);
        Track t1 = CreateTrack("Reset Song", "Artist 1", "Album 1", @"C:\music\reset.mp3", 180);

        var data = new ListeningStatsData();
        data.PlayEvents.Add(TrackPlayEvent.FromTrack(t1, now.AddHours(-1)));

        var repo = new InMemoryListeningStatsRepository(data);
        var audioMock = new Mock<IAudioService>();
        var (libraryVm, playlistVm, _) = CreateLibraryAndPlaylist(audioMock.Object, [t1]);
        using var ownedLibrary = libraryVm;
        using var ownedPlaylist = playlistVm;
        using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
        var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);
        var dispatcher = new QueuedDispatcherStub();

        using var sut = new LibraryStatsViewModel(
            libraryStatsSvc,
            listeningSvc,
            libraryVm,
            audioMock.Object,
            dispatcher,
            playlistVm,
            playlistNamePrompt: name => name,
            messageNotifier: (_, _, _) => { })
        {
            IsActiveView = true,
        };
        Assert.Equal(1, sut.TotalPlayCount);
        Assert.True(sut.HasData);
        Assert.False(sut.IsEmpty);
        Assert.Single(sut.TopTracks);

        // 非表示状態へ切替後に全リセットを実行
        sut.IsActiveView = false;

        // Act
        bool succeeded = listeningSvc.ResetAllStats();
        dispatcher.FlushAll();

        // Assert: 非表示状態のままでも即座に空状態へ反映されていること
        Assert.True(succeeded);
        Assert.Equal(0, sut.TotalPlayCount);
        Assert.False(sut.HasData);
        Assert.True(sut.IsEmpty);
        Assert.Empty(sut.TopTracks);
        Assert.Empty(sut.TopArtists);
        Assert.Empty(sut.TopAlbums);

        // Dispose 後は StatsReset 通知が来てもディスパッチャに積まれないこと
        sut.Dispose();
        listeningSvc.ResetAllStats();
        Assert.Equal(0, dispatcher.PendingCount);
    }
}
