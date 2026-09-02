using System.Configuration;
using System.Data;
using System.Windows;
using NLog;

namespace AudioEffector;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// コンストラクタ。未処理の例外イベントハンドラを登録します。
    /// </summary>
    public App()
    {
        Logger.Info("アプリケーションを起動しています...");
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settingsService = new AudioEffector.Services.SettingsService();
        var settings = settingsService.LoadSettings();
        AudioEffector.Services.ThemeManager.ApplyTheme(settings.Theme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("アプリケーションを終了します。");
        LogManager.Shutdown();
        base.OnExit(e);
    }

    /// <summary>
    /// アプリケーション全体で発生した未処理の例外を処理します。
    /// </summary>
    /// <param name="sender">イベントのソース。</param>
    /// <param name="e">例外イベントのデータ。</param>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Fatal(e.Exception, "未処理の例外が発生しました。");
        MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

