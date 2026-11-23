using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="LibraryView"/> のリスト表示における展開楽曲リストの表示・バインディング・操作分離を検証するテストクラス
/// </summary>
public sealed class LibraryViewTests
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

    private static LibraryViewModel CreateViewModel()
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

        return new LibraryViewModel(
            libraryAppService,
            audioServiceMock.Object,
            settingsServiceMock.Object);
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
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
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

    /// <summary>
    /// リスト表示でアルバムのExpanderを展開した際、ダーク・ライト両テーマで詳細情報（トラック番号・再生中・お気に入り・曲名・アーティスト・再生時間・形式/音質）が表示され、バインディングエラーが0件であることを検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void ListView展開楽曲リスト_Expander展開時_詳細項目が表示されバインディングエラーが0件であること(string themeFileName)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var vm = CreateViewModel();
            vm.IsGridView = false;

            var track1 = new Track(
                TrackId.New(),
                AudioPath.Create(@"C:\Music\track1.flac"),
                "First Song",
                "Artist A",
                "Test Album",
                TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(34),
                trackNumber: 1,
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
                AudioPath.Create(@"C:\Music\track2.wav"),
                "Playing Song",
                "Artist B feat. C",
                "Test Album",
                TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(12),
                trackNumber: 2,
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
                AudioPath.Create(@"C:\Music\track3.mp3"),
                "Bonus Track",
                "Artist A",
                "Test Album",
                TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(5),
                trackNumber: 0,
                bitrate: 320,
                sampleRate: 44100,
                bitsPerSample: 0,
                format: "MP3",
                isFavorite: false)
            {
                FilePath = string.Empty
            };

            var album = new Album("Test Album", "Artist A");
            album.Tracks.Add(track1);
            album.Tracks.Add(track2);
            album.Tracks.Add(track3);
            vm.Albums.Add(album);

            using var listener = new BindingErrorTraceListener();
            EnsureApplicationResources();
            var host = new Grid
            {
                Name = "MainWindowRoot",
                DataContext = new MainWindowHostStub()
            };
            NameScope.SetNameScope(host, new NameScope());
            host.RegisterName("MainWindowRoot", host);

            var sut = new LibraryView
            {
                DataContext = vm
            };
            AttachThemeResources(sut.Resources, themeFileName);
            host.Children.Add(sut);

            // Act
            host.Measure(new Size(960, 640));
            host.Arrange(new Rect(0, 0, 960, 640));
            host.UpdateLayout();

            var expander = FindVisualChildren<Expander>(sut).First();
            expander.IsExpanded = true;
            host.Measure(new Size(960, 640));
            host.Arrange(new Rect(0, 0, 960, 640));
            host.UpdateLayout();

            var trackNumberTexts = FindVisualChildren<TextBlock>(expander)
                .Where(tb => tb.Name == "TrackNumberText")
                .Select(tb => tb.Text)
                .ToArray();
            var trackTitleTexts = FindVisualChildren<TextBlock>(expander)
                .Where(tb => tb.Name == "TrackTitleText")
                .Select(tb => tb.Text)
                .ToArray();
            var trackArtistTexts = FindVisualChildren<TextBlock>(expander)
                .Where(tb => tb.Name == "TrackArtistText")
                .Select(tb => tb.Text)
                .ToArray();
            var trackDurationTexts = FindVisualChildren<TextBlock>(expander)
                .Where(tb => tb.Name == "TrackDurationText")
                .Select(tb => tb.Text)
                .ToArray();
            var favoriteIcons = FindVisualChildren<Button>(expander)
                .Where(b => b.Name == "FavoriteToggleButton")
                .Select(b => Assert.IsType<TextBlock>(b.Content).Text)
                .ToArray();

            // Assert
            Assert.False(listener.HasErrors, $"LibraryView ({themeFileName}) でバインディングエラーが検出されました: {string.Join("; ", listener.Errors)}");
            Assert.Equal(["1", "▶", "-"], trackNumberTexts);
            Assert.Equal(["First Song", "Playing Song", "Bonus Track"], trackTitleTexts);
            Assert.Equal(["Artist A", "Artist B feat. C", "Artist A"], trackArtistTexts);
            Assert.Equal(["04:34", "05:12", "03:05"], trackDurationTexts);
            Assert.Equal(["★", "☆", "☆"], favoriteIcons);
        });
    }

    /// <summary>
    /// お気に入りトグルボタン押下時に再生要求が誤発火せず、お気に入り状態および表示アイコンが即時更新されることを検証します。
    /// </summary>
    [Fact]
    public void FavoriteToggleButton_実行時_再生要求を発火せずお気に入り状態とアイコンが更新されること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var vm = CreateViewModel();
            vm.IsGridView = false;

            var playbackFired = false;
            vm.PlaybackRequested += (_, _, _, _) => playbackFired = true;

            var track = new Track(
                TrackId.New(),
                AudioPath.Create(@"C:\Music\fav_test.flac"),
                "Favorite Song",
                "Artist X",
                "Album X",
                TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(20),
                trackNumber: 1,
                format: "FLAC",
                isFavorite: false)
            {
                FilePath = string.Empty
            };
            var album = new Album("Album X", "Artist X");
            album.Tracks.Add(track);
            vm.Albums.Add(album);

            EnsureApplicationResources();
            var host = new Grid
            {
                Name = "MainWindowRoot",
                DataContext = new MainWindowHostStub()
            };
            NameScope.SetNameScope(host, new NameScope());
            host.RegisterName("MainWindowRoot", host);

            var sut = new LibraryView
            {
                DataContext = vm
            };
            AttachThemeResources(sut.Resources, "DarkTheme.xaml");
            host.Children.Add(sut);

            host.Measure(new Size(960, 640));
            host.Arrange(new Rect(0, 0, 960, 640));
            host.UpdateLayout();

            var expander = FindVisualChildren<Expander>(sut).First();
            expander.IsExpanded = true;
            host.Measure(new Size(960, 640));
            host.Arrange(new Rect(0, 0, 960, 640));
            host.UpdateLayout();

            var favButton = FindVisualChildren<Button>(expander).Single(b => b.Name == "FavoriteToggleButton");
            var favIcon = Assert.IsType<TextBlock>(favButton.Content);
            Assert.Equal("☆", favIcon.Text);

            // Act
            Assert.False(LibraryView.ShouldTriggerRowPlay(favIcon));
            favButton.Command.Execute(favButton.CommandParameter);
            host.UpdateLayout();

            // Assert
            Assert.True(track.IsFavorite);
            Assert.Equal("★", favIcon.Text);
            Assert.False(playbackFired);
        });
    }

    /// <summary>
    /// クリック元要素の種別に応じて、行クリック再生の発火可否が正しく判定されることを検証します。
    /// </summary>
    [Fact]
    public void ShouldTriggerRowPlay_クリック元要素の種別_子ボタンやCheckBoxはfalseそれ以外はtrueを返すこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var titleText = new TextBlock { Text = "Title" };
            var buttonChildText = new TextBlock { Text = "★" };
            var button = new Button { Content = buttonChildText };
            var checkBox = new CheckBox();

            var container = new StackPanel();
            container.Children.Add(titleText);
            container.Children.Add(button);
            container.Children.Add(checkBox);
            container.Measure(new Size(400, 100));
            container.Arrange(new Rect(0, 0, 400, 100));
            container.UpdateLayout();

            // Act
            var fromTitle = LibraryView.ShouldTriggerRowPlay(titleText);
            var fromButtonChild = LibraryView.ShouldTriggerRowPlay(buttonChildText);
            var fromButton = LibraryView.ShouldTriggerRowPlay(button);
            var fromCheckBox = LibraryView.ShouldTriggerRowPlay(checkBox);
            var fromNull = LibraryView.ShouldTriggerRowPlay(null);

            // Assert
            Assert.True(fromTitle);
            Assert.False(fromButtonChild);
            Assert.False(fromButton);
            Assert.False(fromCheckBox);
            Assert.False(fromNull);
        });
    }
}
