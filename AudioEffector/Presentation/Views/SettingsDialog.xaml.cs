using System.Windows;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
