using System;
using System.Windows;
using AudioEffector.Application;
using AudioEffector.Infrastructure;
using AudioEffector.Presentation;
using AudioEffector.Presentation.Views;
using AudioEffector.Services;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace AudioEffector;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// DIサービスプロバイダー
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; private set; }

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

        var services = new ServiceCollection();

        // 4層構造のDIサービス登録
        services.AddInfrastructureServices();
        services.AddApplicationServices();
        services.AddPresentationServices();

        ServiceProvider = services.BuildServiceProvider();

        // テーマ設定の適用
        var settingsService = new AudioEffector.Services.SettingsService();
        var settings = settingsService.LoadSettings();
        AudioEffector.Presentation.Themes.ThemeManager.ApplyTheme(settings.Theme);

        // メインウィンドウの表示（DIコンテナ経由で解決）
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        this.MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// アプリケーション終了時のリソース解放処理
    /// </summary>
    /// <param name="e">終了イベント引数</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("アプリケーションを終了します。");
        LogManager.Shutdown();
        base.OnExit(e);
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
