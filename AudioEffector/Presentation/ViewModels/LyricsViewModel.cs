using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 現在再生中楽曲の歌詞読み込み・再生位置同期ハイライト・行クリックシーク・手動スクロール一時停止を統括する独立 ViewModel です
/// </summary>
public sealed class LyricsViewModel :
    ViewModelBase,
    IHandle<TrackChangedEvent>,
    IHandle<PlaybackStateChangedEvent>,
    IHandle<LyricsUpdatedEvent>,
    IDisposable
{
    /// <summary>
    /// 再生位置同期タイマーの既定ポーリング間隔（ミリ秒）
    /// </summary>
    public const int DEFAULT_SYNC_INTERVAL_MS = 100;

    /// <summary>
    /// 手動スクロール後の自動追従復帰待機時間（秒）
    /// </summary>
    public const int DEFAULT_AUTO_SCROLL_RESUME_SECONDS = 5;

    private const int NEAR_ACTIVE_LINE_RADIUS = 2;

    private readonly IAudioService _audioService;
    private readonly IEventBus _eventBus;
    private readonly LyricsApplicationService _lyricsService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeSpan _syncInterval;
    private readonly TimeSpan _autoScrollResumeDelay;
    private readonly Timer _syncTimer;
    private readonly Timer _autoScrollResumeTimer;
    private readonly object _loadLock = new();
    private readonly object _timerLock = new();

    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private int _syncTickInProgress;
    private bool _disposed;

    private Track? _currentTrack;
    private LyricsDocument? _currentDocument;
    private bool _isPlaying;
    private bool _isActiveView;
    private bool _isLoading;
    private bool _hasError;
    private bool _hasLyrics;
    private bool _isSynced;
    private bool _isAutoScrollPaused;
    private bool _pendingReloadOnActivate;
    private bool _hasLoadedForCurrentTrack;
    private LyricsSourceType _sourceType = LyricsSourceType.None;
    private string _errorMessage = string.Empty;
    private int _currentLineIndex = -1;
    private int _currentLineEndIndex = -1;
    private int _nearStartIndex = -1;
    private int _nearEndIndex = -1;
    private Action<Track>? _generateLrcRequested;

    /// <summary>
    /// 現在再生中楽曲に対して LRC 自動生成・再生成（#98 / #102）の起動が要求された際に発生するイベント
    /// </summary>
    public event Action<Track>? GenerateLrcRequested
    {
        add
        {
            _generateLrcRequested += value;
            NotifyGenerateLrcAvailabilityChanged();
        }
        remove
        {
            _generateLrcRequested -= value;
            NotifyGenerateLrcAvailabilityChanged();
        }
    }

    /// <summary>
    /// 歌詞パネルの閉じる操作が要求された際に発生するイベント
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// 指定インデックスの歌詞行をビューポート中央へスクロールするよう View へ要求するイベント
    /// </summary>
    public event Action<int>? ScrollToLineRequested;

    /// <summary>
    /// 表示対象の歌詞行 ViewModel コレクションを取得します
    /// </summary>
    public ObservableCollection<LyricLineItemViewModel> Lines { get; } = new();

    /// <summary>
    /// 現在対象の楽曲を取得します
    /// </summary>
    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (SetProperty(ref _currentTrack, value))
            {
                OnPropertyChanged(nameof(HasTrack));
                NotifyStatePropertiesChanged();
            }
        }
    }

    /// <summary>
    /// 対象楽曲が存在するかどうかを取得します
    /// </summary>
    public bool HasTrack => _currentTrack != null;

    /// <summary>
    /// 現在アクティブな歌詞行（複数行グループの場合は先頭行）のインデックスを取得します（該当なし時は -1）
    /// </summary>
    public int CurrentLineIndex
    {
        get => _currentLineIndex;
        private set => SetProperty(ref _currentLineIndex, value);
    }

    /// <summary>
    /// 現在アクティブな歌詞行範囲の末尾インデックスを取得します（該当なし時は -1）
    /// </summary>
    public int CurrentLineEndIndex
    {
        get => _currentLineEndIndex;
        private set => SetProperty(ref _currentLineEndIndex, value);
    }

    /// <summary>
    /// 表示可能な歌詞データを保持しているかどうかを取得します
    /// </summary>
    public bool HasLyrics
    {
        get => _hasLyrics;
        private set
        {
            if (SetProperty(ref _hasLyrics, value))
            {
                NotifyStatePropertiesChanged();
            }
        }
    }

    /// <summary>
    /// タイムタグによる再生位置同期に対応した歌詞を表示中かどうかを取得します
    /// </summary>
    public bool IsSynced
    {
        get => _isSynced;
        private set
        {
            if (SetProperty(ref _isSynced, value))
            {
                NotifyStatePropertiesChanged();
            }
        }
    }

    /// <summary>
    /// タイムタグを含まない非同期（プレーン）歌詞を表示中かどうかを取得します
    /// </summary>
    public bool IsUnsynced => HasLyrics && !IsSynced;

    /// <summary>
    /// 歌詞データの非同期読み込みを実行中かどうかを取得します
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                NotifyStatePropertiesChanged();
            }
        }
    }

    /// <summary>
    /// 歌詞読み込みで例外が発生しエラー表示状態かどうかを取得します
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                NotifyStatePropertiesChanged();
            }
        }
    }

    /// <summary>
    /// 楽曲未再生のエンプティステートを表示すべきかどうかを取得します
    /// </summary>
    public bool IsNoTrackState => !HasTrack && !IsLoading && !HasError;

    /// <summary>
    /// 歌詞未検出のエンプティステートを表示すべきかどうかを取得します
    /// </summary>
    public bool IsLyricsNotFoundState => HasTrack && !HasLyrics && !IsLoading && !HasError;

    /// <summary>
    /// ユーザーの手動スクロールにより自動スクロールが一時停止中かどうかを取得します
    /// </summary>
    public bool IsAutoScrollPaused
    {
        get => _isAutoScrollPaused;
        private set
        {
            if (SetProperty(ref _isAutoScrollPaused, value))
            {
                OnPropertyChanged(nameof(ShowResumeAutoScrollButton));
            }
        }
    }

    /// <summary>
    /// フローティング「現在の再生行へ戻る」ボタンを表示すべきかどうかを取得します
    /// </summary>
    public bool ShowResumeAutoScrollButton => IsAutoScrollPaused && HasLyrics && IsSynced;

    /// <summary>
    /// エンプティ／エラー状態の見出しメッセージを取得します
    /// </summary>
    public string EmptyStateTitle
    {
        get
        {
            if (HasError)
            {
                return "歌詞を読み込めませんでした";
            }

            if (!HasTrack)
            {
                return "再生中の楽曲がありません";
            }

            return "歌詞が見つかりません";
        }
    }

    /// <summary>
    /// エンプティ／エラー状態の補足メッセージを取得します
    /// </summary>
    public string EmptyStateMessage
    {
        get
        {
            if (HasError)
            {
                return string.IsNullOrWhiteSpace(_errorMessage)
                    ? "ファイルの読み取り権限や形式を確認して再読み込みしてください"
                    : _errorMessage;
            }

            if (!HasTrack)
            {
                return "楽曲を再生すると、対応する歌詞が自動的に表示されます";
            }

            return "同フォルダの .lrc または埋め込み歌詞が見つかりませんでした";
        }
    }

    /// <summary>
    /// 読み込み元ソース種別を取得します
    /// </summary>
    public LyricsSourceType SourceType
    {
        get => _sourceType;
        private set
        {
            if (SetProperty(ref _sourceType, value))
            {
                OnPropertyChanged(nameof(SourceBadgeText));
                OnPropertyChanged(nameof(HasSourceBadge));
            }
        }
    }

    /// <summary>
    /// ヘッダーに表示する同期・ソース種別バッジ文字列を取得します
    /// </summary>
    public string SourceBadgeText
    {
        get
        {
            if (!HasLyrics)
            {
                return string.Empty;
            }

            if (!IsSynced)
            {
                return "同期なし";
            }

            return SourceType switch
            {
                LyricsSourceType.LrcFile => "LRC 同期",
                LyricsSourceType.EmbeddedTag => "埋込 同期",
                _ => "同期歌詞",
            };
        }
    }

    /// <summary>
    /// ヘッダーのソース種別バッジを表示すべきかどうかを取得します
    /// </summary>
    public bool HasSourceBadge => HasLyrics && !string.IsNullOrEmpty(SourceBadgeText);

    /// <summary>
    /// 歌詞ビューが現在画面上に表示されアクティブかどうかを取得または設定します
    /// </summary>
    public bool IsActiveView
    {
        get => _isActiveView;
        set
        {
            if (!SetProperty(ref _isActiveView, value))
            {
                return;
            }

            if (value)
            {
                OnViewActivated();
            }
            else
            {
                OnViewDeactivated();
            }
        }
    }

    /// <summary>
    /// 現在楽曲が再生中かどうかを取得します
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                UpdateSyncTimerState();
            }
        }
    }

    /// <summary>
    /// 歌詞再読み込み操作を実行可能かどうかを取得します
    /// </summary>
    public bool CanReloadLyrics => HasTrack && !IsLoading;

    /// <summary>
    /// LRC 自動生成・再生成操作を実行可能かどうかを取得します
    /// </summary>
    public bool CanGenerateLrc => HasTrack && !IsLoading && _generateLrcRequested != null;

    /// <summary>
    /// LRC 自動生成・再生成ボタンのツールチップ文字列を取得します
    /// </summary>
    public string GenerateLrcToolTip
    {
        get
        {
            if (_generateLrcRequested == null)
            {
                return "LRC 自動生成機能（#98）は現在利用できません";
            }

            if (!HasTrack)
            {
                return "楽曲の再生中に利用できます";
            }

            if (IsLoading)
            {
                return "歌詞の読み込み中は利用できません";
            }

            return "LRC を自動生成・再生成";
        }
    }

    /// <summary>
    /// 指定された同期歌詞行のタイムスタンプ位置へシークするコマンドを取得します
    /// </summary>
    public ICommand SeekToLineCommand { get; }

    /// <summary>
    /// 手動スクロールによる一時停止を解除し現在の再生行へ即時復帰するコマンドを取得します
    /// </summary>
    public ICommand ResumeAutoScrollCommand { get; }

    /// <summary>
    /// 現在再生中楽曲の歌詞をキャッシュ無視で再読み込みするコマンドを取得します
    /// </summary>
    public ICommand ReloadLyricsCommand { get; }

    /// <summary>
    /// LRC 自動生成・再生成（#98 / #102）の起動を要求するコマンドを取得します
    /// </summary>
    public ICommand RequestGenerateLrcCommand { get; }

    /// <summary>
    /// 歌詞パネルを閉じる要求を発行するコマンドを取得します
    /// </summary>
    public ICommand ClosePanelCommand { get; }

    /// <summary>
    /// 同期タイマーが現在稼働中かどうかを取得します（単体テスト検証用）
    /// </summary>
    internal bool IsSyncTimerRunning { get; private set; }

    /// <summary>
    /// 手動スクロール自動復帰タイマーが現在稼働中かどうかを取得します（単体テスト検証用）
    /// </summary>
    internal bool IsAutoScrollResumeTimerRunning { get; private set; }

    /// <summary>
    /// <see cref="LyricsViewModel"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="audioService">オーディオ再生サービス</param>
    /// <param name="eventBus">ドメインイベントバス</param>
    /// <param name="lyricsService">歌詞アプリケーションサービス</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    public LyricsViewModel(
        IAudioService audioService,
        IEventBus eventBus,
        LyricsApplicationService lyricsService,
        IUiDispatcher uiDispatcher)
        : this(
            audioService,
            eventBus,
            lyricsService,
            uiDispatcher,
            TimeSpan.FromMilliseconds(DEFAULT_SYNC_INTERVAL_MS),
            TimeSpan.FromSeconds(DEFAULT_AUTO_SCROLL_RESUME_SECONDS))
    {
    }

    /// <summary>
    /// タイマー間隔を指定して <see cref="LyricsViewModel"/> の新しいインスタンスを初期化します（テスト用）
    /// </summary>
    /// <param name="audioService">オーディオ再生サービス</param>
    /// <param name="eventBus">ドメインイベントバス</param>
    /// <param name="lyricsService">歌詞アプリケーションサービス</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    /// <param name="syncInterval">再生位置同期タイマー間隔</param>
    /// <param name="autoScrollResumeDelay">手動スクロール自動復帰待機時間</param>
    internal LyricsViewModel(
        IAudioService audioService,
        IEventBus eventBus,
        LyricsApplicationService lyricsService,
        IUiDispatcher uiDispatcher,
        TimeSpan syncInterval,
        TimeSpan autoScrollResumeDelay)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _lyricsService = lyricsService ?? throw new ArgumentNullException(nameof(lyricsService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _syncInterval = syncInterval > TimeSpan.Zero ? syncInterval : TimeSpan.FromMilliseconds(DEFAULT_SYNC_INTERVAL_MS);
        _autoScrollResumeDelay = autoScrollResumeDelay > TimeSpan.Zero ? autoScrollResumeDelay : TimeSpan.FromSeconds(DEFAULT_AUTO_SCROLL_RESUME_SECONDS);

        SeekToLineCommand = new RelayCommand(ExecuteSeekToLine, CanExecuteSeekToLine);
        ResumeAutoScrollCommand = new RelayCommand(_ => ResumeAutoScroll(), _ => IsAutoScrollPaused);
        ReloadLyricsCommand = new RelayCommand(_ => ReloadLyrics(), _ => CanReloadLyrics);
        RequestGenerateLrcCommand = new RelayCommand(_ => ExecuteRequestGenerateLrc(), _ => CanGenerateLrc);
        ClosePanelCommand = new RelayCommand(_ => CloseRequested?.Invoke());

        _syncTimer = new Timer(OnSyncTimerCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _autoScrollResumeTimer = new Timer(OnAutoScrollResumeTimerCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        // イベント購読（LyricsUpdatedEvent は IEventBus 経由のみ購読して二重購読を防止する）
        _eventBus.Subscribe<TrackChangedEvent>(HandleAsync);
        _eventBus.Subscribe<PlaybackStateChangedEvent>(HandleAsync);
        _eventBus.Subscribe<LyricsUpdatedEvent>(HandleAsync);

        _audioService.TrackChanged += OnAudioServiceTrackChanged;
        _audioService.PlaybackStateChanged += OnAudioServicePlaybackStateChanged;
        _audioService.PlaybackStopped += OnAudioServicePlaybackStopped;

        // 初期状態の取り込み
        _isPlaying = _audioService.IsPlaying;
        if (_audioService.CurrentTrack != null)
        {
            _currentTrack = _audioService.CurrentTrack;
            _ = LoadLyricsForTrackAsync(_currentTrack, forceReload: false);
        }
    }

    /// <summary>
    /// 楽曲変更イベントを受信し、対象楽曲の歌詞を非同期読み込みします
    /// </summary>
    /// <param name="domainEvent">楽曲変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(TrackChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        HandleTrackChanged(domainEvent.Track);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 再生状態変更イベントを受信し、同期タイマーの稼働状態を更新します
    /// </summary>
    /// <param name="domainEvent">再生状態変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(PlaybackStateChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        bool isPlaying = domainEvent.State == PlaybackState.Playing;
        _uiDispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            IsPlaying = isPlaying;
            if (isPlaying && IsActiveView && HasLyrics && IsSynced)
            {
                UpdatePlaybackPosition(_audioService.CurrentTime);
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 歌詞更新イベントを受信し、現在再生中楽曲と一致する場合に最新歌詞へ更新します
    /// </summary>
    /// <param name="domainEvent">歌詞更新イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(LyricsUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(domainEvent.TrackFilePath))
        {
            return Task.CompletedTask;
        }

        _uiDispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            Track? track = _currentTrack;
            if (track == null || !IsSameFilePath(track.FilePath, domainEvent.TrackFilePath))
            {
                return;
            }

            if (domainEvent.Document != null)
            {
                CancelActiveLoadAndBumpGeneration();
                ApplyLoadedDocument(domainEvent.Document);
                _pendingReloadOnActivate = false;
                return;
            }

            if (IsActiveView)
            {
                _pendingReloadOnActivate = false;
                _ = LoadLyricsForTrackAsync(track, forceReload: true);
            }
            else
            {
                _pendingReloadOnActivate = true;
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 指定された再生位置に応じてアクティブ歌詞行および近接行を O(1) 差分で更新します
    /// </summary>
    /// <param name="position">現在の楽曲再生位置</param>
    public void UpdatePlaybackPosition(TimeSpan position)
    {
        if (_disposed || _currentDocument == null || !_currentDocument.IsSynced || Lines.Count == 0)
        {
            return;
        }

        (int newStart, int newEnd) = _currentDocument.FindActiveLineRange(position);
        if (newStart == _currentLineIndex && newEnd == _currentLineEndIndex)
        {
            return;
        }

        ApplyActiveRangeDiff(newStart, newEnd);

        if (newStart >= 0 && !IsAutoScrollPaused)
        {
            ScrollToLineRequested?.Invoke(newStart);
        }
    }

    /// <summary>
    /// ユーザーによる手動スクロール操作を通知し、自動スクロールの一時停止と復帰タイマーを開始・リセットします
    /// </summary>
    public void NotifyManualScroll()
    {
        if (_disposed || !HasLyrics || !IsSynced)
        {
            return;
        }

        IsAutoScrollPaused = true;
        RestartAutoScrollResumeTimer();
    }

    /// <summary>
    /// 手動スクロール一時停止を解除し、現在のアクティブ行へ自動スクロール追従を即時復帰します
    /// </summary>
    public void ResumeAutoScroll()
    {
        if (_disposed)
        {
            return;
        }

        StopAutoScrollResumeTimer();
        IsAutoScrollPaused = false;

        if (CurrentLineIndex >= 0 && CurrentLineIndex < Lines.Count)
        {
            ScrollToLineRequested?.Invoke(CurrentLineIndex);
        }
    }

    /// <summary>
    /// 現在再生中楽曲の歌詞をキャッシュ無視で強制再読み込みします
    /// </summary>
    public void ReloadLyrics()
    {
        if (_disposed || _currentTrack == null || IsLoading)
        {
            return;
        }

        _pendingReloadOnActivate = false;
        _ = LoadLyricsForTrackAsync(_currentTrack, forceReload: true);
    }

    /// <summary>
    /// 現在の再生位置を <see cref="IAudioService"/> から取得してアクティブ行を同期します
    /// </summary>
    internal void SyncCurrentPosition()
    {
        if (_disposed || !IsActiveView || !HasLyrics || !IsSynced)
        {
            return;
        }

        UpdatePlaybackPosition(_audioService.CurrentTime);
    }

    /// <summary>
    /// テストおよび内部処理向けに指定楽曲の歌詞を非同期読み込みし、完了を待機します
    /// </summary>
    /// <param name="track">対象楽曲（<see langword="null"/> の場合は表示をクリア）</param>
    /// <param name="forceReload">キャッシュを無視して再読み込みする場合は <see langword="true"/></param>
    /// <returns>読み込み完了を表す非同期タスク</returns>
    internal async Task LoadLyricsForTrackAsync(Track? track, bool forceReload)
    {
        if (_disposed)
        {
            return;
        }

        long generation;
        CancellationToken cancellationToken;

        lock (_loadLock)
        {
            if (_disposed)
            {
                return;
            }

            generation = Interlocked.Increment(ref _loadGeneration);
            CancelAndDisposeLoadCtsLocked();

            if (track == null)
            {
                _uiDispatcher.Invoke(() =>
                {
                    if (_disposed || generation != Volatile.Read(ref _loadGeneration))
                    {
                        return;
                    }

                    ClearLyricsState();
                });
                return;
            }

            _loadCts = new CancellationTokenSource();
            cancellationToken = _loadCts.Token;
        }

        _uiDispatcher.Invoke(() =>
        {
            if (_disposed || generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            HasError = false;
            _errorMessage = string.Empty;
            IsLoading = true;
            _hasLoadedForCurrentTrack = true;
            UpdateSyncTimerState();
        });

        try
        {
            LyricsDocument? document = await _lyricsService
                .LoadForTrackAsync(track, forceReload, cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || _disposed || generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            _uiDispatcher.Invoke(() =>
            {
                if (_disposed || generation != Volatile.Read(ref _loadGeneration))
                {
                    return;
                }

                ApplyLoadedDocument(document);
            });
        }
        catch (OperationCanceledException)
        {
            // 連続曲送りや Dispose によるキャンセルは正常終了として扱う
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LyricsViewModel] 歌詞読み込みに失敗しました: {ex.Message}");

            if (_disposed || generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            _uiDispatcher.Invoke(() =>
            {
                if (_disposed || generation != Volatile.Read(ref _loadGeneration))
                {
                    return;
                }

                ApplyErrorState("ファイルの読み取り権限や形式を確認して再読み込みしてください");
            });
        }
    }

    private void OnAudioServiceTrackChanged(Track? track)
    {
        if (_disposed)
        {
            return;
        }

        HandleTrackChanged(track);
    }

    private void OnAudioServicePlaybackStateChanged(bool isPlaying)
    {
        if (_disposed)
        {
            return;
        }

        _uiDispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            IsPlaying = isPlaying;
            if (isPlaying && IsActiveView && HasLyrics && IsSynced)
            {
                UpdatePlaybackPosition(_audioService.CurrentTime);
            }
        });
    }

    private void OnAudioServicePlaybackStopped()
    {
        if (_disposed)
        {
            return;
        }

        _uiDispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            IsPlaying = false;
        });
    }

    private void HandleTrackChanged(Track? track)
    {
        _uiDispatcher.Invoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (AreSameTrack(_currentTrack, track) && (_hasLoadedForCurrentTrack || track == null))
            {
                return;
            }

            StopAutoScrollResumeTimer();
            IsAutoScrollPaused = false;
            _pendingReloadOnActivate = false;
            _hasLoadedForCurrentTrack = false;
            CurrentTrack = track;

            _ = LoadLyricsForTrackAsync(track, forceReload: false);
        });
    }

    private void ApplyLoadedDocument(LyricsDocument? document)
    {
        ResetActiveRangeInternal();
        Lines.Clear();
        _currentDocument = document;
        IsLoading = false;
        HasError = false;
        _errorMessage = string.Empty;

        if (document == null || !document.HasVisibleLyrics)
        {
            HasLyrics = false;
            IsSynced = false;
            SourceType = LyricsSourceType.None;
            UpdateSyncTimerState();
            return;
        }

        foreach (LyricLine line in document.Lines)
        {
            TimeSpan? effectiveTimestamp = document.IsSynced
                ? document.GetEffectiveTimestamp(line)
                : null;
            Lines.Add(new LyricLineItemViewModel(line, effectiveTimestamp, document.IsSynced));
        }

        SourceType = document.SourceType;
        IsSynced = document.IsSynced;
        HasLyrics = true;
        UpdateSyncTimerState();

        if (document.IsSynced)
        {
            UpdatePlaybackPosition(_audioService.CurrentTime);
            if (CurrentLineIndex < 0 && Lines.Count > 0 && !IsAutoScrollPaused)
            {
                ScrollToLineRequested?.Invoke(0);
            }
        }
        else if (Lines.Count > 0)
        {
            ScrollToLineRequested?.Invoke(0);
        }
    }

    private void ApplyErrorState(string message)
    {
        ResetActiveRangeInternal();
        Lines.Clear();
        _currentDocument = null;
        IsLoading = false;
        HasLyrics = false;
        IsSynced = false;
        SourceType = LyricsSourceType.None;
        _errorMessage = message;
        HasError = true;
        UpdateSyncTimerState();
    }

    private void ClearLyricsState()
    {
        ResetActiveRangeInternal();
        Lines.Clear();
        _currentDocument = null;
        IsLoading = false;
        HasLyrics = false;
        IsSynced = false;
        HasError = false;
        _errorMessage = string.Empty;
        SourceType = LyricsSourceType.None;
        StopAutoScrollResumeTimer();
        IsAutoScrollPaused = false;
        UpdateSyncTimerState();
    }

    private void ApplyActiveRangeDiff(int newStart, int newEnd)
    {
        int oldStart = _currentLineIndex;
        int oldEnd = _currentLineEndIndex;

        // 旧アクティブ範囲のうち新範囲外となった行を解除（O(1) 差分更新）
        if (oldStart >= 0 && oldEnd >= oldStart)
        {
            int clampedOldEnd = Math.Min(oldEnd, Lines.Count - 1);
            for (int i = oldStart; i <= clampedOldEnd; i++)
            {
                if (newStart < 0 || i < newStart || i > newEnd)
                {
                    Lines[i].IsActive = false;
                }
            }
        }

        // 新アクティブ範囲のうち間奏空行以外をアクティブ化（同一タイムスタンプ複数行の同時ハイライト対応）
        if (newStart >= 0 && newEnd >= newStart)
        {
            int clampedNewEnd = Math.Min(newEnd, Lines.Count - 1);
            for (int i = newStart; i <= clampedNewEnd; i++)
            {
                if (!Lines[i].IsInstrumental)
                {
                    Lines[i].IsActive = true;
                }
            }
        }

        // 近接行（前後 ±2 行）も旧範囲と新範囲の差分のみ更新
        int newNearStart = newStart >= 0 ? Math.Max(0, newStart - NEAR_ACTIVE_LINE_RADIUS) : -1;
        int newNearEnd = newStart >= 0 ? Math.Min(Lines.Count - 1, newEnd + NEAR_ACTIVE_LINE_RADIUS) : -1;

        if (_nearStartIndex >= 0 && _nearEndIndex >= _nearStartIndex)
        {
            int clampedOldNearEnd = Math.Min(_nearEndIndex, Lines.Count - 1);
            for (int i = _nearStartIndex; i <= clampedOldNearEnd; i++)
            {
                bool isStillNear = newNearStart >= 0 && i >= newNearStart && i <= newNearEnd && !Lines[i].IsActive;
                if (!isStillNear)
                {
                    Lines[i].IsNearActive = false;
                }
            }
        }

        if (newNearStart >= 0 && newNearEnd >= newNearStart)
        {
            for (int i = newNearStart; i <= newNearEnd; i++)
            {
                Lines[i].IsNearActive = !Lines[i].IsActive && !Lines[i].IsInstrumental;
            }
        }

        _nearStartIndex = newNearStart;
        _nearEndIndex = newNearEnd;
        CurrentLineIndex = newStart;
        CurrentLineEndIndex = newEnd;
    }

    private void ResetActiveRangeInternal()
    {
        _currentLineIndex = -1;
        _currentLineEndIndex = -1;
        _nearStartIndex = -1;
        _nearEndIndex = -1;
        OnPropertyChanged(nameof(CurrentLineIndex));
        OnPropertyChanged(nameof(CurrentLineEndIndex));
    }

    private bool CanExecuteSeekToLine(object? parameter)
    {
        return parameter is LyricLineItemViewModel line
            && !_disposed
            && _currentDocument != null
            && IsSynced
            && line.IsSeekable
            && _audioService.TotalTime > TimeSpan.Zero;
    }

    private void ExecuteSeekToLine(object? parameter)
    {
        if (parameter is not LyricLineItemViewModel line || _currentDocument == null)
        {
            return;
        }

        TimeSpan? seekPosition = _currentDocument.GetSeekPosition(line.Index);
        TimeSpan totalTime = _audioService.TotalTime;
        if (!seekPosition.HasValue || totalTime <= TimeSpan.Zero)
        {
            return;
        }

        double ratio = seekPosition.Value.TotalSeconds / totalTime.TotalSeconds;
        double clampedRatio = Math.Clamp(ratio, 0.0, 1.0);

        bool wasPaused = IsAutoScrollPaused;
        StopAutoScrollResumeTimer();
        IsAutoScrollPaused = false;
        _audioService.SeekTo(clampedRatio);
        int previousLineIndex = _currentLineIndex;
        UpdatePlaybackPosition(seekPosition.Value);

        if (wasPaused && _currentLineIndex == previousLineIndex && _currentLineIndex >= 0)
        {
            ScrollToLineRequested?.Invoke(_currentLineIndex);
        }
    }

    private void ExecuteRequestGenerateLrc()
    {
        Track? track = _currentTrack;
        if (track == null || IsLoading)
        {
            return;
        }

        _generateLrcRequested?.Invoke(track);
    }

    private void OnViewActivated()
    {
        if (_pendingReloadOnActivate && _currentTrack != null)
        {
            _pendingReloadOnActivate = false;
            _ = LoadLyricsForTrackAsync(_currentTrack, forceReload: true);
            return;
        }

        UpdateSyncTimerState();
        if (HasLyrics && IsSynced)
        {
            UpdatePlaybackPosition(_audioService.CurrentTime);
            if (CurrentLineIndex >= 0 && !IsAutoScrollPaused)
            {
                ScrollToLineRequested?.Invoke(CurrentLineIndex);
            }
        }
    }

    private void OnViewDeactivated()
    {
        StopSyncTimer();
        StopAutoScrollResumeTimer();
        IsAutoScrollPaused = false;
    }

    private void UpdateSyncTimerState()
    {
        if (_disposed)
        {
            StopSyncTimer();
            return;
        }

        bool shouldRun = IsActiveView && IsPlaying && HasLyrics && IsSynced && !IsLoading;
        if (shouldRun)
        {
            StartSyncTimer();
        }
        else
        {
            StopSyncTimer();
        }
    }

    private void StartSyncTimer()
    {
        lock (_timerLock)
        {
            if (_disposed || IsSyncTimerRunning)
            {
                return;
            }

            _syncTimer.Change(_syncInterval, _syncInterval);
            IsSyncTimerRunning = true;
        }
    }

    private void StopSyncTimer()
    {
        lock (_timerLock)
        {
            if (!IsSyncTimerRunning)
            {
                return;
            }

            if (!_disposed)
            {
                _syncTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            IsSyncTimerRunning = false;
        }
    }

    private void RestartAutoScrollResumeTimer()
    {
        lock (_timerLock)
        {
            if (_disposed)
            {
                return;
            }

            _autoScrollResumeTimer.Change(_autoScrollResumeDelay, Timeout.InfiniteTimeSpan);
            IsAutoScrollResumeTimerRunning = true;
        }
    }

    private void StopAutoScrollResumeTimer()
    {
        lock (_timerLock)
        {
            if (!IsAutoScrollResumeTimerRunning)
            {
                return;
            }

            if (!_disposed)
            {
                _autoScrollResumeTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            IsAutoScrollResumeTimerRunning = false;
        }
    }

    private void OnSyncTimerCallback(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _syncTickInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            _uiDispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!_disposed && IsActiveView && IsPlaying && HasLyrics && IsSynced)
                    {
                        UpdatePlaybackPosition(_audioService.CurrentTime);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _syncTickInProgress, 0);
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref _syncTickInProgress, 0);
        }
    }

    private void OnAutoScrollResumeTimerCallback(object? state)
    {
        if (_disposed)
        {
            return;
        }

        _uiDispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (IsAutoScrollPaused)
            {
                ResumeAutoScroll();
            }
            else
            {
                StopAutoScrollResumeTimer();
            }
        });
    }

    private void CancelActiveLoadAndBumpGeneration()
    {
        lock (_loadLock)
        {
            Interlocked.Increment(ref _loadGeneration);
            CancelAndDisposeLoadCtsLocked();
        }
    }

    private void CancelAndDisposeLoadCtsLocked()
    {
        if (_loadCts == null)
        {
            return;
        }

        try
        {
            _loadCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済みであれば無視
        }

        _loadCts.Dispose();
        _loadCts = null;
    }

    private void NotifyStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsUnsynced));
        OnPropertyChanged(nameof(IsNoTrackState));
        OnPropertyChanged(nameof(IsLyricsNotFoundState));
        OnPropertyChanged(nameof(ShowResumeAutoScrollButton));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(SourceBadgeText));
        OnPropertyChanged(nameof(HasSourceBadge));
        OnPropertyChanged(nameof(CanReloadLyrics));
        NotifyGenerateLrcAvailabilityChanged();
    }

    private void NotifyGenerateLrcAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanGenerateLrc));
        OnPropertyChanged(nameof(GenerateLrcToolTip));
    }

    private static bool AreSameTrack(Track? left, Track? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        return IsSameFilePath(left.FilePath, right.FilePath);
    }

    private static bool IsSameFilePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// イベント購読解除・同期タイマー停止・キャンセルトークン解放を行います
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_timerLock)
        {
            IsSyncTimerRunning = false;
            IsAutoScrollResumeTimerRunning = false;
            _syncTimer.Dispose();
            _autoScrollResumeTimer.Dispose();
        }

        lock (_loadLock)
        {
            Interlocked.Increment(ref _loadGeneration);
            CancelAndDisposeLoadCtsLocked();
        }

        _eventBus.Unsubscribe<TrackChangedEvent>(HandleAsync);
        _eventBus.Unsubscribe<PlaybackStateChangedEvent>(HandleAsync);
        _eventBus.Unsubscribe<LyricsUpdatedEvent>(HandleAsync);

        _audioService.TrackChanged -= OnAudioServiceTrackChanged;
        _audioService.PlaybackStateChanged -= OnAudioServicePlaybackStateChanged;
        _audioService.PlaybackStopped -= OnAudioServicePlaybackStopped;
    }
}
