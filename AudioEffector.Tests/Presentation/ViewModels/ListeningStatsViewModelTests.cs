using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="ListeningStatsViewModel"/> の対象曲統計表示・右クリック指定曲ピン留め・再生中曲復帰・アクティブ連動・TOP N 表示を検証するテストクラス
/// </summary>
public sealed class ListeningStatsViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
    }

    private sealed class InMemoryListeningStatsRepository : IListeningStatsRepository
    {
        private ListeningStatsData _data = new();

        public ListeningStatsData Load() => _data.Clone();

        public void Save(ListeningStatsData data) => _data = data.Clone();

        public ListeningStatsData Update(Action<ListeningStatsData> update)
        {
            ListeningStatsData working = _data.Clone();
            update(working);
            _data = working.Clone();
            return _data.Clone();
        }
    }

    private static Track CreateTrack(
        string title,
        int durationSeconds = 210,
        string artist = "Artist A",
        string album = "Album A",
        string? filePath = null,
        AlbumId albumId = default)
    {
        return new Track
        {
            FilePath = filePath ?? $@"C:\Music\{album}\{artist}\{title}.flac",
            Title = title,
            Artist = artist,
            Album = album,
            AlbumId = albumId,
            Duration = TimeSpan.FromSeconds(durationSeconds),
        };
    }

    private static LibraryViewModel CreateLibraryViewModel(IAudioService audioService)
    {
        var trackRepoMock = new Mock<ITrackRepository>();
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        favoriteRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var metadataMock = new Mock<IMetadataExtractor>();
        var eventBusMock = new Mock<IEventBus>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var libraryService = new LibraryApplicationService(
            trackRepoMock.Object,
            favoriteRepoMock.Object,
            metadataMock.Object,
            eventBusMock.Object);

        return new LibraryViewModel(libraryService, audioService, settingsMock.Object);
    }

    /// <summary>
    /// 再生中の楽曲が切り替わり再生完了が記録された際、累計再生回数・累計再生時間・最終再生日時および TOP N が即時反映されることを検証します。
    /// </summary>
    [Fact]
    public void Refresh_再生完了統計記録後_対象曲のKPIとTOP5ランキングが正しく表示されること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var track = CreateTrack("Starlight", 185);
        audioMock.Setup(a => a.CurrentTrack).Returns(track);
        var playedAt = new DateTime(2026, 11, 29, 14, 30, 0);
        using var statsService = new ListeningStatsApplicationService(repository, audioMock.Object, () => playedAt);
        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = true,
        };

        // Act
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);

        // Assert
        Assert.Same(track, sut.TargetTrack);
        Assert.Equal(1, sut.TotalPlayCount);
        Assert.Equal("1 回", sut.TotalPlayCountDisplay);
        Assert.Equal(TimeSpan.FromSeconds(185), sut.TotalListeningTime);
        Assert.Equal("3分 5秒", sut.TotalListeningTimeDisplay);
        Assert.Equal("2026/11/29 14:30", sut.LastPlayedAtDisplay);
        Assert.True(sut.HasTargetTrackStats);
        Assert.Single(sut.TopPlayedTracks);
        Assert.Equal(1, sut.TopPlayedTracks[0].Rank);
        Assert.Equal(100.0, sut.TopPlayedTracks[0].PlayCountPercent);
    }

    /// <summary>
    /// 再生中以外の楽曲を SetTargetTrack で指定した場合にピン留め表示となり、ReturnToPlayingTrackCommand で再生中の曲へ戻せることを検証します。
    /// </summary>
    [Fact]
    public void SetTargetTrack_再生中以外の曲を指定し戻すコマンド実行_対象曲表示と再生中曲復帰が正しく切り替わること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var playingTrack = CreateTrack("PlayingTrack", 180);
        var selectedTrack = CreateTrack("SelectedTrack", 240);
        audioMock.Setup(a => a.CurrentTrack).Returns(playingTrack);

        using var statsService = new ListeningStatsApplicationService(
            repository,
            audioMock.Object,
            () => new DateTime(2026, 11, 29, 15, 0, 0));
        statsService.RecordPlaybackCompleted(playingTrack);
        statsService.RecordPlaybackCompleted(selectedTrack);
        statsService.RecordPlaybackCompleted(selectedTrack);

        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = true,
        };

        // Act 1: 再生中以外の曲を右クリック指定
        sut.SetTargetTrack(selectedTrack);

        // Assert 1
        Assert.Equal("SelectedTrack", sut.TargetTrackTitle);
        Assert.Equal(2, sut.TotalPlayCount);
        Assert.True(sut.IsShowingNonPlayingTrack);
        Assert.True(sut.CanReturnToPlayingTrack);
        Assert.True(sut.ReturnToPlayingTrackCommand.CanExecute(null));

        // Act 2: 「再生中の曲に戻す」を実行
        sut.ReturnToPlayingTrackCommand.Execute(null);

        // Assert 2
        Assert.Equal("PlayingTrack", sut.TargetTrackTitle);
        Assert.Equal(1, sut.TotalPlayCount);
        Assert.False(sut.IsShowingNonPlayingTrack);
        Assert.False(sut.CanReturnToPlayingTrack);
    }

    /// <summary>
    /// ダッシュボードが非表示（IsActiveView = false）の間の統計更新は遅延され、再表示時に最新統計へ反映されることを検証します。
    /// </summary>
    [Fact]
    public void IsActiveView_非表示中の統計更新後にアクティブ化した場合_最新統計が遅延反映されること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var track = CreateTrack("LazyTrack", 120);
        audioMock.Setup(a => a.CurrentTrack).Returns(track);
        using var statsService = new ListeningStatsApplicationService(repository, audioMock.Object);
        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = false,
        };

        // Act 1: 非表示中に再生完了
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);

        // Assert 1: 非表示中は即時再描画しない
        Assert.Equal(0, sut.TotalPlayCount);

        // Act 2: アクティブ化
        sut.IsActiveView = true;

        // Assert 2: 最新統計が反映される
        Assert.Equal(1, sut.TotalPlayCount);
    }

    /// <summary>
    /// 複数アルバム（同名別アーティスト含む）が存在する場合でも、対象曲の所属アルバム内 TOP 5 のみが表示され、
    /// TOP 5 行クリック・別アルバム切替・統計 0 件アルバム切替・再生中曲復帰へ正しく追従することを検証します。
    /// </summary>
    [Fact]
    public void Refresh_複数アルバム混在時_所属アルバム内TOP5のみ表示され切替と空状態へ追従すること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        using var libraryVm = CreateLibraryViewModel(audioMock.Object);

        var albumAId = AlbumId.New();
        var albumBId = AlbumId.New();
        var albumCId = AlbumId.New();

        var albumATrack1 = CreateTrack("Song A1", 180, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\01.flac", albumAId);
        var albumATrack2 = CreateTrack("Song A2 (Feat)", 220, "Guest Vocal", "Greatest Hits", @"C:\Music\AlbumA\02.flac", albumAId);
        var albumBTrack1 = CreateTrack("Song B1 Top", 240, "Artist B", "Greatest Hits", @"C:\Music\AlbumB\01.flac", albumBId);
        var albumCTrack1 = CreateTrack("Song C1 Unplayed", 200, "Artist C", "Unplayed Album", @"C:\Music\AlbumC\01.flac", albumCId);

        libraryVm.Albums.Add(new Album("Greatest Hits", "Artist A", 2024, new List<Track> { albumATrack1, albumATrack2 }, albumAId));
        libraryVm.Albums.Add(new Album("Greatest Hits", "Artist B", 2025, new List<Track> { albumBTrack1 }, albumBId));
        libraryVm.Albums.Add(new Album("Unplayed Album", "Artist C", 2026, new List<Track> { albumCTrack1 }, albumCId));

        audioMock.Setup(a => a.CurrentTrack).Returns(albumATrack1);

        using var statsService = new ListeningStatsApplicationService(
            repository,
            audioMock.Object,
            () => new DateTime(2026, 11, 29, 17, 0, 0));

        statsService.RecordPlaybackCompleted(albumATrack1);
        statsService.RecordPlaybackCompleted(albumATrack2);
        statsService.RecordPlaybackCompleted(albumATrack2);
        statsService.RecordPlaybackCompleted(albumATrack2);
        for (int i = 0; i < 8; i++)
        {
            statsService.RecordPlaybackCompleted(albumBTrack1);
        }

        using var sut = new ListeningStatsViewModel(
            statsService,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            libraryVm)
        {
            IsActiveView = true,
        };

        // Assert 1: Album A の再生中は Album A の 2 曲のみ（別アーティストのコンピレーション曲含む）表示、同名 Album B (8回) は除外
        Assert.Equal("アルバム内でよく聴く曲 TOP 5", sut.TopTracksSectionTitle);
        Assert.Equal("Greatest Hits", sut.TargetAlbumDisplay);
        Assert.True(sut.HasTargetAlbumDisplay);
        Assert.Equal(2, sut.TopPlayedTracks.Count);
        Assert.Equal("Song A2 (Feat)", sut.TopPlayedTracks[0].Title);
        Assert.Equal(3, sut.TopPlayedTracks[0].PlayCount);
        Assert.Equal("Song A1", sut.TopPlayedTracks[1].Title);
        Assert.DoesNotContain(sut.TopPlayedTracks, item => item.Title == "Song B1 Top");

        // Act 2: TOP 5 の 1 位行クリック → 対象曲は Album A の Song A2 へ切替、Album A ランキングは維持される
        sut.SelectTopTrackCommand.Execute(sut.TopPlayedTracks[0]);
        Assert.Equal("Song A2 (Feat)", sut.TargetTrackTitle);
        Assert.Equal(3, sut.TotalPlayCount);
        Assert.Equal(2, sut.TopPlayedTracks.Count);
        Assert.DoesNotContain(sut.TopPlayedTracks, item => item.Title == "Song B1 Top");

        // Act 3: 同名別アルバム (Album B) の曲へ SetTargetTrack → Album B の TOP 5 へ即座に追従
        sut.SetTargetTrack(albumBTrack1);
        Assert.Equal("Song B1 Top", sut.TargetTrackTitle);
        Assert.Equal(8, sut.TotalPlayCount);
        Assert.Single(sut.TopPlayedTracks);
        Assert.Equal("Song B1 Top", sut.TopPlayedTracks[0].Title);

        // Act 4: 再生統計 0 件のアルバム (Album C) へ切替 → 空一覧＋アルバム未記録エンプティメッセージ
        sut.SetTargetTrack(albumCTrack1);
        Assert.Empty(sut.TopPlayedTracks);
        Assert.False(sut.HasTopPlayedTracks);
        Assert.Equal("Unplayed Album", sut.TargetAlbumDisplay);
        Assert.Contains("このアルバムの再生統計はまだ記録されていません", sut.TopTracksEmptyMessage);

        // Act 5: 再生中の曲へ戻す → Album A ランキングへ復帰
        sut.ReturnToPlayingTrackCommand.Execute(null);
        Assert.Equal("Song A1", sut.TargetTrackTitle);
        Assert.Equal(2, sut.TopPlayedTracks.Count);
        Assert.Equal("Song A2 (Feat)", sut.TopPlayedTracks[0].Title);
    }

    /// <summary>
    /// 対象曲が未設定（TargetTrack == null）の場合、他曲の統計が存在しても TOP 5 は空となり未選択向けメッセージを返すことを検証します。
    /// </summary>
    [Fact]
    public void Refresh_対象曲未設定時_TOP5は空となり未選択向けエンプティメッセージを返すこと()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        audioMock.Setup(a => a.CurrentTrack).Returns((Track?)null);

        using var statsService = new ListeningStatsApplicationService(repository, audioMock.Object);
        statsService.RecordPlaybackCompleted(CreateTrack("Some Song"));

        // Act
        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = true,
        };

        // Assert
        Assert.Null(sut.TargetTrack);
        Assert.Empty(sut.TopPlayedTracks);
        Assert.False(sut.HasTopPlayedTracks);
        Assert.False(sut.HasTargetAlbumDisplay);
        Assert.Contains("楽曲を再生または選択すると", sut.TopTracksEmptyMessage);
    }

    /// <summary>
    /// DI コンテナから ListeningStatsViewModel を解決した際、単一 public コンストラクタにより曖昧性エラーなく
    /// LibraryViewModel が自動注入されることを検証します。
    /// </summary>
    [Fact]
    public void ServiceProvider_ListeningStatsViewModel解決時_LibraryViewModelが自動注入され初期化できること()
    {
        // Arrange
        var audioMock = new Mock<IAudioService>();
        var trackRepoMock = new Mock<ITrackRepository>();
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        favoriteRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var metadataMock = new Mock<IMetadataExtractor>();
        var eventBusMock = new Mock<IEventBus>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var services = new ServiceCollection();
        services.AddSingleton<IListeningStatsRepository>(new InMemoryListeningStatsRepository());
        services.AddSingleton(audioMock.Object);
        services.AddSingleton<IUiDispatcher>(new ImmediateDispatcherStub());
        services.AddSingleton(trackRepoMock.Object);
        services.AddSingleton(favoriteRepoMock.Object);
        services.AddSingleton(metadataMock.Object);
        services.AddSingleton(eventBusMock.Object);
        services.AddSingleton(settingsMock.Object);
        services.AddSingleton<LibraryApplicationService>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<ListeningStatsApplicationService>();
        services.AddSingleton<ListeningStatsViewModel>();

        // Act & Assert
        using ServiceProvider provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ListeningStatsViewModel>();
        Assert.NotNull(resolved);
        Assert.Equal("アルバム内でよく聴く曲 TOP 5", resolved.TopTracksSectionTitle);
    }

    /// <summary>
    /// FormatDuration が 0 秒・秒のみ・分秒・時間分の各レンジを期待通りの日本語文字列へ整形することを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, "0分")]
    [InlineData(45, "45秒")]
    [InlineData(185, "3分 5秒")]
    [InlineData(4500, "1時間 15分")]
    public void FormatDuration_各種秒数入力時_期待される日本語書式を返すこと(int totalSeconds, string expected)
    {
        // Arrange & Act
        string actual = ListeningStatsViewModel.FormatDuration(TimeSpan.FromSeconds(totalSeconds));

        // Assert
        Assert.Equal(expected, actual);
    }
}
