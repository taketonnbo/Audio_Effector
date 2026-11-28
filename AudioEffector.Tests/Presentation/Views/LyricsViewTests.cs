using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Moq;
using Xunit;
using Track = AudioEffector.Domain.Entities.Track;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="LyricsView"/> の XAML バインディングエラー 0 件・UI 仮想化・コンパクトモード・テーマ対応・クリックシーク後選択解除を検証するテストクラス
/// </summary>
public sealed class LyricsViewTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => action();
    }

    private sealed class FixedLyricsProviderStub : ILyricsProvider
    {
        private readonly Func<Track, LyricsDocument?> _factory;

        public FixedLyricsProviderStub(Func<Track, LyricsDocument?> factory)
        {
            _factory = factory;
        }

        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_factory(track));
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
                    _ = new System.Windows.Application();
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
            throw threadEx;
        }
    }

    private static void EnsureApplicationResources()
    {
        var appResources = System.Windows.Application.Current?.Resources;
        if (appResources == null)
        {
            return;
        }

        if (!appResources.Contains("BoolToVis"))
        {
            appResources["BoolToVis"] = new BoolToVisConverter();
        }

        if (!appResources.Contains("InvertBool"))
        {
            appResources["InvertBool"] = new InvertBoolConverter();
        }

        if (!appResources.Contains("NullToVis"))
        {
            appResources["NullToVis"] = new NullToVisConverter();
        }
    }

    private static void AttachThemeResources(ResourceDictionary target, string themeFileName)
    {
        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AudioEffector;component/Presentation/Themes/{themeFileName}")
        });
        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/ScrollBarResources.xaml")
        });
        target["BoolToVis"] = new BoolToVisConverter();
        target["InvertBool"] = new InvertBoolConverter();
        target["NullToVis"] = new NullToVisConverter();
    }

    private static void RunWithHostWindow(LyricsView view, Action<Window> action)
    {
        var hostWindow = new Window
        {
            Content = view,
            Width = 420,
            Height = 360,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0,
            ShowInTaskbar = false,
            ShowActivated = false
        };

        try
        {
            hostWindow.Show();
            hostWindow.UpdateLayout();
            action(hostWindow);
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        yield return root;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            foreach (DependencyObject descendant in EnumerateVisualTree(VisualTreeHelper.GetChild(root, i)))
            {
                yield return descendant;
            }
        }
    }

    private static Track CreateSampleTrack(string filePath)
    {
        return new Track(
            TrackId.New(),
            AudioPath.Create(filePath),
            "Midnight City",
            "M83",
            "Hurry Up, We're Dreaming",
            TimeSpan.FromMinutes(4));
    }

    private static LyricsDocument CreateSampleSyncedDocument()
    {
        return new LyricsDocument(
            new[]
            {
                new LyricLine(0, TimeSpan.FromSeconds(10), "Waiting in a car"),
                new LyricLine(1, TimeSpan.FromSeconds(20), "The night city grows"),
                new LyricLine(2, TimeSpan.FromSeconds(20), "夜の街が広がっていく"),
                new LyricLine(3, TimeSpan.FromSeconds(30), string.Empty),
                new LyricLine(4, TimeSpan.FromSeconds(40), "Look and see her eyes"),
            },
            isSynced: true,
            sourceType: LyricsSourceType.LrcFile);
    }

    /// <summary>
    /// ダーク・ライト両テーマで LyricsView を全状態描画した際、XAML バインディングエラーが 0 件であり高負荷エフェクトを使用していないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void LyricsView_ダークライト両テーマ描画時_全状態でバインディングエラー0件かつ高負荷エフェクト未使用であること(string themeFileName)
    {
        RunInStaThread(() =>
        {
            // Arrange
            var trackSynced = CreateSampleTrack(@"C:\Music\synced.flac");
            var trackNotFound = CreateSampleTrack(@"C:\Music\notfound.flac");
            var audioMock = new Mock<IAudioService>();
            audioMock.Setup(a => a.TotalTime).Returns(TimeSpan.FromSeconds(240));
            var eventBus = new InMemoryEventBus();
            var provider = new FixedLyricsProviderStub(t =>
                t.FilePath == trackSynced.FilePath ? CreateSampleSyncedDocument() : null);
            var appService = new LyricsApplicationService(provider, eventBus);
            using var vm = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

            using var listener = new BindingErrorTraceListener();
            EnsureApplicationResources();

            var sut = new LyricsView
            {
                DataContext = vm
            };
            AttachThemeResources(sut.Resources, themeFileName);

            // Act & Assert
            RunWithHostWindow(sut, host =>
            {
                // 1. 未再生状態
                var noTrackContainer = Assert.IsType<Grid>(sut.FindName("NoTrackContainer"));
                Assert.Equal(Visibility.Visible, noTrackContainer.Visibility);

                // 2. 同期歌詞表示 + アクティブ行 + 手動スクロール一時停止
                audioMock.Raise(a => a.TrackChanged += null, trackSynced);
                vm.UpdatePlaybackPosition(TimeSpan.FromSeconds(22));
                vm.NotifyManualScroll();
                host.UpdateLayout();

                var listBox = Assert.IsType<ListBox>(sut.FindName("LyricsListBox"));
                var resumeButton = Assert.IsType<Button>(sut.FindName("ResumeAutoScrollButton"));
                var badgeText = Assert.IsType<TextBlock>(sut.FindName("SourceTypeBadgeText"));
                Assert.Equal(Visibility.Visible, listBox.Visibility);
                Assert.Equal(Visibility.Visible, resumeButton.Visibility);
                Assert.Equal("LRC 同期", badgeText.Text);

                // 高負荷エフェクト（DropShadowEffect / BlurEffect）がビジュアルツリーに一切存在しないこと
                bool hasHeavyEffect = EnumerateVisualTree(sut)
                    .OfType<UIElement>()
                    .Any(el => el.Effect is DropShadowEffect or BlurEffect);
                Assert.False(hasHeavyEffect);

                // 3. 歌詞未検出状態
                audioMock.Raise(a => a.TrackChanged += null, trackNotFound);
                host.UpdateLayout();
                var notFoundContainer = Assert.IsType<Grid>(sut.FindName("LyricsNotFoundContainer"));
                Assert.Equal(Visibility.Visible, notFoundContainer.Visibility);

                // 4. コンパクトモード切替
                sut.IsCompactMode = true;
                host.UpdateLayout();
                var headerBorder = Assert.IsType<Border>(sut.FindName("LyricsHeaderBorder"));
                Assert.Equal(Visibility.Collapsed, headerBorder.Visibility);

                Assert.False(
                    listener.HasErrors,
                    $"LyricsView ({themeFileName}) でバインディングエラーが検出されました: {string.Join("; ", listener.Errors)}");
            });
        });
    }

    /// <summary>
    /// LyricsListBox に UI 仮想化の必須 4 属性が設定されており、親 ScrollViewer に内包されていないことを検証します。
    /// </summary>
    [Fact]
    public void LyricsListBox_初期化時_UI仮想化4属性が設定され親ScrollViewerに内包されていないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            EnsureApplicationResources();
            var sut = new LyricsView();
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");

            // Act
            var listBox = Assert.IsType<ListBox>(sut.FindName("LyricsListBox"));
            bool isVirtualizing = VirtualizingPanel.GetIsVirtualizing(listBox);
            var virtualizationMode = VirtualizingPanel.GetVirtualizationMode(listBox);
            var scrollUnit = VirtualizingPanel.GetScrollUnit(listBox);
            bool canContentScroll = ScrollViewer.GetCanContentScroll(listBox);

            DependencyObject? parent = LogicalTreeHelper.GetParent(listBox);
            bool hasAncestorScrollViewer = false;
            while (parent != null)
            {
                if (parent is ScrollViewer)
                {
                    hasAncestorScrollViewer = true;
                    break;
                }

                parent = LogicalTreeHelper.GetParent(parent);
            }

            // Assert
            Assert.True(isVirtualizing);
            Assert.Equal(VirtualizationMode.Recycling, virtualizationMode);
            Assert.Equal(ScrollUnit.Pixel, scrollUnit);
            Assert.True(canContentScroll);
            Assert.False(hasAncestorScrollViewer);
        });
    }

    /// <summary>
    /// 同期歌詞行をクリックしてシーク実行した後、LyricsListBox.SelectedItem が null に解除され残留ハイライトが残らないことを検証します。
    /// </summary>
    [Fact]
    public void LyricRow_クリックシーク実行後_SelectedItemが解除され間奏空行ではシークされないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var track = CreateSampleTrack(@"C:\Music\click_seek.flac");
            var audioMock = new Mock<IAudioService>();
            audioMock.Setup(a => a.TotalTime).Returns(TimeSpan.FromSeconds(100));
            var eventBus = new InMemoryEventBus();
            var provider = new FixedLyricsProviderStub(_ => CreateSampleSyncedDocument());
            var appService = new LyricsApplicationService(provider, eventBus);
            using var vm = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

            audioMock.Raise(a => a.TrackChanged += null, track);

            EnsureApplicationResources();
            var sut = new LyricsView { DataContext = vm };
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");

            // Act & Assert
            RunWithHostWindow(sut, host =>
            {
                var listBox = Assert.IsType<ListBox>(sut.FindName("LyricsListBox"));
                var rowBorders = FindVisualChildren<Border>(sut).Where(b => b.Name == "LyricRowBorder").ToArray();
                Assert.True(rowBorders.Length >= 4);

                // シーク可能な行（index 1: 20秒 / 100秒 = 0.2）をクリック
                Border seekableRow = rowBorders[1];
                listBox.SelectedItem = seekableRow.DataContext;

                seekableRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = seekableRow
                });
                seekableRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                    Source = seekableRow
                });

                audioMock.Verify(a => a.SeekTo(It.Is<double>(r => Math.Abs(r - 0.2) < 0.0001)), Times.Once);
                Assert.Null(listBox.SelectedItem);

                // 間奏空行（index 3）をクリックしても SeekTo は増えず SelectedItem は解除される
                Border interludeRow = rowBorders[3];
                interludeRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = interludeRow
                });
                interludeRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                    Source = interludeRow
                });

                audioMock.Verify(a => a.SeekTo(It.IsAny<double>()), Times.Once);
                Assert.Null(listBox.SelectedItem);
            });
        });
    }
}
