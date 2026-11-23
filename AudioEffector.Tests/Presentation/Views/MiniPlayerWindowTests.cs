using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Presentation.Controls;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Track = AudioEffector.Domain.Entities.Track;

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

    private static ResourceDictionary LoadThemeDictionary(string themePath = "/AudioEffector;component/Presentation/Themes/DarkTheme.xaml")
    {
        if (System.Windows.Application.Current == null && !UriParser.IsKnownScheme("pack"))
        {
            _ = new System.Windows.Application();
        }

        return new ResourceDictionary
        {
            Source = new Uri(themePath, UriKind.Relative)
        };
    }

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

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 1500)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            PumpDispatcher();
            Thread.Sleep(15);
        }
        PumpDispatcher();
    }

    private static PlayerControlViewModel CreateTestPlayerControlViewModel(ISettingsService settingsService)
    {
        var audioServiceMock = new Mock<IAudioService>();
        var audioEngineMock = new Mock<IAudioEngine>();
        var eventBus = new InMemoryEventBus();
        return new PlayerControlViewModel(
            audioServiceMock.Object,
            audioEngineMock.Object,
            eventBus,
            settingsService);
    }

    /// <summary>
    /// 下部連結再生キューをコマンドで展開・格納した際、アニメーション完了後にウィンドウ高さが 100px ⇔ 350px へ遷移し、格納時は Collapsed になることを検証します。
    /// また、展開によって誤って最大化抑止クローズが発火しないことを確認します。
    /// </summary>
    [Fact]
    public void ToggleMiniPlayerQueueCommand_展開と格納を切り替えた場合_高さと可視状態が正しく遷移しウィンドウが閉じられないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            bool closed = false;
            sut.Closed += (_, _) => closed = true;

            try
            {
                sut.Show();
                var drawer = Assert.IsType<Border>(sut.FindName("QueueDrawerContainer"));
                Assert.Equal(Visibility.Collapsed, drawer.Visibility);
                Assert.Equal(100, sut.ActualHeight);

                // Act (展開)
                playerVm.ToggleMiniPlayerQueueCommand.Execute(null);
                WaitUntil(() => Math.Abs(sut.ActualHeight - 350) < 1.0);

                // Assert (展開後)
                Assert.False(closed, "キュー展開による高さ延長でウィンドウが閉じられてはなりません。");
                Assert.Equal(Visibility.Visible, drawer.Visibility);
                Assert.True(drawer.IsHitTestVisible);
                Assert.Equal(350, sut.ActualHeight);

                // Act (格納)
                playerVm.CloseMiniPlayerQueueCommand.Execute(null);
                WaitUntil(() => Math.Abs(sut.ActualHeight - 100) < 1.0 && drawer.Visibility == Visibility.Collapsed);

                // Assert (格納後)
                Assert.False(closed);
                Assert.Equal(Visibility.Collapsed, drawer.Visibility);
                Assert.False(drawer.IsHitTestVisible);
                Assert.Equal(100, sut.ActualHeight);
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
    /// ミニプレイヤーの連結キュー用 ListBox に、パフォーマンス設計書準拠のUI仮想化属性が適用されていることを検証します。
    /// </summary>
    [Fact]
    public void MiniQueueListBox_初期化時_UI仮想化およびピクセルスクロール属性が設定されていること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            // Act
            var sut = new MiniPlayerWindow(settingsServiceMock.Object);
            var listBox = Assert.IsType<ListBox>(sut.FindName("MiniQueueListBox"));

            // Assert
            Assert.True(VirtualizingPanel.GetIsVirtualizing(listBox));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(listBox));
            Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(listBox));
            Assert.True(ScrollViewer.GetCanContentScroll(listBox));
        });
    }

    /// <summary>
    /// 再生キュー展開中（高さ 350px）であっても、想定サイズを超える最大化要求（1920x1080）は引き続き抑止して閉じられることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_キュー展開中に最大化サイズ変更を受信した場合_拡大を抑止しミニプレイヤーを閉じること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var savedSettings = new AppSettings();
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(() => savedSettings);
            settingsServiceMock.Setup(s => s.SaveSettings(It.IsAny<AppSettings>()))
                .Callback<AppSettings>(s => savedSettings = s);
            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            playerVm.IsMiniPlayerQueueOpen = true;

            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 115,
                Top = 165,
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
                PumpDispatcher();
                Assert.False(closed);
                Assert.Equal(350, sut.ActualHeight);

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
                Assert.True((updatedWp.Flags & SWP_NOSIZE) != 0);
                Assert.True((updatedWp.Flags & SWP_NOMOVE) != 0);
                Assert.True(closed);
                Assert.False(playerVm.IsMiniPlayerQueueOpen);
                Assert.Equal(165, savedSettings.MiniPlayerTop);
                Assert.Equal(115, savedSettings.MiniPlayerLeft);
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
    /// MiniPlayerWindow を実際の DataContext 構成（PlayerControlViewModel のバインディングおよびキュー項目・空状態切替）で表示・展開・格納した際、
    /// WPF データバインディングエラーが 1 件も発生しないことを BindingErrorTraceListener で検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_キュー展開および格納操作時_バインディングエラーが0件であること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = new ResourceDictionary
            {
                Source = new Uri("/AudioEffector;component/Presentation/Themes/DarkTheme.xaml", UriKind.Relative)
            };

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            playerVm.CurrentTrack = new Track
            {
                FilePath = @"C:\Music\current.mp3",
                Title = "Current Song",
                Artist = "Current Artist"
            };
            playerVm.PlayQueue = new ObservableCollection<Track>
            {
                new Track { FilePath = @"C:\Music\q1.mp3", Title = "Queue Track 1", Artist = "Artist 1" },
                new Track { FilePath = @"C:\Music\q2.mp3", Title = "Queue Track 2", Artist = "Artist 2" }
            };

            using var listener = new BindingErrorTraceListener();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                // Act: 表示 → キュー展開 → 格納 → クローズ
                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();

                playerVm.ToggleMiniPlayerQueueCommand.Execute(null);
                WaitUntil(() => Math.Abs(sut.ActualHeight - 350) < 1.0);
                sut.UpdateLayout();

                playerVm.CloseMiniPlayerQueueCommand.Execute(null);
                WaitUntil(() => Math.Abs(sut.ActualHeight - 100) < 1.0);
                sut.UpdateLayout();

                // Assert
                Assert.False(listener.HasErrors, $"MiniPlayerWindow でバインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// 正方形（アルバムアート）デザインかつ回転ONで MiniPlayerWindow を表示した際、サイズが 220x284（上部アート218x218＋下部ドック64＋枠2）となり、
    /// 丸い窓コンテナが表示・横長レイアウトおよび静止全面コンテナが非表示となり、バインディングエラーが発生しないことを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_正方形デザイン回転ON設定時_220x284サイズで丸い窓が表示されバインディングエラーが0件であること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = LoadThemeDictionary();

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            playerVm.CurrentTrack = new Track
            {
                FilePath = @"C:\Music\square.mp3",
                Title = "Square Track",
                Artist = "Square Artist",
                Album = "Square Album"
            };

            using var listener = new BindingErrorTraceListener();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                // Act
                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();

                var standardGrid = Assert.IsAssignableFrom<UIElement>(sut.FindName("StandardLayoutGrid"));
                var squareGrid = Assert.IsAssignableFrom<UIElement>(sut.FindName("SquareLayoutGrid"));
                var artAreaGrid = Assert.IsAssignableFrom<FrameworkElement>(sut.FindName("SquareArtAreaGrid"));
                var infoDockGrid = Assert.IsAssignableFrom<FrameworkElement>(sut.FindName("SquareInfoDockGrid"));
                var circularContainer = Assert.IsAssignableFrom<UIElement>(sut.FindName("SquareCircularArtContainer"));
                var staticContainer = Assert.IsAssignableFrom<UIElement>(sut.FindName("SquareStaticAlbumArtContainer"));
                var overlayLayer = Assert.IsAssignableFrom<FrameworkElement>(sut.FindName("SquareOverlayControlsLayer"));

                // Assert
                Assert.Equal(220, sut.ActualWidth);
                Assert.Equal(284, sut.ActualHeight);
                Assert.Equal(218, artAreaGrid.ActualWidth);
                Assert.Equal(218, artAreaGrid.ActualHeight);
                Assert.Equal(Visibility.Visible, infoDockGrid.Visibility);
                Assert.Equal(Visibility.Collapsed, standardGrid.Visibility);
                Assert.Equal(Visibility.Visible, squareGrid.Visibility);
                Assert.Equal(Visibility.Visible, circularContainer.Visibility);
                Assert.Equal(Visibility.Collapsed, staticContainer.Visibility);
                Assert.Equal(Visibility.Collapsed, overlayLayer.Visibility);
                Assert.NotNull(overlayLayer.Style);
                Assert.Contains(
                    overlayLayer.Style.Triggers.OfType<DataTrigger>(),
                    t => t.Binding is System.Windows.Data.Binding b
                        && b.ElementName == "SquareLayoutGrid"
                        && b.Path?.Path == nameof(UIElement.IsMouseOver)
                        && string.Equals(t.Value?.ToString(), "True", StringComparison.OrdinalIgnoreCase));
                Assert.False(listener.HasErrors, $"正方形ミニプレイヤーでバインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// 正方形デザインかつ回転ON時、IsPlaying の変化に応じてアルバムアート回転アニメーションが開始・一時停止し、
    /// 回転OFFや標準デザインへのリアルタイム切替時にレイアウトとサイズ・アニメーションが正しく切り替わることを検証します。
    /// </summary>
    [Fact]
    public void UpdateDesignSettings_再生状態と設定切替時_回転アニメーションおよびレイアウトサイズがリアルタイムに連動すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
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
                PumpDispatcher();

                var rotateTransform = Assert.IsType<System.Windows.Media.RotateTransform>(sut.FindName("SquareAlbumArtRotateTransform"));
                var circularContainer = Assert.IsAssignableFrom<UIElement>(sut.FindName("SquareCircularArtContainer"));
                var staticContainer = Assert.IsAssignableFrom<UIElement>(sut.FindName("SquareStaticAlbumArtContainer"));
                var standardGrid = Assert.IsAssignableFrom<UIElement>(sut.FindName("StandardLayoutGrid"));
                var squareGrid = Assert.IsAssignableFrom<UIElement>(sut.FindName("SquareLayoutGrid"));

                // Assert: 停止中はアニメーション未開始
                Assert.False(rotateTransform.HasAnimatedProperties);

                // Act: 再生開始 → 回転アニメーションが開始される
                playerVm.IsPlaying = true;
                PumpDispatcher();
                Assert.True(rotateTransform.HasAnimatedProperties);

                // Act: 回転OFFへリアルタイム切替 → アニメーション停止＆静止全面表示へ切替
                sut.UpdateDesignSettings(MiniPlayerDesignStyle.SquareAlbumArt, rotateAlbumArt: false);
                PumpDispatcher();
                Assert.False(rotateTransform.HasAnimatedProperties);
                Assert.Equal(0.0, rotateTransform.Angle);
                Assert.Equal(Visibility.Collapsed, circularContainer.Visibility);
                Assert.Equal(Visibility.Visible, staticContainer.Visibility);
                Assert.Equal(220, sut.Width);
                Assert.Equal(284, sut.Height);

                // Act: 標準デザインへリアルタイム切替 → 350x100 に戻る
                sut.UpdateDesignSettings(MiniPlayerDesignStyle.Standard, rotateAlbumArt: true);
                PumpDispatcher();
                Assert.Equal(Visibility.Visible, standardGrid.Visibility);
                Assert.Equal(Visibility.Collapsed, squareGrid.Visibility);
                Assert.Equal(350, sut.Width);
                Assert.Equal(100, sut.Height);
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// 正方形デザインかつ回転ON時、IsPlaying の true / false / true 遷移に応じて、
    /// 再生中に回転角度が進み、一時停止中は角度が維持され、再開時は 0 度へ戻らず一時停止時の角度の続きから進むことを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_正方形デザイン再生一時停止再開時_角度が進行し一時停止で維持され再開で続きから進むこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
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
                PumpDispatcher();

                var rotateTransform = Assert.IsType<System.Windows.Media.RotateTransform>(sut.FindName("SquareAlbumArtRotateTransform"));

                // Act 1: 再生開始 → 角度が 0 度より進むこと
                playerVm.IsPlaying = true;
                WaitUntil(() => rotateTransform.Angle > 1.0, timeoutMs: 2000);
                double playingAngle = rotateTransform.Angle;
                Assert.True(playingAngle > 1.0, $"再生中に角度が進むべきです (Angle={playingAngle})");

                // Act 2: 一時停止 → WPFクロックの Pause 反映ティック後に角度を記録し、さらに経過しても維持されること
                playerVm.IsPlaying = false;
                Thread.Sleep(60);
                PumpDispatcher();
                double pausedAngle = rotateTransform.Angle;
                Thread.Sleep(150);
                PumpDispatcher();
                double afterPauseWaitAngle = rotateTransform.Angle;

                // Assert 2: 一時停止中は角度が変化しないこと（許容誤差 0.1 度）
                Assert.True(
                    Math.Abs(afterPauseWaitAngle - pausedAngle) < 0.1,
                    $"一時停止中は角度が維持されるべきです (Paused={pausedAngle}, AfterWait={afterPauseWaitAngle})");

                // Act 3: 再生再開 → 0 度へ戻らず、一時停止時の角度の続きからさらに進むこと
                playerVm.IsPlaying = true;
                WaitUntil(() => rotateTransform.Angle > pausedAngle + 1.0, timeoutMs: 2000);
                double resumedAngle = rotateTransform.Angle;

                // Assert 3
                Assert.True(
                    resumedAngle > pausedAngle + 1.0,
                    $"再開後は一時停止時の角度から続きで進むべきです (Paused={pausedAngle}, Resumed={resumedAngle})");
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// MiniPlayerWindow の横長および正方形（回転ON/OFF）の各アルバムアート Image が
    /// 高解像度ソース（MiniPlayerAlbumArtImage）へバインドされ、RenderOptions.BitmapScalingMode="HighQuality" が適用されており、
    /// バインディングエラーが発生しないことを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_アルバムアート表示_全ImageがMiniPlayerAlbumArtImageバインドかつHighQualityスケーリングであること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = new ResourceDictionary
            {
                Source = new Uri("/AudioEffector;component/Presentation/Themes/DarkTheme.xaml", UriKind.Relative)
            };

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            var highResBitmap = new WriteableBitmap(500, 500, 96, 96, PixelFormats.Bgra32, null);
            highResBitmap.Freeze();
            playerVm.NowPlayingImage = highResBitmap;

            using var listener = new BindingErrorTraceListener();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                // Act
                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();

                var standardImage = Assert.IsType<Image>(sut.FindName("StandardAlbumArtImage"));
                var squareCircularImage = Assert.IsType<Image>(sut.FindName("SquareCircularAlbumArtImage"));
                var squareStaticImage = Assert.IsType<Image>(sut.FindName("SquareStaticAlbumArtImage"));

                // Assert
                Assert.Same(highResBitmap, standardImage.Source);
                Assert.Same(highResBitmap, squareCircularImage.Source);
                Assert.Same(highResBitmap, squareStaticImage.Source);

                Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(standardImage));
                Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(squareCircularImage));
                Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(squareStaticImage));

                // Act: NowPlayingImage を null に戻した際もエラーなくクリアされること
                playerVm.NowPlayingImage = null;
                PumpDispatcher();
                Assert.Null(standardImage.Source);
                Assert.Null(squareCircularImage.Source);
                Assert.Null(squareStaticImage.Source);

                Assert.False(listener.HasErrors, $"アルバムアートバインディングでエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                sut.Close();
            }
        });
    }

    private static byte[] CreateSolidPngBytes(int width, int height, Color color)
    {
        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        wb.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(wb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static (MainViewModel ViewModel, Mock<IAudioService> AudioServiceMock, Mock<ISettingsService> SettingsServiceMock, IDisposable Scope) CreateTestMainViewModel()
    {
        if (System.Windows.Application.Current == null)
        {
            _ = new System.Windows.Application();
        }

        var settingsServiceMock = new Mock<ISettingsService>();
        settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

        var audioServiceMock = new Mock<IAudioService>();
        var audioEngineMock = new Mock<IAudioEngine>();
        var eventBusMock = new Mock<IEventBus>();
        var spectrumCalculatorMock = new Mock<ISpectrumCalculator>();
        var services = new ServiceCollection();
        services.AddSingleton(settingsServiceMock.Object);
        services.AddSingleton(audioServiceMock.Object);
        services.AddSingleton(_ => new PlayerControlViewModel(
            audioServiceMock.Object,
            audioEngineMock.Object,
            eventBusMock.Object,
            settingsServiceMock.Object,
            null,
            spectrumCalculatorMock.Object));

        var provider = services.BuildServiceProvider();
        var previousProvider = App.ServiceProvider;
        App.ServiceProvider = provider;

        var mainVm = new MainViewModel();

        var scope = new DisposableAction(() =>
        {
            mainVm.Dispose();
            App.ServiceProvider = previousProvider;
            provider.Dispose();
        });

        return (mainVm, audioServiceMock, settingsServiceMock, scope);
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }

    /// <summary>
    /// 実運用と同じ MainViewModel を DataContext とした場合、未再生時および埋め込み画像なし楽曲の再生時は
    /// NowPlayingImage に既定背景が設定されていても MiniPlayerAlbumArtImage は null を維持し、
    /// 正方形ミニプレイヤー（回転ON/OFF）の背面「♫」フォールバックが覆われないことを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_MainViewModel利用時の未再生およびアート未設定時_既定背景で覆われずImageSourceがnullであること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = new ResourceDictionary
            {
                Source = new Uri("/AudioEffector;component/Presentation/Themes/DarkTheme.xaml", UriKind.Relative)
            };

            var (mainVm, audioServiceMock, settingsServiceMock, scope) = CreateTestMainViewModel();
            using (scope)
            {
                settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
                {
                    MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                    MiniPlayerRotateAlbumArt = true
                });
                mainVm.TrackArtworkBytesLoader = _ => Task.FromResult<byte[]?>(null);

                using var listener = new BindingErrorTraceListener();
                var sut = new MiniPlayerWindow(settingsServiceMock.Object)
                {
                    DataContext = mainVm,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 100,
                    Top = 100,
                    Opacity = 0,
                    ShowInTaskbar = false,
                    ShowActivated = false
                };
                sut.Resources.MergedDictionaries.Add(themeDict);

                try
                {
                    // Act 1: 初期未再生状態
                    sut.Show();
                    sut.UpdateLayout();
                    PumpDispatcher();

                    var standardImage = Assert.IsType<Image>(sut.FindName("StandardAlbumArtImage"));
                    var squareCircularImage = Assert.IsType<Image>(sut.FindName("SquareCircularAlbumArtImage"));
                    var squareStaticImage = Assert.IsType<Image>(sut.FindName("SquareStaticAlbumArtImage"));

                    // Assert 1: 未再生時は既定画像で覆われず null であること
                    Assert.Null(mainVm.MiniPlayerAlbumArtImage);
                    Assert.Null(standardImage.Source);
                    Assert.Null(squareCircularImage.Source);
                    Assert.Null(squareStaticImage.Source);

                    // Act 2: 埋め込み画像なし楽曲へ変更し、回転OFF（静止側）でも検証
                    audioServiceMock.Raise(a => a.TrackChanged += null, new Track
                    {
                        FilePath = @"C:\Music\no_art.mp3",
                        Title = "No Art Song",
                        Artist = "Artist"
                    });
                    sut.UpdateDesignSettings(MiniPlayerDesignStyle.SquareAlbumArt, rotateAlbumArt: false);
                    Thread.Sleep(50);
                    PumpDispatcher();

                    // Assert 2: 画像なし楽曲でも null を維持すること
                    Assert.Null(mainVm.MiniPlayerAlbumArtImage);
                    Assert.Null(standardImage.Source);
                    Assert.Null(squareCircularImage.Source);
                    Assert.Null(squareStaticImage.Source);
                    Assert.False(listener.HasErrors, $"MainViewModel 経路でバインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
                }
                finally
                {
                    sut.Close();
                }
            }
        });
    }

    /// <summary>
    /// MainViewModel で A → B と曲送りした際、B の画像読み込みが先に完了し A の読み込みが後から完了しても、
    /// 世代管理により古い A の結果が除外され B のアルバムアートが維持されることを検証します。
    /// </summary>
    [Fact]
    public void MainViewModel_高速曲送りで古い曲の画像読み込みが後から完了した場合_最新曲のアルバムアートが上書きされないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (mainVm, audioServiceMock, _, scope) = CreateTestMainViewModel();
            using (scope)
            {
                var tcsA = new TaskCompletionSource<byte[]?>();
                var tcsB = new TaskCompletionSource<byte[]?>();
                byte[] pngA = CreateSolidPngBytes(16, 16, Colors.Red);   // DecodePixelWidth=500 → 500x500
                byte[] pngB = CreateSolidPngBytes(24, 12, Colors.Blue);  // DecodePixelWidth=500 → 500x250

                mainVm.TrackArtworkBytesLoader = path => path.EndsWith("trackA.mp3", StringComparison.OrdinalIgnoreCase)
                    ? tcsA.Task
                    : tcsB.Task;

                var trackA = new Track { FilePath = @"C:\Music\trackA.mp3", Title = "Track A", Artist = "Artist A" };
                var trackB = new Track { FilePath = @"C:\Music\trackB.mp3", Title = "Track B", Artist = "Artist B" };

                // Act: A → B の順に曲送りし、B を先に完了させてから A を後から完了させる
                audioServiceMock.Raise(a => a.TrackChanged += null, trackA);
                audioServiceMock.Raise(a => a.TrackChanged += null, trackB);

                tcsB.SetResult(pngB);
                WaitUntil(() => mainVm.MiniPlayerAlbumArtImage != null, timeoutMs: 2000);
                var imageB = mainVm.MiniPlayerAlbumArtImage;
                Assert.NotNull(imageB);
                Assert.Equal(250, imageB!.PixelHeight);

                tcsA.SetResult(pngA);
                Thread.Sleep(80);
                PumpDispatcher();

                // Assert: 古い A の結果で上書きされず B のまま維持されること
                Assert.Same(imageB, mainVm.MiniPlayerAlbumArtImage);
                Assert.Same(imageB, mainVm.NowPlayingImage);
            }
        });
    }

    /// <summary>
    /// MainViewModel で Track A の画像読み込み中に CurrentTrack が null になった場合、
    /// 後から完了した Track A の画像で MiniPlayerAlbumArtImage が上書きされないことを検証します。
    /// </summary>
    [Fact]
    public void MainViewModel_画像読み込み中にCurrentTrackがnullになった場合_後から完了した古い画像で上書きされないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var (mainVm, audioServiceMock, _, scope) = CreateTestMainViewModel();
            using (scope)
            {
                var tcsA = new TaskCompletionSource<byte[]?>();
                byte[] pngA = CreateSolidPngBytes(16, 16, Colors.Green);
                mainVm.TrackArtworkBytesLoader = _ => tcsA.Task;

                var trackA = new Track { FilePath = @"C:\Music\trackA.mp3", Title = "Track A", Artist = "Artist A" };

                // Act: Track A 読み込み中に null へ遷移し、その後 A の読み込みを完了させる
                audioServiceMock.Raise(a => a.TrackChanged += null, trackA);
                audioServiceMock.Raise(a => a.TrackChanged += null, (Track?)null!);
                PumpDispatcher();
                Assert.Null(mainVm.MiniPlayerAlbumArtImage);

                tcsA.SetResult(pngA);
                Thread.Sleep(80);
                PumpDispatcher();

                // Assert: null のまま維持されること
                Assert.Null(mainVm.MiniPlayerAlbumArtImage);
            }
        });
    }

    /// <summary>
    /// 正方形デザインのオーバーレイ層が SquareLayoutGrid.IsMouseOver トリガーのみを持ち、
    /// クリック後残留の原因となる IsKeyboardFocusWithin トリガーを含まないこと、および各オーバーレイボタンが Focusable=False であることを検証します。
    /// </summary>
    [Fact]
    public void SquareOverlayControlsLayer_正方形デザイン初期化時_IsMouseOverトリガーのみを持ちオーバーレイボタンがFocusableFalseであること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            // Act
            var sut = new MiniPlayerWindow(settingsServiceMock.Object);
            var overlayLayer = Assert.IsAssignableFrom<FrameworkElement>(sut.FindName("SquareOverlayControlsLayer"));
            var restoreButton = Assert.IsType<Button>(sut.FindName("SquareRestoreButton"));
            var previousButton = Assert.IsType<Button>(sut.FindName("SquarePreviousButton"));
            var playPauseButton = Assert.IsType<Button>(sut.FindName("SquarePlayPauseButton"));
            var nextButton = Assert.IsType<Button>(sut.FindName("SquareNextButton"));

            // Assert
            Assert.NotNull(overlayLayer.Style);
            var dataTriggers = overlayLayer.Style.Triggers.OfType<DataTrigger>().ToList();
            Assert.Single(dataTriggers);
            var trigger = dataTriggers[0];
            var binding = Assert.IsType<System.Windows.Data.Binding>(trigger.Binding);
            Assert.Equal("SquareLayoutGrid", binding.ElementName);
            Assert.Equal(nameof(UIElement.IsMouseOver), binding.Path?.Path);
            Assert.Equal("True", trigger.Value?.ToString(), ignoreCase: true);
            Assert.DoesNotContain(
                dataTriggers,
                t => t.Binding is System.Windows.Data.Binding b
                    && b.Path?.Path == nameof(UIElement.IsKeyboardFocusWithin));

            Assert.False(restoreButton.Focusable);
            Assert.False(previousButton.Focusable);
            Assert.False(playPauseButton.Focusable);
            Assert.False(nextButton.Focusable);
        });
    }

    /// <summary>
    /// 正方形ミニプレイヤーでオーバーレイ内の再生操作ボタンへフォーカス試行およびクリック操作を行った後でも、
    /// コマンドは正常に実行され、非ホバー状態では SquareOverlayControlsLayer が Collapsed を維持することを検証します。
    /// </summary>
    [Fact]
    public void SquareOverlayControlsLayer_オーバーレイボタンへフォーカスや操作を行った後_非ホバー状態ではCollapsedを維持すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

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
                sut.UpdateLayout();
                PumpDispatcher();

                var squareGrid = Assert.IsAssignableFrom<FrameworkElement>(sut.FindName("SquareLayoutGrid"));
                var overlayLayer = Assert.IsAssignableFrom<FrameworkElement>(sut.FindName("SquareOverlayControlsLayer"));
                var previousButton = Assert.IsType<Button>(sut.FindName("SquarePreviousButton"));
                var playPauseButton = Assert.IsType<Button>(sut.FindName("SquarePlayPauseButton"));
                var nextButton = Assert.IsType<Button>(sut.FindName("SquareNextButton"));

                // Act: フォーカス試行およびクリック操作を実行
                bool focusedPrevious = previousButton.Focus();
                bool focusedPlayPause = playPauseButton.Focus();
                bool focusedNext = nextButton.Focus();

                ((IInvokeProvider)new ButtonAutomationPeer(previousButton).GetPattern(PatternInterface.Invoke)).Invoke();
                ((IInvokeProvider)new ButtonAutomationPeer(playPauseButton).GetPattern(PatternInterface.Invoke)).Invoke();
                ((IInvokeProvider)new ButtonAutomationPeer(nextButton).GetPattern(PatternInterface.Invoke)).Invoke();
                PumpDispatcher();

                // Assert
                Assert.False(focusedPrevious);
                Assert.False(focusedPlayPause);
                Assert.False(focusedNext);
                Assert.False(squareGrid.IsKeyboardFocusWithin);
                Assert.Equal(1, target.PreviousCount);
                Assert.Equal(1, target.TogglePlayPauseCount);
                Assert.Equal(1, target.NextCount);
                Assert.False(squareGrid.IsMouseOver);
                Assert.Equal(Visibility.Collapsed, overlayLayer.Visibility);
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// 正方形デザインで下部連結再生キューを展開・格納した際、ウィンドウサイズが 220x534 および 220x284 へ正しく遷移することを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_正方形デザインで再生キュー展開格納時_220x534と220x284へ遷移すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = LoadThemeDictionary();

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            using var listener = new BindingErrorTraceListener();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();
                Assert.Equal(220, sut.ActualWidth);
                Assert.Equal(284, sut.ActualHeight);

                // Act: キュー展開
                playerVm.ToggleMiniPlayerQueueCommand.Execute(null);
                WaitUntil(() => Math.Abs(sut.ActualHeight - 534) < 1.0);
                sut.UpdateLayout();

                // Assert: 展開時 220x534
                Assert.Equal(220, sut.ActualWidth);
                Assert.Equal(534, sut.ActualHeight);

                // Act: キュー格納
                playerVm.CloseMiniPlayerQueueCommand.Execute(null);
                WaitUntil(() => Math.Abs(sut.ActualHeight - 284) < 1.0);
                sut.UpdateLayout();

                // Assert: 格納時 220x284
                Assert.Equal(220, sut.ActualWidth);
                Assert.Equal(284, sut.ActualHeight);
                Assert.False(listener.HasErrors, $"正方形キュー開閉でバインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// 正方形ミニプレイヤーの下部情報ドックに曲名・アルバム名（空アルバム時のフォールバック含む）・経過時間・合計時間・シークバーが正しくバインドされ、
    /// ダーク／ライト両テーマでバインディングエラーが 0 件であることを検証します。
    /// </summary>
    [Theory]
    [InlineData("/AudioEffector;component/Presentation/Themes/DarkTheme.xaml")]
    [InlineData("/AudioEffector;component/Presentation/Themes/LightTheme.xaml")]
    public void SquareInfoDock_曲名アルバム名シークバー表示時_フォールバック含め正しくバインドされエラーが0件であること(string themePath)
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = LoadThemeDictionary(themePath);

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            playerVm.CurrentTrack = new Track
            {
                FilePath = @"C:\Music\midnight.mp3",
                Title = "Midnight City",
                Artist = "M83",
                Album = "Hurry Up, We're Dreaming"
            };
            playerVm.CurrentTimeDisplay = "01:23";
            playerVm.TotalTimeDisplay = "04:03";
            playerVm.Progress = 34.5;

            using var listener = new BindingErrorTraceListener();
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                // Act 1: 通常トラック表示
                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();

                var titleText = Assert.IsType<MarqueeTextBlock>(sut.FindName("SquareTrackTitleText"));
                var albumText = Assert.IsType<MarqueeTextBlock>(sut.FindName("SquareTrackAlbumText"));
                var currentTimeText = Assert.IsType<TextBlock>(sut.FindName("SquareCurrentTimeText"));
                var totalTimeText = Assert.IsType<TextBlock>(sut.FindName("SquareTotalTimeText"));
                var seekSlider = Assert.IsType<Slider>(sut.FindName("SquareSeekSlider"));

                // Assert 1
                Assert.Equal("Midnight City", titleText.Text);
                Assert.Equal("Hurry Up, We're Dreaming", albumText.Text);
                Assert.Equal("01:23", currentTimeText.Text);
                Assert.Equal("04:03", totalTimeText.Text);
                Assert.Equal(34.5, seekSlider.Value, precision: 2);
                Assert.True(seekSlider.IsMoveToPointEnabled);
                Assert.False(seekSlider.Focusable);

                // Act 2: アルバム名が空の楽曲 → アーティスト名へフォールバック
                playerVm.CurrentTrack = new Track
                {
                    FilePath = @"C:\Music\single.mp3",
                    Title = "Single Track",
                    Artist = "Fallback Artist",
                    Album = "   "
                };
                PumpDispatcher();

                // Assert 2
                Assert.Equal("Single Track", titleText.Text);
                Assert.Equal("Fallback Artist", albumText.Text);

                // Act 3: アルバム名・アーティスト名ともに空 → Unknown Album へフォールバック
                playerVm.CurrentTrack = new Track
                {
                    FilePath = @"C:\Music\unknown.mp3",
                    Title = "Mystery Track",
                    Artist = string.Empty,
                    Album = string.Empty
                };
                PumpDispatcher();

                // Assert 3
                Assert.Equal("Unknown Album", albumText.Text);
                Assert.False(listener.HasErrors, $"下部ドックバインディングでエラーが発生しました ({themePath}): {string.Join("; ", listener.Errors)}");
            }
            finally
            {
                sut.Close();
            }
        });
    }

    /// <summary>
    /// SquareSeekSlider のドラッグ開始／完了およびトラック直接クリック時に、
    /// MainViewModel の IsDraggingProgress・PauseForSeek / ResumeAfterSeek・SeekTo が正しく呼ばれることを検証します。
    /// </summary>
    [Fact]
    public void SquareSeekSlider_ドラッグ開始完了およびクリックシーク時_IsDraggingProgressとSeekToが正しく呼ばれること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = new ResourceDictionary
            {
                Source = new Uri("/AudioEffector;component/Presentation/Themes/DarkTheme.xaml", UriKind.Relative)
            };

            var (mainVm, audioServiceMock, settingsServiceMock, scope) = CreateTestMainViewModel();
            using (scope)
            {
                settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
                {
                    MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                    MiniPlayerRotateAlbumArt = true
                });

                var sut = new MiniPlayerWindow(settingsServiceMock.Object)
                {
                    DataContext = mainVm,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 100,
                    Top = 100,
                    Opacity = 0,
                    ShowInTaskbar = false,
                    ShowActivated = false
                };
                sut.Resources.MergedDictionaries.Add(themeDict);

                try
                {
                    sut.Show();
                    sut.UpdateLayout();
                    PumpDispatcher();

                    var seekSlider = Assert.IsType<Slider>(sut.FindName("SquareSeekSlider"));

                    // Act 1: ドラッグ開始
                    seekSlider.RaiseEvent(new DragStartedEventArgs(0, 0)
                    {
                        RoutedEvent = Thumb.DragStartedEvent,
                        Source = seekSlider
                    });

                    // Assert 1
                    Assert.True(mainVm.IsDraggingProgress);
                    audioServiceMock.Verify(a => a.PauseForSeek(), Times.Once);

                    // Act 2: ドラッグ完了
                    seekSlider.Value = 45.0;
                    seekSlider.RaiseEvent(new DragCompletedEventArgs(10, 0, false)
                    {
                        RoutedEvent = Thumb.DragCompletedEvent,
                        Source = seekSlider
                    });

                    // Assert 2
                    Assert.False(mainVm.IsDraggingProgress);
                    audioServiceMock.Verify(a => a.SeekTo(45.0), Times.AtLeastOnce);
                    audioServiceMock.Verify(a => a.ResumeAfterSeek(), Times.Once);

                    // Act 3: Thumb ドラッグを経由しないトラック直接クリックシーク
                    seekSlider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                        Source = seekSlider
                    });
                    seekSlider.Value = 72.5;
                    seekSlider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                        Source = seekSlider
                    });

                    // Assert 3
                    Assert.False(mainVm.IsDraggingProgress);
                    audioServiceMock.Verify(a => a.SeekTo(72.5), Times.Once);
                }
                finally
                {
                    sut.Close();
                }
            }
        });
    }

    /// <summary>
    /// シークバーのドラッグ中にミニプレイヤーが閉じられた場合、OnClosed で IsDraggingProgress が安全に解除され、
    /// ResumeAfterSeek が呼ばれてドラッグ状態がリークしないことを検証します。
    /// </summary>
    [Fact]
    public void OnClosed_シークバードラッグ中に閉じた場合_IsDraggingProgressが解除されリークしないこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = new ResourceDictionary
            {
                Source = new Uri("/AudioEffector;component/Presentation/Themes/DarkTheme.xaml", UriKind.Relative)
            };

            var (mainVm, audioServiceMock, settingsServiceMock, scope) = CreateTestMainViewModel();
            using (scope)
            {
                settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
                {
                    MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                    MiniPlayerRotateAlbumArt = true
                });

                var sut = new MiniPlayerWindow(settingsServiceMock.Object)
                {
                    DataContext = mainVm,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 100,
                    Top = 100,
                    Opacity = 0,
                    ShowInTaskbar = false,
                    ShowActivated = false
                };
                sut.Resources.MergedDictionaries.Add(themeDict);

                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();

                var seekSlider = Assert.IsType<Slider>(sut.FindName("SquareSeekSlider"));
                seekSlider.RaiseEvent(new DragStartedEventArgs(0, 0)
                {
                    RoutedEvent = Thumb.DragStartedEvent,
                    Source = seekSlider
                });
                Assert.True(mainVm.IsDraggingProgress);

                // Act: ドラッグ完了を経由せずウィンドウを閉じる
                sut.Close();
                PumpDispatcher();

                // Assert: ドラッグ状態が解除され、ResumeAfterSeek が呼ばれること
                Assert.False(mainVm.IsDraggingProgress);
                audioServiceMock.Verify(a => a.ResumeAfterSeek(), Times.Once);
            }
        });
    }

    /// <summary>
    /// ShouldStartWindowDrag がシークバー（Slider およびそのビジュアルツリー子要素）上では false を返し、
    /// アート領域や曲名テキスト等の通常領域では true を返すことでウィンドウ DragMove との競合を防止することを検証します。
    /// </summary>
    [Fact]
    public void ShouldStartWindowDrag_シークバーおよび子要素上ではFalseを返し通常領域ではTrueを返すこと()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var themeDict = LoadThemeDictionary();

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                MiniPlayerDesignStyle = MiniPlayerDesignStyle.SquareAlbumArt,
                MiniPlayerRotateAlbumArt = true
            });

            using var playerVm = CreateTestPlayerControlViewModel(settingsServiceMock.Object);
            var sut = new MiniPlayerWindow(settingsServiceMock.Object)
            {
                DataContext = playerVm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 100,
                Top = 100,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            sut.Resources.MergedDictionaries.Add(themeDict);

            try
            {
                sut.Show();
                sut.UpdateLayout();
                PumpDispatcher();

                var seekSlider = Assert.IsType<Slider>(sut.FindName("SquareSeekSlider"));
                seekSlider.ApplyTemplate();
                var track = seekSlider.Template.FindName("PART_Track", seekSlider) as System.Windows.Controls.Primitives.Track;
                var artAreaGrid = Assert.IsAssignableFrom<DependencyObject>(sut.FindName("SquareArtAreaGrid"));
                var titleText = Assert.IsAssignableFrom<DependencyObject>(sut.FindName("SquareTrackTitleText"));

                // Act & Assert
                Assert.False(MiniPlayerWindow.ShouldStartWindowDrag(seekSlider));
                Assert.NotNull(track);
                Assert.False(MiniPlayerWindow.ShouldStartWindowDrag(track));
                Assert.NotNull(track!.Thumb);
                Assert.False(MiniPlayerWindow.ShouldStartWindowDrag(track.Thumb));
                Assert.True(MiniPlayerWindow.ShouldStartWindowDrag(artAreaGrid));
                Assert.True(MiniPlayerWindow.ShouldStartWindowDrag(titleText));
            }
            finally
            {
                sut.Close();
            }
        });
    }
}
