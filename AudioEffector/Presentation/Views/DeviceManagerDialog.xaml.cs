using System.Windows;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 外部デバイス管理ダイアログ
/// </summary>
public partial class DeviceManagerDialog : Window
{
    /// <summary>
    /// ViewModelを使用してインスタンスを初期化します
    /// </summary>
    /// <param name="viewModel">デバイス管理用ViewModel</param>
    public DeviceManagerDialog(DeviceManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
