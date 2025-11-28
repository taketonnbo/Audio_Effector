using System.Windows;
using AudioEffector.ViewModels;

namespace AudioEffector
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            (DataContext as MainViewModel)?.Cleanup();
        }
    }
}