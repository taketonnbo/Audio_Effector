using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ミニプレイヤーウィンドウ
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private const int WmSysCommand = 0x0112;
    private const int ScMaximize = 0xF030;
    private const int SysCommandMask = 0xFFF0;

    private readonly ISettingsService _settingsService;
    private MiniPlayerTopmostBehavior _currentBehavior;
    private HwndSource? _hwndSource;
    private double? _lastNormalTop;
    private double? _lastNormalLeft;
    private bool _isClosingOrRestoring;
    private bool _isClosed;

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
    /// ウィンドウハンドル初期化時にWin32メッセージフックを登録します
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = HwndSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (unchecked((int)wParam.ToInt64()) & SysCommandMask) == ScMaximize)
        {
            handled = true;
            RequestRestoreToMainWindow();
        }

        return IntPtr.Zero;
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
            this.WindowState = WindowState.Normal;
            RequestRestoreToMainWindow();
        }
    }

    private void RequestRestoreToMainWindow()
    {
        if (_isClosingOrRestoring || _isClosed)
        {
            return;
        }

        _isClosingOrRestoring = true;
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

        // Save window position (prefer current normal-state coordinates; fall back to last recorded normal coordinates if maximized/minimized)
        bool isCurrentNormal = this.WindowState == WindowState.Normal && !double.IsNaN(this.Top) && !double.IsNaN(this.Left);
        double? topToSave = isCurrentNormal ? this.Top : _lastNormalTop;
        double? leftToSave = isCurrentNormal ? this.Left : _lastNormalLeft;
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
            if (mainWindow != null && mainWindow != this && mainWindow.Visibility != Visibility.Visible)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }
    }
}
