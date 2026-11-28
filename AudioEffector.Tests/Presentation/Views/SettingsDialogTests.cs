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
    [InlineData("データ管理・その他")]
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
    /// プレースホルダー表示となる未定義カテゴリを選択した際、バインディングエラーが発生せず不要なスクロール領域が生じないことを検証します。
    /// </summary>
    [Fact]
    public void プレースホルダーカテゴリ_未定義カテゴリ選択時_バインディングエラーがなく不要なスクロールが出ないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "未実装カテゴリ";

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

    /// <summary>
    /// データ管理・その他カテゴリに「転送時に MP4 を M4A に変換する」チェックボックスが配置されており、
    /// チェック操作が SettingsViewModel.ConvertMp4ToM4a へ TwoWay 反映されバインディングエラーが発生しないことを検証します。
    /// </summary>
    [Fact]
    public void データ管理その他カテゴリ_MP4からM4A変換チェックボックス操作時_ViewModelへ双方向反映されバインディングエラーがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "データ管理・その他";

            var sut = new SettingsDialog(vm);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var checkBox = FindLogicalChildren<CheckBox>(sut)
                .Single(cb => Equals(cb.Content, "転送時に MP4 を M4A に変換する"));

            // Act
            Assert.False(checkBox.IsChecked);
            checkBox.IsChecked = true;
            rootContent.UpdateLayout();

            // Assert
            Assert.True(vm.ConvertMp4ToM4a);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// データ管理・その他カテゴリに「転送時に FLAC を MP3 に変換してデータ量を削減する」チェックボックスが配置されており、
    /// チェック操作が SettingsViewModel.ConvertFlacToMp3 へ TwoWay 反映されバインディングエラーが発生しないことを検証します。
    /// </summary>
    [Fact]
    public void データ管理その他カテゴリ_FLACからMP3変換チェックボックス操作時_ViewModelへ双方向反映されバインディングエラーがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "データ管理・その他";

            var sut = new SettingsDialog(vm);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var checkBox = FindLogicalChildren<CheckBox>(sut)
                .Single(cb => Equals(cb.Content, "転送時に FLAC を MP3 に変換してデータ量を削減する"));

            // Act
            Assert.False(checkBox.IsChecked);
            checkBox.IsChecked = true;
            rootContent.UpdateLayout();

            // Assert
            Assert.True(vm.ConvertFlacToMp3);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// データ管理・その他カテゴリにデータ保存先パス表示（読み取り専用）と「参照...」「リセット」ボタンが配置され、バインディングエラー 0 件で描画されることを検証します。
    /// </summary>
    [Fact]
    public void データ管理その他カテゴリ_データ保存先設定コントロール配置時_読み取り専用表示とコマンド接続が正常でバインディングエラーがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var vm = CreateViewModel();
            vm.SelectedCategory = "データ管理・その他";

            var sut = new SettingsDialog(vm);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);

            // Act
            rootContent.Measure(new Size(760, 520));
            rootContent.Arrange(new Rect(0, 0, 760, 520));
            rootContent.UpdateLayout();

            var pathTextBox = Assert.IsType<TextBox>(sut.FindName("DataDirectoryPathTextBox"));
            var browseButton = Assert.IsType<Button>(sut.FindName("BrowseDataDirectoryButton"));
            var resetButton = Assert.IsType<Button>(sut.FindName("ResetDataDirectoryButton"));

            // Assert
            Assert.True(pathTextBox.IsReadOnly);
            Assert.Equal(vm.DataDirectoryPath, pathTextBox.Text);
            Assert.Same(vm.BrowseDataDirectoryCommand, browseButton.Command);
            Assert.Same(vm.ResetDataDirectoryCommand, resetButton.Command);
            Assert.False(resetButton.IsEnabled);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// 実 WPF ウィンドウ上でデータ保存先をカスタムパスへ変更およびリセットした直後、
    /// 追加のユーザー入力なしで ResetDataDirectoryButton.IsEnabled が false→true→false へ即時遷移することを検証します。
    /// </summary>
    [Fact]
    public void データ管理その他カテゴリ_保存先変更およびリセット直後_リセットボタンのIsEnabledが即時遷移すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
            var audioServiceMock = new Mock<IAudioService>();
            var vm = new SettingsViewModel(
                settingsServiceMock.Object,
                audioServiceMock.Object,
                dataDirectoryProvider: null,
                dataDirectoryMigrationService: null,
                folderPicker: _ => @"D:\CustomAudioData",
                migrationPrompt: (_, _, _) => DataMigrationChoice.Copy,
                messageNotifier: (_, _, _) => { });
            vm.SelectedCategory = "データ管理・その他";

            var sut = new SettingsDialog(vm)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };

            try
            {
                sut.Show();
                sut.UpdateLayout();

                var pathTextBox = Assert.IsType<TextBox>(sut.FindName("DataDirectoryPathTextBox"));
                var resetButton = Assert.IsType<Button>(sut.FindName("ResetDataDirectoryButton"));
                Assert.False(resetButton.IsEnabled);

                // Act 1: カスタムフォルダへ変更
                vm.BrowseDataDirectoryCommand.Execute(null);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
                sut.UpdateLayout();

                // Assert 1
                Assert.Equal(@"D:\CustomAudioData", pathTextBox.Text);
                Assert.True(resetButton.IsEnabled);

                // Act 2: リセット実行
                vm.ResetDataDirectoryCommand.Execute(null);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
                sut.UpdateLayout();

                // Assert 2
                Assert.False(resetButton.IsEnabled);
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// データ管理・その他カテゴリにログレベル選択・ログ保存先パス表示・参照/リセット/フォルダを開くボタンが配置され、
    /// 実 WPF ウィンドウ上でカスタム変更・リセット時の ResetLogDirectoryButton.IsEnabled 遷移およびバインディングエラー 0 件であることを検証します。
    /// </summary>
    [Fact]
    public void データ管理その他カテゴリ_ログ出力設定操作時_双方向反映とリセットボタン活性遷移が正常でバインディングエラーがないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
            var audioServiceMock = new Mock<IAudioService>();
            var vm = new SettingsViewModel(
                settingsServiceMock.Object,
                audioServiceMock.Object,
                dataDirectoryProvider: null,
                dataDirectoryMigrationService: null,
                folderPicker: _ => @"D:\CustomLogFolder",
                migrationPrompt: null,
                messageNotifier: (_, _, _) => { },
                logConfigurationManager: null,
                folderOpener: _ => { });
            vm.SelectedCategory = "データ管理・その他";

            var sut = new SettingsDialog(vm)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };

            try
            {
                sut.Show();
                sut.UpdateLayout();

                var levelComboBox = Assert.IsType<ComboBox>(sut.FindName("LogLevelComboBox"));
                var logPathTextBox = Assert.IsType<TextBox>(sut.FindName("LogDirectoryPathTextBox"));
                var browseLogButton = Assert.IsType<Button>(sut.FindName("BrowseLogDirectoryButton"));
                var resetLogButton = Assert.IsType<Button>(sut.FindName("ResetLogDirectoryButton"));
                var openLogButton = Assert.IsType<Button>(sut.FindName("OpenLogFolderButton"));

                Assert.Equal("Info", levelComboBox.SelectedItem);
                Assert.True(logPathTextBox.IsReadOnly);
                Assert.Same(vm.BrowseLogDirectoryCommand, browseLogButton.Command);
                Assert.Same(vm.ResetLogDirectoryCommand, resetLogButton.Command);
                Assert.Same(vm.OpenLogFolderCommand, openLogButton.Command);
                Assert.False(resetLogButton.IsEnabled);

                // Act 1: ログレベル変更 + カスタムログ保存先変更
                levelComboBox.SelectedItem = "Debug";
                vm.BrowseLogDirectoryCommand.Execute(null);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
                sut.UpdateLayout();

                // Assert 1
                Assert.Equal("Debug", vm.SelectedLogLevel);
                Assert.Equal(@"D:\CustomLogFolder", logPathTextBox.Text);
                Assert.True(resetLogButton.IsEnabled);

                // Act 2: ログ保存先リセット
                vm.ResetLogDirectoryCommand.Execute(null);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
                sut.UpdateLayout();

                // Assert 2
                Assert.False(resetLogButton.IsEnabled);
                Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                sut.Close();
            }
        });
    }
}
