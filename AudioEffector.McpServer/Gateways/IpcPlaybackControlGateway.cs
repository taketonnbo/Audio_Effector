using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Ipc;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// Named Pipe IPC 経由で起動中の Audio Effector（WPF 本体）へ再生制御コマンドを送信するゲートウェイ実装です。
/// </summary>
public sealed class IpcPlaybackControlGateway : IPlaybackControlGateway
{
    /// <summary>
    /// IPC コマンド送受信に適用する既定のタイムアウト時間（3000ms）。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// Named Pipe 接続確立に適用する既定のタイムアウト時間（500ms）。
    /// </summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions IpcJsonOptions = new(McpJsonOptions.Default)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Action<ILogger, string, Exception?> LogWpfNotRunningDelegate =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2001, "IpcWpfAppNotRunning"),
            "Named Pipe への接続が確立できなかったため、Audio Effector 本体が未起動と判定しました（PipeName: {PipeName}）。");

    private static readonly Action<ILogger, string, Exception?> LogIpcCommunicationFailedDelegate =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2002, "IpcCommunicationFailed"),
            "Audio Effector 本体との IPC 通信に失敗またはタイムアウトしました（PipeName: {PipeName}）。");

    private static readonly Action<ILogger, string, string, Exception?> LogIpcErrorResponseDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2003, "IpcErrorResponse"),
            "Audio Effector 本体からエラー応答を受信しました（Code: {ErrorCode}, Detail: {Detail}）。");

    private readonly string _pipeName;
    private readonly TimeSpan _commandTimeout;
    private readonly TimeSpan _connectTimeout;
    private readonly IDataDirectoryProvider? _dataDirectoryProvider;
    private readonly ITrackRepository? _trackRepository;
    private readonly ILogger<IpcPlaybackControlGateway> _logger;

    /// <summary>
    /// 既定のユーザー専用パイプ名およびタイムアウト（3000ms）で <see cref="IpcPlaybackControlGateway"/> を初期化します。
    /// </summary>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ（カスタム <c>--data-dir</c> 判定用）。</param>
    /// <param name="trackRepository">MCP 側トラックリポジトリ（カスタム <c>--data-dir</c> 使用時の ID 解決用）。</param>
    /// <param name="logger">診断ロガー。</param>
    public IpcPlaybackControlGateway(
        IDataDirectoryProvider dataDirectoryProvider,
        ITrackRepository trackRepository,
        ILogger<IpcPlaybackControlGateway> logger)
        : this(
            IpcPipeNameResolver.GetDefaultPipeName(),
            DefaultTimeout,
            DefaultConnectTimeout,
            logger,
            dataDirectoryProvider,
            trackRepository)
    {
        ArgumentNullException.ThrowIfNull(dataDirectoryProvider);
        ArgumentNullException.ThrowIfNull(trackRepository);
    }

    /// <summary>
    /// パイプ名およびタイムアウトを指定して <see cref="IpcPlaybackControlGateway"/> を初期化します。
    /// </summary>
    /// <param name="pipeName">接続先の Named Pipe 名。</param>
    /// <param name="commandTimeout">コマンド送受信全体のタイムアウト時間（既定 3000ms）。</param>
    /// <param name="connectTimeout">パイプ接続確立のタイムアウト時間（既定 500ms）。</param>
    /// <param name="logger">診断ロガー。</param>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ（任意）。</param>
    /// <param name="trackRepository">MCP 側トラックリポジトリ（任意）。</param>
    internal IpcPlaybackControlGateway(
        string pipeName,
        TimeSpan? commandTimeout,
        TimeSpan? connectTimeout,
        ILogger<IpcPlaybackControlGateway> logger,
        IDataDirectoryProvider? dataDirectoryProvider = null,
        ITrackRepository? trackRepository = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(logger);

        _pipeName = pipeName;
        _commandTimeout = commandTimeout ?? DefaultTimeout;
        TimeSpan requestedConnectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _connectTimeout = requestedConnectTimeout <= _commandTimeout ? requestedConnectTimeout : _commandTimeout;
        _dataDirectoryProvider = dataDirectoryProvider;
        _trackRepository = trackRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlaybackControlResult<PlayTrackResponse>> PlayTrackAsync(
        TrackId? trackId,
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? requestTrackId = trackId?.Value.ToString("D");
        string? requestFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();
        Guid? resolvedLocalTrackId = null;

        // --data-dir によるカスタムディレクトリ使用時は MCP 側ライブラリと WPF 側既定ライブラリが異なるため、
        // MCP 側リポジトリで trackId から filePath を解決してから WPF 側へ送信する
        if (trackId.HasValue && _dataDirectoryProvider is { IsCustomDirectory: true } && _trackRepository is not null)
        {
            Track? localTrack = await _trackRepository
                .GetByIdAsync(trackId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (localTrack is null)
            {
                return PlaybackControlResult<PlayTrackResponse>.Failure(
                    McpErrorCodes.NOT_FOUND,
                    McpErrorMapper.GetPublicMessage(McpErrorCodes.NOT_FOUND));
            }

            if (string.IsNullOrWhiteSpace(localTrack.FilePath))
            {
                return PlaybackControlResult<PlayTrackResponse>.Failure(
                    McpErrorCodes.FILE_NOT_FOUND,
                    McpErrorMapper.GetPublicMessage(McpErrorCodes.FILE_NOT_FOUND));
            }

            resolvedLocalTrackId = trackId.Value.Value;
            requestTrackId = null;
            requestFilePath = localTrack.FilePath;
        }

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_TRACK,
            TrackId: requestTrackId,
            FilePath: requestFilePath,
            DeadlineUtc: DateTimeOffset.UtcNow + _commandTimeout);

        return await SendCommandAsync(
            request,
            response =>
            {
                if (string.IsNullOrWhiteSpace(response.PlaybackState) || response.Track is null)
                {
                    LogIpcCommunicationFailedDelegate(_logger, _pipeName, null);
                    return PlaybackControlResult<PlayTrackResponse>.Failure(
                        McpErrorCodes.IPC_CONNECTION_FAILED,
                        McpErrorMapper.GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));
                }

                PlayedTrackDto trackDto = PlayedTrackDto.FromIpcTrackInfo(response.Track);
                if (resolvedLocalTrackId.HasValue)
                {
                    trackDto = trackDto with { Id = resolvedLocalTrackId.Value };
                }

                var dto = new PlayTrackResponse(
                    PlaybackState: response.PlaybackState,
                    Track: trackDto);

                return PlaybackControlResult<PlayTrackResponse>.Success(dto);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PlaybackControlResult<PlayCollectionResponse>> PlayAlbumAsync(
        AlbumId? albumId,
        string? albumName,
        string? artist,
        TrackId? startTrackId,
        int? startTrackIndex,
        bool? shuffle,
        bool? repeat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_ALBUM,
            DeadlineUtc: DateTimeOffset.UtcNow + _commandTimeout,
            AlbumId: albumId?.Value.ToString("D"),
            AlbumName: string.IsNullOrWhiteSpace(albumName) ? null : albumName.Trim(),
            Artist: string.IsNullOrWhiteSpace(artist) ? null : artist.Trim(),
            StartTrackId: startTrackId?.Value.ToString("D"),
            StartTrackIndex: startTrackIndex,
            Shuffle: shuffle,
            Repeat: repeat);

        return await SendCommandAsync(
            request,
            MapCollectionSuccessResponse,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PlaybackControlResult<PlayCollectionResponse>> PlayPlaylistAsync(
        PlaylistId? playlistId,
        string? playlistName,
        TrackId? startTrackId,
        int? startTrackIndex,
        bool? shuffle,
        bool? repeat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new AppIpcRequest(
            Command: AppIpcServer.COMMAND_PLAY_PLAYLIST,
            DeadlineUtc: DateTimeOffset.UtcNow + _commandTimeout,
            PlaylistId: playlistId?.Value.ToString("D"),
            PlaylistName: string.IsNullOrWhiteSpace(playlistName) ? null : playlistName.Trim(),
            StartTrackId: startTrackId?.Value.ToString("D"),
            StartTrackIndex: startTrackIndex,
            Shuffle: shuffle,
            Repeat: repeat);

        return await SendCommandAsync(
            request,
            MapCollectionSuccessResponse,
            cancellationToken).ConfigureAwait(false);
    }

    private PlaybackControlResult<PlayCollectionResponse> MapCollectionSuccessResponse(AppIpcResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.PlaybackState)
            || response.Track is null
            || string.IsNullOrWhiteSpace(response.SourceType)
            || !response.SourceId.HasValue
            || string.IsNullOrWhiteSpace(response.SourceName)
            || !response.TotalTracks.HasValue
            || !response.IsShuffleEnabled.HasValue
            || !response.IsRepeatEnabled.HasValue)
        {
            LogIpcCommunicationFailedDelegate(_logger, _pipeName, null);
            return PlaybackControlResult<PlayCollectionResponse>.Failure(
                McpErrorCodes.IPC_CONNECTION_FAILED,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));
        }

        var dto = new PlayCollectionResponse(
            SourceType: response.SourceType,
            SourceId: response.SourceId.Value,
            SourceName: response.SourceName,
            PlaybackState: response.PlaybackState,
            TotalTracks: response.TotalTracks.Value,
            IsShuffleEnabled: response.IsShuffleEnabled.Value,
            IsRepeatEnabled: response.IsRepeatEnabled.Value,
            Track: PlayedTrackDto.FromIpcTrackInfo(response.Track));

        return PlaybackControlResult<PlayCollectionResponse>.Success(dto);
    }

    private async Task<PlaybackControlResult<T>> SendCommandAsync<T>(
        AppIpcRequest request,
        Func<AppIpcResponse, PlaybackControlResult<T>> onSuccess,
        CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        // 1. Named Pipe 接続確立（パイプ未存在・接続タイムアウトは WPF 本体未起動として扱う）
        try
        {
            using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeoutCts.CancelAfter(_connectTimeout);
            await client.ConnectAsync(connectTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or FileNotFoundException)
        {
            LogWpfNotRunningDelegate(_logger, _pipeName, ex);
            return PlaybackControlResult<T>.Failure(
                McpErrorCodes.WPF_APP_NOT_RUNNING,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.WPF_APP_NOT_RUNNING));
        }
        catch (IOException ex)
        {
            LogIpcCommunicationFailedDelegate(_logger, _pipeName, ex);
            return PlaybackControlResult<T>.Failure(
                McpErrorCodes.IPC_CONNECTION_FAILED,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));
        }

        // 2. コマンド送信および応答受信（接続完了後の最新時刻で有効期限を再算出する）
        AppIpcRequest timedRequest = request with { DeadlineUtc = DateTimeOffset.UtcNow + _commandTimeout };
        AppIpcResponse? response;
        try
        {
            using var ioTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ioTimeoutCts.CancelAfter(_commandTimeout);

            using var writer = new StreamWriter(client, Utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            string requestJson = JsonSerializer.Serialize(timedRequest, IpcJsonOptions);
            await writer.WriteLineAsync(requestJson.AsMemory(), ioTimeoutCts.Token).ConfigureAwait(false);

            string? responseLine = await reader.ReadLineAsync(ioTimeoutCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                LogIpcCommunicationFailedDelegate(_logger, _pipeName, null);
                return PlaybackControlResult<T>.Failure(
                    McpErrorCodes.IPC_CONNECTION_FAILED,
                    McpErrorMapper.GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));
            }

            response = JsonSerializer.Deserialize<AppIpcResponse>(responseLine, IpcJsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or JsonException)
        {
            LogIpcCommunicationFailedDelegate(_logger, _pipeName, ex);
            return PlaybackControlResult<T>.Failure(
                McpErrorCodes.IPC_CONNECTION_FAILED,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));
        }

        if (response is null)
        {
            LogIpcCommunicationFailedDelegate(_logger, _pipeName, null);
            return PlaybackControlResult<T>.Failure(
                McpErrorCodes.IPC_CONNECTION_FAILED,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));
        }

        if (response.Ok)
        {
            return onSuccess(response);
        }

        string mappedErrorCode = MapIpcErrorCode(response.ErrorCode);
        string diagnostic = string.IsNullOrWhiteSpace(response.Error) ? "(詳細なし)" : response.Error;
        LogIpcErrorResponseDelegate(_logger, mappedErrorCode, diagnostic, null);

        return PlaybackControlResult<T>.Failure(
            mappedErrorCode,
            McpErrorMapper.GetPublicMessage(mappedErrorCode));
    }

    private static string MapIpcErrorCode(string? ipcErrorCode)
    {
        if (string.IsNullOrWhiteSpace(ipcErrorCode))
        {
            return McpErrorCodes.INTERNAL_ERROR;
        }

        return ipcErrorCode.ToUpperInvariant() switch
        {
            AppIpcErrorCodes.INVALID_PARAMETER => McpErrorCodes.INVALID_PARAMETER,
            AppIpcErrorCodes.NOT_FOUND => McpErrorCodes.NOT_FOUND,
            AppIpcErrorCodes.FILE_NOT_FOUND => McpErrorCodes.FILE_NOT_FOUND,
            McpErrorCodes.WPF_APP_NOT_RUNNING => McpErrorCodes.WPF_APP_NOT_RUNNING,
            McpErrorCodes.IPC_CONNECTION_FAILED => McpErrorCodes.IPC_CONNECTION_FAILED,
            _ => McpErrorCodes.INTERNAL_ERROR,
        };
    }
}
