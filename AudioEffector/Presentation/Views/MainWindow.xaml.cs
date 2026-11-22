using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// アプリケーションのメインウィンドウ
/// </summary>
public partial class MainWindow : Window
{
    private const int WM_SIZE = 0x0005;
    private const int WM_SHOWWINDOW = 0x0018;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE = 0xF120;
    private const int SYSCOMMAND_MASK = 0xFFF0;
    private const int SIZE_RESTORED = 0;
    private const int SIZE_MAXIMIZED = 2;

    private readonly ISettingsService _settingsService;
    private HwndSource? _hwndSource;
    private MiniPlayerWindow? _miniPlayer;
    private bool _isRestoringFromMiniPlayer;
    private bool _isClosed;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public MainWindow() : this(App.ServiceProvider?.GetService<MainViewModel>() ?? new MainViewModel())
    {
    }

    /// <summary>
    /// ViewModelを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="viewModel">メインViewModel</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _settingsService = (App.ServiceProvider != null
            ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ISettingsService>(App.ServiceProvider)
            : null)
            ?? viewModel.SettingsService
            ?? SettingsApplicationService.Default;

        this.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var appSettings = _settingsService.LoadSettings();
        if (appSettings.StartMinimized)
        {
            this.WindowState = WindowState.Minimized;
        }

        if (this.DataContext is MainViewModel vm)
        {
            vm.SettingsUpdated += () => UpdateShortcuts(_settingsService.LoadSettings());
        }
        UpdateShortcuts(appSettings);
    }

    private void UpdateShortcuts(AudioEffector.Domain.Entities.AppSettings settings)
    {
        this.InputBindings.Clear();
        if (this.DataContext is MainViewModel vm)
        {
            AddShortcut(settings.PlayPauseShortcut, vm.TogglePlayPauseCommand);
            AddShortcut(settings.StopShortcut, vm.StopCommand);
            AddShortcut(settings.NextShortcut, vm.NextCommand);
            AddShortcut(settings.PreviousShortcut, vm.PreviousCommand);
            AddShortcut(settings.MuteShortcut, vm.ToggleMuteCommand);
            AddShortcut(settings.VolumeUpShortcut, vm.IncreaseVolumeCommand);
            AddShortcut(settings.VolumeDownShortcut, vm.DecreaseVolumeCommand);
        }
    }

    private void AddShortcut(AudioEffector.Domain.Entities.ShortcutKeyConfig config, System.Windows.Input.ICommand command)
    {
        if (config != null && config.Key != System.Windows.Input.Key.None)
        {
            try
            {
                this.InputBindings.Add(new System.Windows.Input.KeyBinding(command, config.Key, config.Modifiers));
            }
            catch (System.NotSupportedException)
            {
                // WPF's KeyGesture doesn't support some combinations (e.g. Shift+P). Ignore them safely.
            }
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
        if (_isClosed || _isRestoringFromMiniPlayer)
        {
            return IntPtr.Zero;
        }

        bool isMiniPlayerMode = _miniPlayer != null || this.Visibility != Visibility.Visible;
        if (!isMiniPlayerMode)
        {
            return IntPtr.Zero;
        }

        if (msg == WM_SYSCOMMAND)
        {
            int command = unchecked((int)wParam.ToInt64()) & SYSCOMMAND_MASK;
            if (command == SC_RESTORE || command == SC_MAXIMIZE)
            {
                // OSによる非表示MainWindowの直接復元（黒画面化）を抑止し、WPF正規フローで復帰する
                handled = true;
                RequestRestoreFromMiniPlayer();
            }
        }
        else if (msg == WM_SHOWWINDOW && wParam != IntPtr.Zero)
        {
            // OSがWin32レベルで非表示MainWindowを再表示した場合もWPF可視状態と描画を同期しミニプレイヤーを閉じる
            RequestRestoreFromMiniPlayer();
        }
        else if (msg == WM_SIZE)
        {
            int sizeType = unchecked((int)wParam.ToInt64());
            if (sizeType == SIZE_RESTORED || sizeType == SIZE_MAXIMIZED)
            {
                RequestRestoreFromMiniPlayer();
            }
        }

        return IntPtr.Zero;
    }

    private void RequestRestoreFromMiniPlayer()
    {
        if (_isClosed || _isRestoringFromMiniPlayer)
        {
            return;
        }

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            if (!_isClosed)
            {
                RestoreFromMiniPlayer();
            }
        }));
    }

    /// <summary>
    /// ミニプレイヤー表示状態からメインウィンドウを正常に復帰させ、ミニプレイヤーを閉じます
    /// </summary>
    public void RestoreFromMiniPlayer()
    {
        if (_isClosed || _isRestoringFromMiniPlayer)
        {
            return;
        }

        _isRestoringFromMiniPlayer = true;
        try
        {
            var miniPlayer = _miniPlayer;
            _miniPlayer = null;
            if (miniPlayer != null)
            {
                miniPlayer.Close();
            }

            if (this.Visibility != Visibility.Visible)
            {
                this.Show();
            }

            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }

            this.Activate();
            ForceRedrawWindow();
        }
        finally
        {
            _isRestoringFromMiniPlayer = false;
        }
    }

    private void ForceRedrawWindow()
    {
        this.InvalidateVisual();
        this.UpdateLayout();

        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_isClosed)
                {
                    return;
                }

                if (this.Visibility != Visibility.Visible && _miniPlayer == null)
                {
                    this.Show();
                }

                this.InvalidateVisual();
                this.UpdateLayout();
            }));
        }
    }

    /// <summary>
    /// ウィンドウの状態が変更された際の処理を行います
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnStateChanged(System.EventArgs e)
    {
        base.OnStateChanged(e);

        if (_isClosed || _isRestoringFromMiniPlayer)
        {
            return;
        }

        if (this.WindowState == WindowState.Minimized)
        {
            this.Hide();
            if (_miniPlayer == null)
            {
                _miniPlayer = new MiniPlayerWindow();
                _miniPlayer.DataContext = this.DataContext;
                _miniPlayer.Closed += (s, args) => _miniPlayer = null;
            }
            _miniPlayer.Show();
            _miniPlayer.Activate();
        }
        else if (_miniPlayer != null)
        {
            RestoreFromMiniPlayer();
        }
        else if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
        {
            ForceRedrawWindow();
        }
    }

    /// <summary>
    /// ウィンドウが閉じられようとしている際の処理を行います
    /// </summary>
    /// <param name="e">キャンセル可能なイベントデータ</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.DeviceBrowser.IsTransferring)
        {
            var result = MessageBox.Show("現在データ転送中です。本当にアプリを終了してよろしいですか？\n（終了すると転送が中断されます）", "終了確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// ウィンドウが閉じられた際の処理を行います
    /// ViewModelのクリーンアップを行います
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnClosed(System.EventArgs e)
    {
        _isClosed = true;

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        var miniPlayer = _miniPlayer;
        _miniPlayer = null;
        miniPlayer?.Close();

        base.OnClosed(e);
        (DataContext as MainViewModel)?.Cleanup();
    }

    /// <summary>
    /// スライダーのドラッグ開始時のイベントハンドラ
    /// 再生を一時停止します
    /// </summary>
    private void Slider_DragStarted(object sender, DragStartedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm != null)
        {
            vm.IsDraggingProgress = true;
            if (vm.PlayerControl != null)
            {
                vm.PlayerControl.StartDragging();
            }
            else
            {
                vm.AudioService.PauseForSeek(); // シーク中は再生を一時停止する
            }
        }
    }

    /// <summary>
    /// スライダーのドラッグ終了時のイベントハンドラ
    /// 再生を再開します
    /// </summary>
    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm != null)
        {
            vm.IsDraggingProgress = false;
            if (vm.PlayerControl != null)
            {
                vm.PlayerControl.StopDragging();
            }
            else
            {
                vm.AudioService.ResumeAfterSeek(); // シーク後に再生を再開する
            }
        }
    }

    /// <summary>
    /// タイトルバーの最小化ボタンクリック時の処理を行います
    /// </summary>
    private void TitleBarMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    /// <summary>
    /// タイトルバーの最大化/元に戻すボタンクリック時の処理を行います
    /// </summary>
    private void TitleBarMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    /// <summary>
    /// タイトルバーの閉じるボタンクリック時の処理を行います
    /// </summary>
    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }
}
