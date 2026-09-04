using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Domain.Entities.DataTransfer;

/// <summary>
/// ポータブルデバイス（MTP）上の楽曲ファイル情報を表すドメインエンティティ
/// </summary>
public class DeviceTrack : IEquatable<DeviceTrack>, INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _path = string.Empty;
    private long _fileSizeBytes;

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
    /// デバイス上のファイルパス
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            if (_path != value)
            {
                _path = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set
        {
            if (_fileSizeBytes != value)
            {
                _fileSizeBytes = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public DeviceTrack() : this(string.Empty, string.Empty, string.Empty, string.Empty, 0)
    {
    }

    /// <summary>
    /// DeviceTrackを初期化します
    /// </summary>
    /// <param name="title">楽曲タイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="path">デバイス上のファイルパス</param>
    /// <param name="fileSizeBytes">ファイルサイズ（バイト）</param>
    public DeviceTrack(string title, string artist, string album, string path, long fileSizeBytes = 0)
    {
        _title = string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileName(path) : title.Trim();
        _artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        _album = string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album.Trim();
        _path = path ?? string.Empty;
        _fileSizeBytes = fileSizeBytes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 同一性判定（デバイス上のファイルパスで比較）
    /// </summary>
    /// <param name="other">比較対象のDeviceTrack</param>
    /// <returns>同一の場合はtrue、それ以外はfalse</returns>
    public bool Equals(DeviceTrack? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// オブジェクト等価性判定
    /// </summary>
    /// <param name="obj">比較対象オブジェクト</param>
    /// <returns>等価な場合はtrue</returns>
    public override bool Equals(object? obj) => Equals(obj as DeviceTrack);

    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Path);

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>トラック情報の文字列</returns>
    public override string ToString() => $"{Artist} - {Title} ({Path})";
}
