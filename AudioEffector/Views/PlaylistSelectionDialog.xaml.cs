using System.Collections.ObjectModel;
using System.Windows;
using AudioEffector.Models;

namespace AudioEffector.Views
{
    public partial class PlaylistSelectionDialog : Window
    {
        public ObservableCollection<UserPlaylist> Playlists { get; set; }
        public UserPlaylist? SelectedPlaylist { get; set; }
        public Track Track { get; set; }

        public PlaylistSelectionDialog(ObservableCollection<UserPlaylist> playlists, Track track)
        {
            InitializeComponent();
            Playlists = playlists;
            Track = track;
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
