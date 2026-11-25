using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using AudioEffector.Application;
using AudioEffector.Infrastructure;
using AudioEffector.Infrastructure.Ipc;
using AudioEffector.Presentation;
using AudioEffector.Presentation.Views;
using AudioEffector.Application.ApplicationServices;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace AudioEffector;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "OnExit で単一インスタンス用 Mutex を明示的に解放・破棄するため")]
public partial class App : System.Windows.Application
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 同一ユーザーセッション内での多重起動防止に使用する名前付き Mutex 名。
    /// </summary>
    internal const string SINGLE_INSTANCE_MUTEX_NAME = @"Local\AudioEffector.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private AppIpcServer? _ipcServer;

    /// <summary>
    /// DIサービスプロバイダー
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; internal set; }

#if DEBUG
    private AudioEffector.Presentation.Diagnostics.BindingErrorDebugListener? _bindingErrorListener;
#endif

    /// <summary>
    /// コンストラクタ。未処理の例外イベントハンドラを登録します
    /// </summary>
    public App()
    {
        Logger.Info("アプリケーションを起動しています...");
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    /// <summary>
    /// アプリケーション起動時の初期化処理
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SINGLE_INSTANCE_MUTEX_NAME, out bool createdNew);
        if (!createdNew)
        {
            Logger.Info("Audio Effector はすでに起動しているため、新規インスタンスを終了します。");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        _ownsSingleInstanceMutex = true;
        var startupOptions = AppStartupOptions.Parse(e.Args);

#if DEBUG
        _bindingErrorListener = AudioEffector.Presentation.Diagnostics.BindingErrorDebugListener.StartListening();
#endif

        var services = new ServiceCollection();

        // 4層構造のDIサービス登録
        services.AddInfrastructureServices();
        services.AddApplicationServices();
        services.AddPresentationServices();

        ServiceProvider = services.BuildServiceProvider();

        // テーマ設定の適用
        var settingsService = ServiceProvider.GetRequiredService<ISettingsService>();
        var settings = settingsService.LoadSettings();
        AudioEffector.Presentation.Themes.ThemeManager.ApplyTheme(settings.Theme);

        // メインウィンドウの初期化と表示（DIコンテナ経由で解決）
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        this.MainWindow = mainWindow;
        if (startupOptions.StartInMiniPlayer)
        {
            Logger.Info("ミニプレイヤーモード（--mini-player）で起動します。");
            mainWindow.StartInMiniPlayerMode();
        }
        else if (startupOptions.ForceNormalWindow)
        {
            Logger.Info("通常ウィンドウモード（--normal）で起動します。");
            mainWindow.StartInNormalMode();
        }
        else
        {
            mainWindow.Show();
        }

        // MCP サーバとの Named Pipe IPC 待受を開始
        _ipcServer = new AppIpcServer();
        _ipcServer.Start();
    }

    /// <summary>
    /// アプリケーション終了時のリソース解放処理
    /// </summary>
    /// <param name="e">終了イベント引数</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("アプリケーションを終了します。");
        _ipcServer?.Dispose();
        _ipcServer = null;
        ReleaseSingleInstanceMutex();
        LogManager.Shutdown();
#if DEBUG
        _bindingErrorListener?.Dispose();
        _bindingErrorListener = null;
#endif
        base.OnExit(e);
    }

    private void ReleaseSingleInstanceMutex()
    {
        if (_singleInstanceMutex == null)
        {
            return;
        }

        if (_ownsSingleInstanceMutex)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 終了時のスレッド差分等による例外は安全に無視する
            }

            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
    }

    /// <summary>
    /// アプリケーション全体で発生した未処理の例外を処理します
    /// </summary>
    /// <param name="sender">イベントのソース</param>
    /// <param name="e">例外イベントのデータ</param>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Fatal(e.Exception, "未処理の例外が発生しました。");
        MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
