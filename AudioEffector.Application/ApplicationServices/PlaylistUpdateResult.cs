using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// プレイリスト編集操作（楽曲追加・削除・並び替え・名称変更）の結果を表します。
/// </summary>
/// <param name="Playlist">更新・永続化されたユーザープレイリスト。</param>
/// <param name="Tracks">更新後の収録トラック一覧（収録順）。</param>
/// <param name="ChangedCount">今回変更された件数（追加件数・削除件数・移動件数・名称変更件数）。</param>
/// <param name="SkippedItems">重複指定や未一致によりスキップされたトラック識別子一覧。</param>
public sealed record PlaylistUpdateResult(
    UserPlaylist Playlist,
    IReadOnlyList<Track> Tracks,
    int ChangedCount,
    IReadOnlyList<string> SkippedItems)
{
    /// <summary>
    /// プレイリスト編集結果を生成します。
    /// </summary>
    /// <param name="playlist">更新・永続化されたユーザープレイリスト。</param>
    /// <param name="tracks">更新後の収録トラック一覧。</param>
    /// <param name="changedCount">今回変更された件数。</param>
    /// <param name="skippedItems">スキップされたトラック識別子一覧。</param>
    /// <returns>生成された結果。</returns>
    public static PlaylistUpdateResult Create(
        UserPlaylist playlist,
        IReadOnlyList<Track>? tracks = null,
        int changedCount = 0,
        IReadOnlyList<string>? skippedItems = null)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        return new PlaylistUpdateResult(
            Playlist: playlist,
            Tracks: tracks ?? Array.Empty<Track>(),
            ChangedCount: changedCount,
            SkippedItems: skippedItems ?? Array.Empty<string>());
    }
}
