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
}
