using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="DeviceManagerDialog"/> のダーク／ライト両テーマでの描画整合、逆転送進捗パネル可視性、ProgressBar OneWay バインディング、および中止ボタン活性遷移を検証するテストクラス
/// </summary>
public sealed class DeviceManagerDialogTests
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
    /// DeviceManagerDialog をダーク／ライト両テーマで描画し、IsImporting 遷移時にバインディングエラー 0 件で
    /// 逆転送進捗パネル・ProgressBar・中止ボタン活性が連動することを検証します
    /// </summary>
    [Theory]
    [InlineData("Presentation/Themes/DarkTheme.xaml")]
    [InlineData("Presentation/Themes/LightTheme.xaml")]
    public void DeviceManagerDialog_逆転送状態遷移時_バインディングエラー0件で進捗パネルと中止ボタンが連動すること(string themePath)
    {
        RunInStaThread(() =>
        {
            // Arrange
            using var listener = new BindingErrorTraceListener();
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AudioEffector;component/{themePath}", UriKind.Absolute)
            };

            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = string.Empty
            };

            using var vm = new DeviceManagerViewModel(device, string.Empty, new List<Album>());

            var sut = new DeviceManagerDialog(vm)
            {
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Opacity = 0
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                sut.Show();
                sut.UpdateLayout();

                var importPanel = Assert.IsType<Border>(sut.FindName("ImportProgressPanel"));
                var progressBar = Assert.IsType<ProgressBar>(sut.FindName("ImportProgressBar"));
                var cancelButton = Assert.IsType<Button>(sut.FindName("CancelImportButton"));

                // Assert 1: 初期状態（非逆転送）は進捗パネル非表示・中止ボタン非活性
                Assert.Equal(Visibility.Collapsed, importPanel.Visibility);
                Assert.False(cancelButton.IsEnabled);

                // Act: 逆転送中へ遷移
                vm.IsImporting = true;
                vm.ImportProgress = 64.0;
                sut.UpdateLayout();

                // Assert 2: 逆転送中は進捗パネル表示・ProgressBar 反映・中止ボタン活性
                Assert.Equal(Visibility.Visible, importPanel.Visibility);
                Assert.Equal(64.0, progressBar.Value);
                Assert.True(cancelButton.IsEnabled);
                Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                vm.IsImporting = false;
                sut.Close();
            }
        });
    }
}
