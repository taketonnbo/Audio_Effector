using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// アルバム（所属する楽曲の集約）を表すドメインエンティティ
/// </summary>
public class Album : INotifyPropertyChanged, IEquatable<Album>
{
    private readonly Dictionary<Track, WeakTrackOnDeviceSubscription> _trackSubscriptions = new();
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private uint _year;
    private bool _isSelected;
    private bool _isOnDevice;
    private bool _isTracksExpanded;
    private bool _suppressIsOnDeviceSync;
    private List<Track> _tracks = new();

    /// <summary>
    /// アルバムを一意に識別するID
    /// </summary>
    public AlbumId Id { get; }

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
            UnsubscribeTracks(_tracks);
            _tracks = value ?? new List<Track>();
            SubscribeTracks(_tracks);
            UpdateIsOnDeviceState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(TotalDuration));
            OnPropertyChanged(nameof(RepresentativeTrackFilePath));
            OnPropertyChanged(nameof(ContainsHiRes));
            OnPropertyChanged(nameof(ContainsLossless));
        }
    }

    /// <summary>
    /// カバーアート読み込み用の代表トラックファイルパス（トラックが空の場合は空文字）
    /// </summary>
    public string RepresentativeTrackFilePath =>
        _tracks.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.FilePath))?.FilePath ?? string.Empty;

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
            if (value && _isOnDevice)
            {
                return;
            }

            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();

                if (_tracks != null)
                {
                    foreach (var track in _tracks)
                    {
                        if (value)
                        {
                            if (!track.IsOnDevice)
                            {
                                track.IsSelected = true;
                            }
                        }
                        else
                        {
                            track.IsSelected = false;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// アルバムがデバイス上に存在するかどうか（所属トラック全曲が転送済みの場合にtrue）
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

                if (_isOnDevice && _isSelected)
                {
                    _isSelected = false;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
    }

    /// <summary>
    /// アルバム収録曲トレイ（ソケット）が展開されているかどうか
    /// </summary>
    public bool IsTracksExpanded
    {
        get => _isTracksExpanded;
        set
        {
            if (_isTracksExpanded != value)
            {
                _isTracksExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public Album()
    {
        Id = AlbumId.New();
    }

    /// <summary>
    /// アルバムエンティティを初期化します
    /// </summary>
    /// <param name="name">アルバム名</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="year">リリース年</param>
    /// <param name="tracks">トラックコレクション</param>
    /// <param name="id">アルバムID（未指定時は新規GUIDを採番）</param>
    public Album(string name, string artist, uint year = 0, IEnumerable<Track>? tracks = null, AlbumId? id = null)
    {
        Id = id ?? AlbumId.New();
        _title = string.IsNullOrWhiteSpace(name) ? "Unknown Album" : name.Trim();
        _artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        _year = year;
        _tracks = tracks != null ? new List<Track>(tracks) : new List<Track>();
        SubscribeTracks(_tracks);
        UpdateIsOnDeviceState();
    }

    /// <summary>
    /// トラックを追加します
    /// </summary>
    /// <param name="track">追加対象のトラック</param>
    public void AddTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _tracks.Add(track);
        SubscribeTrack(track);
        UpdateIsOnDeviceState();
        OnPropertyChanged(nameof(Tracks));
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(TotalDuration));
        OnPropertyChanged(nameof(RepresentativeTrackFilePath));
        OnPropertyChanged(nameof(ContainsHiRes));
        OnPropertyChanged(nameof(ContainsLossless));
    }

    /// <summary>
    /// トラックを削除します
    /// </summary>
    /// <param name="track">削除対象のトラック</param>
    /// <returns>削除に成功した場合はtrue、それ以外はfalse</returns>
    public bool RemoveTrack(Track track)
    {
        if (track is null) return false;
        var removed = _tracks.Remove(track);
        if (removed)
        {
            UnsubscribeTrack(track);
            UpdateIsOnDeviceState();
            OnPropertyChanged(nameof(Tracks));
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(TotalDuration));
            OnPropertyChanged(nameof(RepresentativeTrackFilePath));
            OnPropertyChanged(nameof(ContainsHiRes));
            OnPropertyChanged(nameof(ContainsLossless));
        }
        return removed;
    }

    /// <summary>
    /// 所属トラックの IsOnDevice 状態を一括更新し、最後に 1 回だけアルバム全体の IsOnDevice 状態を再評価します。
    /// </summary>
    /// <param name="isOnDeviceEvaluator">各トラックがデバイス上に存在するかを返す判定関数</param>
    public void ApplyTracksOnDeviceState(Func<Track, bool> isOnDeviceEvaluator)
    {
        ArgumentNullException.ThrowIfNull(isOnDeviceEvaluator);

        _suppressIsOnDeviceSync = true;
        try
        {
            foreach (var track in _tracks)
            {
                track.IsOnDevice = isOnDeviceEvaluator(track);
            }
        }
        finally
        {
            _suppressIsOnDeviceSync = false;
        }

        UpdateIsOnDeviceState();
    }

    /// <summary>
    /// 所属トラックの IsOnDevice 状態に基づき、アルバム全体の IsOnDevice 状態を更新します
    /// </summary>
    public void UpdateIsOnDeviceState()
    {
        IsOnDevice = _tracks.Count > 0 && _tracks.All(t => t.IsOnDevice);
    }

    private void SubscribeTracks(IEnumerable<Track> tracks)
    {
        foreach (var track in tracks)
        {
            SubscribeTrack(track);
        }
    }

    private void UnsubscribeTracks(IEnumerable<Track> tracks)
    {
        foreach (var track in tracks)
        {
            UnsubscribeTrack(track);
        }
    }

    private void SubscribeTrack(Track track)
    {
        if (_trackSubscriptions.ContainsKey(track))
        {
            return;
        }

        _trackSubscriptions[track] = new WeakTrackOnDeviceSubscription(this, track);
    }

    private void UnsubscribeTrack(Track track)
    {
        if (_trackSubscriptions.Remove(track, out var subscription))
        {
            subscription.Detach();
        }
    }

    private void OnTrackIsOnDeviceChanged()
    {
        if (_suppressIsOnDeviceSync)
        {
            return;
        }

        UpdateIsOnDeviceState();
    }

    /// <summary>
    /// Album が GC 回収された後に Track.PropertyChanged からの強参照リークを防ぐ弱イベントブリッジ
    /// </summary>
    private sealed class WeakTrackOnDeviceSubscription
    {
        private readonly WeakReference<Album> _albumRef;
        private readonly Track _track;

        public WeakTrackOnDeviceSubscription(Album album, Track track)
        {
            _albumRef = new WeakReference<Album>(album);
            _track = track;
            _track.PropertyChanged += OnPropertyChanged;
        }

        public void Detach()
        {
            _track.PropertyChanged -= OnPropertyChanged;
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Track.IsOnDevice))
            {
                return;
            }

            if (_albumRef.TryGetTarget(out var album))
            {
                album.OnTrackIsOnDeviceChanged();
            }
            else
            {
                _track.PropertyChanged -= OnPropertyChanged;
            }
        }
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
    /// 指定されたアルバムと等価であるかを判定します
    /// </summary>
    /// <param name="other">比較対象のアルバム</param>
    /// <returns>等価な場合はtrue、それ以外はfalse</returns>
    public bool Equals(Album? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id.Equals(other.Id);
    }

    /// <summary>
    /// 指定されたオブジェクトと等価であるかを判定します
    /// </summary>
    /// <param name="obj">比較対象のオブジェクト</param>
    /// <returns>等価な場合はtrue、それ以外はfalse</returns>
    public override bool Equals(object? obj) => Equals(obj as Album);

    /// <summary>
    /// アルバムのハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// アルバムの文字列表現を取得します
    /// </summary>
    /// <returns>アーティスト名とアルバム名およびトラック数</returns>
    public override string ToString() => $"{Artist} - {Name} ({TrackCount} tracks)";
    #endregion
}
