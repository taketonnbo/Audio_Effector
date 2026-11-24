using System.Collections.Generic;
using System.Linq;

namespace AudioEffector.Domain.Entities;

/// <summary>再照合で得られたお気に入りの更新前後のメタデータ</summary>
/// <param name="Original">照合時のエントリ</param>
/// <param name="Updated">照合後のエントリ</param>
public sealed record FavoriteMetadataChange(FavoriteTrackEntry Original, FavoriteTrackEntry Updated)
{
    /// <summary>照合時と同じ状態で残っている項目にのみ変更を適用します</summary>
    /// <param name="entries">最新のお気に入り一覧</param>
    /// <param name="changes">再照合による変更一覧</param>
    /// <returns>更新した項目がある場合はtrue</returns>
    public static bool ApplyTo(List<FavoriteTrackEntry> entries, IReadOnlyList<FavoriteMetadataChange> changes)
    {
        var changesByDate = changes.ToLookup(change => change.Original.AddedAt);
        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var current = entries[i];
            foreach (var change in changesByDate[current.AddedAt])
            {
                var original = change.Original;
                if (original.LastKnownFilePath == current.LastKnownFilePath &&
                    original.Title == current.Title && original.Artist == current.Artist &&
                    original.Album == current.Album && original.TrackNumber == current.TrackNumber &&
                    original.DurationSeconds == current.DurationSeconds)
                {
                    entries[i] = change.Updated;
                    changed = true;
                    break;
                }
            }
        }
        return changed;
    }
}
