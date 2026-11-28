using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="TransferConfirmationDialog"/> の容量不足警告・形式変換確認・統合表示、ボタン操作、およびダーク／ライト両テーマでの描画整合を検証するテストクラス
/// </summary>
public sealed class TransferConfirmationDialogTests
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

    /// <summary>
    /// 形式変換のみ該当する場合、変換確認セクションのみ表示され、転送開始ボタンが既定ボタンとなり、
    /// 各ボタン押下で対応する IsConfirmed 状態が設定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("ProceedButton", true)]
    [InlineData("CancelButton", false)]
    public void ボタン操作_形式変換のみ確認時_期待されるセクション表示とIsConfirmedが設定されること(string buttonName, bool expectedConfirmed)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var request = new TransferConfirmationRequest(
                totalTrackCount: 10,
                requiredBytes: 100L * 1024 * 1024,
                hasStorageInfo: true,
                freeSpaceBytes: 500L * 1024 * 1024,
                mp4ToM4aCount: 2,
                flacToMp3Count: 5,
                copyOnlyCount: 3);

            var sut = new TransferConfirmationDialog(request);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);
            rootContent.Measure(new Size(520, 600));
            rootContent.Arrange(new Rect(0, 0, 520, 600));
            rootContent.UpdateLayout();

            var spaceBorder = Assert.IsType<Border>(sut.FindName("InsufficientSpaceWarningBorder"));
            var transcodeBorder = Assert.IsType<Border>(sut.FindName("TranscodeConfirmationBorder"));
            var proceedButton = Assert.IsType<Button>(sut.FindName("ProceedButton"));
            var cancelButton = Assert.IsType<Button>(sut.FindName("CancelButton"));
            var targetButton = Assert.IsType<Button>(sut.FindName(buttonName));

            // Act
            Assert.Equal(Visibility.Collapsed, spaceBorder.Visibility);
            Assert.Equal(Visibility.Visible, transcodeBorder.Visibility);
            Assert.True(proceedButton.IsDefault);
            Assert.False(cancelButton.IsDefault);
            targetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Assert
            Assert.Equal(expectedConfirmed, sut.IsConfirmed);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// 容量不足と形式変換の両方に該当する場合、両セクションが 1 つのダイアログ内に統合表示され、
    /// 安全側としてキャンセルボタンが既定ボタンへ切り替わることを検証します。
    /// </summary>
    [Fact]
    public void 統合表示_容量不足と形式変換が同時該当する場合_両セクションが表示されキャンセルが既定ボタンになること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var request = new TransferConfirmationRequest(
                totalTrackCount: 18,
                requiredBytes: 800L * 1024 * 1024,
                hasStorageInfo: true,
                freeSpaceBytes: 300L * 1024 * 1024,
                mp4ToM4aCount: 3,
                flacToMp3Count: 15,
                copyOnlyCount: 0);

            var sut = new TransferConfirmationDialog(request);
            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);

            // Act
            rootContent.Measure(new Size(520, 600));
            rootContent.Arrange(new Rect(0, 0, 520, 600));
            rootContent.UpdateLayout();

            var spaceBorder = Assert.IsType<Border>(sut.FindName("InsufficientSpaceWarningBorder"));
            var transcodeBorder = Assert.IsType<Border>(sut.FindName("TranscodeConfirmationBorder"));
            var estimatedNote = Assert.IsType<TextBlock>(sut.FindName("EstimatedSizeNoteTextBlock"));
            var proceedButton = Assert.IsType<Button>(sut.FindName("ProceedButton"));
            var cancelButton = Assert.IsType<Button>(sut.FindName("CancelButton"));

            // Assert
            Assert.Equal(Visibility.Visible, spaceBorder.Visibility);
            Assert.Equal(Visibility.Visible, transcodeBorder.Visibility);
            Assert.Equal(Visibility.Visible, estimatedNote.Visibility);
            Assert.False(proceedButton.IsDefault);
            Assert.True(cancelButton.IsDefault);
            Assert.Equal("そのまま転送を開始", proceedButton.Content);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// ダーク・ライト両テーマのリソース辞書適用状態でレイアウト計測してもバインディングエラーが発生せず、
    /// プライマリボタンへテーマブラシが正しく適用されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("Presentation/Themes/DarkTheme.xaml")]
    [InlineData("Presentation/Themes/LightTheme.xaml")]
    public void テーマ適用_ダークおよびライトテーマ描画時_バインディングエラー0件でテーマブラシが適用されること(string themePath)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AudioEffector;component/{themePath}", UriKind.Absolute)
            };

            var request = new TransferConfirmationRequest(
                totalTrackCount: 5,
                requiredBytes: 100L * 1024 * 1024,
                hasStorageInfo: true,
                freeSpaceBytes: 500L * 1024 * 1024,
                mp4ToM4aCount: 0,
                flacToMp3Count: 5,
                copyOnlyCount: 0);

            var sut = new TransferConfirmationDialog(request);
            sut.Resources.MergedDictionaries.Add(themeDict);

            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);

            // Act
            rootContent.Measure(new Size(520, 600));
            rootContent.Arrange(new Rect(0, 0, 520, 600));
            rootContent.UpdateLayout();

            var proceedButton = Assert.IsType<Button>(sut.FindName("ProceedButton"));

            // Assert
            Assert.Same(themeDict["InverseTextForegroundBrush"], proceedButton.Foreground);
            Assert.Same(themeDict["NeonCyanBrush"], proceedButton.Background);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }
}
