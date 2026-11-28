using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Moq;
using Xunit;
using Track = AudioEffector.Domain.Entities.Track;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="LibraryStatsView"/> の実 WPF 描画・Dark/Light 両テーマ適用・XAML バインディングエラー 0 件・コマンド活性連動を検証するテストクラス
/// </summary>
public sealed class LibraryStatsViewTests
{
    private sealed class DispatcherUiDispatcherStub : IUiDispatcher
    {
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        public DispatcherUiDispatcherStub(System.Windows.Threading.Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Invoke(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _dispatcher.Invoke(action);
            }
        }

        public void BeginInvoke(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _dispatcher.BeginInvoke(action);
            }
        }
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

        public void ClearAll() => _data = new ListeningStatsData();
    }

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new System.Windows.Threading.DispatcherSynchronizationContext(
                        System.Windows.Threading.Dispatcher.CurrentDispatcher));

                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown,
                    };
                }

                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    private static void AttachThemeResources(ResourceDictionary target, string themeFileName)
    {
        var themeUri = new Uri($"pack://application:,,,/AudioEffector;component/Presentation/Themes/{themeFileName}", UriKind.Absolute);
        target.MergedDictionaries.Add(new ResourceDictionary { Source = themeUri });
    }

    private static (LibraryViewModel LibraryVm, PlaylistViewModel PlaylistVm) CreateLibraryAndPlaylist(IAudioService audioService)
    {
        var trackRepoMock = new Mock<ITrackRepository>();
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        var playlistRepoMock = new Mock<IPlaylistRepository>();
        var metadataMock = new Mock<IMetadataExtractor>();
        var settingsMock = new Mock<ISettingsService>();
        var eventBus = new InMemoryEventBus();

        favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        playlistRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserPlaylist>());
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var libraryService = new LibraryApplicationService(trackRepoMock.Object, favoriteRepoMock.Object, metadataMock.Object, eventBus);
        var playlistService = new PlaylistApplicationService(playlistRepoMock.Object, trackRepoMock.Object, eventBus);
        var playlistVm = new PlaylistViewModel(playlistService, libraryService, audioService, eventBus);
        var libraryVm = new LibraryViewModel(libraryService, audioService, settingsMock.Object, playlistVm);
        return (libraryVm, playlistVm);
    }

    /// <summary>
    /// Dark / Light 両テーマで LibraryStatsView を実 WPF ホストウィンドウ上へ描画し、データ 0 件→蓄積後の切替とコマンド活性・バインディングエラー 0 件を検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void LibraryStatsView_DarkおよびLightテーマ描画時_バインディングエラー0件でエンプティと集計表示およびボタン活性が連動すること(string themeFileName)
    {
        RunInStaThread(() =>
        {
            // Arrange
            var now = new DateTime(2026, 5, 13, 21, 0, 0);
            var repo = new InMemoryListeningStatsRepository();
            var audioMock = new Mock<IAudioService>();
            var (libraryVm, playlistVm) = CreateLibraryAndPlaylist(audioMock.Object);
            var dispatcher = new DispatcherUiDispatcherStub(System.Windows.Threading.Dispatcher.CurrentDispatcher);

            using var listeningSvc = new ListeningStatsApplicationService(repo, audioMock.Object, () => now);
            var libraryStatsSvc = new LibraryStatsApplicationService(repo, () => now);
            using var vm = new LibraryStatsViewModel(
                libraryStatsSvc,
                listeningSvc,
                libraryVm,
                audioMock.Object,
                dispatcher,
                playlistVm,
                playlistNamePrompt: _ => null,
                messageNotifier: (_, _, _) => { });
            vm.IsActiveView = true;

            using var bindingListener = new BindingErrorTraceListener();
            var sut = new LibraryStatsView { DataContext = vm };
            AttachThemeResources(sut.Resources, themeFileName);

            var hostWindow = new Window
            {
                Width = 1040,
                Height = 760,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -20000,
                Top = -20000,
                Content = sut,
            };
            AttachThemeResources(hostWindow.Resources, themeFileName);

            try
            {
                // Act 1: 初期 0 件状態を描画
                hostWindow.Show();
                hostWindow.UpdateLayout();

                var emptyPanel = Assert.IsType<Border>(sut.FindName("EmptyStatePanel"));
                var dashboardPanel = Assert.IsType<StackPanel>(sut.FindName("StatsDashboardContent"));
                var playTopButton = Assert.IsType<Button>(sut.FindName("PlayTopTracksButton"));
                var createPlaylistButton = Assert.IsType<Button>(sut.FindName("CreateTopTracksPlaylistButton"));

                Assert.Equal(Visibility.Visible, emptyPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, dashboardPanel.Visibility);
                Assert.False(playTopButton.IsEnabled);
                Assert.False(createPlaylistButton.IsEnabled);

                // Act 2: 再生データを蓄積して即時再描画・タブ切替
                var track = new Track
                {
                    FilePath = @"C:\music\sample.flac",
                    Title = "Sample Song",
                    Artist = "Sample Artist",
                    Album = "Sample Album",
                    Duration = TimeSpan.FromMinutes(4),
                };
                libraryVm.Tracks.Add(track);
                listeningSvc.RecordPlaybackCompleted(track);
                vm.SelectAlbumRankingCommand.Execute(null);
                vm.SelectDayOfWeekHabitCommand.Execute(null);
                hostWindow.UpdateLayout();

                // Assert
                Assert.Equal(Visibility.Collapsed, emptyPanel.Visibility);
                Assert.Equal(Visibility.Visible, dashboardPanel.Visibility);
                Assert.True(playTopButton.IsEnabled);
                Assert.True(createPlaylistButton.IsEnabled);

                var totalPlayText = Assert.IsType<TextBlock>(sut.FindName("TotalPlayCountText"));
                Assert.Equal("1 回", totalPlayText.Text);
                Assert.Empty(bindingListener.Errors);
            }
            finally
            {
                hostWindow.Close();
                playlistVm.Dispose();
            }
        });
    }
}
