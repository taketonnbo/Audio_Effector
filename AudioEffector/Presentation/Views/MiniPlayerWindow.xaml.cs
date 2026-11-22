using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ミニプレイヤーウィンドウ
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_THICKFRAME = 0x00040000;

    private const int WM_SIZE = 0x0005;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SYSCOMMAND = 0x0112;

    private const int HTCLIENT = 1;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SYSCOMMAND_MASK = 0xFFF0;
    private const int SIZE_MAXIMIZED = 2;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int RESIZE_TOLERANCE_PX = 4;

    private readonly ISettingsService _settingsService;
    private MiniPlayerTopmostBehavior _currentBehavior;
    private HwndSource? _hwndSource;
    private double? _lastNormalTop;
    private double? _lastNormalLeft;
    private bool _isClosingOrRestoring;
    private bool _isRestoringFromMaximize;
    private bool _isClosed;

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public MiniPlayerWindow() : this(null)
    {
    }

    /// <summary>
    /// 設定サービスを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    public MiniPlayerWindow(ISettingsService? settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService
            ?? (App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ISettingsService>(App.ServiceProvider)
                : null)
            ?? SettingsApplicationService.Default;

        this.Loaded += MiniPlayerWindow_Loaded;
        this.Deactivated += MiniPlayerWindow_Deactivated;
        this.LocationChanged += MiniPlayerWindow_LocationChanged;
    }

    private void MiniPlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        _currentBehavior = settings.MiniPlayerTopmostBehavior;
        ApplyTopmostBehavior();

        if (settings.MiniPlayerTop.HasValue && settings.MiniPlayerLeft.HasValue)
        {
            double top = settings.MiniPlayerTop.Value;
            double left = settings.MiniPlayerLeft.Value;

            // Ensure the window is within the virtual screen bounds
            if (left < SystemParameters.VirtualScreenWidth - 50 &&
                top < SystemParameters.VirtualScreenHeight - 50 &&
                left + this.Width > SystemParameters.VirtualScreenLeft &&
                top + this.Height > SystemParameters.VirtualScreenTop)
            {
                this.Top = top;
                this.Left = left;
            }
        }

        RecordNormalWindowPosition();
    }

    private void MiniPlayerWindow_LocationChanged(object? sender, EventArgs e)
    {
        RecordNormalWindowPosition();
    }

    private void RecordNormalWindowPosition()
    {
        if (this.WindowState == WindowState.Normal && !_isClosingOrRestoring && !double.IsNaN(this.Top) && !double.IsNaN(this.Left))
        {
            _lastNormalTop = this.Top;
            _lastNormalLeft = this.Left;
        }
    }

    /// <summary>
    /// ウィンドウハンドル初期化時にWin32スタイル調整およびメッセージフックを登録します
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = HwndSource.FromVisual(this) as HwndSource;
        if (_hwndSource != null)
        {
            // Windows の Win+↑ (Aero Snap) をミニプレイヤーでも受信できるよう最大化関連スタイルを有効化する
            // （外観フレームは WM_NCCALCSIZE で 0px に維持し、サイズ拡大は WM_WINDOWPOSCHANGING / WM_SIZE / WM_SYSCOMMAND で抑止する）
            long style = GetWindowLongPtr(_hwndSource.Handle, GWL_STYLE).ToInt64();
            long updatedStyle = style | WS_MAXIMIZEBOX | WS_THICKFRAME;
            if (updatedStyle != style)
            {
                _ = SetWindowLongPtr(_hwndSource.Handle, GWL_STYLE, new IntPtr(updatedStyle));
                _ = SetWindowPos(
                    _hwndSource.Handle,
                    IntPtr.Zero,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }

            _hwndSource.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
        {
            // WS_THICKFRAME 付与に伴う非クライアント領域（標準枠）を 0px に保ち透過角丸デザインを維持する
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_NCHITTEST)
        {
            // WS_THICKFRAME 付与によるウィンドウ端でのリサイズカーソル表示・手動リサイズを防止する
            handled = true;
            return new IntPtr(HTCLIENT);
        }

        if (msg == WM_SYSCOMMAND && (unchecked((int)wParam.ToInt64()) & SYSCOMMAND_MASK) == SC_MAXIMIZE)
        {
            handled = true;
            RequestRestoreToMainWindow();
            return IntPtr.Zero;
        }

        if (msg == WM_SIZE && unchecked((int)wParam.ToInt64()) == SIZE_MAXIMIZED)
        {
            // Win+↑ (Aero Snap) は WM_SYSCOMMAND を経由せず WM_SIZE(SIZE_MAXIMIZED) を直接送るためここで捕捉する
            RequestRestoreToMainWindow();
            return IntPtr.Zero;
        }

        if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((wp.Flags & SWP_NOSIZE) == 0 && IsUnexpectedResizeOrMaximize(wp.Cx, wp.Cy))
            {
                wp.Flags |= SWP_NOSIZE | SWP_NOMOVE;
                Marshal.StructureToPtr(wp, lParam, false);
                RequestRestoreToMainWindow();
            }
        }

        return IntPtr.Zero;
    }

    private bool IsUnexpectedResizeOrMaximize(int widthPx, int heightPx)
    {
        if (widthPx <= 0 || heightPx <= 0 || double.IsNaN(this.Width) || double.IsNaN(this.Height))
        {
            return false;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int expectedWidthPx = (int)Math.Round(this.Width * dpi.DpiScaleX);
        int expectedHeightPx = (int)Math.Round(this.Height * dpi.DpiScaleY);

        return widthPx > expectedWidthPx + RESIZE_TOLERANCE_PX || heightPx > expectedHeightPx + RESIZE_TOLERANCE_PX;
    }

    /// <summary>
    /// ウィンドウ状態変更時に最大化への遷移を検知し、通常画面への復帰処理を行います
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (this.WindowState == WindowState.Maximized)
        {
            RequestRestoreToMainWindow();
            this.WindowState = WindowState.Normal;
        }
    }

    private void RequestRestoreToMainWindow()
    {
        if (_isClosingOrRestoring || _isClosed)
        {
            return;
        }

        _isClosingOrRestoring = true;
        _isRestoringFromMaximize = true;

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            if (!_isClosed)
            {
                Close();
            }
        }));
    }

    /// <summary>
    /// 最前面表示の動作設定を更新します
    /// </summary>
    /// <param name="behavior">最前面表示の動作設定</param>
    public void UpdateTopmostBehavior(MiniPlayerTopmostBehavior behavior)
    {
        _currentBehavior = behavior;
        ApplyTopmostBehavior();
    }

    private void ApplyTopmostBehavior()
    {
        if (_currentBehavior == MiniPlayerTopmostBehavior.AlwaysOnTop || _currentBehavior == MiniPlayerTopmostBehavior.OnDisplayOnly)
        {
            this.Topmost = true;
        }
        else
        {
            this.Topmost = false;
        }
    }

    private void MiniPlayerWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_currentBehavior == MiniPlayerTopmostBehavior.OnDisplayOnly)
        {
            this.Topmost = false;
        }
    }

    private void Window_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    /// <summary>
    /// ウィンドウが閉じられた際の処理を行います
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _isClosingOrRestoring = true;

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        // Save window position:
        // - When closing via maximize-suppression restore flow, prioritize the last recorded normal coordinates.
        // - Otherwise, use current coordinates when Normal, or fall back to last recorded normal coordinates.
        bool isCurrentNormal = this.WindowState == WindowState.Normal && !double.IsNaN(this.Top) && !double.IsNaN(this.Left);
        double? topToSave = _isRestoringFromMaximize
            ? (_lastNormalTop ?? (isCurrentNormal ? this.Top : null))
            : (isCurrentNormal ? this.Top : _lastNormalTop);
        double? leftToSave = _isRestoringFromMaximize
            ? (_lastNormalLeft ?? (isCurrentNormal ? this.Left : null))
            : (isCurrentNormal ? this.Left : _lastNormalLeft);

        if (topToSave.HasValue && leftToSave.HasValue)
        {
            var settings = _settingsService.LoadSettings();
            settings.MiniPlayerTop = topToSave.Value;
            settings.MiniPlayerLeft = leftToSave.Value;
            _settingsService.SaveSettings(settings);
        }

        base.OnClosed(e);

        var app = System.Windows.Application.Current;
        if (app != null && app.CheckAccess())
        {
            var mainWindow = app.MainWindow;
            if (mainWindow is MainWindow audioMainWindow)
            {
                audioMainWindow.RestoreFromMiniPlayer();
            }
            else if (mainWindow != null && mainWindow != this && mainWindow.Visibility != Visibility.Visible)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }
    }
}
