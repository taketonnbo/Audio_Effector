using System;
using System.ComponentModel;
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

    /// <summary>
    /// ダイアログを閉じる前に、逆転送実行中であれば確認のうえ安全にキャンセルします
    /// </summary>
    /// <param name="e">キャンセル可能なイベントデータ</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is DeviceManagerViewModel vm && vm.IsImporting)
        {
            var result = MessageBox.Show(
                "現在、接続機器から PC への楽曲取り込みを実行中です。中止してダイアログを閉じますか？",
                "取り込み中止の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            vm.CancelImport(suppressResultDialog: true);
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// ダイアログが閉じた際に ViewModel のリソースを解放します
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as DeviceManagerViewModel)?.Dispose();
    }
}
