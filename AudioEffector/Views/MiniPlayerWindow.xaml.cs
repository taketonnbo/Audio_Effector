using System.Windows;
using System.Windows.Input;
using AudioEffector.Services;

namespace AudioEffector.Views
{
    public partial class MiniPlayerWindow : Window
    {
        private readonly ISettingsService _settingsService;

        public MiniPlayerWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            this.Loaded += MiniPlayerWindow_Loaded;
        }

        private void MiniPlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.LoadSettings();
            this.Topmost = settings.MiniPlayerAlwaysOnTop;

            if (settings.MiniPlayerTop.HasValue && settings.MiniPlayerLeft.HasValue)
            {
                double top = settings.MiniPlayerTop.Value;
                double left = settings.MiniPlayerLeft.Value;

                // Ensure the window is within the virtual screen bounds
                if (left < SystemParameters.VirtualScreenWidth - 50 &&
                    top < SystemParameters.VirtualScreenHeight - 50 &&
                    left + this.Width > SystemParameters.VirtualScreenLeft &&
                    top + this.Height > SystemParameters.VirtualScreenTop)
                {
                    this.Top = top;
                    this.Left = left;
                }
            }
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
            // Save window position
            var settings = _settingsService.LoadSettings();
            settings.MiniPlayerTop = this.Top;
            settings.MiniPlayerLeft = this.Left;
            _settingsService.SaveSettings(settings);

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
