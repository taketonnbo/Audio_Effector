using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 楽曲（トラック）を一意に識別する強型付けID
/// </summary>
public readonly record struct TrackId : IEquatable<TrackId>
{
    /// <summary>
    /// 内部GUID値
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// 指定されたGUIDでTrackIdを初期化します
    /// </summary>
    /// <param name="value">一意のGUID</param>
    public TrackId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("TrackIdに空のGUIDを指定することはできません", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// 新しいTrackIdを生成します
    /// </summary>
    /// <returns>新規生成されたTrackId</returns>
    public static TrackId New() => new(Guid.NewGuid());

    /// <summary>
    /// GUIDからTrackIdを生成します
    /// </summary>
    /// <param name="value">GUID値</param>
    /// <returns>生成されたTrackId</returns>
    public static TrackId From(Guid value) => new(value);

    /// <summary>
    /// 文字列からTrackIdを生成します
    /// </summary>
    /// <param name="value">GUID文字列</param>
    /// <returns>生成されたTrackId</returns>
    public static TrackId From(string value)
    {
        if (!Guid.TryParse(value, out var guid))
        {
            throw new ArgumentException($"不正なGUID文字列です: {value}", nameof(value));
        }

        return new TrackId(guid);
    }

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>GUID文字列</returns>
    public override string ToString() => Value.ToString();
}
