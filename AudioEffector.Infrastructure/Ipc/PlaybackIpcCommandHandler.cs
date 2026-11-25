using System.IO;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Ipc;

/// <summary>
/// MCP サーバからの再生制御 IPC コマンド（<c>play_track</c> 等）を解決し、
/// <see cref="IUiDispatcher"/> 経由で UI スレッド上の <see cref="IAudioService"/> へディスパッチするハンドラです。
/// </summary>
public sealed class PlaybackIpcCommandHandler : IAppIpcCommandHandler
{
    /// <summary>
    /// 再生開始後の状態を表す文字列。
    /// </summary>
    public const string PLAYBACK_STATE_PLAYING = "playing";

    private readonly ITrackRepository _trackRepository;
    private readonly LibraryApplicationService _libraryService;
    private readonly IAudioService _audioService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// <see cref="PlaybackIpcCommandHandler"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="trackRepository">トラックリポジトリ。</param>
    /// <param name="libraryService">楽曲ライブラリアプリケーションサービス。</param>
    /// <param name="audioService">オーディオ再生サービス。</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー。</param>
    public PlaybackIpcCommandHandler(
        ITrackRepository trackRepository,
        LibraryApplicationService libraryService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher)
        : this(trackRepository, libraryService, audioService, uiDispatcher, File.Exists, static () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>
    /// ファイル存在確認および現在時刻取得デリゲートを差し替え可能なコンストラクタ（単体テスト用）。
    /// </summary>
    /// <param name="trackRepository">トラックリポジトリ。</param>
    /// <param name="libraryService">楽曲ライブラリアプリケーションサービス。</param>
    /// <param name="audioService">オーディオ再生サービス。</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー。</param>
    /// <param name="fileExists">ファイル存在確認デリゲート。</param>
    /// <param name="utcNow">現在 UTC 時刻取得デリゲート（省略時は <see cref="DateTimeOffset.UtcNow"/>）。</param>
    internal PlaybackIpcCommandHandler(
        ITrackRepository trackRepository,
        LibraryApplicationService libraryService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        Func<string, bool> fileExists,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(trackRepository);
        ArgumentNullException.ThrowIfNull(libraryService);
        ArgumentNullException.ThrowIfNull(audioService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(fileExists);

        _trackRepository = trackRepository;
        _libraryService = libraryService;
        _audioService = audioService;
        _uiDispatcher = uiDispatcher;
        _fileExists = fileExists;
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<AppIpcResponse> HandleAsync(AppIpcRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(request.Command, AppIpcServer.COMMAND_PLAY_TRACK, StringComparison.OrdinalIgnoreCase))
        {
            return await HandlePlayTrackAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return AppIpcResponse.Failure(AppIpcErrorCodes.UNSUPPORTED_COMMAND, "unsupported_command");
    }

    private async Task<AppIpcResponse> HandlePlayTrackAsync(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        bool hasTrackId = !string.IsNullOrWhiteSpace(request.TrackId);
        bool hasFilePath = !string.IsNullOrWhiteSpace(request.FilePath);

        if (!hasTrackId && !hasFilePath)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INVALID_PARAMETER,
                "trackId または filePath のいずれかを指定してください。");
        }

        Track? resolvedTrack;
        if (hasTrackId)
        {
            if (!Guid.TryParse(request.TrackId!.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "trackId には有効な GUID 文字列を指定してください。");
            }

            resolvedTrack = await _trackRepository
                .GetByIdAsync(TrackId.From(parsedGuid), cancellationToken)
                .ConfigureAwait(false);

            if (resolvedTrack is null)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "指定された trackId に一致するトラックが見つかりません。");
            }

            if (string.IsNullOrWhiteSpace(resolvedTrack.FilePath) || !_fileExists(resolvedTrack.FilePath))
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.FILE_NOT_FOUND,
                    "対象の音声ファイルが見つかりません。");
            }
        }
        else
        {
            string trimmedPath = request.FilePath!.Trim();
            if (!Path.IsPathFullyQualified(trimmedPath))
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "filePath には絶対パスを指定してください。");
            }

            if (!_fileExists(trimmedPath))
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.FILE_NOT_FOUND,
                    "指定された音声ファイルが見つかりません。");
            }

            resolvedTrack = await _libraryService
                .GetTrackByPathAsync(trimmedPath, cancellationToken)
                .ConfigureAwait(false);

            if (resolvedTrack is null)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INTERNAL_ERROR,
                    "指定された音声ファイルのメタデータを解決できませんでした。");
            }
        }

        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        // ウィンドウ状態（メイン画面／ミニプレイヤー）は変更せず、UI スレッド上で直前にも期限確認したうえで再生開始を待機する
        Task<bool>? playTask = null;
        bool abortedBeforePlayback = false;
        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                abortedBeforePlayback = true;
                return;
            }

            playTask = _audioService.PlayTrackAsync(resolvedTrack, cancellationToken);
        });

        if (abortedBeforePlayback || playTask is null)
        {
            throw new OperationCanceledException("要求期限を超過したため再生開始を中止しました。", cancellationToken);
        }

        bool started = await playTask.ConfigureAwait(false);
        if (!started)
        {
            ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INTERNAL_ERROR,
                "音声ファイルの再生開始に失敗しました。");
        }

        var trackInfo = new AppIpcTrackInfo(
            Id: resolvedTrack.Id.Value,
            Title: resolvedTrack.Title,
            Artist: resolvedTrack.Artist,
            Album: resolvedTrack.Album,
            DurationSeconds: Math.Round(resolvedTrack.Duration.TotalSeconds, 3),
            DurationString: resolvedTrack.DurationString,
            FilePath: resolvedTrack.FilePath);

        return AppIpcResponse.SuccessPlayback(PLAYBACK_STATE_PLAYING, trackInfo);
    }

    private bool IsExpired(DateTimeOffset? deadlineUtc)
    {
        return deadlineUtc.HasValue && _utcNow() >= deadlineUtc.Value;
    }

    private void ThrowIfCancelledOrExpired(DateTimeOffset? deadlineUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsExpired(deadlineUtc))
        {
            throw new OperationCanceledException("要求期限を超過したため処理を中止しました。", cancellationToken);
        }
    }
}
