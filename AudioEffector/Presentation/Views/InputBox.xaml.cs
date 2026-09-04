using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 汎用的なテキスト入力ダイアログ
/// </summary>
public partial class InputBox : Window, INotifyPropertyChanged
{
    private string _inputText = string.Empty;

    /// <summary>
    /// 入力されたテキスト
    /// </summary>
    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText != value)
            {
                _inputText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// ダイアログに表示するメッセージ
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// プロパティ変更イベント
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// PropertyChangedイベントを発行します
    /// </summary>
    /// <param name="propertyName">変更されたプロパティ名</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// メッセージと初期テキストを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="message">表示メッセージ</param>
    /// <param name="defaultText">テキストボックスの初期値</param>
    public InputBox(string message, string defaultText = "")
    {
        InitializeComponent();
        DataContext = this;
        Message = message;
        InputText = defaultText;

        Loaded += (s, e) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };

        MouseLeftButtonDown += (s, e) => DragMove();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
