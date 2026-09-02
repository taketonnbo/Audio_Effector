using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// アルバム（所属する楽曲の集約）を表すドメインエンティティ
/// </summary>
public class Album : IEquatable<Album>
{
    private readonly List<Track> _tracks;

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// アルバムアーティスト名
    /// </summary>
    public string Artist { get; }

    /// <summary>
    /// リリース年
    /// </summary>
    public uint Year { get; }

    /// <summary>
    /// アルバムに所属するトラック一覧（読み取り専用）
    /// </summary>
    public IReadOnlyList<Track> Tracks => _tracks.AsReadOnly();

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
    /// アルバムエンティティを初期化します
    /// </summary>
    /// <param name="name">アルバム名</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="year">リリース年</param>
    /// <param name="tracks">初期トラックコレクション</param>
    public Album(string name, string artist, uint year = 0, IEnumerable<Track>? tracks = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Unknown Album" : name.Trim();
        Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        Year = year;
        _tracks = tracks != null ? new List<Track>(tracks) : [];
    }

    /// <summary>
    /// アルバムにトラックを追加します
    /// </summary>
    /// <param name="track">追加対象のトラック</param>
    public void AddTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!_tracks.Contains(track))
        {
            _tracks.Add(track);
            _tracks.Sort((a, b) => a.TrackNumber.CompareTo(b.TrackNumber));
        }
    }

    /// <summary>
    /// アルバムからトラックを削除します
    /// </summary>
    /// <param name="track">削除対象のトラック</param>
    /// <returns>削除された場合はtrue、存在しなかった場合はfalse</returns>
    public bool RemoveTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return _tracks.Remove(track);
    }

    /// <summary>
    /// 同一性判定（アルバム名とアーティスト名で判定）
    /// </summary>
    /// <param name="other">比較対象のAlbum</param>
    /// <returns>同一の場合はtrue、それ以外はfalse</returns>
    public bool Equals(Album? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Artist, other.Artist, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// オブジェクト等価性判定
    /// </summary>
    /// <param name="obj">比較対象オブジェクト</param>
    /// <returns>等価な場合はtrue</returns>
    public override bool Equals(object? obj) => Equals(obj as Album);

    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Name),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Artist));

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>アルバム情報の文字列</returns>
    public override string ToString() => $"{Artist} - {Name} ({TrackCount} tracks)";
}
