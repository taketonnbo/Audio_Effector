using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AudioEffector.Application.Common;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="TransferResultDialog"/> の成功・キャンセル・失敗曲一覧・致命的エラー表示およびダーク／ライト両テーマでの描画整合を検証するテストクラス
/// </summary>
public sealed class TransferResultDialogTests
{
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
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
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
            ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    /// <summary>
    /// 全曲成功（失敗曲なし）の結果サマリ表示時に、失敗一覧および致命的エラー領域が非表示で件数が正しく表示されることを検証します
    /// </summary>
    [Fact]
    public void 全曲成功表示_失敗一覧なしサマリ指定時_件数が表示されエラー領域が非表示であること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var summary = new TransferResultSummary(successCount: 12, skippedCount: 3);
            var sut = new TransferResultDialog(summary);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);

            // Act
            rootContent.Measure(new Size(560, 500));
            rootContent.Arrange(new Rect(0, 0, 560, 500));
            rootContent.UpdateLayout();

            var failedBorder = Assert.IsType<Border>(sut.FindName("FailedItemsBorder"));
            var fatalBorder = Assert.IsType<Border>(sut.FindName("FatalErrorBorder"));
            var successText = Assert.IsType<TextBlock>(sut.FindName("SuccessCountTextBlock"));
            var skippedText = Assert.IsType<TextBlock>(sut.FindName("SkippedCountTextBlock"));
            var closeButton = Assert.IsType<Button>(sut.FindName("CloseButton"));

            // Assert
            Assert.Equal(Visibility.Collapsed, failedBorder.Visibility);
            Assert.Equal(Visibility.Collapsed, fatalBorder.Visibility);
            Assert.Equal("12 曲", successText.Text);
            Assert.Equal("3 曲", skippedText.Text);
            closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// 失敗曲一覧および致命的エラーが存在するサマリをダーク／ライト両テーマで描画した際、
    /// バインディングエラー 0 件で失敗一覧とエラーメッセージが表示されることを検証します
    /// </summary>
    [Theory]
    [InlineData("Presentation/Themes/DarkTheme.xaml")]
    [InlineData("Presentation/Themes/LightTheme.xaml")]
    public void 失敗およびテーマ適用_ダークおよびライトテーマ描画時_バインディングエラー0件で失敗一覧とボタンブラシが適用されること(string themePath)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AudioEffector;component/{themePath}", UriKind.Absolute)
            };

            var failedItems = new List<TransferFailedItem>
            {
                new("Broken Track 1", "01.flac", "変換エラー"),
                new("Missing Track 2", "02.mp3", "ファイルが見つかりません")
            };
            var summary = new TransferResultSummary(
                successCount: 5,
                skippedCount: 1,
                failedItems: failedItems,
                wasAbortedByFatalError: true,
                fatalErrorMessage: "デバイスが切断されました");

            var sut = new TransferResultDialog(summary);
            sut.Resources.MergedDictionaries.Add(themeDict);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);

            // Act
            rootContent.Measure(new Size(560, 600));
            rootContent.Arrange(new Rect(0, 0, 560, 600));
            rootContent.UpdateLayout();

            var failedBorder = Assert.IsType<Border>(sut.FindName("FailedItemsBorder"));
            var fatalBorder = Assert.IsType<Border>(sut.FindName("FatalErrorBorder"));
            var failedListBox = Assert.IsType<ListBox>(sut.FindName("FailedItemsListBox"));
            var closeButton = Assert.IsType<Button>(sut.FindName("CloseButton"));

            // Assert
            Assert.Equal(Visibility.Visible, failedBorder.Visibility);
            Assert.Equal(Visibility.Visible, fatalBorder.Visibility);
            Assert.Equal(2, failedListBox.Items.Count);
            Assert.Same(themeDict["NeonCyanBrush"], closeButton.Background);
            Assert.Same(themeDict["InverseTextForegroundBrush"], closeButton.Foreground);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }
}
