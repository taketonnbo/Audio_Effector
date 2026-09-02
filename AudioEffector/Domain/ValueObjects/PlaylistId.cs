using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// プレイリストを一意に識別する強型付けID
/// </summary>
public readonly record struct PlaylistId : IEquatable<PlaylistId>
{
    /// <summary>
    /// 内部GUID値
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// 指定されたGUIDでPlaylistIdを初期化します
    /// </summary>
    /// <param name="value">一意のGUID</param>
    public PlaylistId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("PlaylistIdに空のGUIDを指定することはできません", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// 新しいPlaylistIdを生成します
    /// </summary>
    /// <returns>新規生成されたPlaylistId</returns>
    public static PlaylistId New() => new(Guid.NewGuid());

    /// <summary>
    /// GUIDからPlaylistIdを生成します
    /// </summary>
    /// <param name="value">GUID値</param>
    /// <returns>生成されたPlaylistId</returns>
    public static PlaylistId From(Guid value) => new(value);

    /// <summary>
    /// 文字列からPlaylistIdを生成します
    /// </summary>
    /// <param name="value">GUID文字列</param>
    /// <returns>生成されたPlaylistId</returns>
    public static PlaylistId From(string value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"不正なGUID文字列です: {value}", nameof(value));
        }

        return new PlaylistId(guid);
    }

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>GUID文字列</returns>
    public override string ToString() => Value.ToString();
}
