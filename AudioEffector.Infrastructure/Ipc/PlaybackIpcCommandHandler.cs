using System.Globalization;
using System.IO;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Ipc;

/// <summary>
/// MCP サーバからの再生制御 IPC コマンド（<c>play_track</c>・<c>get_playback_state</c> 等）を解決し、
/// <see cref="IUiDispatcher"/> 経由で UI スレッド上の <see cref="IAudioService"/> へディスパッチするハンドラです。
/// </summary>
public sealed class PlaybackIpcCommandHandler : IAppIpcCommandHandler
{
    /// <summary>
    /// 再生中の状態を表す文字列。
    /// </summary>
    public const string PLAYBACK_STATE_PLAYING = "playing";

    /// <summary>
    /// 一時停止中の状態を表す文字列。
    /// </summary>
    public const string PLAYBACK_STATE_PAUSED = "paused";

    /// <summary>
    /// 停止中・未選択の状態を表す文字列。
    /// </summary>
    public const string PLAYBACK_STATE_STOPPED = "stopped";

    /// <summary>
    /// コレクション再生ソース種別（アルバム）。
    /// </summary>
    public const string SOURCE_TYPE_ALBUM = "album";

    /// <summary>
    /// コレクション再生ソース種別（プレイリスト）。
    /// </summary>
    public const string SOURCE_TYPE_PLAYLIST = "playlist";

    private readonly ITrackRepository _trackRepository;
    private readonly LibraryApplicationService _libraryService;
    private readonly PlaylistApplicationService? _playlistService;
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
    /// <param name="playlistService">プレイリストアプリケーションサービス（<c>play_playlist</c> 対応時に指定）。</param>
    public PlaybackIpcCommandHandler(
        ITrackRepository trackRepository,
        LibraryApplicationService libraryService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        PlaylistApplicationService? playlistService = null)
        : this(trackRepository, libraryService, audioService, uiDispatcher, File.Exists, static () => DateTimeOffset.UtcNow, playlistService)
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
    /// <param name="playlistService">プレイリストアプリケーションサービス（任意）。</param>
    internal PlaybackIpcCommandHandler(
        ITrackRepository trackRepository,
        LibraryApplicationService libraryService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        Func<string, bool> fileExists,
        Func<DateTimeOffset>? utcNow = null,
        PlaylistApplicationService? playlistService = null)
    {
        ArgumentNullException.ThrowIfNull(trackRepository);
        ArgumentNullException.ThrowIfNull(libraryService);
        ArgumentNullException.ThrowIfNull(audioService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(fileExists);

        _trackRepository = trackRepository;
        _libraryService = libraryService;
        _playlistService = playlistService;
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

        if (string.Equals(request.Command, AppIpcServer.COMMAND_PLAY_ALBUM, StringComparison.OrdinalIgnoreCase))
        {
            return await HandlePlayAlbumAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_PLAY_PLAYLIST, StringComparison.OrdinalIgnoreCase))
        {
            return await HandlePlayPlaylistAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_GET_PLAYBACK_STATE, StringComparison.OrdinalIgnoreCase))
        {
            return HandleGetPlaybackState(request, cancellationToken);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_PAUSE_PLAYBACK, StringComparison.OrdinalIgnoreCase))
        {
            return HandlePausePlayback(request, cancellationToken);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_RESUME_PLAYBACK, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleResumePlaybackAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_STOP_PLAYBACK, StringComparison.OrdinalIgnoreCase))
        {
            return HandleStopPlayback(request, cancellationToken);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_NEXT_TRACK, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleNextTrackAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Command, AppIpcServer.COMMAND_PREVIOUS_TRACK, StringComparison.OrdinalIgnoreCase))
        {
            return await HandlePreviousTrackAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return AppIpcResponse.Failure(AppIpcErrorCodes.UNSUPPORTED_COMMAND, "unsupported_command");
    }

    private AppIpcResponse HandlePausePlayback(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        AppIpcResponse? response = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                aborted = true;
                return;
            }

            Track? currentTrack = _audioService.CurrentTrack;
            if (_audioService.IsPaused && currentTrack is not null)
            {
                response = AppIpcResponse.SuccessPlaybackControl(PLAYBACK_STATE_PAUSED, CreateTrackInfo(currentTrack));
                return;
            }

            if (!_audioService.IsPlaying || currentTrack is null)
            {
                response = AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "現在再生中または一時停止中のトラックが存在しないため一時停止できません。",
                    AppIpcErrorReasons.NO_ACTIVE_TRACK);
                return;
            }

            bool paused = _audioService.Pause();
            if (!paused)
            {
                response = AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "現在再生中または一時停止中のトラックが存在しないため一時停止できません。",
                    AppIpcErrorReasons.NO_ACTIVE_TRACK);
                return;
            }

            Track? trackAfterPause = _audioService.CurrentTrack ?? currentTrack;
            response = AppIpcResponse.SuccessPlaybackControl(PLAYBACK_STATE_PAUSED, CreateTrackInfo(trackAfterPause));
        });

        if (aborted || response is null)
        {
            throw new OperationCanceledException("要求期限を超過したため一時停止処理を中止しました。", cancellationToken);
        }

        return response;
    }

    private async Task<AppIpcResponse> HandleResumePlaybackAsync(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        AppIpcResponse? immediateResponse = null;
        Task<bool>? resumeTask = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                aborted = true;
                return;
            }

            Track? currentTrack = _audioService.CurrentTrack;
            if (_audioService.IsPlaying && currentTrack is not null)
            {
                immediateResponse = AppIpcResponse.SuccessPlaybackControl(PLAYBACK_STATE_PLAYING, CreateTrackInfo(currentTrack));
                return;
            }

            bool hasQueueTracks = (_audioService.UserQueue?.Count ?? 0) > 0
                || (_audioService.AlbumQueue?.Count ?? 0) > 0;
            if (!_audioService.IsPaused && currentTrack is null && !hasQueueTracks)
            {
                immediateResponse = AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "再開対象のトラックが存在しません。",
                    AppIpcErrorReasons.NO_ACTIVE_TRACK);
                return;
            }

            resumeTask = _audioService.ResumeAsync(cancellationToken);
        });

        if (aborted)
        {
            throw new OperationCanceledException("要求期限を超過したため再生再開処理を中止しました。", cancellationToken);
        }

        if (immediateResponse is not null)
        {
            return immediateResponse;
        }

        if (resumeTask is null)
        {
            throw new OperationCanceledException("要求期限を超過したため再生再開処理を中止しました。", cancellationToken);
        }

        bool resumed = await resumeTask.ConfigureAwait(false);
        if (!resumed)
        {
            ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.NOT_FOUND,
                "再開対象のトラックが存在しないか、再生を再開できませんでした。",
                AppIpcErrorReasons.NO_ACTIVE_TRACK);
        }

        return CaptureTransportResultOnUiThread(PLAYBACK_STATE_PLAYING, request.DeadlineUtc, cancellationToken);
    }

    private AppIpcResponse HandleStopPlayback(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        AppIpcResponse? response = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                aborted = true;
                return;
            }

            if (_audioService.IsPlaying || _audioService.IsPaused || _audioService.CurrentTrack is not null)
            {
                _audioService.Stop();
            }

            response = AppIpcResponse.SuccessPlaybackControl(PLAYBACK_STATE_STOPPED, track: null);
        });

        if (aborted || response is null)
        {
            throw new OperationCanceledException("要求期限を超過したため停止処理を中止しました。", cancellationToken);
        }

        return response;
    }

    private async Task<AppIpcResponse> HandleNextTrackAsync(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        Task<bool>? nextTask = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                aborted = true;
                return;
            }

            nextTask = _audioService.NextAsync(cancellationToken);
        });

        if (aborted || nextTask is null)
        {
            throw new OperationCanceledException("要求期限を超過したため次曲移動処理を中止しました。", cancellationToken);
        }

        bool movedToNext = await nextTask.ConfigureAwait(false);
        if (!movedToNext)
        {
            ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);
            return AppIpcResponse.SuccessPlaybackControl(PLAYBACK_STATE_STOPPED, track: null);
        }

        return CaptureTransportResultOnUiThread(PLAYBACK_STATE_PLAYING, request.DeadlineUtc, cancellationToken);
    }

    private async Task<AppIpcResponse> HandlePreviousTrackAsync(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        AppIpcResponse? immediateResponse = null;
        Task<bool>? previousTask = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                aborted = true;
                return;
            }

            if (_audioService.CurrentTrack is null)
            {
                immediateResponse = AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "現在再生中または一時停止中のトラックが存在しないため前の曲へ戻れません。",
                    AppIpcErrorReasons.NO_ACTIVE_TRACK);
                return;
            }

            previousTask = _audioService.PreviousAsync(cancellationToken);
        });

        if (aborted)
        {
            throw new OperationCanceledException("要求期限を超過したため前曲移動処理を中止しました。", cancellationToken);
        }

        if (immediateResponse is not null)
        {
            return immediateResponse;
        }

        if (previousTask is null)
        {
            throw new OperationCanceledException("要求期限を超過したため前曲移動処理を中止しました。", cancellationToken);
        }

        bool moved = await previousTask.ConfigureAwait(false);
        if (!moved)
        {
            ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INTERNAL_ERROR,
                "前の楽曲の再生開始に失敗しました。");
        }

        return CaptureTransportResultOnUiThread(defaultActiveState: PLAYBACK_STATE_PLAYING, request.DeadlineUtc, cancellationToken);
    }

    private AppIpcResponse CaptureTransportResultOnUiThread(
        string defaultActiveState,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(deadlineUtc, cancellationToken);

        AppIpcResponse? response = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(deadlineUtc))
            {
                aborted = true;
                return;
            }

            Track? currentTrack = _audioService.CurrentTrack;
            if (currentTrack is null)
            {
                response = AppIpcResponse.SuccessPlaybackControl(PLAYBACK_STATE_STOPPED, track: null);
                return;
            }

            string playbackState = _audioService.IsPaused && !_audioService.IsPlaying
                ? PLAYBACK_STATE_PAUSED
                : defaultActiveState;
            response = AppIpcResponse.SuccessPlaybackControl(playbackState, CreateTrackInfo(currentTrack));
        });

        if (aborted || response is null)
        {
            throw new OperationCanceledException("要求期限を超過したため操作結果の取得を中止しました。", cancellationToken);
        }

        return response;
    }

    private AppIpcResponse HandleGetPlaybackState(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        AppIpcResponse? snapshot = null;
        bool aborted = false;

        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(request.DeadlineUtc))
            {
                aborted = true;
                return;
            }

            bool isPlaying = _audioService.IsPlaying;
            bool isPaused = _audioService.IsPaused;
            Track? currentTrack = _audioService.CurrentTrack;
            TimeSpan currentTime = _audioService.CurrentTime;
            TimeSpan totalTime = _audioService.TotalTime;
            float volume = _audioService.Volume;
            bool isShuffleEnabled = _audioService.IsShuffleEnabled;
            bool isRepeatEnabled = _audioService.IsRepeatEnabled;
            int userQueueCount = _audioService.UserQueue?.Count ?? 0;
            int albumQueueCount = _audioService.AlbumQueue?.Count ?? 0;

            string playbackState;
            AppIpcTrackInfo? trackInfo;
            TimeSpan position;
            TimeSpan duration;

            if (isPlaying && currentTrack is not null)
            {
                playbackState = PLAYBACK_STATE_PLAYING;
                trackInfo = CreateTrackInfo(currentTrack);
                duration = totalTime > TimeSpan.Zero ? totalTime : currentTrack.Duration;
                position = ClampPosition(currentTime, duration);
            }
            else if (isPaused && currentTrack is not null)
            {
                playbackState = PLAYBACK_STATE_PAUSED;
                trackInfo = CreateTrackInfo(currentTrack);
                duration = totalTime > TimeSpan.Zero ? totalTime : currentTrack.Duration;
                position = ClampPosition(currentTime, duration);
            }
            else
            {
                playbackState = PLAYBACK_STATE_STOPPED;
                trackInfo = null;
                position = TimeSpan.Zero;
                duration = TimeSpan.Zero;
            }

            double clampedVolume = Math.Round(Math.Clamp((double)volume, 0.0, 1.0), 4);

            snapshot = AppIpcResponse.SuccessPlaybackState(
                playbackState: playbackState,
                track: trackInfo,
                positionSeconds: Math.Round(position.TotalSeconds, 3),
                positionString: FormatDuration(position),
                durationSeconds: Math.Round(duration.TotalSeconds, 3),
                durationString: FormatDuration(duration),
                volume: clampedVolume,
                isShuffleEnabled: isShuffleEnabled,
                isRepeatEnabled: isRepeatEnabled,
                userQueueCount: userQueueCount,
                albumQueueCount: albumQueueCount);
        });

        if (aborted || snapshot is null)
        {
            throw new OperationCanceledException("要求期限を超過したため再生状態の取得を中止しました。", cancellationToken);
        }

        return snapshot;
    }

    private static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (duration > TimeSpan.Zero && position > duration)
        {
            return duration;
        }

        return position;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        TimeSpan safe = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return safe.TotalHours >= 1.0
            ? safe.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : safe.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
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

        return AppIpcResponse.SuccessPlayback(PLAYBACK_STATE_PLAYING, CreateTrackInfo(resolvedTrack));
    }

    private async Task<AppIpcResponse> HandlePlayAlbumAsync(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        if (request.TrackFilePaths is { Count: > 0 })
        {
            return await HandlePreResolvedCollectionAsync(SOURCE_TYPE_ALBUM, request, cancellationToken)
                .ConfigureAwait(false);
        }

        bool hasAlbumId = !string.IsNullOrWhiteSpace(request.AlbumId);
        bool hasAlbumName = !string.IsNullOrWhiteSpace(request.AlbumName);

        if (!hasAlbumId && !hasAlbumName)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INVALID_PARAMETER,
                "albumId または albumName のいずれかを指定してください。");
        }

        AlbumId? parsedAlbumId = null;
        if (hasAlbumId)
        {
            if (!Guid.TryParse(request.AlbumId!.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "albumId には有効な GUID 文字列を指定してください。");
            }

            parsedAlbumId = AlbumId.From(parsedGuid);
        }

        if (!TryValidateStartTrackParameters(request, out TrackId? parsedStartTrackId, out AppIpcResponse? startParamError))
        {
            return startParamError!;
        }

        Album? resolvedAlbum;
        if (parsedAlbumId.HasValue)
        {
            resolvedAlbum = await _libraryService
                .GetAlbumByIdAsync(parsedAlbumId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (resolvedAlbum is null)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "指定された albumId に一致するアルバムが見つかりません。");
            }
        }
        else
        {
            string trimmedAlbumName = request.AlbumName!.Trim();
            string? trimmedArtist = string.IsNullOrWhiteSpace(request.Artist) ? null : request.Artist.Trim();

            IReadOnlyList<Album> allAlbums = await _libraryService
                .GetAllAlbumsAsync(cancellationToken)
                .ConfigureAwait(false);

            List<Album> matchedAlbums = allAlbums
                .Where(a => string.Equals(a.Name, trimmedAlbumName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (trimmedArtist is not null)
            {
                matchedAlbums = matchedAlbums
                    .Where(a => string.Equals(a.Artist, trimmedArtist, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (matchedAlbums.Count == 0)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "指定された条件に一致するアルバムが見つかりません。");
            }

            if (matchedAlbums.Count > 1)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "同名アルバムが複数存在するため一意に特定できません。albumId または artist を指定してください。",
                    AppIpcErrorReasons.AMBIGUOUS_ALBUM);
            }

            resolvedAlbum = matchedAlbums[0];
        }

        List<Track> playableTracks = resolvedAlbum.Tracks
            .OrderBy(t => t.TrackNumber)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id.Value)
            .Where(t => !string.IsNullOrWhiteSpace(t.FilePath) && _fileExists(t.FilePath))
            .ToList();

        if (playableTracks.Count == 0)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.FILE_NOT_FOUND,
                "アルバム内に再生可能な音声ファイルが見つかりません。");
        }

        return await PlayCollectionCoreAsync(
            SOURCE_TYPE_ALBUM,
            resolvedAlbum.Id.Value,
            resolvedAlbum.Name,
            playableTracks,
            parsedStartTrackId,
            request.StartTrackIndex,
            request.Shuffle,
            request.Repeat,
            request.DeadlineUtc,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AppIpcResponse> HandlePlayPlaylistAsync(AppIpcRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

        if (request.TrackFilePaths is { Count: > 0 })
        {
            return await HandlePreResolvedCollectionAsync(SOURCE_TYPE_PLAYLIST, request, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_playlistService is null)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INTERNAL_ERROR,
                "プレイリストサービスが構成されていません。");
        }

        bool hasPlaylistId = !string.IsNullOrWhiteSpace(request.PlaylistId);
        bool hasPlaylistName = !string.IsNullOrWhiteSpace(request.PlaylistName);

        if (!hasPlaylistId && !hasPlaylistName)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INVALID_PARAMETER,
                "playlistId または playlistName のいずれかを指定してください。");
        }

        PlaylistId? parsedPlaylistId = null;
        if (hasPlaylistId)
        {
            if (!Guid.TryParse(request.PlaylistId!.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "playlistId には有効な GUID 文字列を指定してください。");
            }

            parsedPlaylistId = PlaylistId.From(parsedGuid);
        }

        if (!TryValidateStartTrackParameters(request, out TrackId? parsedStartTrackId, out AppIpcResponse? startParamError))
        {
            return startParamError!;
        }

        IReadOnlyList<UserPlaylist> allPlaylists = await _playlistService
            .GetAllPlaylistsAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? Array.Empty<UserPlaylist>();

        UserPlaylist? resolvedPlaylist;
        if (parsedPlaylistId.HasValue)
        {
            resolvedPlaylist = allPlaylists.FirstOrDefault(p => p.Id == parsedPlaylistId.Value);
            if (resolvedPlaylist is null)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "指定された playlistId に一致するプレイリストが見つかりません。");
            }
        }
        else
        {
            string trimmedPlaylistName = request.PlaylistName!.Trim();
            List<UserPlaylist> matchedPlaylists = allPlaylists
                .Where(p => string.Equals(p.Name, trimmedPlaylistName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedPlaylists.Count == 0)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "指定されたプレイリスト名に一致するプレイリストが見つかりません。");
            }

            if (matchedPlaylists.Count > 1)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "同名のプレイリストが複数存在するため一意に特定できません。playlistId を指定してください。",
                    AppIpcErrorReasons.AMBIGUOUS_PLAYLIST);
            }

            resolvedPlaylist = matchedPlaylists[0];
        }

        IReadOnlyList<Track> resolvedTracks = await _playlistService
            .ResolvePlaylistTracksAsync(resolvedPlaylist, cancellationToken)
            .ConfigureAwait(false);

        if (resolvedTracks.Count == 0)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.NOT_FOUND,
                "指定されたプレイリストに再生対象の楽曲が含まれていません。");
        }

        List<Track> playableTracks = resolvedTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.FilePath) && _fileExists(t.FilePath))
            .ToList();

        if (playableTracks.Count == 0)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.FILE_NOT_FOUND,
                "プレイリスト内に再生可能な音声ファイルが見つかりません。");
        }

        return await PlayCollectionCoreAsync(
            SOURCE_TYPE_PLAYLIST,
            resolvedPlaylist.Id.Value,
            resolvedPlaylist.Name,
            playableTracks,
            parsedStartTrackId,
            request.StartTrackIndex,
            request.Shuffle,
            request.Repeat,
            request.DeadlineUtc,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AppIpcResponse> HandlePreResolvedCollectionAsync(
        string sourceType,
        AppIpcRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> rawPaths = request.TrackFilePaths!;
        if (request.StartTrackIndex.HasValue && (request.StartTrackIndex.Value < 0 || request.StartTrackIndex.Value >= rawPaths.Count))
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INVALID_PARAMETER,
                $"startTrackIndex は 0〜{rawPaths.Count - 1} の範囲で指定してください。");
        }

        var playableTracks = new List<Track>(rawPaths.Count);
        int? resolvedStartIndex = null;

        for (int i = 0; i < rawPaths.Count; i++)
        {
            ThrowIfCancelledOrExpired(request.DeadlineUtc, cancellationToken);

            string rawPath = rawPaths[i];
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "trackFilePaths に空のファイルパスが含まれています。");
            }

            string trimmedPath = rawPath.Trim();
            if (!Path.IsPathFullyQualified(trimmedPath))
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "trackFilePaths には絶対パスを指定してください。");
            }

            if (!_fileExists(trimmedPath))
            {
                if (request.StartTrackIndex.HasValue && request.StartTrackIndex.Value == i)
                {
                    return AppIpcResponse.Failure(
                        AppIpcErrorCodes.FILE_NOT_FOUND,
                        "指定された開始トラックの音声ファイルが見つかりません。");
                }

                continue;
            }

            Track? track = await _libraryService
                .GetTrackByPathAsync(trimmedPath, cancellationToken)
                .ConfigureAwait(false);
            if (track is null)
            {
                if (request.StartTrackIndex.HasValue && request.StartTrackIndex.Value == i)
                {
                    return AppIpcResponse.Failure(
                        AppIpcErrorCodes.INTERNAL_ERROR,
                        "指定された開始トラックのメタデータを解決できませんでした。");
                }

                continue;
            }

            if (request.StartTrackIndex.HasValue && request.StartTrackIndex.Value == i)
            {
                resolvedStartIndex = playableTracks.Count;
            }

            playableTracks.Add(track);
        }

        if (playableTracks.Count == 0)
        {
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.FILE_NOT_FOUND,
                "コレクション内に再生可能な音声ファイルが見つかりません。");
        }

        Guid sourceId = request.SourceId ?? Guid.Empty;
        string sourceName = !string.IsNullOrWhiteSpace(request.SourceName)
            ? request.SourceName.Trim()
            : (string.Equals(sourceType, SOURCE_TYPE_ALBUM, StringComparison.Ordinal)
                ? playableTracks[0].Album
                : "Playlist");

        return await PlayCollectionCoreAsync(
            sourceType,
            sourceId,
            sourceName,
            playableTracks,
            startTrackId: null,
            startTrackIndex: resolvedStartIndex,
            request.Shuffle,
            request.Repeat,
            request.DeadlineUtc,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AppIpcResponse> PlayCollectionCoreAsync(
        string sourceType,
        Guid sourceId,
        string sourceName,
        List<Track> playableTracks,
        TrackId? startTrackId,
        int? startTrackIndex,
        bool? shuffle,
        bool? repeat,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
    {
        Track? startTrack = null;
        int? resolvedStartIndex = null;
        if (startTrackId.HasValue)
        {
            int foundIndex = playableTracks.FindIndex(t => t.Id == startTrackId.Value);
            if (foundIndex < 0)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.NOT_FOUND,
                    "指定された startTrackId は対象コレクション内の再生可能トラックに見つかりません。");
            }

            resolvedStartIndex = foundIndex;
            startTrack = playableTracks[foundIndex];
        }
        else if (startTrackIndex.HasValue)
        {
            if (startTrackIndex.Value < 0 || startTrackIndex.Value >= playableTracks.Count)
            {
                return AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    $"startTrackIndex は 0〜{playableTracks.Count - 1} の範囲で指定してください。");
            }

            resolvedStartIndex = startTrackIndex.Value;
            startTrack = playableTracks[startTrackIndex.Value];
        }

        ThrowIfCancelledOrExpired(deadlineUtc, cancellationToken);

        Task<Track?>? playTask = null;
        bool abortedBeforePlayback = false;
        _uiDispatcher.Invoke(() =>
        {
            if (cancellationToken.IsCancellationRequested || IsExpired(deadlineUtc))
            {
                abortedBeforePlayback = true;
                return;
            }

            playTask = _audioService.PlayPlaylistAsync(
                playableTracks,
                startTrack,
                shuffle,
                repeat,
                sourceName: sourceName,
                startTrackIndex: resolvedStartIndex,
                cancellationToken: cancellationToken);
        });

        if (abortedBeforePlayback || playTask is null)
        {
            throw new OperationCanceledException("要求期限を超過したため再生開始を中止しました。", cancellationToken);
        }

        Track? startedTrack = await playTask.ConfigureAwait(false);
        if (startedTrack is null)
        {
            ThrowIfCancelledOrExpired(deadlineUtc, cancellationToken);
            return AppIpcResponse.Failure(
                AppIpcErrorCodes.INTERNAL_ERROR,
                "コレクションの再生開始に失敗しました。");
        }

        return AppIpcResponse.SuccessCollectionPlayback(
            sourceType: sourceType,
            sourceId: sourceId,
            sourceName: sourceName,
            playbackState: PLAYBACK_STATE_PLAYING,
            totalTracks: playableTracks.Count,
            isShuffleEnabled: _audioService.IsShuffleEnabled,
            isRepeatEnabled: _audioService.IsRepeatEnabled,
            track: CreateTrackInfo(startedTrack));
    }

    private static bool TryValidateStartTrackParameters(
        AppIpcRequest request,
        out TrackId? parsedStartTrackId,
        out AppIpcResponse? errorResponse)
    {
        parsedStartTrackId = null;
        errorResponse = null;

        if (!string.IsNullOrWhiteSpace(request.StartTrackId))
        {
            if (!Guid.TryParse(request.StartTrackId.Trim(), out Guid parsedGuid) || parsedGuid == Guid.Empty)
            {
                errorResponse = AppIpcResponse.Failure(
                    AppIpcErrorCodes.INVALID_PARAMETER,
                    "startTrackId には有効な GUID 文字列を指定してください。");
                return false;
            }

            parsedStartTrackId = TrackId.From(parsedGuid);
        }

        if (request.StartTrackIndex.HasValue && request.StartTrackIndex.Value < 0)
        {
            errorResponse = AppIpcResponse.Failure(
                AppIpcErrorCodes.INVALID_PARAMETER,
                "startTrackIndex には 0 以上の整数を指定してください。");
            return false;
        }

        return true;
    }

    private static AppIpcTrackInfo CreateTrackInfo(Track track)
    {
        return new AppIpcTrackInfo(
            Id: track.Id.Value,
            Title: track.Title,
            Artist: track.Artist,
            Album: track.Album,
            DurationSeconds: Math.Round(track.Duration.TotalSeconds, 3),
            DurationString: track.DurationString,
            FilePath: track.FilePath);
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
