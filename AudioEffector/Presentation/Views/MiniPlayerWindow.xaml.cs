using System;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views
{
    public partial class MiniPlayerWindow : Window
    {
        private readonly ISettingsService _settingsService;
        private MiniPlayerTopmostBehavior _currentBehavior;

        public MiniPlayerWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsApplicationService(new AudioEffector.Infrastructure.Repository.JsonSettingsRepository());
            this.Loaded += MiniPlayerWindow_Loaded;
            this.Deactivated += MiniPlayerWindow_Deactivated;
        }

        private void MiniPlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.LoadSettings();
            _currentBehavior = settings.MiniPlayerTopmostBehavior;
            ApplyTopmostBehavior();

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

        public void UpdateTopmostBehavior(MiniPlayerTopmostBehavior behavior)
        {
            _currentBehavior = behavior;
            ApplyTopmostBehavior();
        }

        private void ApplyTopmostBehavior()
        {
            if (_currentBehavior == MiniPlayerTopmostBehavior.AlwaysOnTop || _currentBehavior == MiniPlayerTopmostBehavior.OnDisplayOnly)
            {
                this.Topmost = true;
            }
            else
            {
                this.Topmost = false;
            }
        }

        private void MiniPlayerWindow_Deactivated(object sender, EventArgs e)
        {
            if (_currentBehavior == MiniPlayerTopmostBehavior.OnDisplayOnly)
            {
                this.Topmost = false;
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
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.Visibility != Visibility.Visible)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
            }
        }
    }
}
