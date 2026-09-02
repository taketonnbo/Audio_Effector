using System;

namespace AudioEffector.Domain.Entities.DataTransfer;

/// <summary>
/// ポータブルデバイス（MTP）上のアルバムフォルダ情報を表すドメインエンティティ
/// </summary>
public class DeviceAlbum : IEquatable<DeviceAlbum>
{
    /// <summary>
    /// アルバムタイトル
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist { get; }

    /// <summary>
    /// デバイス上のアルバムフォルダパス
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// DeviceAlbumを初期化します
    /// </summary>
    /// <param name="title">アルバムタイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="path">デバイス上のフォルダパス</param>
    public DeviceAlbum(string title, string artist, string path)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Unknown Album" : title.Trim();
        Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        Path = path ?? string.Empty;
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
