using System.ComponentModel;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AudioEffector.McpServer.Tools;

/// <summary>
/// 起動中の Audio Effector（WPF 本体）と連携して楽曲の再生制御を行う MCP ツール群です。
/// </summary>
[McpServerToolType]
public sealed class PlaybackTools
{
    private static readonly string[] RequiredTrackParameters = ["trackId", "filePath"];
    private static readonly string[] RequiredAlbumParameters = ["albumId", "albumName"];
    private static readonly string[] RequiredPlaylistParameters = ["playlistId", "playlistName"];

    private readonly IPlaybackControlGateway _playbackGateway;
    private readonly ILogger<PlaybackTools> _logger;

    /// <summary>
    /// <see cref="PlaybackTools"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="playbackGateway">再生制御ゲートウェイ。</param>
    /// <param name="logger">診断ロガー。</param>
    public PlaybackTools(
        IPlaybackControlGateway playbackGateway,
        ILogger<PlaybackTools> logger)
    {
        ArgumentNullException.ThrowIfNull(playbackGateway);
        ArgumentNullException.ThrowIfNull(logger);

        _playbackGateway = playbackGateway;
        _logger = logger;
    }

    /// <summary>
    /// 起動中の Audio Effector の現在の再生状態・再生中トラック・再生位置・音量・再生モード・キュー件数を取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>現在の再生状態スナップショットを含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "get_playback_state")]
    [Description("起動中の Audio Effector の現在の再生状態（playing / paused / stopped）、再生中または一時停止中のトラック情報、現在再生位置、総再生時間、音量、シャッフル・リピート状態、再生キュー件数を取得します（読み取り専用・副作用なし）。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> GetPlaybackStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PlaybackControlResult<PlaybackStateResponse> result = await _playbackGateway
                .GetPlaybackStateAsync(cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector の再生を一時停止します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>一時停止後の再生状態およびトラック情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "pause_playback")]
    [Description("起動中の Audio Effector の再生を一時停止します（すでに一時停止中の場合は状態を維持して paused を返します）。ウィンドウ状態は変更しません。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> PausePlaybackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PlaybackControlResult<PlaybackActionResponse> result = await _playbackGateway
                .PausePlaybackAsync(cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector の一時停止中の再生を再開します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>再開後の再生状態およびトラック情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "resume_playback")]
    [Description("起動中の Audio Effector の一時停止中の再生を再開します（すでに再生中の場合は状態を維持して playing を返します）。ウィンドウ状態は変更しません。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> ResumePlaybackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PlaybackControlResult<PlaybackActionResponse> result = await _playbackGateway
                .ResumePlaybackAsync(cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector の再生を停止します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>停止後の再生状態を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "stop_playback")]
    [Description("起動中の Audio Effector の再生を停止します（すでに停止中の場合は状態を維持して stopped を返します）。ウィンドウ状態は変更しません。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> StopPlaybackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PlaybackControlResult<PlaybackActionResponse> result = await _playbackGateway
                .StopPlaybackAsync(cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector で次の楽曲へ移動します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>移動後の再生状態およびトラック情報（次曲なし時は stopped / track: null）を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "next_track")]
    [Description("起動中の Audio Effector で再生キューの次の曲へ移動します（次曲がない場合は停止状態 stopped / track: null を返します）。ウィンドウ状態は変更しません。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> NextTrackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PlaybackControlResult<PlaybackActionResponse> result = await _playbackGateway
                .NextTrackAsync(cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector で前の楽曲（または現在曲の先頭）へ戻ります。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>移動後の再生状態およびトラック情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "previous_track")]
    [Description("起動中の Audio Effector で前の曲（再生位置が 3 秒超の場合は現在曲の先頭、先頭曲かつリピート有効時は末尾曲）へ戻ります。ウィンドウ状態は変更しません。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> PreviousTrackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            PlaybackControlResult<PlaybackActionResponse> result = await _playbackGateway
                .PreviousTrackAsync(cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector で指定されたトラック ID または音声ファイルパスの楽曲再生を開始します。
    /// </summary>
    /// <param name="trackId">再生対象トラックの ID（GUID 文字列。<paramref name="filePath"/> 未指定時は必須）。</param>
    /// <param name="filePath">再生対象音声ファイルの絶対パス（<paramref name="trackId"/> 未指定時は必須）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>再生開始結果およびトラック情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "play_track")]
    [Description("起動中の Audio Effector で指定した楽曲（trackId または filePath）の再生を開始し、画面表示をリアルタイム同期します。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> PlayTrackAsync(
        [Description("再生対象トラックの TrackId（GUID 文字列。filePath 未指定時は必須）")] string? trackId = null,
        [Description("再生対象音声ファイルの絶対パス（trackId 未指定時は必須）")] string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        bool hasTrackId = !string.IsNullOrWhiteSpace(trackId);
        bool hasFilePath = !string.IsNullOrWhiteSpace(filePath);

        if (!hasTrackId && !hasFilePath)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "trackId または filePath のいずれかを指定してください。",
                new { parameters = RequiredTrackParameters });
        }

        TrackId? parsedTrackId = null;
        if (hasTrackId)
        {
            if (!Guid.TryParse(trackId!.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                return McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "trackId には有効な GUID 文字列を指定してください。",
                    new { parameter = "trackId" });
            }

            parsedTrackId = TrackId.From(parsedGuid);
        }

        string? normalizedFilePath = hasFilePath ? filePath!.Trim() : null;
        if (!hasTrackId && normalizedFilePath is not null && !Path.IsPathFullyQualified(normalizedFilePath))
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "filePath には絶対パスを指定してください。",
                new { parameter = "filePath" });
        }

        try
        {
            PlaybackControlResult<PlayTrackResponse> result = await _playbackGateway
                .PlayTrackAsync(parsedTrackId, normalizedFilePath, cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector で指定されたアルバム全体の再生を開始し、残りの収録曲を再生キューへ展開します。
    /// </summary>
    /// <param name="albumId">再生対象アルバムの ID（GUID 文字列。<paramref name="albumName"/> 未指定時は必須。両方指定時は優先）。</param>
    /// <param name="albumName">再生対象のアルバム名（<paramref name="albumId"/> 未指定時は必須）。</param>
    /// <param name="artist"><paramref name="albumName"/> 指定時の同名アルバム絞り込み用アーティスト名（任意）。</param>
    /// <param name="startTrackId">途中曲から再生する場合の開始トラック ID（GUID 文字列、任意）。</param>
    /// <param name="startTrackIndex">途中曲から再生する場合の 0 始まりインデックス（<paramref name="startTrackId"/> 優先、任意）。</param>
    /// <param name="shuffle">シャッフル再生の有効／無効（省略時は現在の本体設定を維持）。</param>
    /// <param name="repeat">リピート再生の有効／無効（省略時は現在の本体設定を維持）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>アルバム再生開始結果および開始トラック情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "play_album")]
    [Description("起動中の Audio Effector で指定したアルバム（albumId または albumName）全体の再生を開始し、残りの収録曲をキューへ展開します。シャッフル・リピート・途中曲開始に対応します。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> PlayAlbumAsync(
        [Description("再生対象アルバムの AlbumId（GUID 文字列。albumName 未指定時は必須、両方指定時は albumId 優先）")] string? albumId = null,
        [Description("再生対象のアルバム名（albumId 未指定時は必須）")] string? albumName = null,
        [Description("albumName 指定時の同名アルバム絞り込み用アーティスト名（任意）")] string? artist = null,
        [Description("途中曲から再生する場合の開始トラック ID（GUID 文字列、任意）")] string? startTrackId = null,
        [Description("途中曲から再生する場合の 0 始まりインデックス（startTrackId 優先、0 以上、任意）")] int? startTrackIndex = null,
        [Description("シャッフル再生の有効／無効（省略時は現在設定を維持）")] bool? shuffle = null,
        [Description("リピート再生の有効／無効（省略時は現在設定を維持）")] bool? repeat = null,
        CancellationToken cancellationToken = default)
    {
        bool hasAlbumId = !string.IsNullOrWhiteSpace(albumId);
        bool hasAlbumName = !string.IsNullOrWhiteSpace(albumName);

        if (!hasAlbumId && !hasAlbumName)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "albumId または albumName のいずれかを指定してください。",
                new { parameters = RequiredAlbumParameters });
        }

        AlbumId? parsedAlbumId = null;
        if (hasAlbumId)
        {
            if (!Guid.TryParse(albumId!.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                return McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "albumId には有効な GUID 文字列を指定してください。",
                    new { parameter = "albumId" });
            }

            parsedAlbumId = AlbumId.From(parsedGuid);
        }

        if (!TryValidateStartTrackParameters(startTrackId, startTrackIndex, out TrackId? parsedStartTrackId, out CallToolResult? startParamError))
        {
            return startParamError!;
        }

        string? normalizedAlbumName = hasAlbumName ? albumName!.Trim() : null;
        string? normalizedArtist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();

        try
        {
            PlaybackControlResult<PlayCollectionResponse> result = await _playbackGateway
                .PlayAlbumAsync(
                    parsedAlbumId,
                    normalizedAlbumName,
                    normalizedArtist,
                    parsedStartTrackId,
                    startTrackIndex,
                    shuffle,
                    repeat,
                    cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// 起動中の Audio Effector で指定されたユーザー作成プレイリストの再生を開始し、残りの収録曲を再生キューへ展開します。
    /// </summary>
    /// <param name="playlistId">再生対象プレイリストの ID（GUID 文字列。<paramref name="playlistName"/> 未指定時は必須。両方指定時は優先）。</param>
    /// <param name="playlistName">再生対象のプレイリスト名（<paramref name="playlistId"/> 未指定時は必須）。</param>
    /// <param name="startTrackId">途中曲から再生する場合の開始トラック ID（GUID 文字列、任意）。</param>
    /// <param name="startTrackIndex">途中曲から再生する場合の 0 始まりインデックス（<paramref name="startTrackId"/> 優先、任意）。</param>
    /// <param name="shuffle">シャッフル再生の有効／無効（省略時は現在の本体設定を維持）。</param>
    /// <param name="repeat">リピート再生の有効／無効（省略時は現在の本体設定を維持）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>プレイリスト再生開始結果および開始トラック情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "play_playlist")]
    [Description("起動中の Audio Effector で指定したユーザー作成プレイリスト（playlistId または playlistName）の再生を開始し、残りの収録曲をキューへ展開します。シャッフル・リピート・途中曲開始に対応します。Audio Effector 本体が未起動の場合は、先に launch_app（例: mode: \"mini_player\"）で起動してから再実行してください。")]
    public async Task<CallToolResult> PlayPlaylistAsync(
        [Description("再生対象プレイリストの PlaylistId（GUID 文字列。playlistName 未指定時は必須、両方指定時は playlistId 優先）")] string? playlistId = null,
        [Description("再生対象のプレイリスト名（playlistId 未指定時は必須）")] string? playlistName = null,
        [Description("途中曲から再生する場合の開始トラック ID（GUID 文字列、任意）")] string? startTrackId = null,
        [Description("途中曲から再生する場合の 0 始まりインデックス（startTrackId 優先、0 以上、任意）")] int? startTrackIndex = null,
        [Description("シャッフル再生の有効／無効（省略時は現在設定を維持）")] bool? shuffle = null,
        [Description("リピート再生の有効／無効（省略時は現在設定を維持）")] bool? repeat = null,
        CancellationToken cancellationToken = default)
    {
        bool hasPlaylistId = !string.IsNullOrWhiteSpace(playlistId);
        bool hasPlaylistName = !string.IsNullOrWhiteSpace(playlistName);

        if (!hasPlaylistId && !hasPlaylistName)
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "playlistId または playlistName のいずれかを指定してください。",
                new { parameters = RequiredPlaylistParameters });
        }

        PlaylistId? parsedPlaylistId = null;
        if (hasPlaylistId)
        {
            if (!Guid.TryParse(playlistId!.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                return McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "playlistId には有効な GUID 文字列を指定してください。",
                    new { parameter = "playlistId" });
            }

            parsedPlaylistId = PlaylistId.From(parsedGuid);
        }

        if (!TryValidateStartTrackParameters(startTrackId, startTrackIndex, out TrackId? parsedStartTrackId, out CallToolResult? startParamError))
        {
            return startParamError!;
        }

        string? normalizedPlaylistName = hasPlaylistName ? playlistName!.Trim() : null;

        try
        {
            PlaybackControlResult<PlayCollectionResponse> result = await _playbackGateway
                .PlayPlaylistAsync(
                    parsedPlaylistId,
                    normalizedPlaylistName,
                    parsedStartTrackId,
                    startTrackIndex,
                    shuffle,
                    repeat,
                    cancellationToken)
                .ConfigureAwait(false);

            return FormatGatewayResult(result);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    private static bool TryValidateStartTrackParameters(
        string? startTrackId,
        int? startTrackIndex,
        out TrackId? parsedStartTrackId,
        out CallToolResult? errorResult)
    {
        parsedStartTrackId = null;
        errorResult = null;

        if (!string.IsNullOrWhiteSpace(startTrackId))
        {
            if (!Guid.TryParse(startTrackId.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                errorResult = McpResponseFormatter.Error(
                    McpErrorCodes.INVALID_PARAMETER,
                    "startTrackId には有効な GUID 文字列を指定してください。",
                    new { parameter = "startTrackId" });
                return false;
            }

            parsedStartTrackId = TrackId.From(parsedGuid);
        }

        if (startTrackIndex.HasValue && startTrackIndex.Value < 0)
        {
            errorResult = McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                "startTrackIndex には 0 以上の整数を指定してください。",
                new { parameter = "startTrackIndex", min = 0 });
            return false;
        }

        return true;
    }

    private static CallToolResult FormatGatewayResult<T>(PlaybackControlResult<T> result)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            return McpResponseFormatter.Success(result.Value);
        }

        string errorCode = string.IsNullOrWhiteSpace(result.ErrorCode)
            ? McpErrorCodes.INTERNAL_ERROR
            : result.ErrorCode;
        string errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? McpErrorMapper.GetPublicMessage(errorCode)
            : result.ErrorMessage;

        return McpResponseFormatter.Error(errorCode, errorMessage, result.Details);
    }
}
