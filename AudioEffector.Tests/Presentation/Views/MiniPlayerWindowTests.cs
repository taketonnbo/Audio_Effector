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
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE = 0xF120;

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
                SendMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MAXIMIZE), IntPtr.Zero);
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
    /// 下位4ビットが付加された SC_MAXIMIZE（SC_MAXIMIZE | 0x0002）を受信した際も、0xFFF0 マスク判定で正しく最大化を抑止し閉じられることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_SC_MAXIMIZE下位ビット付き受信時_マスク判定で最大化を抑止し閉じること()
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
                Left = 140,
                Top = 190,
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
                SendMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MAXIMIZE | 0x0002), IntPtr.Zero);
                PumpDispatcher();

                // Assert
                Assert.NotEqual(WindowState.Maximized, sut.WindowState);
                Assert.True(closed, "SC_MAXIMIZE | 0x0002 受信時に MiniPlayerWindow が閉じられるべきです。");
                Assert.Equal(190, savedSettings.MiniPlayerTop);
                Assert.Equal(140, savedSettings.MiniPlayerLeft);
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
    /// 最大化以外のシステムコマンド（SC_RESTORE 等）を受信した際、誤ってミニプレイヤーが閉じられないことを検証します。
    /// </summary>
    [Fact]
    public void WndProc_最大化以外のシステムコマンド受信時_ミニプレイヤーが閉じられないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 150,
                Top = 210,
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
                SendMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_RESTORE), IntPtr.Zero);
                PumpDispatcher();

                // Assert
                Assert.False(closed, "最大化以外の WM_SYSCOMMAND では閉じられてはなりません。");
                Assert.Equal(WindowState.Normal, sut.WindowState);
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
    /// WindowState が直接 Maximized へ変更された場合でも、即座に Normal へ戻され直前の通常位置を保持して閉じられることを検証します。
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
    /// WindowState.Minimized 等の非 Normal 状態で閉じられた際、異常座標ではなく直前の通常位置がフォールバック保存されることを検証します。
    /// </summary>
    [Fact]
    public void OnClosed_非Normal状態Minimizedで閉じられた場合_直前の通常位置がフォールバック保存されること()
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
                Left = 160,
                Top = 220,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            bool closed = false;
            sut.Closed += (_, _) => closed = true;

            try
            {
                sut.Show();
                sut.WindowState = WindowState.Minimized;
                PumpDispatcher();

                // Act
                sut.Close();

                // Assert
                Assert.True(closed);
                Assert.Equal(220, savedSettings.MiniPlayerTop);
                Assert.Equal(160, savedSettings.MiniPlayerLeft);
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
                SendMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MAXIMIZE), IntPtr.Zero);
                SendMessage(hwnd, WM_SYSCOMMAND, new IntPtr(SC_MAXIMIZE), IntPtr.Zero);
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
