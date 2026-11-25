using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// アルバムを一意に識別する強型付けID
/// </summary>
public readonly record struct AlbumId : IEquatable<AlbumId>
{
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
    /// 新しいAlbumIdを生成します
    /// </summary>
    /// <returns>新規生成されたAlbumId</returns>
    public static AlbumId New() => new(Guid.NewGuid());

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
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>GUID文字列</returns>
    public override string ToString() => Value.ToString();
}
