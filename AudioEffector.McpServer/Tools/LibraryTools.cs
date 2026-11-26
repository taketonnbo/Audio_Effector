using System.ComponentModel;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AudioEffector.McpServer.Tools;

/// <summary>
/// 楽曲ライブラリの参照・検索機能を提供する MCP ツール群です。
/// </summary>
[McpServerToolType]
public sealed class LibraryTools
{
    private readonly LibraryApplicationService _libraryService;
    private readonly ILogger<LibraryTools> _logger;

    /// <summary>
    /// <see cref="LibraryTools"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="libraryService">楽曲ライブラリアプリケーションサービス。</param>
    /// <param name="logger">診断ロガー。</param>
    public LibraryTools(
        LibraryApplicationService libraryService,
        ILogger<LibraryTools> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryService);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryService = libraryService;
        _logger = logger;
    }

    /// <summary>
    /// 楽曲ライブラリをキーワード・各種メタデータフィルタ・お気に入り条件およびページネーションで検索します。
    /// </summary>
    /// <param name="keyword">タイトル・アーティスト・アルバムを対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="artist">アーティスト名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="album">アルバム名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="genre">ジャンル名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="isFavoriteOnly"><see langword="true"/> の場合、お気に入り登録済みの楽曲のみに絞り込みます（既定値: <see langword="false"/>）。</param>
    /// <param name="limit">返却する最大件数（1〜200、既定値: 50）。</param>
    /// <param name="offset">ページネーション用の取得開始位置（0 以上、既定値: 0）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>一致した楽曲一覧およびページネーション情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "search_tracks")]
    [Description("楽曲ライブラリをキーワードやアーティスト・アルバム・ジャンル・お気に入り条件で検索し、一致した楽曲一覧を取得します。")]
    public async Task<CallToolResult> SearchTracksAsync(
        [Description("タイトル・アーティスト・アルバムを対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）")] string? keyword = null,
        [Description("アーティスト名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）")] string? artist = null,
        [Description("アルバム名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）")] string? album = null,
        [Description("ジャンル名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）")] string? genre = null,
        [Description("true の場合、お気に入り登録済みの楽曲のみに絞り込みます（既定値: false）")] bool isFavoriteOnly = false,
        [Description("返却する最大件数（1〜200、既定値: 50）")] int limit = TrackSearchQuery.DEFAULT_LIMIT,
        [Description("ページネーション用の取得開始位置（0 以上、既定値: 0）")] int offset = TrackSearchQuery.DEFAULT_OFFSET,
        CancellationToken cancellationToken = default)
    {
        if (limit < TrackSearchQuery.MIN_LIMIT || limit > TrackSearchQuery.MAX_LIMIT)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"limit は {TrackSearchQuery.MIN_LIMIT}〜{TrackSearchQuery.MAX_LIMIT} の範囲で指定してください。",
                new
                {
                    parameter = "limit",
                    min = TrackSearchQuery.MIN_LIMIT,
                    max = TrackSearchQuery.MAX_LIMIT,
                });
        }

        if (offset < TrackSearchQuery.DEFAULT_OFFSET)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"offset は {TrackSearchQuery.DEFAULT_OFFSET} 以上で指定してください。",
                new
                {
                    parameter = "offset",
                    min = TrackSearchQuery.DEFAULT_OFFSET,
                });
        }

        try
        {
            var query = new TrackSearchQuery(
                Keyword: keyword,
                Artist: artist,
                Album: album,
                Genre: genre,
                IsFavoriteOnly: isFavoriteOnly,
                Limit: limit,
                Offset: offset);

            TrackSearchResult searchResult = await _libraryService
                .SearchTracksAsync(query, cancellationToken)
                .ConfigureAwait(false);

            var trackDtos = searchResult.Tracks
                .Select(TrackSummaryDto.FromEntity)
                .ToList();

            var response = new SearchTracksResponse(
                TotalCount: searchResult.TotalCount,
                Count: trackDtos.Count,
                Offset: searchResult.Offset,
                Limit: searchResult.Limit,
                Tracks: trackDtos);

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 楽曲ライブラリからキーワード・アーティスト・ジャンル・リリース年およびページネーション条件でアルバム一覧を検索します。
    /// </summary>
    /// <param name="keyword">アルバム名またはアーティスト名を対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="artist">アーティスト名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="genre">収録楽曲のジャンル名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
    /// <param name="year">リリース年での絞り込み条件（0 以上、省略可）。</param>
    /// <param name="includeTracks"><see langword="true"/> の場合、各アルバムの収録トラック一覧を含めます（既定値: <see langword="false"/>）。</param>
    /// <param name="limit">返却する最大件数（1〜200、既定値: 50）。</param>
    /// <param name="offset">ページネーション用の取得開始位置（0 以上、既定値: 0）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>一致したアルバム一覧およびページネーション情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "search_albums")]
    [Description("楽曲ライブラリをキーワード・アーティスト・ジャンル・リリース年で検索し、一致したアルバム一覧（任意で収録トラック一覧付き）を取得します。")]
    public async Task<CallToolResult> SearchAlbumsAsync(
        [Description("アルバム名またはアーティスト名を対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）")] string? keyword = null,
        [Description("アーティスト名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）")] string? artist = null,
        [Description("収録楽曲のジャンル名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）")] string? genre = null,
        [Description("リリース年での絞り込み条件（0 以上、省略可）")] int? year = null,
        [Description("true の場合、各アルバムの収録トラック一覧を含めます（既定値: false）")] bool includeTracks = false,
        [Description("返却する最大件数（1〜200、既定値: 50）")] int limit = AlbumSearchQuery.DEFAULT_LIMIT,
        [Description("ページネーション用の取得開始位置（0 以上、既定値: 0）")] int offset = AlbumSearchQuery.DEFAULT_OFFSET,
        CancellationToken cancellationToken = default)
    {
        if (limit < AlbumSearchQuery.MIN_LIMIT || limit > AlbumSearchQuery.MAX_LIMIT)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"limit は {AlbumSearchQuery.MIN_LIMIT}〜{AlbumSearchQuery.MAX_LIMIT} の範囲で指定してください。",
                new
                {
                    parameter = "limit",
                    min = AlbumSearchQuery.MIN_LIMIT,
                    max = AlbumSearchQuery.MAX_LIMIT,
                });
        }

        if (offset < AlbumSearchQuery.DEFAULT_OFFSET)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"offset は {AlbumSearchQuery.DEFAULT_OFFSET} 以上で指定してください。",
                new
                {
                    parameter = "offset",
                    min = AlbumSearchQuery.DEFAULT_OFFSET,
                });
        }

        if (year.HasValue && year.Value < AlbumSearchQuery.MIN_YEAR)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"year は {AlbumSearchQuery.MIN_YEAR} 以上で指定してください。",
                new
                {
                    parameter = "year",
                    min = AlbumSearchQuery.MIN_YEAR,
                });
        }

        try
        {
            var query = new AlbumSearchQuery(
                Keyword: keyword,
                Artist: artist,
                Genre: genre,
                Year: year,
                Limit: limit,
                Offset: offset);

            AlbumSearchResult searchResult = await _libraryService
                .SearchAlbumsAsync(query, cancellationToken)
                .ConfigureAwait(false);

            var albumDtos = searchResult.Albums
                .Select(album => AlbumSummaryDto.FromEntity(album, includeTracks))
                .ToList();

            var response = new SearchAlbumsResponse(
                TotalCount: searchResult.TotalCount,
                Count: albumDtos.Count,
                Offset: searchResult.Offset,
                Limit: searchResult.Limit,
                Albums: albumDtos);

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }
}
