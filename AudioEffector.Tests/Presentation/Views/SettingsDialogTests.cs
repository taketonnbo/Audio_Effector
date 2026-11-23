using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
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
    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
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
    /// プレースホルダー表示となる「データ管理・その他」カテゴリを選択した際、バインディングエラーが発生せず不要なスクロール領域が生じないことを検証します。
    /// </summary>
    [Fact]
    public void プレースホルダーカテゴリ_データ管理その他選択時_バインディングエラーがなく不要なスクロールが出ないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "データ管理・その他";

            var sut = new SettingsDialog(vm);

            // Act
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var contentScrollViewer = FindLogicalChildren<ScrollViewer>(sut).Single();

            // Assert
            Assert.Equal(0, contentScrollViewer.ScrollableHeight);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// 通常ウィンドウサイズ（800×600相当）で項目数が収まるカテゴリを表示した際、ScrollableHeight が 0 となることを検証します。
    /// </summary>
    [Fact]
    public void 非スクロール状態_通常ウィンドウ高さで項目が少ないカテゴリ表示時_ScrollableHeightが0であること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var vm = CreateViewModel();
            vm.SelectedCategory = "オーディオデバイス";

            var sut = new SettingsDialog(vm);

            // Act
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var contentScrollViewer = FindLogicalChildren<ScrollViewer>(sut).Single();

            // Assert
            Assert.Equal(0, contentScrollViewer.ScrollableHeight);
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

    /// <summary>
    /// 一般カテゴリに「画面最小化時にコンパクトプレイヤーを表示しない」チェックボックスが配置されており、
    /// チェック操作が SettingsViewModel.DisableMiniPlayerOnMinimize へ TwoWay 反映されバインディングエラーが発生しないことを検証します。
    /// </summary>
    [Fact]
    public void 一般カテゴリ_最小化時コンパクトプレイヤー非表示チェックボックス操作時_ViewModelへ双方向反映されバインディングエラーがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "一般";

            var sut = new SettingsDialog(vm);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var checkBox = FindLogicalChildren<CheckBox>(sut)
                .Single(cb => Equals(cb.Content, "画面最小化時にコンパクトプレイヤーを表示しない"));

            // Act
            Assert.False(checkBox.IsChecked);
            checkBox.IsChecked = true;
            rootContent.UpdateLayout();

            // Assert
            Assert.True(vm.DisableMiniPlayerOnMinimize);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// 一般カテゴリにミニプレイヤーのデザイン選択コンボボックスとアルバムアート回転チェックボックスが配置されており、
    /// 正方形デザイン選択時のみ回転チェックボックスが有効化され、双方向反映およびバインディングエラー0件であることを検証します。
    /// </summary>
    [Fact]
    public void 一般カテゴリ_ミニプレイヤーデザインと回転設定操作時_ViewModelへ双方向反映され有効状態が連動しバインディングエラーがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "一般";

            var sut = new SettingsDialog(vm);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var designComboBox = Assert.IsType<ComboBox>(sut.FindName("MiniPlayerDesignStyleComboBox"));
            var rotateCheckBox = Assert.IsType<CheckBox>(sut.FindName("MiniPlayerRotateAlbumArtCheckBox"));

            // Act & Assert: 初期状態は横長（標準）のため回転チェックボックスは無効
            Assert.Equal("横長（標準）", designComboBox.SelectedItem);
            Assert.False(rotateCheckBox.IsEnabled);
            Assert.True(rotateCheckBox.IsChecked);

            // Act: 正方形デザインへ切替 → 回転チェックボックスが有効化される
            designComboBox.SelectedItem = "正方形（アルバムアート）";
            rootContent.UpdateLayout();
            Assert.Equal("正方形（アルバムアート）", vm.SelectedMiniPlayerDesignStyle);
            Assert.True(rotateCheckBox.IsEnabled);

            // Act: 回転チェックボックスを OFF へ変更
            rotateCheckBox.IsChecked = false;
            rootContent.UpdateLayout();

            // Assert
            Assert.False(vm.MiniPlayerRotateAlbumArt);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }
}
