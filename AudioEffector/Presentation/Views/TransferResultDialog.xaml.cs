using System;
using System.Windows;
using AudioEffector.Application.Common;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 機器転送および逆転送の完了・キャンセル・中断後に結果サマリ（成功／スキップ／失敗一覧）を表示するダイアログ
/// </summary>
public partial class TransferResultDialog : Window
{
    /// <summary>
    /// 表示対象の転送結果サマリを取得します
    /// </summary>
    public TransferResultSummary Summary { get; }

    /// <summary>
    /// 転送結果サマリを指定してダイアログを初期化します
    /// </summary>
    /// <param name="summary">転送結果サマリ</param>
    public TransferResultDialog(TransferResultSummary summary)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        InitializeComponent();
        DataContext = Summary;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
        {
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }
}
