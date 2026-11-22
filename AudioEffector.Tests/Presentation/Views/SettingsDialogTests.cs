using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="SettingsDialog"/> の右側コンテンツ領域のスクロール構造およびカテゴリ切替挙動を検証するテストクラス
/// </summary>
public sealed class SettingsDialogTests
{
    private static readonly Uri DarkThemeUri = new("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml", UriKind.Absolute);
    private static readonly Uri ScrollBarResourcesUri = new("pack://application:,,,/AudioEffector;component/Presentation/Themes/ScrollBarResources.xaml", UriKind.Absolute);

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }

                EnsureThemeResourcesLoaded();
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

    private static void EnsureThemeResourcesLoaded()
    {
        var dictionaries = System.Windows.Application.Current!.Resources.MergedDictionaries;
        if (!dictionaries.Any(d => d.Source == DarkThemeUri))
        {
            dictionaries.Add(new ResourceDictionary { Source = DarkThemeUri });
        }

        if (!dictionaries.Any(d => d.Source == ScrollBarResourcesUri))
        {
            dictionaries.Add(new ResourceDictionary { Source = ScrollBarResourcesUri });
        }
    }

    private static SettingsViewModel CreateViewModel()
    {
        var settingsServiceMock = new Mock<ISettingsService>();
        settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var audioServiceMock = new Mock<IAudioService>();
        return new SettingsViewModel(settingsServiceMock.Object, audioServiceMock.Object);
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is DependencyObject depObj)
            {
                if (depObj is T match)
                {
                    yield return match;
                }

                foreach (var descendant in FindLogicalChildren<T>(depObj))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// 右側コンテンツ領域が単一の ScrollViewer で構成され、二重スクロールが存在しないことを検証します。
    /// </summary>
    [Fact]
    public void ScrollViewer構成_設定画面初期化時_右側領域が単一ScrollViewerで構成され二重スクロールがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var vm = CreateViewModel();

            // Act
            var sut = new SettingsDialog(vm);
            var scrollViewers = FindLogicalChildren<ScrollViewer>(sut).ToList();

            // Assert
            var contentScrollViewer = Assert.Single(scrollViewers);
            Assert.Equal("SettingsContentScrollViewer", contentScrollViewer.Name);
            Assert.Equal(ScrollBarVisibility.Auto, contentScrollViewer.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, contentScrollViewer.HorizontalScrollBarVisibility);
        });
    }

    /// <summary>
    /// ウィンドウ高さを縮めた状態で各カテゴリを表示した際、コンテンツが縦スクロール可能になりバインディングエラーが発生しないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("一般")]
    [InlineData("オーディオデバイス")]
    [InlineData("エフェクト・再生")]
    [InlineData("ショートカット")]
    public void スクロール可能状態_画面高さが不足する各カテゴリ表示時_ScrollableHeightが正かつバインディングエラーがないこと(string category)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = category;

            var sut = new SettingsDialog(vm)
            {
                Width = 800,
                Height = 260
            };

            // Act
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 200));
            rootContent.Arrange(new Rect(0, 0, 760, 200));
            rootContent.UpdateLayout();

            var contentScrollViewer = FindLogicalChildren<ScrollViewer>(sut).Single();

            // Assert
            Assert.True(
                contentScrollViewer.ScrollableHeight > 0,
                $"カテゴリ「{category}」で縦スクロール可能であるべきです (ExtentHeight={contentScrollViewer.ExtentHeight}, ViewportHeight={contentScrollViewer.ViewportHeight})");
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// スクロール後に別カテゴリへ切り替えた際、スクロール位置が先頭（VerticalOffset == 0）へリセットされることを検証します。
    /// </summary>
    [Fact]
    public void カテゴリ切替_下方向へスクロール後に別カテゴリを選択時_スクロール位置が先頭へリセットされること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var vm = CreateViewModel();
            vm.SelectedCategory = "ショートカット";

            var sut = new SettingsDialog(vm);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 200));
            rootContent.Arrange(new Rect(0, 0, 760, 200));
            rootContent.UpdateLayout();

            var contentScrollViewer = FindLogicalChildren<ScrollViewer>(sut).Single();
            var categoryListBox = FindLogicalChildren<ListBox>(sut).Single();

            contentScrollViewer.ScrollToVerticalOffset(100);
            contentScrollViewer.UpdateLayout();
            Assert.True(contentScrollViewer.VerticalOffset > 0);

            // Act
            categoryListBox.SelectedItem = "一般";
            contentScrollViewer.UpdateLayout();

            // Assert
            Assert.Equal(0, contentScrollViewer.VerticalOffset);
        });
    }
}
