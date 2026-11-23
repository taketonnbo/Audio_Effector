using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="MiniPlayerWindow"/> の最大化ショートカット（Win+↑ / Aero Snap / SC_MAXIMIZE）有効化・抑止および通常位置保護挙動を検証するテストクラス
/// </summary>
public sealed class MiniPlayerWindowTests
{
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_THICKFRAME = 0x00040000;

    private const int WM_SIZE = 0x0005;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SYSCOMMAND = 0x0112;

    private const int HTCLIENT = 1;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE = 0xF120;
    private const int SIZE_MAXIMIZED = 2;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

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
    /// ウィンドウハンドル初期化時、Win+↑ (Aero Snap) を受信可能にする WS_MAXIMIZEBOX と WS_THICKFRAME が付与され、サイズは 350x100 を維持することを検証します。
    /// </summary>
    [Fact]
    public void OnSourceInitialized_初期化時_WinUp受信用のウィンドウスタイルが付与されサイズを維持すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                // Act
                sut.Show();
                IntPtr hwnd = new WindowInteropHelper(sut).Handle;
                long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();

                // Assert
                Assert.True((style & WS_MAXIMIZEBOX) != 0, "Win+↑ を受信するため WS_MAXIMIZEBOX が付与されているべきです。");
                Assert.True((style & WS_THICKFRAME) != 0, "Win+↑ (Aero Snap) を受信するため WS_THICKFRAME が付与されているべきです。");
                Assert.Equal(new IntPtr(HTCLIENT), SendMessage(hwnd, WM_NCHITTEST, IntPtr.Zero, IntPtr.Zero));
                Assert.Equal(350, sut.ActualWidth);
                Assert.Equal(100, sut.ActualHeight);
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// OSから SC_MAXIMIZE（システムコマンド最大化）を受信した際、ミニプレイヤーが最大化されず閉じられ、通常位置が保持されたまま保存されることを検証します。
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
    /// Win+↑ (Aero Snap) 経路で WM_SIZE(SIZE_MAXIMIZED) を直接受信した際も、ミニプレイヤーが閉じられ通常位置が保存されることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_WM_SIZEでSIZE_MAXIMIZED受信時_ミニプレイヤーを閉じ通常位置を保存すること()
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
                Left = 130,
                Top = 170,
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
                SendMessage(hwnd, WM_SIZE, new IntPtr(SIZE_MAXIMIZED), IntPtr.Zero);
                PumpDispatcher();

                // Assert
                Assert.True(closed, "WM_SIZE(SIZE_MAXIMIZED) 受信時に MiniPlayerWindow が閉じられるべきです。");
                Assert.Equal(170, savedSettings.MiniPlayerTop);
                Assert.Equal(130, savedSettings.MiniPlayerLeft);
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
    /// Win+↑ (Aero Snap) 等により想定サイズを超える WM_WINDOWPOSCHANGING を受信した際、拡大フラグを抑止してミニプレイヤーが閉じられることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_WM_WINDOWPOSCHANGINGで拡大要求受信時_サイズ変更を抑止しミニプレイヤーを閉じること()
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
                Left = 110,
                Top = 160,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            bool closed = false;
            sut.Closed += (_, _) => closed = true;

            IntPtr wpPtr = IntPtr.Zero;
            try
            {
                sut.Show();
                IntPtr hwnd = new WindowInteropHelper(sut).Handle;

                var wp = new WINDOWPOS
                {
                    Hwnd = hwnd,
                    X = 0,
                    Y = 0,
                    Cx = 1920,
                    Cy = 1080,
                    Flags = 0
                };
                wpPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINDOWPOS>());
                Marshal.StructureToPtr(wp, wpPtr, false);

                // Act
                SendMessage(hwnd, WM_WINDOWPOSCHANGING, IntPtr.Zero, wpPtr);
                var updatedWp = Marshal.PtrToStructure<WINDOWPOS>(wpPtr);
                PumpDispatcher();

                // Assert
                Assert.True((updatedWp.Flags & SWP_NOSIZE) != 0, "想定サイズ超過時は SWP_NOSIZE で拡大を抑止すべきです。");
                Assert.True((updatedWp.Flags & SWP_NOMOVE) != 0, "想定サイズ超過時は SWP_NOMOVE で位置移動を抑止すべきです。");
                Assert.True(closed, "拡大要求受信時に MiniPlayerWindow が閉じられるべきです。");
                Assert.Equal(160, savedSettings.MiniPlayerTop);
                Assert.Equal(110, savedSettings.MiniPlayerLeft);
            }
            finally
            {
                if (wpPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(wpPtr);
                }

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

    private sealed class FakeShortcutTarget : IShortcutCommandTarget
    {
        public bool IsPlaying { get; set; }
        public int TogglePlayPauseCount { get; private set; }
        public int StopCount { get; private set; }
        public int NextCount { get; private set; }
        public int PreviousCount { get; private set; }
        public int ToggleMuteCount { get; private set; }
        public int IncreaseVolumeCount { get; private set; }
        public int DecreaseVolumeCount { get; private set; }

        public ICommand TogglePlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand IncreaseVolumeCommand { get; }
        public ICommand DecreaseVolumeCommand { get; }

        public FakeShortcutTarget()
        {
            TogglePlayPauseCommand = new RelayCommand(_ =>
            {
                TogglePlayPauseCount++;
                IsPlaying = !IsPlaying;
            });
            StopCommand = new RelayCommand(_ => StopCount++);
            NextCommand = new RelayCommand(_ => NextCount++);
            PreviousCommand = new RelayCommand(_ => PreviousCount++);
            ToggleMuteCommand = new RelayCommand(_ => ToggleMuteCount++);
            IncreaseVolumeCommand = new RelayCommand(_ => IncreaseVolumeCount++);
            DecreaseVolumeCommand = new RelayCommand(_ => DecreaseVolumeCount++);
        }
    }

    /// <summary>
    /// ミニプレイヤー表示中に WM_APPCOMMAND（再生/一時停止・次の曲）を受信した際、対応するコマンドが実行されることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_WM_APPCOMMAND受信時_対応するメディアコマンドを実行すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            var target = new FakeShortcutTarget();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = target,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                sut.Show();
                IntPtr hwnd = new WindowInteropHelper(sut).Handle;

                // Act
                SendMessage(hwnd, ShortcutKeyHelper.WM_APPCOMMAND, hwnd, new IntPtr(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE << 16));
                SendMessage(hwnd, ShortcutKeyHelper.WM_APPCOMMAND, hwnd, new IntPtr(ShortcutKeyHelper.APPCOMMAND_MEDIA_NEXTTRACK << 16));

                // Assert
                Assert.Equal(1, target.TogglePlayPauseCount);
                Assert.Equal(1, target.NextCount);
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// ミニプレイヤーで設定済みカスタムショートカット（Space / 単一キー）が PreviewKeyDown 経由で発火することを検証します。
    /// </summary>
    [Fact]
    public void OnPreviewKeyDown_設定済みカスタムショートカット押下時_対応するコマンドを実行すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settings = new AppSettings
            {
                PlayPauseShortcut = new ShortcutKeyConfig { Key = Key.Space, Modifiers = ModifierKeys.None },
                MuteShortcut = new ShortcutKeyConfig { Key = Key.M, Modifiers = ModifierKeys.None }
            };
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(settings);

            var target = new FakeShortcutTarget();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = target,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            try
            {
                sut.Show();
                var presentationSource = PresentationSource.FromVisual(sut);
                Assert.NotNull(presentationSource);

                var spaceArgs = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource!, 0, Key.Space)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                var mArgs = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource!, 0, Key.M)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };

                // Act
                sut.RaiseEvent(spaceArgs);
                sut.RaiseEvent(mArgs);

                // Assert
                Assert.True(spaceArgs.Handled);
                Assert.True(mArgs.Handled);
                Assert.Equal(1, target.TogglePlayPauseCount);
                Assert.Equal(1, target.ToggleMuteCount);
            }
            finally
            {
                sut.Close();
            }
        });
    }
}
