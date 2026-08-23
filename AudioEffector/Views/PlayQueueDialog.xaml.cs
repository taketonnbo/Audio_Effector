using System.Windows;
using System.Windows.Input;

namespace AudioEffector.Views
{
    public partial class PlayQueueDialog : Window
    {
        public PlayQueueDialog()
        {
            InitializeComponent();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
