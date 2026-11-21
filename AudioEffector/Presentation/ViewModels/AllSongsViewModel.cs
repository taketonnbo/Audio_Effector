using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// サイドメニュー「すべての曲」ビューを担当するViewModel。
/// ライブラリ内の全楽曲のフラット一覧表示、検索フィルタリング、ソート、一括再生および個別操作を提供します。
/// </summary>
public sealed class AllSongsViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioService _audioService;
    private readonly Random _random = new();

    private string _searchKeyword = string.Empty;
    private string _selectedSortOption = "Title";
    private bool _isAscending = true;
    private Track? _selectedTrack;
    private string _summaryText = "全 0 曲 (0 分)";
    private bool _disposed;

    /// <summary>
    /// 検索・ソート適用後の表示用トラックコレクション
    /// </summary>
    public ObservableCollection<Track> FilteredTracks { get; } = new();

    /// <summary>
    /// ライブラリ内の全トラックコレクション（FilteredTracksの互換エイリアス）
    /// </summary>
    public ObservableCollection<Track> AllTracks => FilteredTracks;

    /// <summary>
    /// ソート順の選択肢一覧
    /// </summary>
    public IReadOnlyList<string> SortOptions { get; } = ["Title", "Artist", "Album", "Duration", "Genre"];

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
                RefreshTracks();
            }
        }
    }

    /// <summary>
    /// 選択中のソート基準（Title / Artist / Album / Duration / Genre）
    /// </summary>
    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value ?? "Title"))
            {
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
        set => SetProperty(ref _selectedTrack, value);
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
    /// AllSongsViewModelを初期化します
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    public AllSongsViewModel(LibraryViewModel libraryViewModel, IAudioService audioService)
    {
        _libraryViewModel = libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

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

        _libraryViewModel.Tracks.CollectionChanged += OnLibraryCollectionChanged;
        _libraryViewModel.Albums.CollectionChanged += OnLibraryCollectionChanged;

        RefreshTracks();
    }

    /// <summary>
    /// ライブラリの全楽曲から検索・ソート条件を適用して表示リストを再構築します
    /// </summary>
    public void RefreshTracks()
    {
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
            FilteredTracks.Clear();
            foreach (var track in sorted)
            {
                FilteredTracks.Add(track);
            }

            UpdateSummary(sorted);
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AllTracks));
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
        PlaybackRequested?.Invoke(list, firstTrack, "すべての曲", $"{list.Count} 曲");
        _audioService.SetPlaylist(list, firstTrack);
        _audioService.PlayTrack(firstTrack);
    }

    /// <summary>
    /// 表示中の全曲をシャッフルして即時再生します
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
        PlaybackRequested?.Invoke(list, firstTrack, "すべての曲（シャッフル）", $"{list.Count} 曲");
        _audioService.SetPlaylist(list, firstTrack);
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

        PlaybackRequested?.Invoke(list, track, "すべての曲", $"{list.Count} 曲");
        _audioService.SetPlaylist(list, track);
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
                        .ThenBy(t => t.TrackNumber),
            "Album" => isAscending
                ? tracks.OrderBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.TrackNumber)
                        .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderByDescending(t => t.Album, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.TrackNumber),
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

    private void OnLibraryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshTracks();
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
            _libraryViewModel.Tracks.CollectionChanged -= OnLibraryCollectionChanged;
            _libraryViewModel.Albums.CollectionChanged -= OnLibraryCollectionChanged;
            _disposed = true;
        }
    }
}
