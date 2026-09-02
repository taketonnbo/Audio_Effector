using System;
using System.Collections.Generic;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// ユーザー作成プレイリストを表すドメインエンティティ
/// </summary>
public class UserPlaylist : IEquatable<UserPlaylist>
{
    private readonly List<TrackId> _trackIds;

    /// <summary>
    /// 一意のプレイリストID
    /// </summary>
    public PlaylistId Id { get; }

    /// <summary>
    /// プレイリスト名
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// プレイリストに含まれるトラックIDのコレクション（順序保持）
    /// </summary>
    public IReadOnlyList<TrackId> TrackIds => _trackIds.AsReadOnly();

    /// <summary>
    /// トラック数
    /// </summary>
    public int TrackCount => _trackIds.Count;

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// プレイリストエンティティを初期化します
    /// </summary>
    /// <param name="id">プレイリストID</param>
    /// <param name="name">プレイリスト名</param>
    /// <param name="trackIds">初期トラックIDコレクション</param>
    /// <param name="createdAt">作成日時（未指定時は現在日時）</param>
    /// <param name="updatedAt">更新日時（未指定時は現在日時）</param>
    public UserPlaylist(
        PlaylistId id,
        string name,
        IEnumerable<TrackId>? trackIds = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("プレイリスト名を空にすることはできません", nameof(name));
        }

        Id = id;
        Name = name.Trim();
        _trackIds = trackIds != null ? new List<TrackId>(trackIds) : [];
        CreatedAt = createdAt ?? DateTime.UtcNow;
        UpdatedAt = updatedAt ?? DateTime.UtcNow;
    }

    /// <summary>
    /// プレイリスト名を変更します
    /// </summary>
    /// <param name="newName">新しいプレイリスト名</param>
    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("プレイリスト名を空にすることはできません", nameof(newName));
        }

        Name = newName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// プレイリストの末尾にトラックIDを追加します
    /// </summary>
    /// <param name="trackId">追加対象のトラックID</param>
    public void AddTrack(TrackId trackId)
    {
        _trackIds.Add(trackId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// プレイリストの指定位置にトラックIDを挿入します
    /// </summary>
    /// <param name="index">挿入位置インデックス</param>
    /// <param name="trackId">挿入対象のトラックID</param>
    public void InsertTrack(int index, TrackId trackId)
    {
        var clampedIndex = Math.Clamp(index, 0, _trackIds.Count);
        _trackIds.Insert(clampedIndex, trackId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// プレイリストから指定されたトラックIDを削除します（最初に見つかった1件）
    /// </summary>
    /// <param name="trackId">削除対象のトラックID</param>
    /// <returns>削除された場合はtrue、見つからなかった場合はfalse</returns>
    public bool RemoveTrack(TrackId trackId)
    {
        var removed = _trackIds.Remove(trackId);
        if (removed)
        {
            UpdatedAt = DateTime.UtcNow;
        }

        return removed;
    }

    /// <summary>
    /// プレイリストの指定インデックスのトラックを削除します
    /// </summary>
    /// <param name="index">削除対象のインデックス</param>
    /// <returns>削除された場合はtrue</returns>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _trackIds.Count) return false;

        _trackIds.RemoveAt(index);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// プレイリスト内のトラック順序を並び替えます
    /// </summary>
    /// <param name="oldIndex">移動元のインデックス</param>
    /// <param name="newIndex">移動先のインデックス</param>
    public void Reorder(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _trackIds.Count ||
            newIndex < 0 || newIndex >= _trackIds.Count ||
            oldIndex == newIndex)
        {
            return;
        }

        var item = _trackIds[oldIndex];
        _trackIds.RemoveAt(oldIndex);
        _trackIds.Insert(newIndex, item);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// プレイリスト内の全トラックをクリアします
    /// </summary>
    public void Clear()
    {
        _trackIds.Clear();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 同一性判定（PlaylistIdで判定）
    /// </summary>
    /// <param name="other">比較対象のUserPlaylist</param>
    /// <returns>同一の場合はtrue、それ以外はfalse</returns>
    public bool Equals(UserPlaylist? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id.Equals(other.Id);
    }

    /// <summary>
    /// オブジェクト等価性判定
    /// </summary>
    /// <param name="obj">比較対象オブジェクト</param>
    /// <returns>等価な場合はtrue</returns>
    public override bool Equals(object? obj) => Equals(obj as UserPlaylist);

    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>プレイリスト情報の文字列</returns>
    public override string ToString() => $"{Name} ({TrackCount} tracks)";
}
