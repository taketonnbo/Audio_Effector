using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 左側メイン画面「リスニング統計」ビューを担当する ViewModel。
/// 期間フィルタ別の KPI サマリ、曲・アーティスト・アルバム TOP 10 ランキング、
/// 日別・月別再生推移、時間帯別・曜日別傾向、および再生・プレイリスト作成・画面遷移アクションを提供します。
/// </summary>
public sealed class LibraryStatsViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 各ランキングの既定表示件数（TOP 10）
    /// </summary>
    public const int DefaultTopCount = LibraryStatsApplicationService.DefaultTopCount;

    private readonly LibraryStatsApplicationService _libraryStatsService;
    private readonly ListeningStatsApplicationService _listeningStatsService;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioService _audioService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly PlaylistViewModel? _playlistViewModel;
    private readonly Func<string, string?> _playlistNamePrompt;
    private readonly Action<string, string, bool> _messageNotifier;

    private readonly StatsCommand _selectPeriodFilterCommand;
    private readonly StatsCommand _playTopTracksCommand;
    private readonly StatsCommand _createTopTracksPlaylistCommand;
    private readonly StatsCommand _playTrackCommand;
    private readonly StatsCommand _playNextTrackCommand;
    private readonly StatsCommand _enqueueTrackCommand;
    private readonly StatsCommand _addTrackToPlaylistCommand;
    private readonly StatsCommand _navigateToArtistCommand;
    private readonly StatsCommand _navigateToAlbumCommand;
    private readonly StatsCommand _selectArtistRankingCommand;
    private readonly StatsCommand _selectAlbumRankingCommand;
    private readonly StatsCommand _selectDailyTrendCommand;
    private readonly StatsCommand _selectMonthlyTrendCommand;
    private readonly StatsCommand _selectHourlyHabitCommand;
    private readonly StatsCommand _selectDayOfWeekHabitCommand;
    private readonly StatsCommand _refreshCommand;

    private sealed class StatsCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public StatsCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private StatsPeriodFilter _selectedPeriodFilter = StatsPeriodFilter.AllTime;
    private LibraryStatsSnapshot? _currentSnapshot;
    private bool _isArtistRankingSelected = true;
    private bool _isMonthlyTrendSelected = true;
    private bool _isDayOfWeekHabitSelected;
    private bool _isActiveView;
    private bool _isDirty;
    private int _isDeferredRefreshScheduled;
    private bool _disposed;

    /// <summary>よく聴く曲 TOP 10 コレクション</summary>
    public ObservableCollection<LibraryStatsTrackItemViewModel> TopTracks { get; } = new();

    /// <summary>よく聴くアーティスト TOP 10 コレクション</summary>
    public ObservableCollection<LibraryStatsArtistItemViewModel> TopArtists { get; } = new();

    /// <summary>よく聴くアルバム TOP 10 コレクション</summary>
    public ObservableCollection<LibraryStatsAlbumItemViewModel> TopAlbums { get; } = new();

    /// <summary>現在表示中の再生推移グラフバー一覧（日別または月別）</summary>
    public ObservableCollection<LibraryStatsChartBarViewModel> TrendBars { get; } = new();

    /// <summary>現在表示中のリスニング傾向グラフバー一覧（時間帯別または曜日別）</summary>
    public ObservableCollection<LibraryStatsChartBarViewModel> HabitBars { get; } = new();

    /// <summary>
    /// 再生リスト更新および再生開始を要求するイベント（対象トラック群, 開始トラック, リスト名, サブタイトル）
    /// </summary>
    public event Action<IReadOnlyList<Track>, Track?, string, string>? PlaybackRequested;

    /// <summary>
    /// 再生キューへの追加を要求するイベント（対象トラック群, playNext: true なら次に再生）
    /// </summary>
    public event Action<IReadOnlyList<Track>, bool>? EnqueueRequested;

    /// <summary>
    /// 指定アーティスト画面への遷移を要求するイベント（アーティスト名）
    /// </summary>
    public event Action<string>? ArtistNavigationRequested;

    /// <summary>
    /// 指定アルバム詳細画面への遷移を要求するイベント（対象アルバム）
    /// </summary>
    public event Action<Album>? AlbumNavigationRequested;

    /// <summary>
    /// 選択中の集計期間フィルタ（今週／今月／今年／全期間）
    /// </summary>
    public StatsPeriodFilter SelectedPeriodFilter
    {
        get => _selectedPeriodFilter;
        set
        {
            if (SetProperty(ref _selectedPeriodFilter, value))
            {
                _isMonthlyTrendSelected = value is StatsPeriodFilter.ThisYear or StatsPeriodFilter.AllTime;
                NotifyPeriodPropertiesChanged();
                OnPropertyChanged(nameof(IsDailyTrendSelected));
                OnPropertyChanged(nameof(IsMonthlyTrendSelected));
                OnPropertyChanged(nameof(TrendChartTitle));
                Refresh();
            }
        }
    }

    /// <summary>「今週」フィルタが選択されているかどうか</summary>
    public bool IsThisWeekSelected => _selectedPeriodFilter == StatsPeriodFilter.ThisWeek;

    /// <summary>「今月」フィルタが選択されているかどうか</summary>
    public bool IsThisMonthSelected => _selectedPeriodFilter == StatsPeriodFilter.ThisMonth;

    /// <summary>「今年」フィルタが選択されているかどうか</summary>
    public bool IsThisYearSelected => _selectedPeriodFilter == StatsPeriodFilter.ThisYear;

    /// <summary>「全期間」フィルタが選択されているかどうか</summary>
    public bool IsAllTimeSelected => _selectedPeriodFilter == StatsPeriodFilter.AllTime;

    /// <summary>選択中期間の日本語表示名</summary>
    public string SelectedPeriodDisplayName => GetPeriodDisplayName(_selectedPeriodFilter);

    /// <summary>選択期間内に 1 件以上の再生データが存在するかどうか</summary>
    public bool HasData => _currentSnapshot?.HasData == true;

    /// <summary>選択期間内の再生データが 0 件かどうか</summary>
    public bool IsEmpty => !HasData;

    /// <summary>よく聴く曲 TOP 10 にデータが存在するかどうか</summary>
    public bool HasTopTracks => TopTracks.Count > 0;

    /// <summary>よく聴くアーティスト TOP 10 にデータが存在するかどうか</summary>
    public bool HasTopArtists => TopArtists.Count > 0;

    /// <summary>よく聴くアルバム TOP 10 にデータが存在するかどうか</summary>
    public bool HasTopAlbums => TopAlbums.Count > 0;

    /// <summary>エンプティ状態の見出し文言</summary>
    public string EmptyStateTitle => _selectedPeriodFilter == StatsPeriodFilter.AllTime
        ? "リスニング統計データがありません"
        : string.Format(CultureInfo.InvariantCulture, "「{0}」の再生データがありません", SelectedPeriodDisplayName);

    /// <summary>エンプティ状態の補足説明文言</summary>
    public string EmptyStateMessage => _selectedPeriodFilter == StatsPeriodFilter.AllTime
        ? "楽曲を最後まで再生すると、再生回数・ランキング・視聴傾向がここに自動集計されます。"
        : "他の期間フィルタへ切り替えるか、楽曲を再生して統計データを蓄積してください。";

    /// <summary>総再生回数</summary>
    public int TotalPlayCount => _currentSnapshot?.Summary.TotalPlayCount ?? 0;

    /// <summary>総再生回数の表示文字列</summary>
    public string TotalPlayCountDisplay =>
        string.Format(CultureInfo.InvariantCulture, "{0:N0} 回", TotalPlayCount);

    /// <summary>日平均再生回数の表示文字列</summary>
    public string DailyAveragePlayCountDisplay =>
        string.Format(
            CultureInfo.InvariantCulture,
            "日平均 {0:F1} 回",
            _currentSnapshot?.Summary.DailyAveragePlayCount ?? 0.0);

    /// <summary>累計再生時間の表示文字列</summary>
    public string TotalListeningDurationDisplay =>
        ListeningStatsViewModel.FormatDuration(_currentSnapshot?.Summary.TotalListeningDuration ?? TimeSpan.Zero);

    /// <summary>ユニーク再生曲数の表示文字列</summary>
    public string UniqueTrackCountDisplay =>
        string.Format(CultureInfo.InvariantCulture, "{0:N0} 曲", _currentSnapshot?.Summary.UniqueTrackCount ?? 0);

    /// <summary>ユニークアーティスト数・アルバム数の内訳表示文字列</summary>
    public string UniqueLibraryBreakdownDisplay =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} アーティスト / {1:N0} アルバム",
            _currentSnapshot?.Summary.UniqueArtistCount ?? 0,
            _currentSnapshot?.Summary.UniqueAlbumCount ?? 0);

    /// <summary>トップアーティスト名表示文字列</summary>
    public string TopArtistNameDisplay =>
        !string.IsNullOrWhiteSpace(_currentSnapshot?.Summary.TopArtistName)
            ? _currentSnapshot!.Summary.TopArtistName
            : "—";

    /// <summary>トップアーティスト再生回数表示文字列</summary>
    public string TopArtistPlayCountDisplay =>
        (_currentSnapshot?.Summary.TopArtistPlayCount ?? 0) > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:N0} 回再生", _currentSnapshot!.Summary.TopArtistPlayCount)
            : "再生データなし";

    /// <summary>右カラムランキングでアーティスト表示が選択されているかどうか</summary>
    public bool IsArtistRankingSelected
    {
        get => _isArtistRankingSelected;
        set
        {
            if (SetProperty(ref _isArtistRankingSelected, value))
            {
                OnPropertyChanged(nameof(IsAlbumRankingSelected));
            }
        }
    }

    /// <summary>右カラムランキングでアルバム表示が選択されているかどうか</summary>
    public bool IsAlbumRankingSelected => !_isArtistRankingSelected;

    /// <summary>再生推移グラフで月別表示が選択されているかどうか</summary>
    public bool IsMonthlyTrendSelected
    {
        get => _isMonthlyTrendSelected;
        set
        {
            if (SetProperty(ref _isMonthlyTrendSelected, value))
            {
                OnPropertyChanged(nameof(IsDailyTrendSelected));
                OnPropertyChanged(nameof(TrendChartTitle));
                RebuildTrendBars();
            }
        }
    }

    /// <summary>再生推移グラフで日別表示が選択されているかどうか</summary>
    public bool IsDailyTrendSelected => !_isMonthlyTrendSelected;

    /// <summary>再生推移グラフの見出し文字列</summary>
    public string TrendChartTitle => _isMonthlyTrendSelected ? "月別の再生推移" : "日別の再生推移";

    /// <summary>リスニング傾向グラフで曜日別表示が選択されているかどうか</summary>
    public bool IsDayOfWeekHabitSelected
    {
        get => _isDayOfWeekHabitSelected;
        set
        {
            if (SetProperty(ref _isDayOfWeekHabitSelected, value))
            {
                OnPropertyChanged(nameof(IsHourlyHabitSelected));
                OnPropertyChanged(nameof(HabitPeakInsight));
                RebuildHabitBars();
            }
        }
    }

    /// <summary>リスニング傾向グラフで時間帯別表示が選択されているかどうか</summary>
    public bool IsHourlyHabitSelected => !_isDayOfWeekHabitSelected;

    /// <summary>現在選択中のリスニング傾向（時間帯別または曜日別）のピークインサイト文言</summary>
    public string HabitPeakInsight
    {
        get
        {
            if (_currentSnapshot == null)
            {
                return "再生データが蓄積されると傾向が表示されます。";
            }

            return _isDayOfWeekHabitSelected
                ? _currentSnapshot.Habits.DayOfWeekPeakInsight
                : _currentSnapshot.Habits.HourlyPeakInsight;
        }
    }

    /// <summary>
    /// 統計ビューが現在画面上に表示中かどうか（非表示時は更新を遅延します）
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

    /// <summary>プレイリスト ViewModel への参照</summary>
    public PlaylistViewModel? Playlist => _playlistViewModel ?? _libraryViewModel.Playlist;

    /// <summary>期間フィルタを選択するコマンド</summary>
    public ICommand SelectPeriodFilterCommand => _selectPeriodFilterCommand;

    /// <summary>表示中のよく聴く曲 TOP 10 を先頭から連続再生するコマンド</summary>
    public ICommand PlayTopTracksCommand => _playTopTracksCommand;

    /// <summary>表示中のよく聴く曲 TOP 10 から新規プレイリストを作成するコマンド</summary>
    public ICommand CreateTopTracksPlaylistCommand => _createTopTracksPlaylistCommand;

    /// <summary>指定されたランキング楽曲を即時再生するコマンド</summary>
    public ICommand PlayTrackCommand => _playTrackCommand;

    /// <summary>指定されたランキング楽曲を次に再生するコマンド</summary>
    public ICommand PlayNextTrackCommand => _playNextTrackCommand;

    /// <summary>指定されたランキング楽曲を再生キュー末尾に追加するコマンド</summary>
    public ICommand EnqueueTrackCommand => _enqueueTrackCommand;

    /// <summary>指定されたランキング楽曲を既存プレイリストへ追加するダイアログを開くコマンド</summary>
    public ICommand AddTrackToPlaylistCommand => _addTrackToPlaylistCommand;

    /// <summary>指定されたアーティスト（または楽曲のアーティスト）画面へ遷移するコマンド</summary>
    public ICommand NavigateToArtistCommand => _navigateToArtistCommand;

    /// <summary>指定されたアルバム詳細画面へ遷移するコマンド</summary>
    public ICommand NavigateToAlbumCommand => _navigateToAlbumCommand;

    /// <summary>右カラムランキングをアーティスト表示へ切り替えるコマンド</summary>
    public ICommand SelectArtistRankingCommand => _selectArtistRankingCommand;

    /// <summary>右カラムランキングをアルバム表示へ切り替えるコマンド</summary>
    public ICommand SelectAlbumRankingCommand => _selectAlbumRankingCommand;

    /// <summary>推移グラフを日別表示へ切り替えるコマンド</summary>
    public ICommand SelectDailyTrendCommand => _selectDailyTrendCommand;

    /// <summary>推移グラフを月別表示へ切り替えるコマンド</summary>
    public ICommand SelectMonthlyTrendCommand => _selectMonthlyTrendCommand;

    /// <summary>傾向グラフを時間帯別表示へ切り替えるコマンド</summary>
    public ICommand SelectHourlyHabitCommand => _selectHourlyHabitCommand;

    /// <summary>傾向グラフを曜日別表示へ切り替えるコマンド</summary>
    public ICommand SelectDayOfWeekHabitCommand => _selectDayOfWeekHabitCommand;

    /// <summary>統計データを最新状態へ再読み込みするコマンド</summary>
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>
    /// <see cref="LibraryStatsViewModel"/> を初期化します
    /// </summary>
    /// <param name="libraryStatsService">ライブラリ全体統計集計サービス</param>
    /// <param name="listeningStatsService">再生統計更新通知サービス</param>
    /// <param name="libraryViewModel">ライブラリ ViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    /// <param name="playlistViewModel">プレイリスト ViewModel（任意）</param>
    [ActivatorUtilitiesConstructor]
    public LibraryStatsViewModel(
        LibraryStatsApplicationService libraryStatsService,
        ListeningStatsApplicationService listeningStatsService,
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        PlaylistViewModel? playlistViewModel = null)
        : this(
            libraryStatsService,
            listeningStatsService,
            libraryViewModel,
            audioService,
            uiDispatcher,
            playlistViewModel,
            playlistNamePrompt: null,
            messageNotifier: null)
    {
    }

    /// <summary>
    /// テスト用にダイアログ入力および通知デリゲートを差し替えて <see cref="LibraryStatsViewModel"/> を初期化します
    /// </summary>
    /// <param name="libraryStatsService">ライブラリ全体統計集計サービス</param>
    /// <param name="listeningStatsService">再生統計更新通知サービス</param>
    /// <param name="libraryViewModel">ライブラリ ViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="uiDispatcher">UI スレッドディスパッチャー</param>
    /// <param name="playlistViewModel">プレイリスト ViewModel（任意）</param>
    /// <param name="playlistNamePrompt">新規プレイリスト名入力プロンプト（任意）</param>
    /// <param name="messageNotifier">完了・エラー通知デリゲート（任意）</param>
    internal LibraryStatsViewModel(
        LibraryStatsApplicationService libraryStatsService,
        ListeningStatsApplicationService listeningStatsService,
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        IUiDispatcher uiDispatcher,
        PlaylistViewModel? playlistViewModel,
        Func<string, string?>? playlistNamePrompt,
        Action<string, string, bool>? messageNotifier)
    {
        _libraryStatsService = libraryStatsService ?? throw new ArgumentNullException(nameof(libraryStatsService));
        _listeningStatsService = listeningStatsService ?? throw new ArgumentNullException(nameof(listeningStatsService));
        _libraryViewModel = libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _playlistViewModel = playlistViewModel;
        _playlistNamePrompt = playlistNamePrompt ?? ShowDefaultPlaylistNamePrompt;
        _messageNotifier = messageNotifier ?? ShowDefaultMessage;

        _selectPeriodFilterCommand = new StatsCommand(SelectPeriodFilter);
        _playTopTracksCommand = new StatsCommand(_ => PlayTopTracks(), _ => TopTracks.Count > 0);
        _createTopTracksPlaylistCommand = new StatsCommand(
            _ => CreateTopTracksPlaylist(),
            _ => TopTracks.Count > 0 && Playlist != null);
        _playTrackCommand = new StatsCommand(PlayTrackItem, CanHandleTrackParameter);
        _playNextTrackCommand = new StatsCommand(param => EnqueueTrackItem(param, playNext: true), CanHandleTrackParameter);
        _enqueueTrackCommand = new StatsCommand(param => EnqueueTrackItem(param, playNext: false), CanHandleTrackParameter);
        _addTrackToPlaylistCommand = new StatsCommand(AddTrackToPlaylist, param => CanHandleTrackParameter(param) && Playlist != null);
        _navigateToArtistCommand = new StatsCommand(NavigateToArtist, CanHandleArtistParameter);
        _navigateToAlbumCommand = new StatsCommand(NavigateToAlbum, param => param is LibraryStatsAlbumItemViewModel);
        _selectArtistRankingCommand = new StatsCommand(_ => IsArtistRankingSelected = true);
        _selectAlbumRankingCommand = new StatsCommand(_ => IsArtistRankingSelected = false);
        _selectDailyTrendCommand = new StatsCommand(_ => IsMonthlyTrendSelected = false);
        _selectMonthlyTrendCommand = new StatsCommand(_ => IsMonthlyTrendSelected = true);
        _selectHourlyHabitCommand = new StatsCommand(_ => IsDayOfWeekHabitSelected = false);
        _selectDayOfWeekHabitCommand = new StatsCommand(_ => IsDayOfWeekHabitSelected = true);
        _refreshCommand = new StatsCommand(_ => Refresh());

        _listeningStatsService.StatsUpdated += OnStatsUpdated;
        _libraryViewModel.LibrarySynced += OnLibrarySynced;
        _libraryViewModel.TrackRemoved += OnLibraryTrackRemoved;
        _libraryViewModel.AlbumRemoved += OnLibraryAlbumRemoved;

        RefreshCore();
    }

    /// <summary>
    /// 現在の期間フィルタに基づき統計データを最新状態へ再集計します
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
    /// 表示中のよく聴く曲 TOP 10 を先頭から連続再生します
    /// </summary>
    public void PlayTopTracks()
    {
        List<Track> tracks = ResolveCurrentTopTracks();
        if (tracks.Count == 0)
        {
            return;
        }

        Track first = tracks[0];
        string listName = string.Format(CultureInfo.InvariantCulture, "よく聴く曲 TOP {0}（{1}）", tracks.Count, SelectedPeriodDisplayName);
        string subtitle = string.Format(CultureInfo.InvariantCulture, "{0} 曲", tracks.Count);
        _audioService.SetPlaylist(tracks, first);
        PlaybackRequested?.Invoke(tracks, first, listName, subtitle);
        _audioService.PlayTrack(first);
    }

    /// <summary>
    /// 表示中のよく聴く曲 TOP 10 から新規ユーザープレイリストを作成します
    /// </summary>
    /// <returns>作成された <see cref="UserPlaylist"/>（キャンセルまたは失敗時は null）</returns>
    public UserPlaylist? CreateTopTracksPlaylist()
    {
        PlaylistViewModel? playlistVm = Playlist;
        if (playlistVm == null)
        {
            return null;
        }

        List<Track> tracks = ResolveCurrentTopTracks();
        if (tracks.Count == 0)
        {
            return null;
        }

        string defaultName = string.Format(
            CultureInfo.InvariantCulture,
            "よく聴く曲 TOP {0}（{1}）",
            tracks.Count,
            SelectedPeriodDisplayName);
        string? playlistName = _playlistNamePrompt(defaultName);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return null;
        }

        UserPlaylist? created = playlistVm.CreatePlaylistWithTracks(playlistName.Trim(), tracks);
        if (created != null)
        {
            _messageNotifier(
                string.Format(CultureInfo.InvariantCulture, "プレイリスト「{0}」を作成しました（{1} 曲）。", created.Name, tracks.Count),
                "プレイリスト作成完了",
                false);
        }

        return created;
    }

    /// <summary>
    /// 期間フィルタの日本語表示名を取得します
    /// </summary>
    /// <param name="filter">期間フィルタ種別</param>
    /// <returns>日本語表示名</returns>
    public static string GetPeriodDisplayName(StatsPeriodFilter filter)
    {
        return filter switch
        {
            StatsPeriodFilter.ThisWeek => "今週",
            StatsPeriodFilter.ThisMonth => "今月",
            StatsPeriodFilter.ThisYear => "今年",
            StatsPeriodFilter.AllTime => "全期間",
            _ => "全期間",
        };
    }

    private void SelectPeriodFilter(object? parameter)
    {
        if (parameter is StatsPeriodFilter filter)
        {
            SelectedPeriodFilter = filter;
        }
        else if (parameter is string text && Enum.TryParse(text, ignoreCase: true, out StatsPeriodFilter parsed))
        {
            SelectedPeriodFilter = parsed;
        }
    }

    private void PlayTrackItem(object? parameter)
    {
        Track? target = ResolveTrackFromParameter(parameter);
        if (target == null)
        {
            return;
        }

        List<Track> tracks = ResolveCurrentTopTracks();
        Track? matchedInList = tracks.FirstOrDefault(
            t => string.Equals(t.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase));
        if (matchedInList != null)
        {
            target = matchedInList;
        }
        else
        {
            tracks.Insert(0, target);
        }

        string listName = string.Format(CultureInfo.InvariantCulture, "よく聴く曲 TOP {0}（{1}）", tracks.Count, SelectedPeriodDisplayName);
        string subtitle = string.Format(CultureInfo.InvariantCulture, "{0} 曲", tracks.Count);
        _audioService.SetPlaylist(tracks, target);
        PlaybackRequested?.Invoke(tracks, target, listName, subtitle);
        _audioService.PlayTrack(target);
    }

    private void EnqueueTrackItem(object? parameter, bool playNext)
    {
        Track? target = ResolveTrackFromParameter(parameter);
        if (target == null)
        {
            return;
        }

        if (EnqueueRequested != null)
        {
            EnqueueRequested.Invoke(new[] { target }, playNext);
        }
        else
        {
            _audioService.EnqueueTracks(new[] { target }, playNext);
        }
    }

    private void AddTrackToPlaylist(object? parameter)
    {
        PlaylistViewModel? playlistVm = Playlist;
        Track? target = ResolveTrackFromParameter(parameter);
        if (playlistVm == null || target == null)
        {
            return;
        }

        playlistVm.ShowAddToPlaylistDialogCommand.Execute(target);
    }

    private void NavigateToArtist(object? parameter)
    {
        string? artistName = parameter switch
        {
            LibraryStatsArtistItemViewModel artistItem => artistItem.ArtistName,
            LibraryStatsTrackItemViewModel trackItem => trackItem.Artist,
            string text => text,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(artistName))
        {
            return;
        }

        ArtistNavigationRequested?.Invoke(artistName.Trim());
    }

    private void NavigateToAlbum(object? parameter)
    {
        if (parameter is not LibraryStatsAlbumItemViewModel albumItem)
        {
            return;
        }

        Album? album = ResolveAlbumFromLibrary(albumItem);
        if (album == null)
        {
            return;
        }

        AlbumNavigationRequested?.Invoke(album);
    }

    private static bool CanHandleTrackParameter(object? parameter) =>
        parameter is LibraryStatsTrackItemViewModel or Track;

    private static bool CanHandleArtistParameter(object? parameter) =>
        parameter switch
        {
            LibraryStatsArtistItemViewModel a => !string.IsNullOrWhiteSpace(a.ArtistName),
            LibraryStatsTrackItemViewModel t => !string.IsNullOrWhiteSpace(t.Artist),
            string s => !string.IsNullOrWhiteSpace(s),
            _ => false,
        };

    private List<Track> ResolveCurrentTopTracks()
    {
        var tracks = new List<Track>(TopTracks.Count);
        foreach (LibraryStatsTrackItemViewModel item in TopTracks)
        {
            tracks.Add(ResolveTrackFromLibrary(item.FilePath) ?? CreateTrackFromRankedStat(item.Stat));
        }

        return tracks;
    }

    private Track? ResolveTrackFromParameter(object? parameter)
    {
        return parameter switch
        {
            LibraryStatsTrackItemViewModel item => ResolveTrackFromLibrary(item.FilePath) ?? CreateTrackFromRankedStat(item.Stat),
            Track track => track,
            _ => null,
        };
    }

    private Track? ResolveTrackFromLibrary(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        Track? matched = _libraryViewModel.Tracks.FirstOrDefault(
            t => !string.IsNullOrWhiteSpace(t.FilePath)
                 && string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (matched != null)
        {
            return matched;
        }

        foreach (Album album in _libraryViewModel.Albums)
        {
            matched = album.Tracks.FirstOrDefault(
                t => !string.IsNullOrWhiteSpace(t.FilePath)
                     && string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return null;
    }

    private Album? ResolveAlbumFromLibrary(LibraryStatsAlbumItemViewModel albumItem)
    {
        if (!string.IsNullOrWhiteSpace(albumItem.RepresentativeFilePath))
        {
            foreach (Album album in _libraryViewModel.Albums)
            {
                if (album.Tracks.Any(t =>
                    !string.IsNullOrWhiteSpace(t.FilePath)
                    && string.Equals(t.FilePath, albumItem.RepresentativeFilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    return album;
                }
            }
        }

        Album? exactMatch = _libraryViewModel.Albums.FirstOrDefault(a =>
            string.Equals(a.Title, albumItem.AlbumTitle, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Artist, albumItem.ArtistName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            return exactMatch;
        }

        return _libraryViewModel.Albums.FirstOrDefault(a =>
            string.Equals(a.Title, albumItem.AlbumTitle, StringComparison.OrdinalIgnoreCase));
    }

    private static Track CreateTrackFromRankedStat(RankedTrackStat stat)
    {
        TimeSpan estimatedDuration = stat.PlayCount > 0 && stat.TotalListeningDuration > TimeSpan.Zero
            ? TimeSpan.FromTicks(stat.TotalListeningDuration.Ticks / stat.PlayCount)
            : TimeSpan.Zero;

        return new Track
        {
            FilePath = stat.FilePath,
            Title = stat.Title,
            Artist = stat.Artist,
            Album = stat.Album,
            Duration = estimatedDuration,
        };
    }

    private void OnStatsUpdated(TrackListeningStat updatedStat)
    {
        _ = updatedStat;
        ScheduleDeferredRefresh();
    }

    private void OnLibrarySynced(LibrarySyncResult result)
    {
        _ = result;
        ScheduleDeferredRefresh();
    }

    private void OnLibraryTrackRemoved(Track removedTrack)
    {
        _ = removedTrack;
        ScheduleDeferredRefresh();
    }

    private void OnLibraryAlbumRemoved(Album removedAlbum)
    {
        _ = removedAlbum;
        ScheduleDeferredRefresh();
    }

    private void ScheduleDeferredRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _isDeferredRefreshScheduled, 1) != 0)
        {
            return;
        }

        _uiDispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _isDeferredRefreshScheduled, 0);
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
        IReadOnlyCollection<Track> libraryTracks = CollectLibraryTracks();
        _currentSnapshot = _libraryStatsService.GetStats(_selectedPeriodFilter, DefaultTopCount, libraryTracks);

        TopTracks.Clear();
        foreach (RankedTrackStat stat in _currentSnapshot.TopTracks)
        {
            TopTracks.Add(new LibraryStatsTrackItemViewModel(stat));
        }

        TopArtists.Clear();
        foreach (RankedArtistStat stat in _currentSnapshot.TopArtists)
        {
            TopArtists.Add(new LibraryStatsArtistItemViewModel(stat));
        }

        TopAlbums.Clear();
        foreach (RankedAlbumStat stat in _currentSnapshot.TopAlbums)
        {
            TopAlbums.Add(new LibraryStatsAlbumItemViewModel(stat));
        }

        RebuildTrendBars();
        RebuildHabitBars();
        NotifySnapshotPropertiesChanged();
        RaiseCommandsCanExecuteChanged();
    }

    private IReadOnlyCollection<Track> CollectLibraryTracks()
    {
        if (_libraryViewModel.Tracks.Count > 0)
        {
            return _libraryViewModel.Tracks.ToList();
        }

        if (_libraryViewModel.Albums.Count > 0)
        {
            return _libraryViewModel.Albums
                .SelectMany(a => a.Tracks)
                .ToList();
        }

        return Array.Empty<Track>();
    }

    private void RebuildTrendBars()
    {
        TrendBars.Clear();
        if (_currentSnapshot == null)
        {
            return;
        }

        IReadOnlyList<ListeningTrendPoint> source = _isMonthlyTrendSelected
            ? _currentSnapshot.MonthlyTrends
            : _currentSnapshot.DailyTrends;
        int maxCount = source.Count > 0 ? source.Max(p => p.PlayCount) : 0;

        foreach (ListeningTrendPoint point in source)
        {
            TrendBars.Add(new LibraryStatsChartBarViewModel(
                point.Label,
                point.DetailLabel,
                point.PlayCount,
                point.TotalListeningDuration,
                point.HeightRatio,
                isPeak: maxCount > 0 && point.PlayCount == maxCount));
        }
    }

    private void RebuildHabitBars()
    {
        HabitBars.Clear();
        if (_currentSnapshot == null)
        {
            return;
        }

        IReadOnlyList<ListeningHabitBucket> source = _isDayOfWeekHabitSelected
            ? _currentSnapshot.Habits.DayOfWeekBuckets
            : _currentSnapshot.Habits.HourlyBuckets;

        foreach (ListeningHabitBucket bucket in source)
        {
            HabitBars.Add(new LibraryStatsChartBarViewModel(
                bucket.Label,
                bucket.DetailLabel,
                bucket.PlayCount,
                bucket.TotalListeningDuration,
                bucket.HeightRatio,
                bucket.IsPeak));
        }
    }

    private void NotifyPeriodPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsThisWeekSelected));
        OnPropertyChanged(nameof(IsThisMonthSelected));
        OnPropertyChanged(nameof(IsThisYearSelected));
        OnPropertyChanged(nameof(IsAllTimeSelected));
        OnPropertyChanged(nameof(SelectedPeriodDisplayName));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void NotifySnapshotPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasTopTracks));
        OnPropertyChanged(nameof(HasTopArtists));
        OnPropertyChanged(nameof(HasTopAlbums));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(TotalPlayCount));
        OnPropertyChanged(nameof(TotalPlayCountDisplay));
        OnPropertyChanged(nameof(DailyAveragePlayCountDisplay));
        OnPropertyChanged(nameof(TotalListeningDurationDisplay));
        OnPropertyChanged(nameof(UniqueTrackCountDisplay));
        OnPropertyChanged(nameof(UniqueLibraryBreakdownDisplay));
        OnPropertyChanged(nameof(TopArtistNameDisplay));
        OnPropertyChanged(nameof(TopArtistPlayCountDisplay));
        OnPropertyChanged(nameof(HabitPeakInsight));
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        _playTopTracksCommand.RaiseCanExecuteChanged();
        _createTopTracksPlaylistCommand.RaiseCanExecuteChanged();
        _playTrackCommand.RaiseCanExecuteChanged();
        _playNextTrackCommand.RaiseCanExecuteChanged();
        _enqueueTrackCommand.RaiseCanExecuteChanged();
        _addTrackToPlaylistCommand.RaiseCanExecuteChanged();
        _navigateToArtistCommand.RaiseCanExecuteChanged();
        _navigateToAlbumCommand.RaiseCanExecuteChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private static string? ShowDefaultPlaylistNamePrompt(string defaultName)
    {
        var inputBox = new InputBox("新規プレイリスト名を入力してください:", defaultName);
        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner && !ReferenceEquals(owner, inputBox))
        {
            inputBox.Owner = owner;
        }

        return inputBox.ShowDialog() == true ? inputBox.InputText : null;
    }

    private static void ShowDefaultMessage(string message, string title, bool isError)
    {
        var app = System.Windows.Application.Current;
        if (app != null && app.Dispatcher.CheckAccess() && app.MainWindow is { IsVisible: true })
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                isError ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// イベント購読を解除してリソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _listeningStatsService.StatsUpdated -= OnStatsUpdated;
            _libraryViewModel.LibrarySynced -= OnLibrarySynced;
            _libraryViewModel.TrackRemoved -= OnLibraryTrackRemoved;
            _libraryViewModel.AlbumRemoved -= OnLibraryAlbumRemoved;
        }
    }
}
