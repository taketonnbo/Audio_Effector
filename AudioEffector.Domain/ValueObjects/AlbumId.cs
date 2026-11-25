using System;
using System.Security.Cryptography;
using System.Text;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// アルバムを一意に識別する決定論的な強型付けID
/// </summary>
public readonly record struct AlbumId : IEquatable<AlbumId>
{
    /// <summary>
    /// アルバムキーから決定論的GUIDを生成するための固定名前空間GUID
    /// </summary>
    private static readonly Guid AlbumKeyNamespace = new("a8f5e3c2-7b41-4d90-9c1e-3f6a2d8b5e04");

    /// <summary>
    /// 内部GUID値
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// 指定されたGUIDでAlbumIdを初期化します
    /// </summary>
    /// <param name="value">一意のGUID</param>
    public AlbumId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("AlbumIdに空のGUIDを指定することはできません", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// GUIDからAlbumIdを生成します
    /// </summary>
    /// <param name="value">GUID値</param>
    /// <returns>生成されたAlbumId</returns>
    public static AlbumId From(Guid value) => new(value);

    /// <summary>
    /// 文字列からAlbumIdを生成します
    /// </summary>
    /// <param name="value">GUID文字列</param>
    /// <returns>生成されたAlbumId</returns>
    public static AlbumId From(string value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"不正なGUID文字列です: {value}", nameof(value));
        }

        return new AlbumId(guid);
    }

    /// <summary>
    /// アルバム名とアーティスト名の組み合わせから決定論的なAlbumIdを生成します
    /// </summary>
    /// <param name="albumName">アルバム名</param>
    /// <param name="artist">アーティスト名</param>
    /// <returns>正規化キーから決定論的に生成されたAlbumId</returns>
    public static AlbumId FromAlbumKey(string? albumName, string? artist)
    {
        string normalizedAlbum = NormalizeKeyPart(albumName);
        string normalizedArtist = NormalizeKeyPart(artist);
        string compositeKey = $"{normalizedAlbum.Length}:{normalizedAlbum}\0{normalizedArtist.Length}:{normalizedArtist}";

        byte[] namespaceBytes = AlbumKeyNamespace.ToByteArray(bigEndian: true);
        byte[] keyBytes = Encoding.UTF8.GetBytes(compositeKey);
        byte[] input = new byte[namespaceBytes.Length + keyBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(keyBytes, 0, input, namespaceBytes.Length, keyBytes.Length);

        byte[] hash = SHA256.HashData(input);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);

        // RFC 4122 UUID v5 相当のバージョンビット (5) およびバリアントビット (RFC 4122) を設定
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new AlbumId(new Guid(guidBytes, bigEndian: true));
    }

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>GUID文字列</returns>
    public override string ToString() => Value.ToString();

    private static string NormalizeKeyPart(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
