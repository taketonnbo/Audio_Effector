using System.Windows.Media.Imaging;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Models
{
    public class Track : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public BitmapImage CoverImage { get; set; }
        public TimeSpan Duration { get; set; }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; OnPropertyChanged(); }
        }

        public uint Year { get; set; }
        public uint TrackNumber { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public string Format { get; set; }
        public bool IsLossless { get; set; }
        public bool IsHiRes { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string QualityInfo => $"{BitsPerSample}bit/{SampleRate / 1000.0:F1}kHz {Format}";
        public string QualityLabel => IsHiRes ? "Hi-Res" : (IsLossless ? "Lossless" : "");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
