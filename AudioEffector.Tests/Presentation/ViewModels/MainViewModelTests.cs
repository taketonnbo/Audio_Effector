using System;
using System.Collections.Generic;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> における右側タブの歌詞表示トグル・最大化モード連動・右側タブ開閉連動を検証するテストクラス
/// </summary>
public sealed class MainViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => action();
    }

    private sealed class EmptyLyricsProviderStub : ILyricsProvider
    {
        public System.Threading.Tasks.Task<LyricsDocument?> LoadLyricsAsync(
            Track track,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<LyricsDocument?>(null);
        }
    }

    private sealed class ServiceProviderScope : IDisposable
    {
        private readonly IServiceProvider? _previous;
        private readonly ServiceProvider _provider;

        public ServiceProviderScope()
        {
            _previous = App.ServiceProvider;

            var services = new ServiceCollection();
            var eventBus = new InMemoryEventBus();
            var dispatcher = new ImmediateDispatcherStub();

            var settingsMock = new Mock<ISettingsService>();
            settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            var audioServiceMock = new Mock<IAudioService>();
            audioServiceMock.Setup(a => a.UserQueue).Returns(Array.Empty<Track>());
            audioServiceMock.Setup(a => a.AlbumQueue).Returns(Array.Empty<Track>());

            var audioEngineMock = new Mock<IAudioEngine>();
            var trackRepoMock = new Mock<ITrackRepository>();
            trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<Track>());
            var favoriteRepoMock = new Mock<IFavoriteRepository>();
            favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
            var recentRepoMock = new Mock<IRecentTrackRepository>();
            recentRepoMock.Setup(r => r.Load()).Returns(Array.Empty<RecentTrackEntry>());
            var transcodeMock = new Mock<IAudioTranscodeService>();

            var libraryService = new LibraryApplicationService(
                trackRepoMock.Object,
                favoriteRepoMock.Object,
                new TagLibMetadataExtractor(),
                eventBus);
            var lyricsService = new LyricsApplicationService(new EmptyLyricsProviderStub(), eventBus);

            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton<IUiDispatcher>(dispatcher);
            services.AddSingleton(settingsMock.Object);
            services.AddSingleton(audioServiceMock.Object);
            services.AddSingleton(audioEngineMock.Object);
            services.AddSingleton(recentRepoMock.Object);
            services.AddSingleton(transcodeMock.Object);
            services.AddSingleton(libraryService);
            services.AddSingleton(lyricsService);

            services.AddSingleton<PlayerControlViewModel>();
            services.AddSingleton<LibraryViewModel>();
            services.AddSingleton<AlbumDetailViewModel>();
            services.AddSingleton<AllSongsViewModel>();
            services.AddSingleton<ArtistsViewModel>();
            services.AddSingleton<RecentViewModel>();
            services.AddSingleton<LyricsViewModel>();

            _provider = services.BuildServiceProvider();
            App.ServiceProvider = _provider;
        }

        public void Dispose()
        {
            App.ServiceProvider = _previous;
            _provider.Dispose();
        }
    }

    /// <summary>
    /// 最大化モードで歌詞表示ボタンを ON にすると自動コンパクト切替し、OFF にすると元の最大化モードへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void ToggleLyricsPanelCommand_最大化モードからONおよびOFF操作_自動コンパクト切替後に元の最大化モードへ復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.NotNull(sut.Lyrics);
        Assert.False(sut.Lyrics!.IsActiveView);

        // Act 1: 最大化モードから歌詞 ON → 自動コンパクト＋歌詞表示
        sut.ToggleLyricsPanelCommand.Execute(null);

        // Assert 1
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);

        // Act 2: 歌詞 OFF → 元の最大化モードへ自動復帰
        sut.ToggleLyricsPanelCommand.Execute(null);

        // Assert 2
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// もともとコンパクトモードの状態で歌詞表示を ON → OFF にした場合、コンパクトモードを維持することを検証します。
    /// </summary>
    [Fact]
    public void ToggleLyricsPanelCommand_コンパクトモードからONおよびOFF操作_コンパクトモードを維持すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.IsAlbumViewMaximized = false;

        // Act 1: 歌詞 ON
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsAlbumViewMaximized);

        // Act 2: 歌詞ヘッダーの閉じる操作（ClosePanelCommand）経由で OFF
        sut.Lyrics!.ClosePanelCommand.Execute(null);

        // Assert
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.False(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// 歌詞パネル表示中に手動で最大化モードへ切り替えた場合、歌詞表示が自動的に OFF となることを検証します。
    /// </summary>
    [Fact]
    public void ToggleAlbumViewSizeModeCommand_歌詞表示中に手動で最大化した場合_歌詞表示が自動OFFになること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsAlbumViewMaximized);

        // Act: 手動で最大化モードへ切替
        sut.ToggleAlbumViewSizeModeCommand.Execute(null);

        // Assert
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);
    }

    /// <summary>
    /// 右側タブ閉鎖時は IsLyricsPanelVisible を維持したまま Lyrics.IsActiveView が false になり、再展開時に true へ復帰することを検証します。
    /// </summary>
    [Fact]
    public void IsRightPanelOpen_歌詞表示中に右側タブを閉鎖および再展開した場合_表示状態維持のままIsActiveViewが連動すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.True(sut.Lyrics!.IsActiveView);

        // Act 1: 右側タブ閉鎖
        sut.IsRightPanelOpen = false;

        // Assert 1
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);

        // Act 2: 右側タブ再展開
        sut.IsRightPanelOpen = true;

        // Assert 2
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// 最大化モードかつ右側タブ閉鎖状態から OpenLrcGenerator を呼ぶと右側タブ自動展開・自動コンパクト切替され、
    /// LRC 生成パネル閉鎖時に元の最大化モードへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_最大化かつ右側タブ閉鎖状態から展開し閉鎖_自動コンパクト後に元の最大化モードへ復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var track = new Track { FilePath = @"C:\Music\GeneratorTarget.flac", Title = "GeneratorTarget" };
        sut.IsRightPanelOpen = false;
        Assert.True(sut.IsAlbumViewMaximized);

        // Act 1: LRC 生成パネル展開
        sut.OpenLrcGenerator(track);

        // Assert 1: 右側タブ自動展開・自動コンパクト切替・排他表示・Lyrics.IsActiveView は false
        Assert.True(sut.IsRightPanelOpen);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.Equal(RightExtensionPanelType.LrcGenerator, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsLrcGeneratorPanelVisible);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);
        Assert.Same(track, sut.LrcGenerator!.TargetTrack);

        // Act 2: LRC 生成パネルを閉じる
        sut.LrcGenerator.CloseGeneratorCommand.Execute(null);

        // Assert 2: 元の最大化モードへ自動復帰
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsLrcGeneratorViewVisible);
        Assert.True(sut.IsAlbumViewMaximized);
    }

    /// <summary>
    /// 最大化モードから OpenLrcGenerator で自動コンパクト切替した後、右上 [♪] ボタンで歌詞表示へ切り替えてから閉鎖しても
    /// 最大化復帰フラグが維持され、元の最大化モードへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_最大化から展開後に右上歌詞トグル経由で切替および閉鎖_元の最大化モードへ自動復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var track = new Track { FilePath = @"C:\Music\ToggleKeepMax.flac", Title = "ToggleKeepMax" };
        sut.IsRightPanelOpen = true;
        Assert.True(sut.IsAlbumViewMaximized);

        // Act 1: 最大化モードから LRC 生成パネル展開
        sut.OpenLrcGenerator(track);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);

        // Act 2: 右上 [♪] ボタンで歌詞表示へ切替
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.False(sut.IsLrcGeneratorViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);

        // Act 3: 右上 [♪] ボタンで歌詞表示を閉鎖
        sut.ToggleLyricsPanelCommand.Execute(null);

        // Assert 3: 元の最大化モードへ自動復帰すること
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);
        Assert.True(sut.IsAlbumViewMaximized);
    }

    /// <summary>
    /// もともとコンパクトモードから OpenLrcGenerator → [← 歌詞へ戻る] → 閉鎖とした場合、コンパクトモードを維持することを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_コンパクトモードから展開し歌詞へ戻ってから閉鎖_コンパクトモードを維持すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.IsAlbumViewMaximized = false;

        // Act 1: コンパクトモードから LRC 生成展開
        sut.OpenLrcGenerator(new Track { FilePath = @"C:\Music\Compact.flac", Title = "Compact" });
        Assert.True(sut.IsLrcGeneratorViewVisible);

        // Act 2: [← 歌詞へ戻る] で歌詞表示へ切替
        sut.LrcGenerator!.BackToLyricsCommand.Execute(null);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics!.IsActiveView);

        // Act 3: 歌詞パネル閉鎖
        sut.Lyrics.ClosePanelCommand.Execute(null);

        // Assert 3: もともとコンパクトだったのでコンパクト維持
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.False(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// プレイリスト詳細表示中にデータ保存先変更で ReloadDataAfterDirectoryChange が実行された際、
    /// 新ディレクトリに存在しない旧プレイリストがクリアされプレイリスト一覧画面へ安全遷移することを検証します。
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ReloadDataAfterDirectoryChange_プレイリスト詳細表示中に新フォルダへ切替時_不在プレイリストをクリアして一覧へ戻ること()
    {
        // Arrange
        var previousProvider = App.ServiceProvider;
        var services = new ServiceCollection();
        var eventBus = new InMemoryEventBus();
        var dispatcher = new ImmediateDispatcherStub();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var audioServiceMock = new Mock<IAudioService>();
        audioServiceMock.Setup(a => a.UserQueue).Returns(Array.Empty<Track>());
        audioServiceMock.Setup(a => a.AlbumQueue).Returns(Array.Empty<Track>());

        var trackRepoMock = new Mock<ITrackRepository>();
        trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(Array.Empty<Track>());
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var recentRepoMock = new Mock<IRecentTrackRepository>();
        recentRepoMock.Setup(r => r.Load()).Returns(Array.Empty<RecentTrackEntry>());

        var oldPlaylist = new UserPlaylist(AudioEffector.Domain.ValueObjects.PlaylistId.From(Guid.NewGuid()), "OldList");
        IReadOnlyList<UserPlaylist> currentPlaylists = [oldPlaylist];
        var playlistRepoMock = new Mock<IPlaylistRepository>();
        playlistRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(() => currentPlaylists);

        var libraryService = new LibraryApplicationService(
            trackRepoMock.Object,
            favoriteRepoMock.Object,
            new TagLibMetadataExtractor(),
            eventBus);
        var playlistService = new PlaylistApplicationService(
            playlistRepoMock.Object,
            trackRepoMock.Object,
            eventBus,
            favoriteRepoMock.Object);
        var lyricsService = new LyricsApplicationService(new EmptyLyricsProviderStub(), eventBus);

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<IUiDispatcher>(dispatcher);
        services.AddSingleton(settingsMock.Object);
        services.AddSingleton(audioServiceMock.Object);
        services.AddSingleton(Mock.Of<IAudioEngine>());
        services.AddSingleton(recentRepoMock.Object);
        services.AddSingleton(Mock.Of<IAudioTranscodeService>());
        services.AddSingleton(libraryService);
        services.AddSingleton(playlistService);
        services.AddSingleton(lyricsService);
        services.AddSingleton<PlayerControlViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<AlbumDetailViewModel>();
        services.AddSingleton<AllSongsViewModel>();
        services.AddSingleton<ArtistsViewModel>();
        services.AddSingleton<RecentViewModel>();
        services.AddSingleton<LyricsViewModel>();

        using var provider = services.BuildServiceProvider();
        App.ServiceProvider = provider;
        try
        {
            using var sut = new MainViewModel();
            Assert.NotNull(sut.Playlist);
            Assert.Single(sut.Playlist!.UserPlaylists);

            sut.CurrentViewType = ViewType.PlaylistTracks;
            await sut.Playlist.SelectPlaylistAsync(oldPlaylist);
            Assert.Equal("OldList", sut.Playlist.CurrentPlaylistName);

            // Act: 新ディレクトリは空のプレイリスト一覧へ切替
            currentPlaylists = [];
            sut.ReloadDataAfterDirectoryChange();

            // Assert
            Assert.Empty(sut.Playlist.UserPlaylists);
            Assert.Null(sut.Playlist.SelectedPlaylist);
            Assert.Equal(string.Empty, sut.Playlist.CurrentPlaylistName);
            Assert.Equal(ViewType.Playlists, sut.CurrentViewType);
        }
        finally
        {
            App.ServiceProvider = previousProvider;
        }
    }
}
