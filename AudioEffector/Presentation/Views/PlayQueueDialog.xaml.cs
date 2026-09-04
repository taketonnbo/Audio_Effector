using System.Windows;
using System.Windows.Input;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 再生キュー一覧を表示するダイアログ
/// </summary>
public partial class PlayQueueDialog : Window
{
    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public PlayQueueDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// マウスの左ボタン押下時にウィンドウのドラッグ移動を開始します
    /// </summary>
    /// <param name="e">イベントデータ</param>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
