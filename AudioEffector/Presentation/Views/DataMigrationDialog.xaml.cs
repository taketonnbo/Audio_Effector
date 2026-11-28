using System.Windows;
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
    /// 変更前・変更後ディレクトリパスを指定してダイアログを初期化します
    /// </summary>
    /// <param name="sourceDirectory">変更前のデータ保存先ディレクトリ</param>
    /// <param name="targetDirectory">変更後のデータ保存先ディレクトリ</param>
    public DataMigrationDialog(string sourceDirectory, string targetDirectory)
    {
        InitializeComponent();
        SourceDirectoryTextBlock.Text = sourceDirectory;
        TargetDirectoryTextBlock.Text = targetDirectory;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.Copy;
        DialogResult = true;
    }

    private void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.Move;
        DialogResult = true;
    }

    private void SwitchOnlyButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.SwitchWithoutMigration;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = DataMigrationChoice.Cancel;
        DialogResult = false;
    }
}
