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
/// ユーザー作成プレイリストの参照・検索・作成機能を提供する MCP ツール群です。
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

    /// <summary>
    /// 指定されたプレイリスト名および任意の初期楽曲一覧から新しいユーザープレイリストを作成・永続化します。
    /// </summary>
    /// <param name="name">作成するプレイリスト名（必須、1〜200 文字、前後空白はトリム）。</param>
    /// <param name="trackIds">初期登録するトラック ID（GUID 文字列）の一覧（任意）。</param>
    /// <param name="filePaths">初期登録する音声ファイルの絶対パス一覧（任意）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>作成されたプレイリスト情報および登録件数を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "create_playlist")]
    [Description("指定したプレイリスト名と任意の初期楽曲一覧（trackIds / filePaths）から新しいユーザープレイリストを作成して永続化します。search_tracks の結果をそのまま trackIds へ渡して AI がプレイリストを自動作成でき、Audio Effector 本体が未起動の状態でも利用できます。")]
    public async Task<CallToolResult> CreatePlaylistAsync(
        [Description("作成するプレイリスト名（必須、1〜200 文字、前後空白はトリムされます）")] string name,
        [Description("初期登録するトラック ID（GUID 文字列）の一覧（省略可、search_tracks の結果をそのまま指定可能）")] string[]? trackIds = null,
        [Description("初期登録する音声ファイルの絶対パス一覧（省略可、trackIds と併用時は trackIds → filePaths の順で登録）")] string[]? filePaths = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "name（プレイリスト名）を指定してください。",
                new { parameter = "name" });
        }

        string trimmedName = name.Trim();
        if (trimmedName.Length > PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"name は {PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH} 文字以内で指定してください。",
                new
                {
                    parameter = "name",
                    maxLength = PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH,
                });
        }

        List<TrackId>? parsedTrackIds = null;
        if (trackIds is { Length: > 0 })
        {
            parsedTrackIds = new List<TrackId>(trackIds.Length);
            foreach (string rawTrackId in trackIds)
            {
                if (string.IsNullOrWhiteSpace(rawTrackId)
                    || !Guid.TryParse(rawTrackId.Trim(), out Guid parsedGuid)
                    || parsedGuid == Guid.Empty)
                {
                    return McpResponseFormatter.Error(
                        McpErrorCodes.INVALID_PARAMETER,
                        "trackIds には有効な GUID 文字列を指定してください。",
                        new { parameter = "trackIds" });
                }

                parsedTrackIds.Add(TrackId.From(parsedGuid));
            }
        }

        List<string>? normalizedFilePaths = null;
        if (filePaths is { Length: > 0 })
        {
            normalizedFilePaths = new List<string>(filePaths.Length);
            foreach (string rawFilePath in filePaths)
            {
                if (string.IsNullOrWhiteSpace(rawFilePath))
                {
                    return McpResponseFormatter.Error(
                        McpErrorCodes.INVALID_PARAMETER,
                        "filePaths に空のファイルパスは指定できません。",
                        new { parameter = "filePaths" });
                }

                string trimmedPath = rawFilePath.Trim();
                if (!Path.IsPathFullyQualified(trimmedPath))
                {
                    return McpResponseFormatter.Error(
                        McpErrorCodes.INVALID_PARAMETER,
                        "filePaths には絶対パスを指定してください。",
                        new { parameter = "filePaths" });
                }

                normalizedFilePaths.Add(trimmedPath);
            }
        }

        try
        {
            PlaylistCreationResult creationResult = await _playlistService
                .CreatePlaylistWithTracksAsync(trimmedName, parsedTrackIds, normalizedFilePaths, cancellationToken)
                .ConfigureAwait(false);

            PlaylistSummaryDto playlistDto = PlaylistSummaryDto.FromEntity(
                creationResult.Playlist,
                creationResult.Tracks);

            var response = new CreatePlaylistResponse(
                Playlist: playlistDto,
                AddedCount: creationResult.Tracks.Count,
                SkippedItems: creationResult.SkippedItems);

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }
}
