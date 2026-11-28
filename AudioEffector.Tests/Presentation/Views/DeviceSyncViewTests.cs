using System;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="DeviceSyncView"/> の実 WPF 描画・詳細進捗パネル・ProgressBar OneWay バインディング・中止ボタン活性遷移を検証するテストクラス
/// </summary>
public sealed class DeviceSyncViewTests
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
    /// DeviceSyncView をダーク／ライト両テーマで実 WPF ホストウィンドウ上へ描画した際、
    /// バインディングエラー 0 件で詳細進捗パネルと中止ボタンの可視性・活性状態が IsTransferring に連動することを検証します
    /// </summary>
    [Theory]
    [InlineData("Presentation/Themes/DarkTheme.xaml")]
    [InlineData("Presentation/Themes/LightTheme.xaml")]
    public void DeviceSyncView_転送状態遷移時_バインディングエラー0件で詳細進捗パネルと中止ボタンが連動すること(string themePath)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AudioEffector;component/{themePath}", UriKind.Absolute)
            };

            var albums = new ObservableCollection<Album>();
            using var vm = new DeviceBrowserViewModel(albums, () => albums, () => { });

            var appResources = System.Windows.Application.Current!.Resources;
            bool hadBoolToVis = appResources.Contains("BoolToVis");
            object? prevBoolToVis = hadBoolToVis ? appResources["BoolToVis"] : null;
            bool hadInvertBool = appResources.Contains("InvertBool");
            object? prevInvertBool = hadInvertBool ? appResources["InvertBool"] : null;

            appResources["BoolToVis"] = new BoolToVisConverter();
            appResources["InvertBool"] = new InvertBoolConverter();

            Window? hostWindow = null;
            try
            {
                var sut = new DeviceSyncView { DataContext = vm };
                sut.Resources.MergedDictionaries.Add(themeDict);

                hostWindow = new Window
                {
                    Width = 800,
                    Height = 700,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0,
                    Content = sut
                };

                hostWindow.Show();
                hostWindow.UpdateLayout();

                var progressPanel = Assert.IsType<Border>(sut.FindName("TransferProgressPanel"));
                var progressBar = Assert.IsType<ProgressBar>(sut.FindName("TransferProgressBar"));
                var cancelButton = Assert.IsType<Button>(sut.FindName("CancelTransferButton"));

                // Assert 1: 初期状態（非転送）はパネル非表示・中止ボタン非活性
                Assert.Equal(Visibility.Collapsed, progressPanel.Visibility);
                Assert.False(cancelButton.IsEnabled);

                // Act: 転送中へ遷移
                vm.IsTransferring = true;
                vm.TransferProgress = 55.0;
                hostWindow.UpdateLayout();

                // Assert 2: 転送中はパネル表示・プログレスバー反映・中止ボタン活性
                Assert.Equal(Visibility.Visible, progressPanel.Visibility);
                Assert.Equal(55.0, progressBar.Value);
                Assert.True(cancelButton.IsEnabled);
                Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                hostWindow?.Close();
                if (hadBoolToVis)
                {
                    appResources["BoolToVis"] = prevBoolToVis;
                }
                else
                {
                    appResources.Remove("BoolToVis");
                }

                if (hadInvertBool)
                {
                    appResources["InvertBool"] = prevInvertBool;
                }
                else
                {
                    appResources.Remove("InvertBool");
                }
            }
        });
    }
}
