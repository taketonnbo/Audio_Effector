using System.ComponentModel;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AudioEffector.McpServer.Tools;

/// <summary>
/// ユーザー作成プレイリストの参照・検索機能を提供する MCP ツール群です。
/// </summary>
[McpServerToolType]
public sealed class PlaylistTools
{
    private readonly PlaylistApplicationService _playlistService;
    private readonly ILogger<PlaylistTools> _logger;

    /// <summary>
    /// <see cref="PlaylistTools"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="playlistService">プレイリストアプリケーションサービス。</param>
    /// <param name="logger">診断ロガー。</param>
    public PlaylistTools(
        PlaylistApplicationService playlistService,
        ILogger<PlaylistTools> logger)
    {
        ArgumentNullException.ThrowIfNull(playlistService);
        ArgumentNullException.ThrowIfNull(logger);

        _playlistService = playlistService;
        _logger = logger;
    }

    /// <summary>
    /// ユーザー作成プレイリストをキーワードおよびページネーション条件で検索します。
    /// </summary>
    /// <param name="keyword">プレイリスト名を対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="includeTracks"><see langword="true"/> の場合、各プレイリストの収録トラック一覧を含めます（既定値: <see langword="false"/>）。</param>
    /// <param name="limit">返却する最大件数（1〜200、既定値: 50）。</param>
    /// <param name="offset">ページネーション用の取得開始位置（0 以上、既定値: 0）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>一致したプレイリスト一覧およびページネーション情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "search_playlists")]
    [Description("ユーザー作成プレイリストをキーワードで検索し、一致したプレイリスト一覧（任意で収録トラック一覧付き）を取得します。")]
    public async Task<CallToolResult> SearchPlaylistsAsync(
        [Description("プレイリスト名を対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）")] string? keyword = null,
        [Description("true の場合、各プレイリストの収録トラック一覧を含めます（既定値: false）")] bool includeTracks = false,
        [Description("返却する最大件数（1〜200、既定値: 50）")] int limit = PlaylistSearchQuery.DEFAULT_LIMIT,
        [Description("ページネーション用の取得開始位置（0 以上、既定値: 0）")] int offset = PlaylistSearchQuery.DEFAULT_OFFSET,
        CancellationToken cancellationToken = default)
    {
        if (limit < PlaylistSearchQuery.MIN_LIMIT || limit > PlaylistSearchQuery.MAX_LIMIT)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"limit は {PlaylistSearchQuery.MIN_LIMIT}〜{PlaylistSearchQuery.MAX_LIMIT} の範囲で指定してください。",
                new
                {
                    parameter = "limit",
                    min = PlaylistSearchQuery.MIN_LIMIT,
                    max = PlaylistSearchQuery.MAX_LIMIT,
                });
        }

        if (offset < PlaylistSearchQuery.DEFAULT_OFFSET)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"offset は {PlaylistSearchQuery.DEFAULT_OFFSET} 以上で指定してください。",
                new
                {
                    parameter = "offset",
                    min = PlaylistSearchQuery.DEFAULT_OFFSET,
                });
        }

        try
        {
            var query = new PlaylistSearchQuery(
                Keyword: keyword,
                Limit: limit,
                Offset: offset);

            PlaylistSearchResult searchResult = await _playlistService
                .SearchPlaylistsAsync(query, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyDictionary<PlaylistId, IReadOnlyList<Track>>? tracksByPlaylist = null;
            if (includeTracks && searchResult.Playlists.Count > 0)
            {
                tracksByPlaylist = await _playlistService
                    .ResolvePlaylistTracksMapAsync(searchResult.Playlists, cancellationToken)
                    .ConfigureAwait(false);
            }

            var playlistDtos = searchResult.Playlists
                .Select(playlist =>
                {
                    IReadOnlyList<Track>? resolvedTracks = null;
                    if (includeTracks)
                    {
                        resolvedTracks = tracksByPlaylist is not null
                            && tracksByPlaylist.TryGetValue(playlist.Id, out IReadOnlyList<Track>? tracks)
                            ? tracks
                            : Array.Empty<Track>();
                    }

                    return PlaylistSummaryDto.FromEntity(playlist, resolvedTracks);
                })
                .ToList();

            var response = new SearchPlaylistsResponse(
                TotalCount: searchResult.TotalCount,
                Count: playlistDtos.Count,
                Offset: searchResult.Offset,
                Limit: searchResult.Limit,
                Playlists: playlistDtos);

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }
}
