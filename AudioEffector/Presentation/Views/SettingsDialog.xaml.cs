using System.Windows;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 設定画面ダイアログ
/// </summary>
public partial class SettingsDialog : Window
{
    /// <summary>
    /// ViewModelを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="viewModel">設定画面用ViewModel</param>
    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
