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
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }
}
