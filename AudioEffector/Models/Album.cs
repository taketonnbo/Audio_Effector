using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AudioEffector.Models
{
    public class Album : INotifyPropertyChanged
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public BitmapImage CoverImage { get; set; }
        public List<Track> Tracks { get; set; } = new List<Track>();
        public uint Year { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();

                    // Propagate selection to tracks
                    if (Tracks != null)
                    {
                        foreach (var track in Tracks)
                        {
                            track.IsSelected = value;
                        }
                    }
                }
            }
        }

        private bool _isOnDevice;
        public bool IsOnDevice
        {
            get => _isOnDevice;
            set
            {
                if (_isOnDevice != value)
                {
                    _isOnDevice = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
