using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// サイドメニュー「すべての曲」ビューを担当するViewModel。
/// ライブラリ内の全楽曲のフラット一覧表示、検索フィルタリング、ソート、一括再生および個別操作を提供します。
/// </summary>
public sealed class AllSongsViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan DefaultSearchDebounceDelay = TimeSpan.FromMilliseconds(200);

    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioService _audioService;
    private readonly TimeSpan _searchDebounceDelay;
    private readonly Random _random = new();

    private string _searchKeyword = string.Empty;
    private string _selectedSortOption = "Title";
    private AllSongsSortOption _selectedSortOptionItem;
    private bool _isAscending = true;
    private Track? _selectedTrack;
    private AllSongsTrackItem? _selectedTrackItem;
    private string _summaryText = "全 0 曲 (0 分)";
    private CancellationTokenSource? _searchDebounceCts;
    private int _isLibraryRefreshPending;
    private bool _disposed;

    /// <summary>
    /// 検索・ソート適用後の表示用トラックコレクション
    /// </summary>
    public ObservableCollection<Track> FilteredTracks { get; } = new();

    /// <summary>
    /// 1始まりの表示連番を付与した表示用行コレクション（UI仮想化リサイクリング時の連番不整合防止用）
    /// </summary>
    public ObservableCollection<AllSongsTrackItem> DisplayTracks { get; } = new();

    /// <summary>
    /// ライブラリ内の全トラックコレクション（FilteredTracksの互換エイリアス）
    /// </summary>
    public ObservableCollection<Track> AllTracks => FilteredTracks;

    /// <summary>
    /// ソート順の選択肢一覧（内部キーと日本語表示ラベルのペア）
    /// </summary>
    public IReadOnlyList<AllSongsSortOption> SortOptions { get; } =
    [
        new("Title", "タイトル"),
        new("Artist", "アーティスト"),
        new("Album", "アルバム"),
        new("Duration", "時間"),
        new("Genre", "ジャンル"),
    ];

    /// <summary>
    /// 検索キーワード（曲名・アーティスト名・アルバム名・ジャンルを横断フィルタ）
    /// </summary>
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value ?? string.Empty))
            {
                ScheduleSearchRefresh();
            }
        }
    }

    /// <summary>
    /// 選択中のソート基準キー（Title / Artist / Album / Duration / Genre）
    /// </summary>
    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            var normalized = value ?? "Title";
            if (SetProperty(ref _selectedSortOption, normalized))
            {
                var matched = SortOptions.FirstOrDefault(o => string.Equals(o.Key, normalized, StringComparison.Ordinal))
                    ?? SortOptions[0];
                if (!Equals(_selectedSortOptionItem, matched))
                {
                    _selectedSortOptionItem = matched;
                    OnPropertyChanged(nameof(SelectedSortOptionItem));
                }

                RefreshTracks();
            }
        }
    }

    /// <summary>
    /// 選択中のソート選択肢アイテム（ComboBox の SelectedItem バインド用）
    /// </summary>
    public AllSongsSortOption SelectedSortOptionItem
    {
        get => _selectedSortOptionItem;
        set
        {
            var next = value ?? SortOptions[0];
            if (SetProperty(ref _selectedSortOptionItem, next))
            {
                if (!string.Equals(_selectedSortOption, next.Key, StringComparison.Ordinal))
                {
                    _selectedSortOption = next.Key;
                    OnPropertyChanged(nameof(SelectedSortOption));
                }

                RefreshTracks();
            }
        }
    }

    /// <summary>
    /// 昇順ソートかどうか
    /// </summary>
    public bool IsAscending
    {
        get => _isAscending;
        set
        {
            if (SetProperty(ref _isAscending, value))
            {
                RefreshTracks();
            }
        }
    }

    /// <summary>
    /// 現在選択中のトラック
    /// </summary>
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetProperty(ref _selectedTrack, value))
            {
                var matchedRow = value != null
                    ? DisplayTracks.FirstOrDefault(row => ReferenceEquals(row.Track, value))
                    : null;
                if (!Equals(_selectedTrackItem, matchedRow))
                {
                    _selectedTrackItem = matchedRow;
                    OnPropertyChanged(nameof(SelectedTrackItem));
                }
            }
        }
    }

    /// <summary>
    /// 現在選択中の表示行（ListBox の SelectedItem バインド用）
    /// </summary>
    public AllSongsTrackItem? SelectedTrackItem
    {
        get => _selectedTrackItem;
        set
        {
            if (SetProperty(ref _selectedTrackItem, value))
            {
                var nextTrack = value?.Track;
                if (!ReferenceEquals(_selectedTrack, nextTrack))
                {
                    _selectedTrack = nextTrack;
                    OnPropertyChanged(nameof(SelectedTrack));
                }
            }
        }
    }

    /// <summary>
    /// 表示中の楽曲数および総再生時間のサマリーテキスト
    /// </summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>
    /// 表示中の楽曲が空かどうか
    /// </summary>
    public bool IsEmpty => FilteredTracks.Count == 0;

    /// <summary>
    /// プレイリスト専用ViewModelへの参照（コンテキストメニューからのプレイリスト追加用）
    /// </summary>
    public PlaylistViewModel? Playlist => _libraryViewModel.Playlist;

    /// <summary>表示中の全曲を先頭から再生するコマンド</summary>
    public ICommand PlayAllCommand { get; }

    /// <summary>表示中の全曲をシャッフル再生するコマンド</summary>
    public ICommand ShufflePlayCommand { get; }

    /// <summary>指定されたトラックを即時再生するコマンド</summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>指定されたトラックを次に再生するコマンド</summary>
    public ICommand PlayNextCommand { get; }

    /// <summary>指定されたトラックをキュー末尾に追加するコマンド</summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>お気に入り登録状態を切り替えるコマンド</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>ソート昇順・降順を切り替えるコマンド</summary>
    public ICommand ToggleSortDirectionCommand { get; }

    /// <summary>検索キーワードをクリアするコマンド</summary>
    public ICommand ClearSearchCommand { get; }

    /// <summary>トラックのプロパティを表示するコマンド</summary>
    public ICommand ShowTrackPropertiesCommand { get; }

    /// <summary>ファイルの保存場所をエクスプローラーで開くコマンド</summary>
    public ICommand OpenFileLocationCommand { get; }

    /// <summary>トラックをライブラリから削除するコマンド</summary>
    public ICommand DeleteTrackCommand { get; }

    /// <summary>
    /// 再生リスト更新および再生開始を要求するイベント（対象トラック群, 開始トラック, リスト名, サブタイトル）
    /// </summary>
    public event Action<IReadOnlyList<Track>, Track?, string, string>? PlaybackRequested;

    /// <summary>
    /// 再生キューへの追加を要求するイベント（対象トラック群, playNext: trueなら次に再生）
    /// </summary>
    public event Action<IReadOnlyList<Track>, bool>? EnqueueRequested;

    /// <summary>
    /// AllSongsViewModelを初期化します（既定の検索デバウンス時間 200ms を適用します）
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    [ActivatorUtilitiesConstructor]
    public AllSongsViewModel(LibraryViewModel libraryViewModel, IAudioService audioService)
        : this(libraryViewModel, audioService, DefaultSearchDebounceDelay)
    {
    }

    /// <summary>
    /// 検索デバウンス時間を指定してAllSongsViewModelを初期化します
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="searchDebounceDelay">検索キーワード入力時のデバウンス遅延時間</param>
    public AllSongsViewModel(LibraryViewModel libraryViewModel, IAudioService audioService, TimeSpan searchDebounceDelay)
    {
        _libraryViewModel = libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _searchDebounceDelay = searchDebounceDelay < TimeSpan.Zero ? TimeSpan.Zero : searchDebounceDelay;
        _selectedSortOptionItem = SortOptions[0];

        PlayAllCommand = new RelayCommand(_ => PlayAll(), _ => FilteredTracks.Count > 0);
        ShufflePlayCommand = new RelayCommand(_ => ShufflePlay(), _ => FilteredTracks.Count > 0);
        PlayTrackCommand = new RelayCommand(PlayTrack);
        PlayNextCommand = new RelayCommand(param => EnqueueSingle(param, playNext: true));
        EnqueueTrackCommand = new RelayCommand(param => EnqueueSingle(param, playNext: false));
        ToggleFavoriteCommand = new RelayCommand(param => _libraryViewModel.ToggleFavoriteCommand.Execute(param));
        ToggleSortDirectionCommand = new RelayCommand(_ => IsAscending = !IsAscending);
        ClearSearchCommand = new RelayCommand(_ => SearchKeyword = string.Empty);
        ShowTrackPropertiesCommand = new RelayCommand(param => _libraryViewModel.ShowTrackPropertiesCommand.Execute(param));
        OpenFileLocationCommand = new RelayCommand(param => _libraryViewModel.OpenFileLocationCommand.Execute(param));
        DeleteTrackCommand = new RelayCommand(param => _libraryViewModel.DeleteTrackCommand.Execute(param));

        _libraryViewModel.Tracks.CollectionChanged += OnTracksCollectionChanged;
        _libraryViewModel.Albums.CollectionChanged += OnAlbumsCollectionChanged;

        RefreshTracks();
    }

    /// <summary>
    /// ライブラリの全楽曲から検索・ソート条件を適用して表示リストを再構築します
    /// </summary>
    public void RefreshTracks()
    {
        if (_disposed)
        {
            return;
        }

        var sourceTracks = CollectSourceTracks();

        IEnumerable<Track> query = sourceTracks;

        if (!string.IsNullOrWhiteSpace(_searchKeyword))
        {
            string keyword = _searchKeyword.Trim();
            query = query.Where(t =>
                (!string.IsNullOrEmpty(t.Title) && t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.Artist) && t.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.Album) && t.Album.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.Genre) && t.Genre.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        query = ApplySort(query, _selectedSortOption, _isAscending);
        var sorted = query.ToList();

        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            FilteredTracks.Clear();
            DisplayTracks.Clear();
            AllSongsTrackItem? nextSelectedRow = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                var track = sorted[i];
                var row = new AllSongsTrackItem(i + 1, track);
                FilteredTracks.Add(track);
                DisplayTracks.Add(row);

                if (_selectedTrack != null && ReferenceEquals(track, _selectedTrack))
                {
                    nextSelectedRow = row;
                }
            }

            if (!Equals(_selectedTrackItem, nextSelectedRow))
            {
                _selectedTrackItem = nextSelectedRow;
                OnPropertyChanged(nameof(SelectedTrackItem));
            }

            UpdateSummary(sorted);
            OnPropertyChanged(nameof(IsEmpty));
            CommandManager.InvalidateRequerySuggested();
        });
    }

    /// <summary>
    /// 表示中の全曲を先頭から再生します
    /// </summary>
    public void PlayAll()
    {
        if (FilteredTracks.Count == 0)
        {
            return;
        }

        var list = FilteredTracks.ToList();
        var firstTrack = list[0];
        _audioService.SetPlaylist(list, firstTrack);
        PlaybackRequested?.Invoke(list, firstTrack, "すべての曲", $"{list.Count} 曲");
        _audioService.PlayTrack(firstTrack);
    }

    /// <summary>
    /// 表示中の全曲をワンショットシャッフルして即時再生します。
    /// プレイヤーバーのシャッフルモード（<see cref="IAudioService.IsShuffleEnabled"/>）の状態は変更せず、
    /// 有効時は <see cref="IAudioService.AlbumQueue"/> のシャッフル後順序とUI表示リストを同期させます。
    /// </summary>
    public void ShufflePlay()
    {
        if (FilteredTracks.Count == 0)
        {
            return;
        }

        var list = FilteredTracks.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        var firstTrack = list[0];
        _audioService.SetPlaylist(list, firstTrack);

        var effectiveList = _audioService.IsShuffleEnabled
            ? BuildEffectiveShuffleList(firstTrack, list, _audioService.AlbumQueue)
            : list;

        PlaybackRequested?.Invoke(effectiveList, firstTrack, "すべての曲（シャッフル）", $"{effectiveList.Count} 曲");
        _audioService.PlayTrack(firstTrack);
    }

    /// <summary>
    /// 指定されたトラックを現在の表示リストコンテキストで即時再生します
    /// </summary>
    /// <param name="parameter">再生対象のトラック</param>
    public void PlayTrack(object? parameter)
    {
        if (parameter is not Track track)
        {
            return;
        }

        var list = FilteredTracks.Count > 0 ? FilteredTracks.ToList() : new List<Track> { track };
        if (!list.Contains(track))
        {
            list.Insert(0, track);
        }

        _audioService.SetPlaylist(list, track);
        PlaybackRequested?.Invoke(list, track, "すべての曲", $"{list.Count} 曲");
        _audioService.PlayTrack(track);
    }

    private void EnqueueSingle(object? parameter, bool playNext)
    {
        if (parameter is not Track track)
        {
            return;
        }

        if (EnqueueRequested != null)
        {
            EnqueueRequested.Invoke(new[] { track }, playNext);
        }
        else
        {
            _audioService.EnqueueTracks(new[] { track }, playNext);
        }
    }

    private List<Track> CollectSourceTracks()
    {
        if (_libraryViewModel.Tracks.Count > 0)
        {
            return _libraryViewModel.Tracks.ToList();
        }

        if (_libraryViewModel.Albums.Count > 0)
        {
            return _libraryViewModel.Albums
                .SelectMany(a => a.Tracks)
                .Distinct()
                .ToList();
        }

        return new List<Track>();
    }

    private static List<Track> BuildEffectiveShuffleList(Track firstTrack, List<Track> fallbackList, IReadOnlyList<Track> albumQueue)
    {
        if (albumQueue.Count == 0 && fallbackList.Count > 1)
        {
            return fallbackList;
        }

        var effective = new List<Track>(albumQueue.Count + 1) { firstTrack };
        foreach (var item in albumQueue)
        {
            if (!string.Equals(item.FilePath, firstTrack.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                effective.Add(item);
            }
        }

        return effective;
    }

    private static IEnumerable<Track> ApplySort(IEnumerable<Track> tracks, string sortOption, bool isAscending)
    {
        return sortOption switch
        {
            "Artist" => isAscending
                ? tracks.OrderBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.TrackNumber)
                        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderByDescending(t => t.Artist, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.TrackNumber)
                        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            "Album" => isAscending
                ? tracks.OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.TrackNumber)
                        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderByDescending(t => t.Album, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.TrackNumber)
                        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            "Duration" => isAscending
                ? tracks.OrderBy(t => t.Duration).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderByDescending(t => t.Duration).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            "Genre" => isAscending
                ? tracks.OrderBy(t => t.Genre, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderByDescending(t => t.Genre, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            _ => isAscending
                ? tracks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderByDescending(t => t.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Artist, StringComparer.OrdinalIgnoreCase),
        };
    }

    private void UpdateSummary(List<Track> tracks)
    {
        var totalDuration = TimeSpan.Zero;
        foreach (var track in tracks)
        {
            totalDuration += track.Duration;
        }

        string durationText = totalDuration.TotalHours >= 1.0
            ? string.Format(CultureInfo.InvariantCulture, "{0:F1} 時間", totalDuration.TotalHours)
            : string.Format(CultureInfo.InvariantCulture, "{0} 分", (int)Math.Round(totalDuration.TotalMinutes));

        SummaryText = string.Format(CultureInfo.InvariantCulture, "全 {0:N0} 曲 ({1})", tracks.Count, durationText);
    }

    private void ScheduleSearchRefresh()
    {
        CancelPendingSearchDebounce();

        // デバウンス時間0または検索クリア時は即時反映
        if (_searchDebounceDelay <= TimeSpan.Zero || string.IsNullOrEmpty(_searchKeyword))
        {
            RefreshTracks();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = RunDebouncedRefreshAsync(cts);
    }

    private async Task RunDebouncedRefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_searchDebounceDelay, cts.Token).ConfigureAwait(false);
            if (!cts.Token.IsCancellationRequested && !_disposed)
            {
                RefreshTracks();
            }
        }
        catch (OperationCanceledException)
        {
            // 後続入力やDisposeによるキャンセルは正常動作
        }
        finally
        {
            if (ReferenceEquals(_searchDebounceCts, cts))
            {
                _searchDebounceCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelPendingSearchDebounce()
    {
        var cts = _searchDebounceCts;
        _searchDebounceCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleLibraryRefresh();
    }

    private void OnAlbumsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Tracks が存在する場合は Tracks.CollectionChanged を正とする（Albumsソート等での無駄な再構築を防止）
        if (_libraryViewModel.Tracks.Count > 0)
        {
            return;
        }

        ScheduleLibraryRefresh();
    }

    private void ScheduleLibraryRefresh()
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            RefreshTracks();
            return;
        }

        // 同一フレーム内の連続的な追加・削除を1回の再構築へコアレッシングする（O(N^2) 防止）
        if (Interlocked.Exchange(ref _isLibraryRefreshPending, 1) == 0)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _isLibraryRefreshPending, 0);
                if (!_disposed)
                {
                    RefreshTracks();
                }
            }));
        }
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _libraryViewModel.Tracks.CollectionChanged -= OnTracksCollectionChanged;
            _libraryViewModel.Albums.CollectionChanged -= OnAlbumsCollectionChanged;
            CancelPendingSearchDebounce();
        }
    }
}
