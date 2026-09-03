using System.Windows;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Views
{
    public partial class DeviceManagerDialog : Window
    {
        public DeviceManagerDialog(DeviceManagerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
