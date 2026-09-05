using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
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
    private readonly ISettingsService _settingsService;

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

    private MiniPlayerWindow? _miniPlayer;

    /// <summary>
    /// ウィンドウの状態が変更された際の処理を行います
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnStateChanged(System.EventArgs e)
    {
        base.OnStateChanged(e);

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
}
