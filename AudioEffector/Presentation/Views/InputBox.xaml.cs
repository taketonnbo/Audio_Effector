using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AudioEffector.Presentation.Views
{
    /// <summary>
    /// 汎用的なテキスト入力ダイアログ。
    /// </summary>
    public partial class InputBox : Window, INotifyPropertyChanged
    {
        private string _inputText;

        /// <summary>
        /// 入力されたテキスト。
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
        /// ダイアログに表示するメッセージ。
        /// </summary>
        public string Message { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// InputBoxのコンストラクタ。
        /// </summary>
        /// <param name="message">表示メッセージ。</param>
        /// <param name="defaultText">テキストボックスの初期値。</param>
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
}
