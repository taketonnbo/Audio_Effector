using System.Windows;
using System.Windows.Controls.Primitives;
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

        private void Slider_DragStarted(object sender, DragStartedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.IsDraggingProgress = true;
                vm.AudioService.PauseForSeek(); // Pause playback during seek
            }
        }

        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.IsDraggingProgress = false;
                vm.AudioService.ResumeAfterSeek(); // Resume playback after seek
            }
        }
    }
}