using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 右側タブ下部「ダッシュボード」パネルを担当する ViewModel。
/// 対象楽曲（再生中またはプレイリスト右クリック選択曲）の累計統計および所属アルバム内のよく聴く曲 TOP N を提供します。
/// </summary>
public sealed class ListeningStatsViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// ダッシュボードに表示するよく聴く曲の既定件数
    /// </summary>
    public const int DefaultTopTracksCount = ListeningStatsApplicationService.DefaultTopCount;

    private readonly ListeningStatsApplicationService _statsService;
    private readonly IAudioService _audioService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly LibraryViewModel? _libraryViewModel;
    private readonly DashboardCommand _returnToPlayingTrackCommand;
    private readonly DashboardCommand _refreshCommand;
    private readonly DashboardCommand _selectTopTrackCommand;

    private sealed class DashboardCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public DashboardCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private Track? _targetTrack;
    private Track? _currentPlayingTrack;
    private string _resolvedAlbumTitle = string.Empty;
    private bool _isPinnedToSelectedTrack;
    private bool _isActiveView;
    private bool _isDirty;
    private int _totalPlayCount;
    private TimeSpan _totalListeningTime;
    private DateTime? _lastPlayedAt;
    private bool _disposed;

    /// <summary>
    /// 対象アルバム内のよく聴く曲 TOP N コレクション
    /// </summary>
    public ObservableCollection<TopPlayedTrackItemViewModel> TopPlayedTracks { get; } = new();

    /// <summary>
    /// 現在ダッシュボードの表示対象となっている楽曲（再生中曲、または右クリック指定曲）
    /// </summary>
    public Track? TargetTrack
    {
        get => _targetTrack;
        private set
        {
            if (SetProperty(ref _targetTrack, value))
            {
                OnPropertyChanged(nameof(HasTargetTrack));
                OnPropertyChanged(nameof(TargetTrackTitle));
                OnPropertyChanged(nameof(TargetTrackArtist));
                OnPropertyChanged(nameof(TargetTrackAlbum));
                OnPropertyChanged(nameof(TargetAlbumDisplay));
                OnPropertyChanged(nameof(HasTargetAlbumDisplay));
                OnPropertyChanged(nameof(TopTracksEmptyMessage));
                NotifyTargetModeProperties();
            }
        }
    }

    /// <summary>
    /// 現在再生中の楽曲
    /// </summary>
    public Track? CurrentPlayingTrack
    {
        get => _currentPlayingTrack;
        private set
        {
            if (SetProperty(ref _currentPlayingTrack, value))
            {
                NotifyTargetModeProperties();
            }
        }
    }

    /// <summary>
    /// 表示対象の楽曲が設定されているかどうか
    /// </summary>
    public bool HasTargetTrack => _targetTrack != null;

    /// <summary>
    /// 再生中以外の楽曲（右クリック選択曲など）を表示中かどうか
    /// </summary>
    public bool IsShowingNonPlayingTrack =>
        _targetTrack != null && !IsSameTrack(_targetTrack, _currentPlayingTrack);

    /// <summary>
    /// 「再生中の曲に戻す」操作が実行可能かどうか
    /// </summary>
    public bool CanReturnToPlayingTrack =>
        _currentPlayingTrack != null && (_isPinnedToSelectedTrack || IsShowingNonPlayingTrack);

    /// <summary>
    /// 対象楽曲のタイトル表示文字列
    /// </summary>
    public string TargetTrackTitle =>
        string.IsNullOrWhiteSpace(_targetTrack?.Title) ? "楽曲が選択されていません" : _targetTrack.Title;

    /// <summary>
    /// 対象楽曲のアーティスト表示文字列
    /// </summary>
    public string TargetTrackArtist =>
        string.IsNullOrWhiteSpace(_targetTrack?.Artist) ? string.Empty : _targetTrack.Artist;

    /// <summary>
    /// 対象楽曲のアルバム表示文字列
    /// </summary>
    public string TargetTrackAlbum =>
        string.IsNullOrWhiteSpace(_targetTrack?.Album) ? string.Empty : _targetTrack.Album;

    /// <summary>
    /// 対象楽曲の累計再生回数
    /// </summary>
    public int TotalPlayCount
    {
        get => _totalPlayCount;
        private set
        {
            if (SetProperty(ref _totalPlayCount, value))
            {
                OnPropertyChanged(nameof(TotalPlayCountDisplay));
                OnPropertyChanged(nameof(HasTargetTrackStats));
            }
        }
    }

    /// <summary>
    /// 対象楽曲の累計再生回数表示文字列
    /// </summary>
    public string TotalPlayCountDisplay =>
        string.Format(CultureInfo.InvariantCulture, "{0:N0} 回", _totalPlayCount);

    /// <summary>
    /// 対象楽曲の累計再生時間
    /// </summary>
    public TimeSpan TotalListeningTime
    {
        get => _totalListeningTime;
        private set
        {
            if (SetProperty(ref _totalListeningTime, value))
            {
                OnPropertyChanged(nameof(TotalListeningTimeDisplay));
            }
        }
    }

    /// <summary>
    /// 対象楽曲の累計再生時間表示文字列
    /// </summary>
    public string TotalListeningTimeDisplay => FormatDuration(_totalListeningTime);

    /// <summary>
    /// 対象楽曲の最終再生日時（未再生時は null）
    /// </summary>
    public DateTime? LastPlayedAt
    {
        get => _lastPlayedAt;
        private set
        {
            if (SetProperty(ref _lastPlayedAt, value))
            {
                OnPropertyChanged(nameof(LastPlayedAtDisplay));
            }
        }
    }

    /// <summary>
    /// 対象楽曲の最終再生日時表示文字列
    /// </summary>
    public string LastPlayedAtDisplay =>
        _lastPlayedAt.HasValue
            ? _lastPlayedAt.Value.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)
            : "未再生";

    /// <summary>
    /// 対象楽曲に 1 回以上の再生統計が存在するかどうか
    /// </summary>
    public bool HasTargetTrackStats => _totalPlayCount > 0;

    /// <summary>
    /// 対象アルバム内のよく聴く曲 TOP N に 1 件以上のデータが存在するかどうか
    /// </summary>
    public bool HasTopPlayedTracks => TopPlayedTracks.Count > 0;

    /// <summary>
    /// ランキングセクションの見出し文字列
    /// </summary>
    public string TopTracksSectionTitle => "アルバム内でよく聴く曲 TOP 5";

    /// <summary>
    /// ランキング集計対象となっているアルバムの表示名
    /// </summary>
    public string TargetAlbumDisplay =>
        !string.IsNullOrWhiteSpace(_resolvedAlbumTitle) ? _resolvedAlbumTitle : TargetTrackAlbum;

    /// <summary>
    /// ランキング集計対象のアルバム表示名が存在するかどうか
    /// </summary>
    public bool HasTargetAlbumDisplay => !string.IsNullOrWhiteSpace(TargetAlbumDisplay);

    /// <summary>
    /// ランキングが空の場合に表示する案内メッセージ
    /// </summary>
    public string TopTracksEmptyMessage =>
        _targetTrack == null
            ? "楽曲を再生または選択すると、アルバム内のランキングが表示されます。"
            : "このアルバムの再生統計はまだ記録されていません。楽曲を最後まで再生すると統計が蓄積されます。";

    /// <summary>
    /// ダッシュボードが現在画面上に表示中かどうか（非表示時は統計更新時の UI 再描画を遅延します）
    /// </summary>
    public bool IsActiveView
    {
        get => _isActiveView;
        set
        {
            if (SetProperty(ref _isActiveView, value) && value && _isDirty)
            {
                _isDirty = false;
                Refresh();
            }
        }
    }

    /// <summary>
    /// 再生中の楽曲の統計表示へ戻すコマンド
    /// </summary>
    public ICommand ReturnToPlayingTrackCommand => _returnToPlayingTrackCommand;

    /// <summary>
    /// 統計データを最新状態へ再読み込みするコマンド
    /// </summary>
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>
    /// TOP N リスト上の楽曲をクリックして統計表示対象へ切り替えるコマンド
    /// </summary>
    public ICommand SelectTopTrackCommand => _selectTopTrackCommand;

    /// <summary>
    /// <see cref="ListeningStatsViewModel"/> を初期化します
    /// </summary>
    /// <param name="statsService">リスニング統計アプリケーションサービス</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    /// <param name="libraryViewModel">アルバム所属曲の厳密解決に用いるライブラリ ViewModel（任意）</param>
    public ListeningStatsViewModel(
        ListeningStatsApplicationService statsService,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        LibraryViewModel? libraryViewModel = null)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _libraryViewModel = libraryViewModel;

        _returnToPlayingTrackCommand = new DashboardCommand(
            _ => ReturnToPlayingTrack(),
            _ => CanReturnToPlayingTrack);
        _refreshCommand = new DashboardCommand(_ => Refresh());
        _selectTopTrackCommand = new DashboardCommand(SelectTopTrack);

        _currentPlayingTrack = _audioService.CurrentTrack;
        _targetTrack = _currentPlayingTrack;

        _audioService.TrackChanged += OnAudioTrackChanged;
        _statsService.StatsUpdated += OnStatsUpdated;
        if (_libraryViewModel != null)
        {
            _libraryViewModel.LibrarySynced += OnLibrarySynced;
            _libraryViewModel.TrackRemoved += OnLibraryTrackRemoved;
            _libraryViewModel.AlbumRemoved += OnLibraryAlbumRemoved;
        }

        RefreshCore();
    }

    /// <summary>
    /// 表示対象の楽曲を明示的に指定します（プレイリスト右クリック導線等で使用）
    /// </summary>
    /// <param name="track">表示対象の楽曲（null 指定時は現在再生中の楽曲へ戻します）</param>
    public void SetTargetTrack(Track? track)
    {
        if (_disposed)
        {
            return;
        }

        _uiDispatcher.Invoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (track == null)
            {
                _isPinnedToSelectedTrack = false;
                TargetTrack = _currentPlayingTrack;
            }
            else
            {
                _isPinnedToSelectedTrack = !IsSameTrack(track, _currentPlayingTrack);
                TargetTrack = track;
            }

            _isDirty = false;
            RefreshCore();
        });
    }

    /// <summary>
    /// 再生中以外の曲のピン留めを解除し、現在再生中の楽曲の統計表示へ戻します
    /// </summary>
    public void ReturnToPlayingTrack()
    {
        if (_disposed || _currentPlayingTrack == null)
        {
            return;
        }

        _uiDispatcher.Invoke(() =>
        {
            if (_disposed || _currentPlayingTrack == null)
            {
                return;
            }

            _isPinnedToSelectedTrack = false;
            TargetTrack = _currentPlayingTrack;
            _isDirty = false;
            RefreshCore();
        });
    }

    /// <summary>
    /// 対象楽曲の統計およびよく聴く曲 TOP N を最新データで再読み込みします
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _uiDispatcher.Invoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _isDirty = false;
            RefreshCore();
        });
    }

    /// <summary>
    /// 累計再生時間を日本語の読みやすい書式へ変換します
    /// </summary>
    /// <param name="duration">変換対象の再生時間</param>
    /// <returns>フォーマット済みの再生時間文字列</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0分";
        }

        int totalHours = (int)duration.TotalHours;
        if (totalHours > 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0}時間 {1}分",
                totalHours,
                duration.Minutes);
        }

        if (duration.Minutes > 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}分 {1}秒",
                duration.Minutes,
                duration.Seconds);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}秒", duration.Seconds);
    }

    private void SelectTopTrack(object? parameter)
    {
        if (parameter is TopPlayedTrackItemViewModel item)
        {
            Track selectedTrack = ResolveTrackFromLibrary(item.Stat.FilePath) ?? item.Stat.ToTrack();
            SetTargetTrack(selectedTrack);
        }
    }

    private void OnLibrarySynced(LibrarySyncResult syncResult)
    {
        _ = syncResult;
        ScheduleRefreshOnLibraryUpdated();
    }

    private void OnLibraryTrackRemoved(Track removedTrack)
    {
        _ = removedTrack;
        ScheduleRefreshOnLibraryUpdated();
    }

    private void OnLibraryAlbumRemoved(Album removedAlbum)
    {
        _ = removedAlbum;
        ScheduleRefreshOnLibraryUpdated();
    }

    private void ScheduleRefreshOnLibraryUpdated()
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

            if (_isActiveView)
            {
                _isDirty = false;
                RefreshCore();
            }
            else
            {
                _isDirty = true;
            }
        });
    }

    private void OnAudioTrackChanged(Track? track)
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

            CurrentPlayingTrack = track;

            if (!_isPinnedToSelectedTrack || _targetTrack == null)
            {
                _isPinnedToSelectedTrack = false;
                TargetTrack = track;
            }
            else if (IsSameTrack(_targetTrack, track))
            {
                _isPinnedToSelectedTrack = false;
                NotifyTargetModeProperties();
            }

            if (_isActiveView)
            {
                _isDirty = false;
                RefreshCore();
            }
            else
            {
                _isDirty = true;
            }
        });
    }

    private void OnStatsUpdated(TrackListeningStat updatedStat)
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

            if (_isActiveView)
            {
                _isDirty = false;
                RefreshCore();
            }
            else
            {
                _isDirty = true;
            }
        });
    }

    private void RefreshCore()
    {
        TrackListeningStat? stat = _targetTrack != null
            ? _statsService.GetTrackStat(_targetTrack.FilePath)
            : null;

        if (stat != null && stat.PlayCount > 0)
        {
            TotalPlayCount = stat.PlayCount;
            TotalListeningTime = stat.TotalListeningDuration;
            LastPlayedAt = stat.LastPlayedAt == DateTime.MinValue ? null : stat.LastPlayedAt;
        }
        else
        {
            TotalPlayCount = 0;
            TotalListeningTime = TimeSpan.Zero;
            LastPlayedAt = null;
        }

        IReadOnlyList<TrackListeningStat> topStats;
        if (_targetTrack == null)
        {
            _resolvedAlbumTitle = string.Empty;
            topStats = Array.Empty<TrackListeningStat>();
        }
        else
        {
            Album? resolvedAlbum = ResolveAlbumForTrack(_targetTrack);
            _resolvedAlbumTitle = resolvedAlbum != null && !string.IsNullOrWhiteSpace(resolvedAlbum.Title)
                ? resolvedAlbum.Title
                : TargetTrackAlbum;

            IReadOnlyCollection<string>? albumTrackFilePaths = resolvedAlbum != null
                ? resolvedAlbum.Tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.FilePath))
                    .Select(t => t.FilePath)
                    .ToList()
                : null;

            topStats = _statsService.GetTopPlayedTracksForAlbum(
                albumTrackFilePaths,
                _targetTrack.Album,
                _targetTrack.Artist,
                DefaultTopTracksCount);
        }

        int maxPlayCount = topStats.Count > 0 ? topStats[0].PlayCount : 0;

        TopPlayedTracks.Clear();
        for (int i = 0; i < topStats.Count; i++)
        {
            TopPlayedTracks.Add(new TopPlayedTrackItemViewModel(i + 1, topStats[i], maxPlayCount));
        }

        OnPropertyChanged(nameof(HasTopPlayedTracks));
        OnPropertyChanged(nameof(TargetAlbumDisplay));
        OnPropertyChanged(nameof(HasTargetAlbumDisplay));
        OnPropertyChanged(nameof(TopTracksEmptyMessage));
        NotifyTargetModeProperties();
    }

    private Album? ResolveAlbumForTrack(Track track)
    {
        if (_libraryViewModel == null || _libraryViewModel.Albums.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            foreach (Album album in _libraryViewModel.Albums)
            {
                if (album.Tracks.Any(t =>
                    !string.IsNullOrWhiteSpace(t.FilePath) &&
                    string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    return album;
                }
            }
        }

        if (track.AlbumId.Value != Guid.Empty)
        {
            foreach (Album album in _libraryViewModel.Albums)
            {
                if (album.Id == track.AlbumId)
                {
                    return album;
                }
            }
        }

        return null;
    }

    private Track? ResolveTrackFromLibrary(string? filePath)
    {
        if (_libraryViewModel == null || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        foreach (Album album in _libraryViewModel.Albums)
        {
            Track? matched = album.Tracks.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.FilePath) &&
                string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return _libraryViewModel.Tracks.FirstOrDefault(t =>
            !string.IsNullOrWhiteSpace(t.FilePath) &&
            string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    private void NotifyTargetModeProperties()
    {
        OnPropertyChanged(nameof(IsShowingNonPlayingTrack));
        OnPropertyChanged(nameof(CanReturnToPlayingTrack));
        _returnToPlayingTrackCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private static bool IsSameTrack(Track? a, Track? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(a.FilePath) && !string.IsNullOrWhiteSpace(b.FilePath))
        {
            return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        return a.Id == b.Id;
    }

    /// <summary>
    /// イベント購読を解除してリソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _audioService.TrackChanged -= OnAudioTrackChanged;
            _statsService.StatsUpdated -= OnStatsUpdated;
            if (_libraryViewModel != null)
            {
                _libraryViewModel.LibrarySynced -= OnLibrarySynced;
                _libraryViewModel.TrackRemoved -= OnLibraryTrackRemoved;
                _libraryViewModel.AlbumRemoved -= OnLibraryAlbumRemoved;
            }
        }
    }
}
