using System.Windows.Media.Imaging;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Models
{
    /// <summary>
    /// 音楽トラックを表すクラス。
    /// </summary>
    public class Track : INotifyPropertyChanged, IEquatable<Track>
    {
        /// <summary>
        /// トラックのファイルパス。
        /// </summary>
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public BitmapImage CoverImage { get; set; }
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// 再生時間のフォーマット文字列（例: 03:45 または 1:23:45）。
        /// </summary>
        public string DurationString => Duration.TotalHours >= 1 
            ? Duration.ToString(@"h\:mm\:ss") 
            : Duration.ToString(@"mm\:ss");

        private bool _isFavorite;
        /// <summary>
        /// お気に入りに登録されているかどうか。
        /// </summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; OnPropertyChanged(); }
        }

        private bool _isPlaying;
        /// <summary>
        /// 現在再生中かどうか。
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        public uint Year { get; set; }
        public uint TrackNumber { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public string Format { get; set; }

        /// <summary>
        /// 可逆圧縮かどうか。
        /// </summary>
        public bool IsLossless { get; set; }

        /// <summary>
        /// ハイレゾ音源かどうか。
        /// </summary>
        public bool IsHiRes { get; set; }

        private bool _isSelected;
        /// <summary>
        /// UI上で選択されているかどうか。
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 音質情報のフォーマット済み文字列（例: 24bit/96.0kHz FLAC や 320kbps/44.1kHz MP3）。
        /// </summary>
        public string QualityInfo 
        {
            get
            {
                if (BitsPerSample > 0)
                {
                    return $"{BitsPerSample}bit/{SampleRate / 1000.0:F1}kHz {Format}";
                }
                else if (Bitrate > 0)
                {
                    return $"{Bitrate}kbps/{SampleRate / 1000.0:F1}kHz {Format}";
                }
                else
                {
                    return $"{SampleRate / 1000.0:F1}kHz {Format}";
                }
            }
        }

        /// <summary>
        /// 音質ラベル（"Hi-Res" または "Lossless"、それ以外は空）。
        /// </summary>
        public string QualityLabel => IsHiRes ? "Hi-Res" : (IsLossless ? "Lossless" : "");

        // Async Album Art Loading
        // アルバムアートの非同期読み込み用

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _artCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage>();
        private bool _isArtLoaded = false;
        private BitmapImage? _albumArt;

        /// <summary>
        /// 遅延読み込みされるアルバムアート。
        /// 初回アクセス時に非同期で読み込みを開始します。
        /// </summary>
        public BitmapImage? AlbumArt
        {
            get
            {
                if (!_isArtLoaded && _albumArt == null && !string.IsNullOrEmpty(FilePath))
                {
                    _isArtLoaded = true; // Prevent multiple triggers / 多重トリガー防止
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
                                                img.DecodePixelWidth = 100; // Thumbnail size / サムネイルサイズ
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

        #region Equality Members
        /// <summary>
        /// 同一のファイルパスを持つトラックかどうかを判定します。
        /// </summary>
        public bool Equals(Track? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            if (string.IsNullOrEmpty(FilePath) || string.IsNullOrEmpty(other.FilePath))
                return false;
            return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as Track);
        }

        public override int GetHashCode()
        {
            return string.IsNullOrEmpty(FilePath) ? base.GetHashCode() : StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
        }

        public static bool operator ==(Track? left, Track? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(Track? left, Track? right)
        {
            return !(left == right);
        }
        #endregion
    }
}
