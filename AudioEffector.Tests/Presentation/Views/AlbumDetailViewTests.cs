using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Moq;
using Xunit;
using Track = AudioEffector.Domain.Entities.Track;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="AlbumDetailView"/> のUI仮想化・テーマ対応・バインディング・操作分離を検証するテストクラス
/// </summary>
public sealed class AlbumDetailViewTests
{
    private sealed class MainWindowHostStub
    {
        public bool IsSelectionMode { get; set; }

        public DeviceBrowserStub DeviceBrowser { get; } = new();
    }

    private sealed class DeviceBrowserStub
    {
        public bool IsTransferring { get; set; }
    }

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(
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

    private static (AlbumDetailViewModel DetailVm, LibraryViewModel LibraryVm) CreateViewModels()
    {
        var trackRepoMock = new Mock<ITrackRepository>();
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        var metadataExtractor = new TagLibMetadataExtractor();
        var eventBus = new InMemoryEventBus();
        var libraryAppService = new LibraryApplicationService(
            trackRepoMock.Object,
            favoriteRepoMock.Object,
            metadataExtractor,
            eventBus);

        var audioServiceMock = new Mock<IAudioService>();
        var settingsServiceMock = new Mock<ISettingsService>();
        settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var libraryVm = new LibraryViewModel(
            libraryAppService,
            audioServiceMock.Object,
            settingsServiceMock.Object);

        var detailVm = new AlbumDetailViewModel(
            libraryVm,
            audioServiceMock.Object,
            playlistViewModel: null,
            artworkBytesLoader: (_, _) => System.Threading.Tasks.Task.FromResult<byte[]?>(null));

        return (detailVm, libraryVm);
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunWithHostWindow(
        AlbumDetailView view,
        MainWindowHostStub? hostStub,
        Action<Window> action)
    {
        var hostWindow = new Window
        {
            DataContext = hostStub ?? new MainWindowHostStub(),
            Content = view,
            Width = 960,
            Height = 640,
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

    private static Album CreateSampleAlbum()
    {
        var track1 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\ram_01.flac"),
            "Give Life Back to Music",
            "Daft Punk",
            "Random Access Memories",
            TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(34),
            trackNumber: 1,
            year: 2013,
            genre: "Electronic",
            sampleRate: 96000,
            bitsPerSample: 24,
            format: "FLAC",
            isFavorite: true,
            isLossless: true,
            isHiRes: true)
        {
            FilePath = string.Empty
        };

        var track2 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\ram_02.wav"),
            "Giorgio by Moroder",
            "Daft Punk",
            "Random Access Memories",
            TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(4),
            trackNumber: 2,
            year: 2013,
            genre: "Electronic",
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "WAV",
            isFavorite: false,
            isLossless: true,
            isHiRes: false)
        {
            FilePath = string.Empty,
            IsPlaying = true
        };

        var track3 = new Track(
            TrackId.New(),
            AudioPath.Create(@"C:\Music\ram_03.mp3"),
            "Bonus Track",
            "Daft Punk feat. Guest",
            "Random Access Memories",
            TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(12),
            trackNumber: 0,
            year: 2013,
            genre: "Electronic",
            bitrate: 320,
            sampleRate: 44100,
            bitsPerSample: 0,
            format: "MP3",
            isFavorite: false)
        {
            FilePath = string.Empty
        };

        var album = new Album("Random Access Memories", "Daft Punk")
        {
            Year = 2013
        };
        album.Tracks.Add(track1);
        album.Tracks.Add(track2);
        album.Tracks.Add(track3);
        return album;
    }

    /// <summary>
    /// ダーク・ライト両テーマで AlbumDetailView を描画した際、Hero メタデータ・収録曲リストの各項目が正しく表示され、バインディングエラーが 0 件であることを検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void AlbumDetailView_ダークライト両テーマ描画時_Heroと収録曲が表示されバインディングエラーが0件であること(string themeFileName)
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (detailVm, libraryVm) = CreateViewModels();
            using var disposableLibrary = libraryVm;
            using var disposableDetail = detailVm;

            var album = CreateSampleAlbum();
            detailVm.SetAlbum(album);

            using var listener = new BindingErrorTraceListener();
            EnsureApplicationResources();

            var sut = new AlbumDetailView
            {
                DataContext = detailVm
            };
            AttachThemeResources(sut.Resources, themeFileName);

            // Act
            RunWithHostWindow(sut, null, _ =>
            {
                var titleText = FindVisualChildren<TextBlock>(sut).Single(tb => tb.Name == "AlbumTitleText");
                var artistText = FindVisualChildren<TextBlock>(sut).Single(tb => tb.Name == "AlbumArtistText");
                var metadataText = FindVisualChildren<TextBlock>(sut).Single(tb => tb.Name == "MetadataSummaryText");
                var heroFormatText = FindVisualChildren<TextBlock>(sut).Single(tb => tb.Name == "HeroFormatText");
                var heroQualityText = FindVisualChildren<TextBlock>(sut).Single(tb => tb.Name == "HeroQualityText");
                var fallbackImage = FindVisualChildren<Image>(sut).Single(img => img.Name == "FallbackCoverImage");

                var trackNumbers = FindVisualChildren<TextBlock>(sut)
                    .Where(tb => tb.Name == "TrackNumberText")
                    .Select(tb => tb.Text)
                    .ToArray();
                var trackTitles = FindVisualChildren<TextBlock>(sut)
                    .Where(tb => tb.Name == "TrackTitleText")
                    .Select(tb => tb.Text)
                    .ToArray();
                var trackArtists = FindVisualChildren<TextBlock>(sut)
                    .Where(tb => tb.Name == "TrackArtistText")
                    .Select(tb => tb.Text)
                    .ToArray();
                var trackDurations = FindVisualChildren<TextBlock>(sut)
                    .Where(tb => tb.Name == "TrackDurationText")
                    .Select(tb => tb.Text)
                    .ToArray();
                var favoriteIcons = FindVisualChildren<Button>(sut)
                    .Where(b => b.Name == "FavoriteToggleButton")
                    .Select(b => Assert.IsType<TextBlock>(b.Content).Text)
                    .ToArray();
                var qualityVisibilities = FindVisualChildren<Border>(sut)
                    .Where(b => b.Name == "TrackQualityBadge")
                    .Select(b => b.Visibility)
                    .ToArray();

                // Assert
                Assert.False(listener.HasErrors, $"AlbumDetailView ({themeFileName}) でバインディングエラーが検出されました: {string.Join("; ", listener.Errors)}");
                Assert.Equal("Random Access Memories", titleText.Text);
                Assert.Equal("Daft Punk", artistText.Text);
                Assert.Equal("2013 • Electronic • 3 曲 • 16:50", metadataText.Text);
                Assert.Equal("FLAC", heroFormatText.Text);
                Assert.Equal("Hi-Res", heroQualityText.Text);
                Assert.Equal(Visibility.Visible, fallbackImage.Visibility);
                Assert.Equal(["1", "▶", "-"], trackNumbers);
                Assert.Equal(["Give Life Back to Music", "Giorgio by Moroder", "Bonus Track"], trackTitles);
                Assert.Equal(["Daft Punk", "Daft Punk", "Daft Punk feat. Guest"], trackArtists);
                Assert.Equal(["04:34", "09:04", "03:12"], trackDurations);
                Assert.Equal(["★", "☆", "☆"], favoriteIcons);
                Assert.Equal([Visibility.Visible, Visibility.Visible, Visibility.Collapsed], qualityVisibilities);
            });
        });
    }

    /// <summary>
    /// 収録曲 ListBox に UI 仮想化の必須 4 属性が設定されており、親 ScrollViewer に内包されていないことを検証します。
    /// </summary>
    [Fact]
    public void TracksListBox_初期化時_UI仮想化4属性が設定され親ScrollViewerに内包されていないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            EnsureApplicationResources();
            var sut = new AlbumDetailView();
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");

            // Act
            var listBox = Assert.IsType<ListBox>(sut.FindName("TracksListBox"));
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
    /// クリック元要素の種別に応じて、行クリック再生の発火可否が正しく判定されることを検証します。
    /// </summary>
    [Fact]
    public void ShouldTriggerRowPlay_クリック元要素の種別_子ボタンやCheckBoxやScrollBarはfalseそれ以外はtrueを返すこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var titleText = new TextBlock { Text = "Title" };
            var buttonChildText = new TextBlock { Text = "★" };
            var button = new Button { Content = buttonChildText };
            var checkBox = new CheckBox();
            var scrollBar = new ScrollBar();

            var container = new StackPanel();
            container.Children.Add(titleText);
            container.Children.Add(button);
            container.Children.Add(checkBox);
            container.Children.Add(scrollBar);
            container.Measure(new Size(400, 100));
            container.Arrange(new Rect(0, 0, 400, 100));
            container.UpdateLayout();

            // Act
            var fromTitle = AlbumDetailView.ShouldTriggerRowPlay(titleText);
            var fromButtonChild = AlbumDetailView.ShouldTriggerRowPlay(buttonChildText);
            var fromButton = AlbumDetailView.ShouldTriggerRowPlay(button);
            var fromCheckBox = AlbumDetailView.ShouldTriggerRowPlay(checkBox);
            var fromScrollBar = AlbumDetailView.ShouldTriggerRowPlay(scrollBar);
            var fromNull = AlbumDetailView.ShouldTriggerRowPlay(null);

            // Assert
            Assert.True(fromTitle);
            Assert.False(fromButtonChild);
            Assert.False(fromButton);
            Assert.False(fromCheckBox);
            Assert.False(fromScrollBar);
            Assert.False(fromNull);
        });
    }

    /// <summary>
    /// ルーティングイベント経由で同一行の曲名領域をクリックした場合のみ再生が発火し、子ボタン押下キャンセルや MouseLeave では発火しないことを検証します。
    /// </summary>
    [Fact]
    public void TrackRow_ルーティングイベント経由クリック操作_同一行内押下離上のみで再生が発火すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (detailVm, libraryVm) = CreateViewModels();
            using var disposableLibrary = libraryVm;
            using var disposableDetail = detailVm;

            var playedTracks = new List<Track?>();
            detailVm.PlaybackRequested += (_, startTrack, _, _) => playedTracks.Add(startTrack);

            var album = new Album("Click Album", "Click Artist");
            var track = new Track(
                TrackId.New(),
                AudioPath.Create(@"C:\Music\click_detail.flac"),
                "Click Track",
                "Click Artist",
                "Click Album",
                TimeSpan.FromMinutes(3),
                trackNumber: 1,
                format: "FLAC")
            {
                FilePath = string.Empty
            };
            album.Tracks.Add(track);
            detailVm.SetAlbum(album);

            EnsureApplicationResources();
            var sut = new AlbumDetailView { DataContext = detailVm };
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");
            RunWithHostWindow(sut, null, _ =>
            {
                var rowBorder = FindVisualChildren<Border>(sut).Single(b => b.Name == "TrackRowBorder");
                var titleText = FindVisualChildren<TextBlock>(sut).Single(tb => tb.Name == "TrackTitleText");
                var favButton = FindVisualChildren<Button>(sut).Single(b => b.Name == "FavoriteToggleButton");
                var favIcon = Assert.IsType<TextBlock>(favButton.Content);

                // Act 1: ボタン上で押下し曲名領域で離上 → 再生されない
                favIcon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseDownEvent,
                    Source = favIcon
                });
                titleText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseUpEvent,
                    Source = titleText
                });

                // Assert 1
                Assert.Empty(playedTracks);

                // Act 2: 行上で押下後 MouseLeave してから離上 → 再生されない
                titleText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseDownEvent,
                    Source = titleText
                });
                rowBorder.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = UIElement.MouseLeaveEvent,
                    Source = rowBorder
                });
                titleText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseUpEvent,
                    Source = titleText
                });

                // Assert 2
                Assert.Empty(playedTracks);

                // Act 3: 同一行の曲名領域で押下→離上 → 再生が 1 回発火する
                titleText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseDownEvent,
                    Source = titleText
                });
                titleText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseUpEvent,
                    Source = titleText
                });

                // Assert 3
                var played = Assert.Single(playedTracks);
                Assert.Same(track, played);
            });
        });
    }

    /// <summary>
    /// 機器転送選択モードおよび転送中状態に応じて、収録曲行の CheckBox の表示状態と活性状態が切り替わることを検証します。
    /// </summary>
    [Fact]
    public void TransferSelectCheckBox_選択モードかつ転送中の場合_表示かつ非活性になること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (detailVm, libraryVm) = CreateViewModels();
            using var disposableLibrary = libraryVm;
            using var disposableDetail = detailVm;
            detailVm.SetAlbum(CreateSampleAlbum());

            EnsureApplicationResources();
            var sut = new AlbumDetailView { DataContext = detailVm };
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");

            var hostStub = new MainWindowHostStub { IsSelectionMode = true };
            hostStub.DeviceBrowser.IsTransferring = true;

            // Act & Assert
            RunWithHostWindow(sut, hostStub, _ =>
            {
                var checkBoxes = FindVisualChildren<CheckBox>(sut)
                    .Where(cb => cb.Name == "TransferSelectCheckBox")
                    .ToArray();

                Assert.Equal(3, checkBoxes.Length);
                Assert.All(checkBoxes, cb =>
                {
                    Assert.Equal(Visibility.Visible, cb.Visibility);
                    Assert.False(cb.IsEnabled);
                });
            });
        });
    }

    /// <summary>
    /// 収録曲が 0 件の場合、再生・キュー・プレイリスト追加ボタンが無効状態になり、戻るボタンは有効であることを検証します。
    /// </summary>
    [Fact]
    public void QuickActionButtons_収録曲0件の場合_再生およびキューおよびプレイリスト追加ボタンが無効になること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (detailVm, libraryVm) = CreateViewModels();
            using var disposableLibrary = libraryVm;
            using var disposableDetail = detailVm;
            detailVm.SetAlbum(new Album("Empty Album", "Unknown"));

            EnsureApplicationResources();
            var sut = new AlbumDetailView { DataContext = detailVm };
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");

            // Act
            RunWithHostWindow(sut, null, _ =>
            {
                var backButton = Assert.IsType<Button>(sut.FindName("BackToAlbumsButton"));
                var playAllButton = Assert.IsType<Button>(sut.FindName("PlayAllButton"));
                var shuffleButton = Assert.IsType<Button>(sut.FindName("ShufflePlayButton"));
                var playNextButton = Assert.IsType<Button>(sut.FindName("PlayNextAlbumButton"));
                var enqueueButton = Assert.IsType<Button>(sut.FindName("EnqueueAlbumButton"));
                var addToPlaylistButton = Assert.IsType<Button>(sut.FindName("AddAlbumToPlaylistButton"));

                // Assert
                Assert.True(backButton.IsEnabled);
                Assert.False(playAllButton.IsEnabled);
                Assert.False(shuffleButton.IsEnabled);
                Assert.False(playNextButton.IsEnabled);
                Assert.False(enqueueButton.IsEnabled);
                Assert.False(addToPlaylistButton.IsEnabled);
            });
        });
    }

    /// <summary>
    /// 収録曲行のコンテキストメニューに仕様通りの項目が揃っていることを検証します。
    /// </summary>
    [Fact]
    public void TrackRowContextMenu_初期生成時_必要なメニュー項目が定義されていること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (detailVm, libraryVm) = CreateViewModels();
            using var disposableLibrary = libraryVm;
            using var disposableDetail = detailVm;
            detailVm.SetAlbum(CreateSampleAlbum());

            EnsureApplicationResources();
            var sut = new AlbumDetailView { DataContext = detailVm };
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");

            // Act
            RunWithHostWindow(sut, null, _ =>
            {
                var rowBorder = FindVisualChildren<Border>(sut).First(b => b.Name == "TrackRowBorder");
                var contextMenu = Assert.IsType<ContextMenu>(rowBorder.ContextMenu);
                var headers = contextMenu.Items.OfType<MenuItem>().Select(mi => mi.Header as string).ToArray();

                // Assert
                Assert.Contains("次に再生", headers);
                Assert.Contains("最後に再生", headers);
                Assert.Contains("プレイリストに追加...", headers);
                Assert.Contains("プロパティ", headers);
                Assert.Contains("ファイルの場所を開く", headers);
                Assert.Contains("削除", headers);
                Assert.Equal(8, headers.Length);
            });
        });
    }
}
