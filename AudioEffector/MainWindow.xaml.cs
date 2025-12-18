using System.Windows;
using System.Windows.Controls.Primitives;
using AudioEffector.ViewModels;

namespace AudioEffector
{
    /// <summary>
    /// アプリケーションのメインウィンドウ。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// ウィンドウが閉じられる際の処理。
        /// ViewModelのクリーンアップを行います。
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            (DataContext as MainViewModel)?.Cleanup();
        }

        /// <summary>
        /// スライダーのドラッグ開始時のイベントハンドラ。
        /// 再生を一時停止します。
        /// </summary>
        private void Slider_DragStarted(object sender, DragStartedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.IsDraggingProgress = true;
                vm.AudioService.PauseForSeek(); // シーク中は再生を一時停止する
            }
        }

        /// <summary>
        /// スライダーのドラッグ終了時のイベントハンドラ。
        /// 再生を再開します。
        /// </summary>
        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.IsDraggingProgress = false;
                vm.AudioService.ResumeAfterSeek(); // シーク後に再生を再開する
            }
        }
    }
}