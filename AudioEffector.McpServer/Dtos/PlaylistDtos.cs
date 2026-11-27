using System.Diagnostics.CodeAnalysis;
using AudioEffector.Domain.Entities;

namespace AudioEffector.McpServer.Dtos;

/// <summary>
/// <c>search_playlists</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="TotalCount">検索条件に一致したプレイリスト総件数（ページネーション適用前）。</param>
/// <param name="Count">今回返却されたプレイリスト件数。</param>
/// <param name="Offset">適用された取得開始位置。</param>
/// <param name="Limit">適用された最大返却件数。</param>
/// <param name="Playlists">検索に一致したプレイリストのサマリ一覧。</param>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "カテゴリ単位の DTO 集約ファイル規約（MCPサーバ詳細設計書 5.1 準拠）")]
public sealed record SearchPlaylistsResponse(
    int TotalCount,
    int Count,
    int Offset,
    int Limit,
    IReadOnlyList<PlaylistSummaryDto> Playlists);

/// <summary>
/// <c>create_playlist</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="Playlist">作成されたプレイリストのサマリ情報（収録トラック一覧を含む）。</param>
/// <param name="AddedCount">プレイリストへ初期登録されたトラック件数。</param>
/// <param name="SkippedItems">重複指定により登録をスキップしたトラック識別子の一覧。</param>
public sealed record CreatePlaylistResponse(
    PlaylistSummaryDto Playlist,
    int AddedCount,
    IReadOnlyList<string> SkippedItems);

/// <summary>
/// プレイリスト編集ツール（<c>add_tracks_to_playlist</c> / <c>remove_tracks_from_playlist</c> / <c>reorder_playlist_track</c> / <c>rename_playlist</c>）の共通応答データ DTO です。
/// </summary>
/// <param name="Operation">実行された編集操作種別（<c>add_tracks</c> / <c>remove_tracks</c> / <c>reorder_track</c> / <c>rename</c>）。</param>
/// <param name="Playlist">編集後のプレイリストのサマリ情報（収録トラック一覧を含む）。</param>
/// <param name="ChangedCount">今回変更された件数（追加件数・削除件数・移動件数・名称変更件数）。</param>
/// <param name="SkippedItems">重複指定や未一致によりスキップされた識別子の一覧。</param>
public sealed record UpdatePlaylistResponse(
    string Operation,
    PlaylistSummaryDto Playlist,
    int ChangedCount,
    IReadOnlyList<string> SkippedItems);

/// <summary>
/// MCP 応答で返却するプレイリストのサマリ DTO です。
/// </summary>
/// <param name="Id">一意のプレイリスト ID（GUID）。</param>
/// <param name="Name">プレイリスト名。</param>
/// <param name="TrackCount">収録トラック数。</param>
/// <param name="CreatedAt">作成日時（UTC）。</param>
/// <param name="UpdatedAt">最終更新日時（UTC）。</param>
/// <param name="Tracks">収録トラック一覧（<c>includeTracks: true</c> 指定時のみ出力）。</param>
public sealed record PlaylistSummaryDto(
    Guid Id,
    string Name,
    int TrackCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<TrackSummaryDto>? Tracks = null)
{
    /// <summary>
    /// <see cref="UserPlaylist"/> ドメインエンティティから <see cref="PlaylistSummaryDto"/> を生成します。
    /// </summary>
    /// <param name="playlist">変換元のプレイリストエンティティ。</param>
    /// <param name="resolvedTracks">解決済み収録トラック一覧（<c>includeTracks: false</c> の場合は <see langword="null"/>）。</param>
    /// <returns>生成された DTO。</returns>
    public static PlaylistSummaryDto FromEntity(
        UserPlaylist playlist,
        IReadOnlyList<Track>? resolvedTracks = null)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        IReadOnlyList<TrackSummaryDto>? trackDtos = resolvedTracks is not null
            ? resolvedTracks.Select(TrackSummaryDto.FromEntity).ToList()
            : null;

        return new PlaylistSummaryDto(
            Id: playlist.Id.Value,
            Name: playlist.Name,
            TrackCount: playlist.TrackCount,
            CreatedAt: playlist.CreatedAt,
            UpdatedAt: playlist.UpdatedAt,
            Tracks: trackDtos);
    }
}
