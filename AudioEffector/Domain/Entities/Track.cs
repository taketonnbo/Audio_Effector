using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// 楽曲（音楽トラック）を表すドメインエンティティ
/// </summary>
public class Track : INotifyPropertyChanged, IEquatable<Track>
{
    private string _filePath = string.Empty;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private TimeSpan _duration;
    private uint _year;
    private uint _trackNumber;
    private int _bitrate;
    private int _sampleRate = 44100;
    private int _bitsPerSample = 16;
    private string _format = "MP3";
    private string _genre = string.Empty;
    private bool _isFavorite;
    private bool _isLossless;
    private bool _isHiRes;
    private bool _isPlaying;
    private bool _isSelected;
    private BitmapImage? _coverImage;

    /// <summary>
    /// 一意のトラックID
    /// </summary>
    public TrackId Id { get; }

    /// <summary>
    /// 音声ファイルのパス
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist
    {
        get => _artist;
        set
        {
            if (_artist != value)
            {
                _artist = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album
    {
        get => _album;
        set
        {
            if (_album != value)
            {
                _album = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// カバー画像
    /// </summary>
    public BitmapImage? CoverImage
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

    /// <summary>
    /// 再生時間
    /// </summary>
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (_duration != value)
            {
                _duration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationString));
            }
        }
    }

    /// <summary>
    /// 再生時間のフォーマット文字列（例: 03:45 または 1:23:45）
    /// </summary>
    public string DurationString => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : Duration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// お気に入りに登録されているかどうか
    /// </summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 現在再生中かどうか
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// リリース年
    /// </summary>
    public uint Year
    {
        get => _year;
        set
        {
            if (_year != value)
            {
                _year = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// トラック番号
    /// </summary>
    public uint TrackNumber
    {
        get => _trackNumber;
        set
        {
            if (_trackNumber != value)
            {
                _trackNumber = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// ビットレート（kbps）
    /// </summary>
    public int Bitrate
    {
        get => _bitrate;
        set
        {
            if (_bitrate != value)
            {
                _bitrate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityInfo));
            }
        }
    }

    /// <summary>
    /// サンプリング周波数（Hz）
    /// </summary>
    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (_sampleRate != value)
            {
                _sampleRate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityInfo));
            }
        }
    }

    /// <summary>
    /// 量子化ビット数（bit）
    /// </summary>
    public int BitsPerSample
    {
        get => _bitsPerSample;
        set
        {
            if (_bitsPerSample != value)
            {
                _bitsPerSample = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityInfo));
            }
        }
    }

    /// <summary>
    /// 音声フォーマット名（例: "FLAC", "MP3"）
    /// </summary>
    public string Format
    {
        get => _format;
        set
        {
            if (_format != value)
            {
                _format = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityInfo));
            }
        }
    }

    /// <summary>
    /// ジャンル名
    /// </summary>
    public string Genre
    {
        get => _genre;
        set
        {
            if (_genre != value)
            {
                _genre = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 可逆圧縮音源かどうか
    /// </summary>
    public bool IsLossless
    {
        get => _isLossless;
        set
        {
            if (_isLossless != value)
            {
                _isLossless = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityLabel));
            }
        }
    }

    /// <summary>
    /// ハイレゾ音源かどうか（96kHz以上または24bit以上）
    /// </summary>
    public bool IsHiRes
    {
        get => _isHiRes;
        set
        {
            if (_isHiRes != value)
            {
                _isHiRes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QualityLabel));
            }
        }
    }

    /// <summary>
    /// UI上で選択されているかどうか
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 音質情報のフォーマット済み文字列（例: 24bit/96.0kHz FLAC または 320kbps/44.1kHz MP3）
    /// </summary>
    public string QualityInfo
    {
        get
        {
            if (BitsPerSample > 0)
            {
                return $"{BitsPerSample}bit/{SampleRate / 1000.0:F1}kHz {Format}";
            }
            if (Bitrate > 0)
            {
                return $"{Bitrate}kbps/{SampleRate / 1000.0:F1}kHz {Format}";
            }
            return $"{SampleRate / 1000.0:F1}kHz {Format}";
        }
    }

    /// <summary>
    /// 音質ラベル（"Hi-Res" または "Lossless"、それ以外は空文字）
    /// </summary>
    public string QualityLabel => IsHiRes ? "Hi-Res" : (IsLossless ? "Lossless" : string.Empty);

    // Async Album Art Loading
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _artCache
        = new System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage>();
    private bool _isArtLoaded;
    private BitmapImage? _albumArt;

    /// <summary>
    /// 遅延読み込みされるアルバムアート
    /// </summary>
    public BitmapImage? AlbumArt
    {
        get
        {
            if (!_isArtLoaded && _albumArt == null && !string.IsNullOrEmpty(FilePath))
            {
                _isArtLoaded = true;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        string? dir = System.IO.Path.GetDirectoryName(FilePath);
                        string key = dir ?? string.Empty;

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
                                            img.DecodePixelWidth = 100;
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

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public Track()
    {
        Id = TrackId.New();
    }

    /// <summary>
    /// トラックエンティティを初期化します
    /// </summary>
    /// <param name="id">トラックID</param>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="title">曲名</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="duration">再生時間</param>
    /// <param name="year">リリース年</param>
    /// <param name="trackNumber">トラック番号</param>
    /// <param name="bitrate">ビットレート（kbps）</param>
    /// <param name="sampleRate">サンプリング周波数（Hz）</param>
    /// <param name="bitsPerSample">量子化ビット数</param>
    /// <param name="format">フォーマット形式</param>
    /// <param name="genre">ジャンル</param>
    /// <param name="isFavorite">お気に入り状態</param>
    /// <param name="isLossless">可逆圧縮かどうか</param>
    /// <param name="isHiRes">ハイレゾ音源かどうか</param>
    public Track(
        TrackId id,
        AudioPath filePath,
        string title,
        string artist,
        string album,
        TimeSpan duration,
        uint year = 0,
        uint trackNumber = 0,
        int bitrate = 0,
        int sampleRate = 44100,
        int bitsPerSample = 16,
        string format = "MP3",
        string genre = "",
        bool isFavorite = false,
        bool isLossless = false,
        bool isHiRes = false)
    {
        Id = id;
        _filePath = filePath.Value;
        _title = string.IsNullOrWhiteSpace(title) ? filePath.FileName : title.Trim();
        _artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        _album = string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album.Trim();
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _year = year;
        _trackNumber = trackNumber;
        _bitrate = bitrate;
        _sampleRate = sampleRate;
        _bitsPerSample = bitsPerSample;
        _format = string.IsNullOrWhiteSpace(format) ? "Unknown" : format.Trim();
        _genre = genre?.Trim() ?? string.Empty;
        _isFavorite = isFavorite;
        _isLossless = isLossless;
        _isHiRes = isHiRes;
    }

    /// <summary>
    /// お気に入り登録状態を更新します
    /// </summary>
    /// <param name="isFavorite">お気に入り状態</param>
    public void SetFavorite(bool isFavorite)
    {
        IsFavorite = isFavorite;
    }

    /// <summary>
    /// タイトルおよびアーティスト情報を更新します
    /// </summary>
    /// <param name="title">曲名</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    public void UpdateMetadata(string title, string artist, string album)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = title.Trim();
        if (!string.IsNullOrWhiteSpace(artist)) Artist = artist.Trim();
        if (!string.IsNullOrWhiteSpace(album)) Album = album.Trim();
    }

    /// <summary>
    /// プロパティ値変更時に発生するイベント
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// プロパティ変更通知を発行します
    /// </summary>
    /// <param name="propertyName">プロパティ名</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Equality Members
    /// <summary>
    /// 指定されたトラックと等価であるかを判定します
    /// </summary>
    /// <param name="other">比較対象のトラック</param>
    /// <returns>等価な場合はtrue、それ以外はfalse</returns>
    public bool Equals(Track? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        if (string.IsNullOrEmpty(FilePath) || string.IsNullOrEmpty(other.FilePath))
            return Id.Equals(other.Id);
        return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 指定されたオブジェクトと等価であるかを判定します
    /// </summary>
    /// <param name="obj">比較対象のオブジェクト</param>
    /// <returns>等価な場合はtrue、それ以外はfalse</returns>
    public override bool Equals(object? obj) => Equals(obj as Track);

    /// <summary>
    /// トラックのハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode()
    {
        return string.IsNullOrEmpty(FilePath) ? Id.GetHashCode() : StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
    }

    /// <summary>
    /// 2つのトラックが等価であるかを判定します
    /// </summary>
    /// <param name="left">左辺のトラック</param>
    /// <param name="right">右辺のトラック</param>
    /// <returns>等価な場合はtrue、それ以外はfalse</returns>
    public static bool operator ==(Track? left, Track? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    /// <summary>
    /// 2つのトラックが異なるかを判定します
    /// </summary>
    /// <param name="left">左辺のトラック</param>
    /// <param name="right">右辺のトラック</param>
    /// <returns>異なる場合はtrue、それ以外はfalse</returns>
    public static bool operator !=(Track? left, Track? right)
    {
        return !(left == right);
    }
    #endregion

    /// <summary>
    /// トラックの文字列表現を取得します
    /// </summary>
    /// <returns>アーティスト名と曲名および品質情報</returns>
    public override string ToString() => $"{Artist} - {Title} ({QualityInfo})";
}
