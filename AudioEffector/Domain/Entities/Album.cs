using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// アルバム（所属する楽曲の集約）を表すドメインエンティティ
/// </summary>
public class Album : INotifyPropertyChanged, IEquatable<Album>
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private uint _year;
    private bool _isSelected;
    private bool _isOnDevice;
    private BitmapImage? _coverImage;
    private List<Track> _tracks = new();

    /// <summary>
    /// アルバム名（Titleプロパティの別名）
    /// </summary>
    public string Name
    {
        get => _title;
        set => Title = value;
    }

    /// <summary>
    /// アルバムのタイトル
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
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>
    /// アルバムアーティスト名
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
    /// アルバムのカバー画像
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
    /// アルバムに含まれるトラックのリスト
    /// </summary>
    public List<Track> Tracks
    {
        get => _tracks;
        set
        {
            _tracks = value ?? new List<Track>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(TotalDuration));
        }
    }

    /// <summary>
    /// トラック数
    /// </summary>
    public int TrackCount => _tracks.Count;

    /// <summary>
    /// アルバムの総再生時間
    /// </summary>
    public TimeSpan TotalDuration => _tracks.Aggregate(TimeSpan.Zero, (sum, track) => sum + track.Duration);

    /// <summary>
    /// ハイレゾ楽曲が含まれているかどうか
    /// </summary>
    public bool ContainsHiRes => _tracks.Any(t => t.IsHiRes);

    /// <summary>
    /// 可逆圧縮楽曲が含まれているかどうか
    /// </summary>
    public bool ContainsLossless => _tracks.Any(t => t.IsLossless);

    /// <summary>
    /// アルバムが選択されているかどうか
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

                if (_tracks != null)
                {
                    foreach (var track in _tracks)
                    {
                        track.IsSelected = value;
                    }
                }
            }
        }
    }

    /// <summary>
    /// アルバムがデバイス上に存在するかどうか
    /// </summary>
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

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public Album()
    {
    }

    /// <summary>
    /// アルバムエンティティを初期化します
    /// </summary>
    public Album(string name, string artist, uint year = 0, IEnumerable<Track>? tracks = null)
    {
        _title = string.IsNullOrWhiteSpace(name) ? "Unknown Album" : name.Trim();
        _artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        _year = year;
        _tracks = tracks != null ? new List<Track>(tracks) : new List<Track>();
    }

    /// <summary>
    /// トラックを追加します
    /// </summary>
    public void AddTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _tracks.Add(track);
        OnPropertyChanged(nameof(Tracks));
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(TotalDuration));
    }

    /// <summary>
    /// トラックを削除します
    /// </summary>
    /// <returns></returns>
    public bool RemoveTrack(Track track)
    {
        if (track is null) return false;
        var removed = _tracks.Remove(track);
        if (removed)
        {
            OnPropertyChanged(nameof(Tracks));
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(TotalDuration));
        }
        return removed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Equality Members
    public bool Equals(Album? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Artist, other.Artist, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as Album);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Name),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Artist));

    public override string ToString() => $"{Artist} - {Name} ({TrackCount} tracks)";
    #endregion
}
