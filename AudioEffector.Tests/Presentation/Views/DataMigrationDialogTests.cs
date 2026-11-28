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
/// <see cref="DataMigrationDialog"/> の各ボタン選択結果、移行先既存データ警告表示、およびダーク／ライト両テーマでの描画整合を検証するテストクラス
/// </summary>
public sealed class DataMigrationDialogTests
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
    /// 移行先に既存データがない場合、警告が非表示で「コピーして切り替え」が既定ボタンとなり、各ボタン操作で対応する SelectedChoice が設定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("CopyButton", DataMigrationChoice.Copy)]
    [InlineData("MoveButton", DataMigrationChoice.Move)]
    [InlineData("SwitchOnlyButton", DataMigrationChoice.SwitchWithoutMigration)]
    [InlineData("CancelButton", DataMigrationChoice.Cancel)]
    public void ボタン操作_各移行選択肢押下時_対応するSelectedChoiceが設定されること(string buttonName, DataMigrationChoice expectedChoice)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var sut = new DataMigrationDialog(@"C:\SourceData", @"D:\TargetData", destinationHasExistingData: false);
            var warningBorder = Assert.IsType<Border>(sut.FindName("DestinationOverwriteWarningBorder"));
            var copyButton = Assert.IsType<Button>(sut.FindName("CopyButton"));
            var targetButton = Assert.IsType<Button>(sut.FindName(buttonName));

            // Act
            Assert.Equal(Visibility.Collapsed, warningBorder.Visibility);
            Assert.True(copyButton.IsDefault);
            targetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // Assert
            Assert.Equal(expectedChoice, sut.SelectedChoice);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }

    /// <summary>
    /// 移行先に既存データが存在する場合、上書き警告が表示され既定ボタンが「移行せず切り替え」へ切り替わることを検証します。
    /// </summary>
    [Fact]
    public void 移行先既存データあり_ダイアログ初期化時_警告が表示され移行せず切り替えが既定ボタンになること()
    {
        RunInStaThread(() =>
        {
            // Arrange & Act
            var sut = new DataMigrationDialog(@"C:\SourceData", @"D:\CloudData", destinationHasExistingData: true);
            var warningBorder = Assert.IsType<Border>(sut.FindName("DestinationOverwriteWarningBorder"));
            var copyButton = Assert.IsType<Button>(sut.FindName("CopyButton"));
            var switchOnlyButton = Assert.IsType<Button>(sut.FindName("SwitchOnlyButton"));

            // Assert
            Assert.True(sut.DestinationHasExistingData);
            Assert.Equal(Visibility.Visible, warningBorder.Visibility);
            Assert.False(copyButton.IsDefault);
            Assert.True(switchOnlyButton.IsDefault);
        });
    }

    /// <summary>
    /// ダーク・ライト両テーマのリソース辞書適用状態でレイアウト計測してもバインディングエラーが発生せず、プライマリボタンへ InverseTextForegroundBrush が適用されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("Presentation/Themes/DarkTheme.xaml")]
    [InlineData("Presentation/Themes/LightTheme.xaml")]
    public void テーマ適用_ダークおよびライトテーマ描画時_バインディングエラー0件でInverseTextForegroundBrushが適用されること(string themePath)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AudioEffector;component/{themePath}", UriKind.Absolute)
            };

            var sut = new DataMigrationDialog(@"C:\SourceData", @"D:\TargetData", destinationHasExistingData: false);
            sut.Resources.MergedDictionaries.Add(themeDict);

            var rootContent = Assert.IsAssignableFrom<FrameworkElement>(sut.Content);

            // Act
            rootContent.Measure(new Size(540, 400));
            rootContent.Arrange(new Rect(0, 0, 540, 400));
            rootContent.UpdateLayout();

            var copyButton = Assert.IsType<Button>(sut.FindName("CopyButton"));

            // Assert
            Assert.Same(themeDict["InverseTextForegroundBrush"], copyButton.Foreground);
            Assert.Same(themeDict["NeonCyanBrush"], copyButton.Background);
            Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
        });
    }
}
