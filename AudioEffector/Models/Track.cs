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

        // Async Album Art Loading
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _artCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage>();
        private bool _isArtLoaded = false;
        private BitmapImage? _albumArt;

        public BitmapImage? AlbumArt
        {
            get
            {
                if (!_isArtLoaded && _albumArt == null && !string.IsNullOrEmpty(FilePath))
                {
                    _isArtLoaded = true; // Prevent multiple triggers
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            string? dir = System.IO.Path.GetDirectoryName(FilePath);
                            string key = dir ?? "";

                            if (_artCache.TryGetValue(key, out var cached))
                            {
                                _albumArt = cached;
                            }
                            else
                            {
                                if (TagLib.File.Create(FilePath) is var file && file.Tag.Pictures.Length > 0)
                                {
                                    var bin = file.Tag.Pictures[0].Data.Data;
                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        try
                                        {
                                            var img = new BitmapImage();
                                            using (var mem = new System.IO.MemoryStream(bin))
                                            {
                                                mem.Position = 0;
                                                img.BeginInit();
                                                img.DecodePixelWidth = 100; // Thumbnail size
                                                img.CacheOption = BitmapCacheOption.OnLoad;
                                                img.StreamSource = mem;
                                                img.EndInit();
                                            }
                                            img.Freeze();
                                            _albumArt = img;
                                            _artCache.TryAdd(key, img);
                                        }
                                        catch { }
                                    });
                                }
                            }
                        }
                        catch { }

                        if (_albumArt != null)
                        {
                            OnPropertyChanged(nameof(AlbumArt));
                        }
                    });
                }
                return _albumArt;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
