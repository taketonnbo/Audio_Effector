using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

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

    private const double StandardWidth = 350.0;
    private const double StandardCompactHeight = 100.0;
    private const double StandardExpandedHeight = 350.0;
    private const double SquareWidth = 220.0;
    private const double SquareCompactHeight = 284.0;
    private const double SquareExpandedHeight = 534.0;
    private const double AlbumArtRotationDurationSeconds = 20.0;

    private readonly ISettingsService _settingsService;
    private readonly WindowShortcutHandler _shortcutHandler = new();
    private Action? _disposeFallbackSettings;
    private MainViewModel? _subscribedViewModel;
    private INotifyPropertyChanged? _subscribedQueueStateSource;
    private Action? _settingsUpdatedHandler;
    private MiniPlayerTopmostBehavior _currentBehavior;
    private MiniPlayerDesignStyle _currentDesignStyle = MiniPlayerDesignStyle.Standard;
    private bool _rotateAlbumArt = true;
    private bool _isPlaying;
    private AnimationClock? _albumArtRotationClock;
    private HwndSource? _hwndSource;
    private double? _lastNormalTop;
    private double? _lastNormalLeft;
    private double _targetWindowHeight = StandardCompactHeight;
    private bool _isQueueOpen;
    private bool _isSeekDragging;
    private bool _isSeekPointerPressed;
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
        var resolvedSettings = settingsService
            ?? (App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ISettingsService>(App.ServiceProvider)
                : null);

        if (resolvedSettings != null)
        {
            _settingsService = resolvedSettings;
        }
        else
        {
#pragma warning disable CA2000 // OnClosed で明示的に破棄する
            var fallbackRepo = new Infrastructure.Repository.JsonSettingsRepository();
            var fallbackService = new SettingsApplicationService(fallbackRepo);
#pragma warning restore CA2000
            _settingsService = fallbackService;
            _disposeFallbackSettings = () =>
            {
                fallbackService.Dispose();
                fallbackRepo.Dispose();
            };
        }

        _shortcutHandler.UpdateSettings(_settingsService.LoadSettings());

        this.Loaded += MiniPlayerWindow_Loaded;
        this.Deactivated += MiniPlayerWindow_Deactivated;
        this.LocationChanged += MiniPlayerWindow_LocationChanged;
        this.DataContextChanged += MiniPlayerWindow_DataContextChanged;

        // Slider(IsMoveToPointEnabled=True) はクラスハンドラで PreviewMouseLeftButtonDown を Handled=true にするため、
        // handledEventsToo: true で購読しトラック直接クリックシークを確実に捕捉する
        SquareSeekSlider.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(SquareSeekSlider_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        SquareSeekSlider.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(SquareSeekSlider_PreviewMouseLeftButtonUp),
            handledEventsToo: true);
        SquareSeekSlider.LostMouseCapture += SquareSeekSlider_LostMouseCapture;
        SquareSeekSlider.MouseLeave += SquareSeekSlider_MouseLeave;
    }

    private void MiniPlayerWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_isSeekDragging)
        {
            EndSeekDrag(e.OldValue);
        }

        _isSeekPointerPressed = false;
        SubscribeQueueStateSource(e.NewValue);
    }

    private void SubscribeQueueStateSource(object? dataContext)
    {
        if (ReferenceEquals(_subscribedQueueStateSource, dataContext))
        {
            return;
        }

        DetachQueueStateSource();

        if (dataContext is INotifyPropertyChanged notifySource)
        {
            _subscribedQueueStateSource = notifySource;
            _subscribedQueueStateSource.PropertyChanged += OnQueueStateSourcePropertyChanged;
        }

        _isPlaying = GetPlayingState(dataContext);
        UpdateQueueDrawerState(GetQueueOpenState(dataContext), animate: false);
        UpdateAlbumArtRotationState();
    }

    private void DetachQueueStateSource()
    {
        if (_subscribedQueueStateSource != null)
        {
            _subscribedQueueStateSource.PropertyChanged -= OnQueueStateSourcePropertyChanged;
            _subscribedQueueStateSource = null;
        }
    }

    private static bool GetQueueOpenState(object? dataContext) => dataContext switch
    {
        MainViewModel mainVm => mainVm.IsMiniPlayerQueueOpen,
        PlayerControlViewModel playerVm => playerVm.IsMiniPlayerQueueOpen,
        _ => false
    };

    private static bool GetPlayingState(object? dataContext) => dataContext switch
    {
        MainViewModel mainVm => mainVm.IsPlaying,
        PlayerControlViewModel playerVm => playerVm.IsPlaying,
        _ => false
    };

    private double GetCurrentWindowWidth() =>
        _currentDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt ? SquareWidth : StandardWidth;

    private double GetCurrentCompactHeight() =>
        _currentDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt ? SquareCompactHeight : StandardCompactHeight;

    private double GetCurrentExpandedHeight() =>
        _currentDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt ? SquareExpandedHeight : StandardExpandedHeight;

    private void OnQueueStateSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsMiniPlayerQueueOpen))
        {
            UpdateQueueDrawerState(GetQueueOpenState(sender), animate: this.IsLoaded);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsPlaying))
        {
            _isPlaying = GetPlayingState(sender);
            UpdateAlbumArtRotationState();
        }
    }

    private void UpdateQueueDrawerState(bool isOpen, bool animate)
    {
        if (_isClosed)
        {
            return;
        }

        _isQueueOpen = isOpen;
        double targetHeight = isOpen ? GetCurrentExpandedHeight() : GetCurrentCompactHeight();
        _targetWindowHeight = targetHeight;

        if (isOpen)
        {
            QueueDrawerContainer.Visibility = Visibility.Visible;
            QueueDrawerContainer.IsHitTestVisible = true;
        }

        if (!animate || !this.IsLoaded)
        {
            BeginAnimation(HeightProperty, null);
            this.Height = targetHeight;
            if (!isOpen)
            {
                QueueDrawerContainer.Visibility = Visibility.Collapsed;
                QueueDrawerContainer.IsHitTestVisible = false;
            }
            return;
        }

        double fromHeight = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
        var animation = new DoubleAnimation
        {
            From = fromHeight,
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(isOpen ? 220 : 200),
            EasingFunction = new CubicEase { EasingMode = isOpen ? EasingMode.EaseOut : EasingMode.EaseIn }
        };

        animation.Completed += (_, _) =>
        {
            if (_isClosed)
            {
                return;
            }

            if (_isQueueOpen == isOpen)
            {
                BeginAnimation(HeightProperty, null);
                this.Height = targetHeight;

                if (!isOpen)
                {
                    QueueDrawerContainer.Visibility = Visibility.Collapsed;
                    QueueDrawerContainer.IsHitTestVisible = false;
                }
            }
        };

        BeginAnimation(HeightProperty, animation);
    }

    private void MiniPlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        _shortcutHandler.UpdateSettings(settings);

        if (this.DataContext is MainViewModel vm && _subscribedViewModel != vm)
        {
            DetachSettingsUpdatedHandler();
            _subscribedViewModel = vm;
            _settingsUpdatedHandler = () => _shortcutHandler.UpdateSettings(_settingsService.LoadSettings());
            _subscribedViewModel.SettingsUpdated += _settingsUpdatedHandler;
        }

        SubscribeQueueStateSource(this.DataContext);

        _currentBehavior = settings.MiniPlayerTopmostBehavior;
        ApplyTopmostBehavior();
        ApplyDesignSettings(settings.MiniPlayerDesignStyle, settings.MiniPlayerRotateAlbumArt);

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

    private void DetachSettingsUpdatedHandler()
    {
        if (_subscribedViewModel != null && _settingsUpdatedHandler != null)
        {
            _subscribedViewModel.SettingsUpdated -= _settingsUpdatedHandler;
        }

        _subscribedViewModel = null;
        _settingsUpdatedHandler = null;
    }

    /// <summary>
    /// トンネリングキー押下イベントでカスタムショートカットおよびハードウェアメディアキーを処理します
    /// </summary>
    /// <param name="e">キーイベントデータ</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_isClosed && _shortcutHandler.HandlePreviewKeyDown(e, DataContext as IShortcutCommandTarget, Keyboard.FocusedElement))
        {
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_isClosed)
        {
            return IntPtr.Zero;
        }

        if (msg == ShortcutKeyHelper.WM_APPCOMMAND)
        {
            if (_shortcutHandler.HandleAppCommand(lParam, DataContext as IShortcutCommandTarget))
            {
                handled = true;
                return new IntPtr(1);
            }

            return IntPtr.Zero;
        }

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
        double allowedWidth = Math.Max(this.Width, GetCurrentWindowWidth());
        int expectedWidthPx = (int)Math.Round(allowedWidth * dpi.DpiScaleX);
        double allowedHeight = Math.Max(this.Height, _targetWindowHeight);
        int expectedHeightPx = (int)Math.Round(allowedHeight * dpi.DpiScaleY);

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

    /// <summary>
    /// ミニプレイヤーのデザイン種別およびアルバムアート回転設定をリアルタイムに更新します
    /// </summary>
    /// <param name="style">ミニプレイヤーのデザイン種別</param>
    /// <param name="rotateAlbumArt">楽曲再生中にアルバムアートを時計回りに回転させるかどうか</param>
    public void UpdateDesignSettings(MiniPlayerDesignStyle style, bool rotateAlbumArt)
    {
        if (_isClosed)
        {
            return;
        }

        ApplyDesignSettings(style, rotateAlbumArt);
    }

    private void ApplyDesignSettings(MiniPlayerDesignStyle style, bool rotateAlbumArt)
    {
        _currentDesignStyle = style;
        _rotateAlbumArt = rotateAlbumArt;

        bool isSquare = style == MiniPlayerDesignStyle.SquareAlbumArt;
        if (!isSquare)
        {
            _isSeekPointerPressed = false;
            if (_isSeekDragging)
            {
                EndSeekDrag(this.DataContext);
            }
        }

        StandardLayoutGrid.Visibility = isSquare ? Visibility.Collapsed : Visibility.Visible;
        SquareLayoutGrid.Visibility = isSquare ? Visibility.Visible : Visibility.Collapsed;
        SquareCircularArtContainer.Visibility = (isSquare && rotateAlbumArt) ? Visibility.Visible : Visibility.Collapsed;
        SquareStaticAlbumArtContainer.Visibility = (isSquare && !rotateAlbumArt) ? Visibility.Visible : Visibility.Collapsed;

        BeginAnimation(HeightProperty, null);
        this.Width = GetCurrentWindowWidth();
        double targetHeight = _isQueueOpen ? GetCurrentExpandedHeight() : GetCurrentCompactHeight();
        _targetWindowHeight = targetHeight;
        this.Height = targetHeight;

        UpdateAlbumArtRotationState();
    }

    private void UpdateAlbumArtRotationState()
    {
        bool shouldEnableRotation = !_isClosed
            && _currentDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt
            && _rotateAlbumArt;

        if (!shouldEnableRotation)
        {
            StopAlbumArtRotation();
            return;
        }

        if (_isPlaying)
        {
            if (_albumArtRotationClock == null)
            {
                var animation = new DoubleAnimation
                {
                    From = 0.0,
                    To = 360.0,
                    Duration = TimeSpan.FromSeconds(AlbumArtRotationDurationSeconds),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                _albumArtRotationClock = animation.CreateClock();
                SquareAlbumArtRotateTransform.ApplyAnimationClock(RotateTransform.AngleProperty, _albumArtRotationClock);
            }
            else
            {
                _albumArtRotationClock.Controller?.Resume();
            }
        }
        else
        {
            _albumArtRotationClock?.Controller?.Pause();
        }
    }

    private void StopAlbumArtRotation()
    {
        if (_albumArtRotationClock != null)
        {
            _albumArtRotationClock.Controller?.Stop();
            SquareAlbumArtRotateTransform.ApplyAnimationClock(RotateTransform.AngleProperty, null);
            _albumArtRotationClock = null;
        }

        SquareAlbumArtRotateTransform.Angle = 0.0;
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
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            if (!ShouldStartWindowDrag(e.OriginalSource as DependencyObject))
            {
                return;
            }

            this.DragMove();
        }
    }

    /// <summary>
    /// 指定されたマウス操作起点要素からウィンドウのドラッグ移動（DragMove）を開始してよいかを判定します
    /// シークバー（Slider およびそのビジュアルツリー子要素）上での操作時は競合防止のため false を返します
    /// </summary>
    /// <param name="source">マウスイベントの起点要素</param>
    /// <returns>ウィンドウドラッグを開始してよい場合は true、シークバー等の操作領域上の場合は false</returns>
    internal static bool ShouldStartWindowDrag(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (current is Slider || current is Thumb)
            {
                return false;
            }

            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return true;
    }

    private void SquareSeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        BeginSeekDrag(this.DataContext);
    }

    private void SquareSeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isSeekPointerPressed = false;
        if (_isClosed)
        {
            return;
        }

        double value = sender is Slider slider ? slider.Value : SquareSeekSlider.Value;
        SeekToProgress(this.DataContext, value);
        EndSeekDrag(this.DataContext);
    }

    private void SquareSeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        _isSeekPointerPressed = true;
    }

    private void SquareSeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasPressed = _isSeekPointerPressed;
        _isSeekPointerPressed = false;
        if (_isClosed || !wasPressed || _isSeekDragging)
        {
            return;
        }

        double value = sender is Slider slider ? slider.Value : SquareSeekSlider.Value;
        SeekToProgress(this.DataContext, value);
    }

    private void SquareSeekSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isSeekDragging)
        {
            _isSeekPointerPressed = false;
        }
    }

    private void SquareSeekSlider_MouseLeave(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Released && !_isSeekDragging)
        {
            _isSeekPointerPressed = false;
        }
    }

    private void BeginSeekDrag(object? dataContext)
    {
        if (_isSeekDragging)
        {
            return;
        }

        _isSeekDragging = true;
        if (dataContext is MainViewModel mainVm)
        {
            mainVm.IsDraggingProgress = true;
            if (mainVm.PlayerControl != null)
            {
                mainVm.PlayerControl.StartDragging();
            }
            else
            {
                mainVm.AudioService.PauseForSeek();
            }
        }
        else if (dataContext is PlayerControlViewModel playerVm)
        {
            playerVm.StartDragging();
        }
    }

    private void EndSeekDrag(object? dataContext)
    {
        if (!_isSeekDragging)
        {
            return;
        }

        _isSeekDragging = false;
        if (dataContext is MainViewModel mainVm)
        {
            mainVm.IsDraggingProgress = false;
            if (mainVm.PlayerControl != null)
            {
                mainVm.PlayerControl.StopDragging();
            }
            else
            {
                mainVm.AudioService.ResumeAfterSeek();
            }
        }
        else if (dataContext is PlayerControlViewModel playerVm)
        {
            playerVm.StopDragging();
        }
    }

    private static void SeekToProgress(object? dataContext, double progress)
    {
        double clamped = Math.Clamp(progress, 0.0, 100.0);
        if (dataContext is MainViewModel mainVm)
        {
            mainVm.Seek(clamped);
        }
        else if (dataContext is PlayerControlViewModel playerVm)
        {
            playerVm.Seek(clamped);
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
        _isSeekPointerPressed = false;
        if (_isSeekDragging)
        {
            EndSeekDrag(this.DataContext);
        }
        SquareSeekSlider.RemoveHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(SquareSeekSlider_PreviewMouseLeftButtonDown));
        SquareSeekSlider.RemoveHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(SquareSeekSlider_PreviewMouseLeftButtonUp));
        SquareSeekSlider.LostMouseCapture -= SquareSeekSlider_LostMouseCapture;
        SquareSeekSlider.MouseLeave -= SquareSeekSlider_MouseLeave;
        StopAlbumArtRotation();
        DetachSettingsUpdatedHandler();
        DetachQueueStateSource();

        if (this.DataContext is MainViewModel closingMainVm && closingMainVm.IsMiniPlayerQueueOpen)
        {
            closingMainVm.IsMiniPlayerQueueOpen = false;
        }
        else if (this.DataContext is PlayerControlViewModel closingPlayerVm && closingPlayerVm.IsMiniPlayerQueueOpen)
        {
            closingPlayerVm.IsMiniPlayerQueueOpen = false;
        }

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

        _disposeFallbackSettings?.Invoke();
        _disposeFallbackSettings = null;

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
