using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AudioEffector.Domain.Entities.DataTransfer;

/// <summary>
/// ポータブルデバイス（MTP）上のアルバムフォルダ情報を表すドメインエンティティ
/// </summary>
public class DeviceAlbum : IEquatable<DeviceAlbum>, INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _path = string.Empty;
    private BitmapImage? _coverImage;

    /// <summary>
    /// アルバムタイトル
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
    /// デバイス上のアルバムフォルダパス
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
    /// アルバムに含まれるデバイストラック一覧
    /// </summary>
    public ObservableCollection<DeviceTrack> Tracks { get; set; } = new();

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public DeviceAlbum() : this(string.Empty, string.Empty, string.Empty)
    {
    }

    /// <summary>
    /// DeviceAlbumを初期化します
    /// </summary>
    /// <param name="title">アルバムタイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="path">デバイス上のフォルダパス</param>
    public DeviceAlbum(string title, string artist, string path)
    {
        _title = string.IsNullOrWhiteSpace(title) ? "Unknown Album" : title.Trim();
        _artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        _path = path ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 同一性判定（フォルダパスで比較）
    /// </summary>
    /// <param name="other">比較対象のDeviceAlbum</param>
    /// <returns>同一の場合はtrue、それ以外はfalse</returns>
    public bool Equals(DeviceAlbum? other)
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
    public override bool Equals(object? obj) => Equals(obj as DeviceAlbum);

    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Path);

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>アルバム情報の文字列</returns>
    public override string ToString() => $"{Artist} - {Title} ({Path})";
}
