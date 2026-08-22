using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AudioEffector.Models
{
    public class DeviceAlbum : INotifyPropertyChanged
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Path { get; set; } // The path of the album folder on the device
        
        private BitmapImage _coverImage;
        public BitmapImage CoverImage
        {
            get => _coverImage;
            set
            {
                if (_coverImage != value)
                {
                    _coverImage = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<DeviceTrack> Tracks { get; set; } = new ObservableCollection<DeviceTrack>();
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
