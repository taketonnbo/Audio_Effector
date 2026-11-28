using System.Windows;
using System.Windows.Controls;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// データ保存先ディレクトリ変更時に、既存データファイルの移行方式（コピー／移動／移行せず切り替え／キャンセル）を確認するダイアログ
/// </summary>
public partial class DataMigrationDialog : Window
{
    /// <summary>
    /// ユーザーが選択した移行アクションを取得します
    /// </summary>
    public DataMigrationChoice SelectedChoice { get; private set; } = DataMigrationChoice.Cancel;

    /// <summary>
    /// 変更先ディレクトリに既存データが存在する警告状態かどうかを取得します
    /// </summary>
    public bool DestinationHasExistingData { get; }

    /// <summary>
    /// 変更前・変更後ディレクトリパスを指定してダイアログを初期化します
    /// </summary>
    /// <param name="sourceDirectory">変更前のデータ保存先ディレクトリ</param>
    /// <param name="targetDirectory">変更後のデータ保存先ディレクトリ</param>
    /// <param name="destinationHasExistingData">変更先フォルダに既存データファイルが存在する場合は true</param>
    public DataMigrationDialog(string sourceDirectory, string targetDirectory, bool destinationHasExistingData = false)
    {
        InitializeComponent();
        DestinationHasExistingData = destinationHasExistingData;
        SourceDirectoryTextBlock.Text = sourceDirectory;
        TargetDirectoryTextBlock.Text = targetDirectory;

        if (destinationHasExistingData)
        {
            DestinationOverwriteWarningBorder.Visibility = Visibility.Visible;
            // 移行先の既存データを誤って上書きしないよう、既定ボタンを「移行せず切り替え」へ切り替える
            CopyButton.IsDefault = false;
            CopyButton.FontWeight = FontWeights.Normal;
            CopyButton.SetResourceReference(Control.BackgroundProperty, "ControlBackgroundHighlightBrush");
            CopyButton.SetResourceReference(Control.ForegroundProperty, "TextForegroundBrush");

            SwitchOnlyButton.IsDefault = true;
            SwitchOnlyButton.FontWeight = FontWeights.SemiBold;
            SwitchOnlyButton.SetResourceReference(Control.BackgroundProperty, "NeonCyanBrush");
            SwitchOnlyButton.SetResourceReference(Control.ForegroundProperty, "InverseTextForegroundBrush");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.Copy;
        TryCloseDialog(true);
    }

    private void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.Move;
        TryCloseDialog(true);
    }

    private void SwitchOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.SwitchWithoutMigration;
        TryCloseDialog(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.Cancel;
        TryCloseDialog(false);
    }

    private void TryCloseDialog(bool result)
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
        {
            DialogResult = result;
        }
        else
        {
            Close();
        }
    }
}
