using System.Configuration;
using System.Data;
using System.Windows;

namespace AudioEffector;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// コンストラクタ。未処理の例外イベントハンドラを登録します。
    /// </summary>
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    /// <summary>
    /// アプリケーション全体で発生した未処理の例外を処理します。
    /// </summary>
    /// <param name="sender">イベントのソース。</param>
    /// <param name="e">例外イベントのデータ。</param>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

