using System;
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
/// <see cref="ListeningStatsView"/> の実 WPF 描画・Dark/Light テーマ適用・XAML バインディングエラー 0 件・コマンド活性連動を検証するテストクラス
/// </summary>
public sealed class ListeningStatsViewTests
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

    /// <summary>
    /// Dark / Light 両テーマで ListeningStatsView を実ホスト Window 上に描画した際、
    /// バインディングエラー 0 件で KPI・TOP 5・再生中曲復帰ボタンが表示されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void ListeningStatsView_両テーマで実ホスト描画時_バインディングエラー0件で表示および戻すボタン活性が反映されること(string themeFileName)
    {
        // Arrange & Act & Assert
        RunInStaThread(() =>
        {
            using var listener = new BindingErrorTraceListener();
            var repository = new InMemoryListeningStatsRepository();
            var audioMock = new Mock<IAudioService>();
            var playingTrack = new Track
            {
                FilePath = @"C:\Music\Playing.flac",
                Title = "Playing Song",
                Artist = "Artist 1",
                Album = "Album 1",
                Duration = TimeSpan.FromSeconds(210),
            };
            var selectedTrack = new Track
            {
                FilePath = @"C:\Music\Selected.flac",
                Title = "Selected Song",
                Artist = "Artist 2",
                Album = "Album 2",
                Duration = TimeSpan.FromSeconds(180),
            };
            audioMock.Setup(a => a.CurrentTrack).Returns(playingTrack);

            using var statsService = new ListeningStatsApplicationService(
                repository,
                audioMock.Object,
                () => new DateTime(2026, 11, 29, 16, 0, 0));
            statsService.RecordPlaybackCompleted(playingTrack);
            statsService.RecordPlaybackCompleted(selectedTrack);
            statsService.RecordPlaybackCompleted(selectedTrack);

            using var viewModel = new ListeningStatsViewModel(
                statsService,
                audioMock.Object,
                new DispatcherUiDispatcherStub(System.Windows.Threading.Dispatcher.CurrentDispatcher))
            {
                IsActiveView = true,
            };

            var sut = new ListeningStatsView { DataContext = viewModel };
            var host = new Window
            {
                Width = 420,
                Height = 420,
                Left = -20000,
                Top = -20000,
                ShowInTaskbar = false,
                Content = sut,
            };
            AttachThemeResources(host.Resources, themeFileName);

            try
            {
                host.Show();
                host.UpdateLayout();

                viewModel.SetTargetTrack(selectedTrack);
                host.UpdateLayout();

                var returnButton = sut.FindName("ReturnToPlayingTrackButton") as Button;
                var playCountText = sut.FindName("TotalPlayCountText") as TextBlock;

                Assert.NotNull(returnButton);
                Assert.NotNull(playCountText);
                Assert.True(returnButton!.IsEnabled);
                Assert.Equal(Visibility.Visible, returnButton.Visibility);
                Assert.Equal("2 回", playCountText!.Text);
                Assert.Empty(listener.Errors);
            }
            finally
            {
                host.Close();
            }
        });
    }
}
