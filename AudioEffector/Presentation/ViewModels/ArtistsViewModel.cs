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
/// サイドメニュー「アーティスト」ビューを担当するViewModel
/// ライブラリ内の楽曲をアーティスト単位で集約し、検索・ソート・詳細ブラウジング・一括再生を提供します
/// </summary>
public sealed class ArtistsViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan DefaultSearchDebounceDelay = TimeSpan.FromMilliseconds(200);

    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioService _audioService;
    private readonly TimeSpan _searchDebounceDelay;
    private readonly Random _random = new();

    private List<ArtistGroup> _allArtists = new();
    private string _searchKeyword = string.Empty;
    private string _selectedSortOption = "Name";
    private ArtistsSortOption _selectedSortOptionItem;
    private bool _isAscending = true;
    private ArtistGroup? _selectedArtist;
    private Track? _selectedTrack;
    private string _summaryText = "全 0 アーティスト";
    private CancellationTokenSource? _searchDebounceCts;
    private int _isLibraryRefreshPending;
    private bool _disposed;

    /// <summary>
    /// 検索・ソート適用後のアーティストグループ一覧
    /// </summary>
    public ObservableCollection<ArtistGroup> FilteredArtists { get; } = new();

    /// <summary>
    /// アーティストグループ一覧（FilteredArtistsの互換エイリアス）
    /// </summary>
    public ObservableCollection<ArtistGroup> ArtistGroups => FilteredArtists;

    /// <summary>
    /// ソート順の選択肢一覧（内部キーと日本語表示ラベルのペア）
    /// </summary>
    public IReadOnlyList<ArtistsSortOption> SortOptions { get; } =
    [
        new("Name", "名前順"),
        new("TrackCount", "曲数順"),
        new("AlbumCount", "アルバム数順"),
    ];

    /// <summary>
    /// 検索キーワード（アーティスト名・アルバム名・曲名で絞り込み）
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
    /// 選択中のソート基準キー（Name / TrackCount / AlbumCount）
    /// </summary>
    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            var normalized = value ?? "Name";
            if (SetProperty(ref _selectedSortOption, normalized))
            {
                var matched = SortOptions.FirstOrDefault(o => string.Equals(o.Key, normalized, StringComparison.Ordinal))
                    ?? SortOptions[0];
                if (!Equals(_selectedSortOptionItem, matched))
                {
                    _selectedSortOptionItem = matched;
                    OnPropertyChanged(nameof(SelectedSortOptionItem));
                }

                ApplyFilterAndSort();
            }
        }
    }

    /// <summary>
    /// 選択中のソート選択肢アイテム（ComboBox の SelectedItem バインド用）
    /// </summary>
    public ArtistsSortOption SelectedSortOptionItem
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

                ApplyFilterAndSort();
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
                ApplyFilterAndSort();
            }
        }
    }

    /// <summary>
    /// 現在選択中のアーティストグループ
    /// </summary>
    public ArtistGroup? SelectedArtist
    {
        get => _selectedArtist;
        set
        {
            if (SetProperty(ref _selectedArtist, value))
            {
                SelectedTrack = null;
                OnPropertyChanged(nameof(HasSelectedArtist));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>
    /// アーティストが選択されているかどうか
    /// </summary>
    public bool HasSelectedArtist => SelectedArtist != null;

    /// <summary>
    /// 詳細ペインで現在選択中のトラック
    /// </summary>
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set => SetProperty(ref _selectedTrack, value);
    }

    /// <summary>
    /// アーティスト総数のサマリーテキスト
    /// </summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>
    /// 表示中のアーティストが空かどうか
    /// </summary>
    public bool IsEmpty => FilteredArtists.Count == 0;

    /// <summary>
    /// プレイリスト専用ViewModelへの参照（コンテキストメニュー用）
    /// </summary>
    public PlaylistViewModel? Playlist => _libraryViewModel.Playlist;

    /// <summary>指定アーティスト（未指定時は選択中アーティスト）の全曲を再生するコマンド</summary>
    public ICommand PlayArtistCommand { get; }

    /// <summary>指定アーティスト（未指定時は選択中アーティスト）の全曲をシャッフル再生するコマンド</summary>
    public ICommand ShuffleArtistCommand { get; }

    /// <summary>指定アーティストの全曲を次に再生するコマンド</summary>
    public ICommand PlayNextArtistCommand { get; }

    /// <summary>指定アーティストの全曲をキュー末尾に追加するコマンド</summary>
    public ICommand EnqueueArtistCommand { get; }

    /// <summary>指定アルバムを再生するコマンド</summary>
    public ICommand PlayAlbumCommand { get; }

    /// <summary>指定トラックを再生するコマンド</summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>指定トラックを次に再生するコマンド</summary>
    public ICommand PlayNextTrackCommand { get; }

    /// <summary>指定トラックをキュー末尾に追加するコマンド</summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>お気に入り登録状態を切り替えるコマンド</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>ソート昇順・降順を切り替えるコマンド</summary>
    public ICommand ToggleSortDirectionCommand { get; }

    /// <summary>検索キーワードをクリアするコマンド</summary>
    public ICommand ClearSearchCommand { get; }

    /// <summary>トラックプロパティを表示するコマンド</summary>
    public ICommand ShowTrackPropertiesCommand { get; }

    /// <summary>ファイルの場所を開くコマンド</summary>
    public ICommand OpenFileLocationCommand { get; }

    /// <summary>トラックを削除するコマンド</summary>
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
    /// ArtistsViewModelを初期化します（既定の検索デバウンス時間 200ms を適用します）
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    [ActivatorUtilitiesConstructor]
    public ArtistsViewModel(LibraryViewModel libraryViewModel, IAudioService audioService)
        : this(libraryViewModel, audioService, DefaultSearchDebounceDelay)
    {
    }

    /// <summary>
    /// 検索デバウンス時間を指定してArtistsViewModelを初期化します
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="searchDebounceDelay">検索キーワード入力時のデバウンス遅延時間</param>
    public ArtistsViewModel(LibraryViewModel libraryViewModel, IAudioService audioService, TimeSpan searchDebounceDelay)
    {
        _libraryViewModel = libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _searchDebounceDelay = searchDebounceDelay < TimeSpan.Zero ? TimeSpan.Zero : searchDebounceDelay;
        _selectedSortOptionItem = SortOptions[0];

        PlayArtistCommand = new RelayCommand(
            param => PlayArtist(param as ArtistGroup ?? SelectedArtist),
            CanExecuteArtistPlayback);
        ShuffleArtistCommand = new RelayCommand(
            param => ShuffleArtist(param as ArtistGroup ?? SelectedArtist),
            CanExecuteArtistPlayback);
        PlayNextArtistCommand = new RelayCommand(
            param => EnqueueArtist(param as ArtistGroup ?? SelectedArtist, playNext: true),
            CanExecuteArtistPlayback);
        EnqueueArtistCommand = new RelayCommand(
            param => EnqueueArtist(param as ArtistGroup ?? SelectedArtist, playNext: false),
            CanExecuteArtistPlayback);
        PlayAlbumCommand = new RelayCommand(PlayAlbum, param => param is Album album && album.Tracks.Count > 0);
        PlayTrackCommand = new RelayCommand(PlayTrack, param => param is Track);
        PlayNextTrackCommand = new RelayCommand(param => EnqueueSingleTrack(param, playNext: true), param => param is Track);
        EnqueueTrackCommand = new RelayCommand(param => EnqueueSingleTrack(param, playNext: false), param => param is Track);
        ToggleFavoriteCommand = new RelayCommand(param => _libraryViewModel.ToggleFavoriteCommand.Execute(param));
        ToggleSortDirectionCommand = new RelayCommand(_ => IsAscending = !IsAscending);
        ClearSearchCommand = new RelayCommand(_ => SearchKeyword = string.Empty);
        ShowTrackPropertiesCommand = new RelayCommand(param => _libraryViewModel.ShowTrackPropertiesCommand.Execute(param));
        OpenFileLocationCommand = new RelayCommand(param => _libraryViewModel.OpenFileLocationCommand.Execute(param));
        DeleteTrackCommand = new RelayCommand(param => _libraryViewModel.DeleteTrackCommand.Execute(param));

        _libraryViewModel.Tracks.CollectionChanged += OnTracksCollectionChanged;
        _libraryViewModel.Albums.CollectionChanged += OnAlbumsCollectionChanged;

        RefreshArtists();
    }

    /// <summary>
    /// ライブラリ情報からアーティストグループキャッシュを再構築し、検索・ソートを適用します
    /// </summary>
    public void RefreshArtists()
    {
        if (_disposed)
        {
            return;
        }

        var sourceTracks = CollectSourceTracks();
        _allArtists = BuildArtistGroups(sourceTracks);
        ApplyFilterAndSort();
    }

    /// <summary>
    /// キャッシュ済みアーティストグループ一覧に対して検索フィルタとソートのみを適用します
    /// </summary>
    private void ApplyFilterAndSort()
    {
        if (_disposed)
        {
            return;
        }

        IEnumerable<ArtistGroup> query = _allArtists;

        if (!string.IsNullOrWhiteSpace(_searchKeyword))
        {
            string keyword = _searchKeyword.Trim();
            query = query.Where(g =>
                g.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                g.Albums.Any(a => a.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                g.Tracks.Any(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        query = ApplySort(query, _selectedSortOption, _isAscending);
        var sorted = query.ToList();
        string? previousSelectedName = _selectedArtist?.Name;

        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            FilteredArtists.Clear();
            foreach (var group in sorted)
            {
                FilteredArtists.Add(group);
            }

            if (previousSelectedName != null)
            {
                SelectedArtist = FilteredArtists.FirstOrDefault(
                    a => string.Equals(a.Name, previousSelectedName, StringComparison.OrdinalIgnoreCase))
                    ?? FilteredArtists.FirstOrDefault();
            }
            else if (SelectedArtist == null && FilteredArtists.Count > 0)
            {
                SelectedArtist = FilteredArtists[0];
            }
            else if (FilteredArtists.Count == 0)
            {
                SelectedArtist = null;
            }

            SummaryText = string.Format(CultureInfo.InvariantCulture, "全 {0:N0} アーティスト", sorted.Count);
            OnPropertyChanged(nameof(IsEmpty));
            CommandManager.InvalidateRequerySuggested();
        });
    }

    /// <summary>
    /// 指定されたアーティストの全楽曲を先頭から再生します
    /// </summary>
    /// <param name="artist">再生対象のアーティストグループ</param>
    public void PlayArtist(ArtistGroup? artist)
    {
        if (artist == null || artist.Tracks.Count == 0)
        {
            return;
        }

        var list = artist.Tracks.ToList();
        var first = list[0];
        _audioService.SetPlaylist(list, first);
        PlaybackRequested?.Invoke(list, first, artist.Name, artist.SummaryText);
        _audioService.PlayTrack(first);
    }

    /// <summary>
    /// 指定されたアーティストの全楽曲をシャッフル再生します
    /// </summary>
    /// <param name="artist">再生対象のアーティストグループ</param>
    public void ShuffleArtist(ArtistGroup? artist)
    {
        if (artist == null || artist.Tracks.Count == 0)
        {
            return;
        }

        var list = artist.Tracks.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        var first = list[0];
        _audioService.SetPlaylist(list, first);

        var effectiveList = _audioService.IsShuffleEnabled
            ? BuildEffectiveShuffleList(first, list, _audioService.AlbumQueue)
            : list;

        PlaybackRequested?.Invoke(effectiveList, first, $"{artist.Name}（シャッフル）", artist.SummaryText);
        _audioService.PlayTrack(first);
    }

    /// <summary>
    /// 指定されたアルバムを再生します
    /// </summary>
    /// <param name="parameter">再生対象のアルバム</param>
    public void PlayAlbum(object? parameter)
    {
        if (parameter is not Album album || album.Tracks.Count == 0)
        {
            return;
        }

        var list = album.Tracks.ToList();
        var first = list[0];
        _audioService.SetPlaylist(list, first);
        PlaybackRequested?.Invoke(list, first, album.Title, album.Artist);
        _audioService.PlayTrack(first);
    }

    /// <summary>
    /// 指定されたトラックをアーティスト全曲コンテキストで即時再生します
    /// </summary>
    /// <param name="parameter">再生対象のトラック</param>
    public void PlayTrack(object? parameter)
    {
        if (parameter is not Track track)
        {
            return;
        }

        var list = SelectedArtist is { Tracks.Count: > 0 }
            ? SelectedArtist.Tracks.ToList()
            : new List<Track> { track };
        if (!list.Contains(track))
        {
            list.Insert(0, track);
        }

        string title = SelectedArtist?.Name ?? track.Artist;
        string subtitle = SelectedArtist?.SummaryText ?? track.Album;
        _audioService.SetPlaylist(list, track);
        PlaybackRequested?.Invoke(list, track, title, subtitle);
        _audioService.PlayTrack(track);
    }

    private bool CanExecuteArtistPlayback(object? parameter)
    {
        var target = parameter as ArtistGroup ?? SelectedArtist;
        return target is { Tracks.Count: > 0 };
    }

    private void EnqueueArtist(ArtistGroup? artist, bool playNext)
    {
        if (artist == null || artist.Tracks.Count == 0)
        {
            return;
        }

        var list = artist.Tracks.ToList();
        if (EnqueueRequested != null)
        {
            EnqueueRequested.Invoke(list, playNext);
        }
        else
        {
            _audioService.EnqueueTracks(list, playNext);
        }
    }

    private void EnqueueSingleTrack(object? parameter, bool playNext)
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

    private static List<ArtistGroup> BuildArtistGroups(List<Track> tracks)
    {
        return tracks
            .GroupBy(
                t => string.IsNullOrWhiteSpace(t.Artist) ? "Unknown Artist" : t.Artist.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                string artistName = group.Key;
                var artistTracks = group
                    .OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.TrackNumber)
                    .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var artistAlbums = artistTracks
                    .GroupBy(
                        t => string.IsNullOrWhiteSpace(t.Album) ? "Unknown Album" : t.Album.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(albumGroup =>
                    {
                        var albumTracks = albumGroup.ToList();
                        uint year = albumTracks.Select(t => t.Year).FirstOrDefault(y => y > 0);
                        return new Album(albumGroup.Key, artistName, year, albumTracks);
                    })
                    .OrderByDescending(a => a.Year)
                    .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ArtistGroup(artistName, artistAlbums, artistTracks);
            })
            .ToList();
    }

    private static IEnumerable<ArtistGroup> ApplySort(IEnumerable<ArtistGroup> artists, string sortOption, bool isAscending)
    {
        return sortOption switch
        {
            "TrackCount" => isAscending
                ? artists.OrderBy(a => a.TrackCount).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : artists.OrderByDescending(a => a.TrackCount).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            "AlbumCount" => isAscending
                ? artists.OrderBy(a => a.AlbumCount).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : artists.OrderByDescending(a => a.AlbumCount).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            _ => isAscending
                ? artists.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : artists.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    private void ScheduleSearchRefresh()
    {
        CancelPendingSearchDebounce();

        // デバウンス時間0または検索クリア時は即時反映
        if (_searchDebounceDelay <= TimeSpan.Zero || string.IsNullOrEmpty(_searchKeyword))
        {
            ApplyFilterAndSort();
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
                ApplyFilterAndSort();
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
            RefreshArtists();
            return;
        }

        // 同一フレーム内の連続的な追加・削除を1回の再構築へコアレッシングする（O(N^2 log N) 防止）
        if (Interlocked.Exchange(ref _isLibraryRefreshPending, 1) == 0)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _isLibraryRefreshPending, 0);
                if (!_disposed)
                {
                    RefreshArtists();
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
