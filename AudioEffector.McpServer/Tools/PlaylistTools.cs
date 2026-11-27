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
/// ユーザー作成プレイリストの参照・検索・作成・編集機能を提供する MCP ツール群です。
/// </summary>
[McpServerToolType]
public sealed class PlaylistTools
{
    private const string OPERATION_ADD_TRACKS = "add_tracks";
    private const string OPERATION_REMOVE_TRACKS = "remove_tracks";
    private const string OPERATION_REORDER_TRACK = "reorder_track";
    private const string OPERATION_RENAME = "rename";

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

    /// <summary>
    /// 既存のユーザー作成プレイリストへ 1 件以上の楽曲（トラック ID またはファイルパス）を一括追加します。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID（GUID 文字列、必須）。</param>
    /// <param name="trackIds">追加するトラック ID（GUID 文字列）の一覧（任意、filePaths とどちらか必須）。</param>
    /// <param name="filePaths">追加する音声ファイルの絶対パス一覧（任意、trackIds とどちらか必須）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>編集後のプレイリスト情報および追加件数を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "add_tracks_to_playlist")]
    [Description("既存のユーザー作成プレイリストへ楽曲（trackIds または filePaths）を一括追加して永続化します。既に収録済みの楽曲は重複登録せず skippedItems へ記録し、Audio Effector 本体が未起動の状態でも利用できます。")]
    public async Task<CallToolResult> AddTracksToPlaylistAsync(
        [Description("編集対象のプレイリスト ID（GUID 文字列、必須）")] string playlistId,
        [Description("追加するトラック ID（GUID 文字列）の一覧（filePaths 未指定時は必須）")] string[]? trackIds = null,
        [Description("追加する音声ファイルの絶対パス一覧（trackIds 未指定時は必須）")] string[]? filePaths = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePlaylistId(playlistId, out PlaylistId parsedPlaylistId, out CallToolResult? idError))
        {
            return idError!;
        }

        bool hasTrackIds = trackIds is { Length: > 0 };
        bool hasFilePaths = filePaths is { Length: > 0 };
        if (!hasTrackIds && !hasFilePaths)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "trackIds または filePaths のいずれかを 1 件以上指定してください。",
                new { parameter = "trackIds" });
        }

        if (!TryParseTrackIds(trackIds, out List<TrackId>? parsedTrackIds, out CallToolResult? trackIdsError))
        {
            return trackIdsError!;
        }

        if (!TryNormalizeFilePaths(filePaths, out List<string>? normalizedFilePaths, out CallToolResult? filePathsError))
        {
            return filePathsError!;
        }

        try
        {
            PlaylistUpdateResult? updateResult = await _playlistService
                .AddTracksAsync(parsedPlaylistId, parsedTrackIds, normalizedFilePaths, cancellationToken)
                .ConfigureAwait(false);

            if (updateResult is null)
            {
                return PlaylistNotFoundError();
            }

            return BuildUpdateResponse(OPERATION_ADD_TRACKS, updateResult);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 既存のユーザー作成プレイリストから、0 始まりインデックス配列またはトラック ID 配列に一致する楽曲を削除します。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID（GUID 文字列、必須）。</param>
    /// <param name="indices">削除する 0 始まりインデックスの一覧（任意、trackIds とどちらか必須）。</param>
    /// <param name="trackIds">削除するトラック ID（GUID 文字列）の一覧（任意、indices とどちらか必須）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>編集後のプレイリスト情報および削除件数を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "remove_tracks_from_playlist")]
    [Description("既存のユーザー作成プレイリストから、0 始まりインデックス配列（indices）またはトラック ID 配列（trackIds）に一致する楽曲を削除して永続化します。複数インデックス指定時もずれなく削除でき、Audio Effector 本体が未起動の状態でも利用できます。")]
    public async Task<CallToolResult> RemoveTracksFromPlaylistAsync(
        [Description("編集対象のプレイリスト ID（GUID 文字列、必須）")] string playlistId,
        [Description("削除する 0 始まりインデックスの一覧（trackIds 未指定時は必須）")] int[]? indices = null,
        [Description("削除するトラック ID（GUID 文字列）の一覧（indices 未指定時は必須）")] string[]? trackIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePlaylistId(playlistId, out PlaylistId parsedPlaylistId, out CallToolResult? idError))
        {
            return idError!;
        }

        bool hasIndices = indices is { Length: > 0 };
        bool hasTrackIds = trackIds is { Length: > 0 };
        if (!hasIndices && !hasTrackIds)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "indices または trackIds のいずれかを 1 件以上指定してください。",
                new { parameter = "indices" });
        }

        if (hasIndices)
        {
            foreach (int index in indices!)
            {
                if (index < 0)
                {
                    return McpResponseFormatter.Error(
                        McpErrorCodes.INVALID_PARAMETER,
                        "indices には 0 以上のインデックスを指定してください。",
                        new { parameter = "indices" });
                }
            }
        }

        if (!TryParseTrackIds(trackIds, out List<TrackId>? parsedTrackIds, out CallToolResult? trackIdsError))
        {
            return trackIdsError!;
        }

        try
        {
            PlaylistUpdateResult? updateResult = await _playlistService
                .RemoveTracksAsync(parsedPlaylistId, indices, parsedTrackIds, cancellationToken)
                .ConfigureAwait(false);

            if (updateResult is null)
            {
                return PlaylistNotFoundError();
            }

            return BuildUpdateResponse(OPERATION_REMOVE_TRACKS, updateResult);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 既存のユーザー作成プレイリスト内の指定位置の楽曲を別の位置へ移動して並び替えます。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID（GUID 文字列、必須）。</param>
    /// <param name="fromIndex">移動元の 0 始まりインデックス（必須）。</param>
    /// <param name="toIndex">移動先の 0 始まりインデックス（必須）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>編集後のプレイリスト情報および移動件数を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "reorder_playlist_track")]
    [Description("既存のユーザー作成プレイリスト内の指定位置（fromIndex）の楽曲を移動先（toIndex）へ並び替えて永続化します。Audio Effector 本体が未起動の状態でも利用できます。")]
    public async Task<CallToolResult> ReorderPlaylistTrackAsync(
        [Description("編集対象のプレイリスト ID（GUID 文字列、必須）")] string playlistId,
        [Description("移動元の 0 始まりインデックス（必須）")] int fromIndex,
        [Description("移動先の 0 始まりインデックス（必須）")] int toIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePlaylistId(playlistId, out PlaylistId parsedPlaylistId, out CallToolResult? idError))
        {
            return idError!;
        }

        if (fromIndex < 0)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "fromIndex は 0 以上で指定してください。",
                new { parameter = "fromIndex" });
        }

        if (toIndex < 0)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "toIndex は 0 以上で指定してください。",
                new { parameter = "toIndex" });
        }

        try
        {
            PlaylistUpdateResult? updateResult = await _playlistService
                .ReorderPlaylistTrackAsync(parsedPlaylistId, fromIndex, toIndex, cancellationToken)
                .ConfigureAwait(false);

            if (updateResult is null)
            {
                return PlaylistNotFoundError();
            }

            return BuildUpdateResponse(OPERATION_REORDER_TRACK, updateResult);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 既存のユーザー作成プレイリストの名称を変更して永続化します。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID（GUID 文字列、必須）。</param>
    /// <param name="newName">新しいプレイリスト名（必須、1〜200 文字、前後空白はトリム）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>編集後のプレイリスト情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "rename_playlist")]
    [Description("既存のユーザー作成プレイリストの名称を変更して永続化します。Audio Effector 本体が未起動の状態でも利用できます。")]
    public async Task<CallToolResult> RenamePlaylistAsync(
        [Description("編集対象のプレイリスト ID（GUID 文字列、必須）")] string playlistId,
        [Description("新しいプレイリスト名（必須、1〜200 文字、前後空白はトリムされます）")] string newName,
        CancellationToken cancellationToken = default)
    {
        if (!TryParsePlaylistId(playlistId, out PlaylistId parsedPlaylistId, out CallToolResult? idError))
        {
            return idError!;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "newName（新しいプレイリスト名）を指定してください。",
                new { parameter = "newName" });
        }

        string trimmedName = newName.Trim();
        if (trimmedName.Length > PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"newName は {PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH} 文字以内で指定してください。",
                new
                {
                    parameter = "newName",
                    maxLength = PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH,
                });
        }

        try
        {
            PlaylistUpdateResult? updateResult = await _playlistService
                .RenamePlaylistAsync(parsedPlaylistId, trimmedName, cancellationToken)
                .ConfigureAwait(false);

            if (updateResult is null)
            {
                return PlaylistNotFoundError();
            }

            return BuildUpdateResponse(OPERATION_RENAME, updateResult);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    private static bool TryParsePlaylistId(
        string? rawPlaylistId,
        out PlaylistId playlistId,
        out CallToolResult? errorResult)
    {
        if (string.IsNullOrWhiteSpace(rawPlaylistId)
            || !Guid.TryParse(rawPlaylistId.Trim(), out Guid parsedGuid)
            || parsedGuid == Guid.Empty)
        {
            playlistId = default;
            errorResult = McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "playlistId には有効な GUID 文字列を指定してください。",
                new { parameter = "playlistId" });
            return false;
        }

        playlistId = PlaylistId.From(parsedGuid);
        errorResult = null;
        return true;
    }

    private static bool TryParseTrackIds(
        string[]? trackIds,
        out List<TrackId>? parsedTrackIds,
        out CallToolResult? errorResult)
    {
        parsedTrackIds = null;
        errorResult = null;

        if (trackIds is not { Length: > 0 })
        {
            return true;
        }

        parsedTrackIds = new List<TrackId>(trackIds.Length);
        foreach (string rawTrackId in trackIds)
        {
            if (string.IsNullOrWhiteSpace(rawTrackId)
                || !Guid.TryParse(rawTrackId.Trim(), out Guid parsedGuid)
                || parsedGuid == Guid.Empty)
            {
                parsedTrackIds = null;
                errorResult = McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "trackIds には有効な GUID 文字列を指定してください。",
                    new { parameter = "trackIds" });
                return false;
            }

            parsedTrackIds.Add(TrackId.From(parsedGuid));
        }

        return true;
    }

    private static bool TryNormalizeFilePaths(
        string[]? filePaths,
        out List<string>? normalizedFilePaths,
        out CallToolResult? errorResult)
    {
        normalizedFilePaths = null;
        errorResult = null;

        if (filePaths is not { Length: > 0 })
        {
            return true;
        }

        normalizedFilePaths = new List<string>(filePaths.Length);
        foreach (string rawFilePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(rawFilePath))
            {
                normalizedFilePaths = null;
                errorResult = McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "filePaths に空のファイルパスは指定できません。",
                    new { parameter = "filePaths" });
                return false;
            }

            string trimmedPath = rawFilePath.Trim();
            if (!Path.IsPathFullyQualified(trimmedPath))
            {
                normalizedFilePaths = null;
                errorResult = McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "filePaths には絶対パスを指定してください。",
                    new { parameter = "filePaths" });
                return false;
            }

            normalizedFilePaths.Add(trimmedPath);
        }

        return true;
    }

    private static CallToolResult PlaylistNotFoundError()
    {
        return McpResponseFormatter.Error(
            McpErrorCodes.NOT_FOUND,
            "指定されたプレイリストが見つかりません。",
            new { parameter = "playlistId" });
    }

    private static CallToolResult BuildUpdateResponse(string operation, PlaylistUpdateResult updateResult)
    {
        PlaylistSummaryDto playlistDto = PlaylistSummaryDto.FromEntity(
            updateResult.Playlist,
            updateResult.Tracks);

        var response = new UpdatePlaylistResponse(
            Operation: operation,
            Playlist: playlistDto,
            ChangedCount: updateResult.ChangedCount,
            SkippedItems: updateResult.SkippedItems);

        return McpResponseFormatter.Success(response);
    }
}
