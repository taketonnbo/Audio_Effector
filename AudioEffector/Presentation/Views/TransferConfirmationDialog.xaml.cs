using System;
using System.Windows;
using System.Windows.Controls;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 機器転送開始前に空き容量不足の警告および形式変換対象の内訳を確認するためのダイアログ
/// </summary>
public partial class TransferConfirmationDialog : Window
{
    /// <summary>
    /// 転送前確認リクエスト情報を取得します
    /// </summary>
    public TransferConfirmationRequest Request { get; }

    /// <summary>
    /// ユーザーが転送の開始（続行）を選択したかどうかを取得します
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// 事前チェック結果を指定してダイアログを初期化します
    /// </summary>
    /// <param name="request">転送前確認リクエスト</param>
    public TransferConfirmationDialog(TransferConfirmationRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        InitializeComponent();
        DataContext = Request;

        if (Request.IsInsufficientSpace)
        {
            // 容量不足警告時は誤って Enter キーで転送開始しないよう、キャンセルを既定ボタンとする
            ProceedButton.IsDefault = false;
            ProceedButton.FontWeight = FontWeights.Normal;
            ProceedButton.SetResourceReference(Control.BackgroundProperty, "ControlBackgroundBrush");
            ProceedButton.SetResourceReference(Control.ForegroundProperty, "TextForegroundBrush");
            ProceedButton.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            ProceedButton.BorderThickness = new Thickness(1);

            CancelButton.IsDefault = true;
            CancelButton.FontWeight = FontWeights.SemiBold;
            CancelButton.SetResourceReference(Control.BackgroundProperty, "NeonCyanBrush");
            CancelButton.SetResourceReference(Control.ForegroundProperty, "InverseTextForegroundBrush");
            CancelButton.BorderThickness = new Thickness(0);
        }
    }

    private void ProceedButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        TryCloseDialog(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
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
