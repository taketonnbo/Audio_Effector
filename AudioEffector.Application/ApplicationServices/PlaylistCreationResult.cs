using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// プレイリスト新規作成および初期トラック解決の結果を表します。
/// </summary>
/// <param name="Playlist">作成・永続化されたユーザープレイリスト。</param>
/// <param name="Tracks">プレイリストへ初期登録されたトラック一覧（収録順）。</param>
/// <param name="SkippedItems">重複指定により登録をスキップしたトラック識別子一覧。</param>
public sealed record PlaylistCreationResult(
    UserPlaylist Playlist,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<string> SkippedItems)
{
    /// <summary>
    /// プレイリスト作成結果を生成します。
    /// </summary>
    /// <param name="playlist">作成・永続化されたユーザープレイリスト。</param>
    /// <param name="tracks">初期登録されたトラック一覧。</param>
    /// <param name="skippedItems">重複指定により登録をスキップしたトラック識別子一覧。</param>
    /// <returns>生成された結果。</returns>
    public static PlaylistCreationResult Create(
        UserPlaylist playlist,
        IReadOnlyList<Track>? tracks = null,
        IReadOnlyList<string>? skippedItems = null)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        return new PlaylistCreationResult(
            Playlist: playlist,
            Tracks: tracks ?? Array.Empty<Track>(),
            SkippedItems: skippedItems ?? Array.Empty<string>());
    }
}
