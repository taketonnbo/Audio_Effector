using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AudioEffector.Views
{
    public partial class InputBox : Window, INotifyPropertyChanged
    {
        private string _inputText;

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

        public string Message { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
