using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="MiniPlayerWindow"/> の最大化ショートカット（Win+↑ / SC_MAXIMIZE）抑止および通常位置保護挙動を検証するテストクラス
/// </summary>
public sealed class MiniPlayerWindowTests
{
    private const int WmSysCommand = 0x0112;
    private const int ScMaximize = 0xF030;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
                // STAスレッド終了前にDispatcherをシャットダウンし、後続テストのハングを防止する
                Dispatcher.CurrentDispatcher.InvokeShutdown();
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

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// OSから SC_MAXIMIZE（Win+↑等）を受信した際、ミニプレイヤーが最大化されず閉じられ、通常位置が保持されたまま保存されることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_SC_MAXIMIZE受信時_最大化を抑止してミニプレイヤーを閉じ通常位置を保存すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var savedSettings = new AppSettings();
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(() => savedSettings);
            settingsServiceMock.Setup(s => s.SaveSettings(It.IsAny<AppSettings>()))
                .Callback<AppSettings>(s => savedSettings = s);

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 120,
                Top = 180,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            bool closed = false;
            sut.Closed += (_, _) => closed = true;

            try
            {
                sut.Show();
                IntPtr hwnd = new WindowInteropHelper(sut).Handle;

                // Act
                SendMessage(hwnd, WmSysCommand, new IntPtr(ScMaximize), IntPtr.Zero);
                PumpDispatcher();

                // Assert
                Assert.NotEqual(WindowState.Maximized, sut.WindowState);
                Assert.True(closed, "SC_MAXIMIZE 受信時に MiniPlayerWindow が閉じられるべきです。");
                Assert.Equal(180, savedSettings.MiniPlayerTop);
                Assert.Equal(120, savedSettings.MiniPlayerLeft);
            }
            finally
            {
                if (!closed)
                {
                    sut.Close();
                }
            }
        });
    }

    /// <summary>
    /// WindowState が直接 Maximized へ変更された場合でも、即座に Normal へ戻されミニプレイヤーが閉じられることを検証します。
    /// </summary>
    [Fact]
    public void OnStateChanged_WindowStateがMaximizedに変更された場合_Normalへ戻しミニプレイヤーを閉じること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var savedSettings = new AppSettings();
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(() => savedSettings);
            settingsServiceMock.Setup(s => s.SaveSettings(It.IsAny<AppSettings>()))
                .Callback<AppSettings>(s => savedSettings = s);

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 200,
                Top = 250,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            bool closed = false;
            sut.Closed += (_, _) => closed = true;

            try
            {
                sut.Show();

                // Act
                sut.WindowState = WindowState.Maximized;
                PumpDispatcher();

                // Assert
                Assert.Equal(WindowState.Normal, sut.WindowState);
                Assert.True(closed, "WindowState.Maximized 遷移時に MiniPlayerWindow が閉じられるべきです。");
                Assert.Equal(250, savedSettings.MiniPlayerTop);
                Assert.Equal(200, savedSettings.MiniPlayerLeft);
            }
            finally
            {
                if (!closed)
                {
                    sut.Close();
                }
            }
        });
    }

    /// <summary>
    /// SC_MAXIMIZE が連続送信されても多重 Close 例外が発生せず安全に閉じられることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_SC_MAXIMIZEを連続受信した場合_例外なく一度だけ閉じられること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false
            };

            int closedCount = 0;
            sut.Closed += (_, _) => closedCount++;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(sut).EnsureHandle();
                sut.Left = 100;
                sut.Top = 100;

                // Act
                SendMessage(hwnd, WmSysCommand, new IntPtr(ScMaximize), IntPtr.Zero);
                SendMessage(hwnd, WmSysCommand, new IntPtr(ScMaximize), IntPtr.Zero);
                PumpDispatcher();

                // Assert
                Assert.Equal(1, closedCount);
                Assert.NotEqual(WindowState.Maximized, sut.WindowState);
            }
            finally
            {
                if (closedCount == 0)
                {
                    sut.Close();
                }
            }
        });
    }
}
