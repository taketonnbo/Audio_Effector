using System.Windows;
using System.Windows.Input;

namespace AudioEffector.Views
{
    public partial class MiniPlayerWindow : Window
    {
        public MiniPlayerWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.Visibility != Visibility.Visible)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
            }
        }
    }
}
