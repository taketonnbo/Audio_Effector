using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// サイドメニュー「最近再生した曲」ビューを担当するViewModel。
/// 再生履歴の自動記録・JSON永続化・検索フィルタリング・再再生・履歴削除を提供します。
/// </summary>
public sealed class RecentViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 保持する最大履歴件数
    /// </summary>
    public const int MaxHistoryCount = 100;

    private readonly IRecentTrackRepository? _repository;
    private readonly IListeningStatsRepository? _statsRepository;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioService _audioService;
    private readonly Func<DateTime> _clock;
    private readonly List<RecentTrackEntry> _allEntries = new();

    private string _searchKeyword = string.Empty;
    private RecentTrackEntry? _selectedEntry;
    private string _summaryText = "最近再生した 0 曲";
    private int _isLibrarySyncPending;
    private bool _disposed;

    /// <summary>
    /// 検索フィルタ適用後の表示用履歴エントリコレクション（新しい順）
    /// </summary>
    public ObservableCollection<RecentTrackEntry> FilteredEntries { get; } = new();

    /// <summary>
    /// 最近再生したトラック一覧（互換用）
    /// </summary>
    public ObservableCollection<Track> RecentTracks { get; } = new();

    /// <summary>
    /// 履歴内検索キーワード（曲名・アーティスト・アルバム）
    /// </summary>
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// 現在選択中の履歴エントリ
    /// </summary>
    public RecentTrackEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    /// <summary>
    /// 履歴件数のサマリーテキスト
    /// </summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>
    /// 表示中の履歴が空かどうか
    /// </summary>
    public bool IsEmpty => FilteredEntries.Count == 0;

    /// <summary>
    /// 履歴が1件以上存在するかどうか
    /// </summary>
    public bool HasHistory => _allEntries.Count > 0;

    /// <summary>
    /// プレイリスト専用ViewModelへの参照（コンテキストメニュー用）
    /// </summary>
    public PlaylistViewModel? Playlist => _libraryViewModel.Playlist;

    /// <summary>表示中の履歴を先頭から連続再生するコマンド</summary>
    public ICommand PlayFromHistoryCommand { get; }

    /// <summary>指定された履歴エントリの楽曲を即時再生するコマンド</summary>
    public ICommand PlayEntryCommand { get; }

    /// <summary>指定された履歴エントリの楽曲を次に再生するコマンド</summary>
    public ICommand PlayNextCommand { get; }

    /// <summary>指定された履歴エントリの楽曲をキュー末尾に追加するコマンド</summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>お気に入り登録状態を切り替えるコマンド</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>指定された履歴エントリを1件削除するコマンド</summary>
    public ICommand RemoveEntryCommand { get; }

    /// <summary>すべての再生履歴をクリアするコマンド</summary>
    public ICommand ClearHistoryCommand { get; }

    /// <summary>検索キーワードをクリアするコマンド</summary>
    public ICommand ClearSearchCommand { get; }

    /// <summary>
    /// 再生リスト更新および再生開始を要求するイベント（対象トラック群, 開始トラック, リスト名, サブタイトル）
    /// </summary>
    public event Action<IReadOnlyList<Track>, Track?, string, string>? PlaybackRequested;

    /// <summary>
    /// 再生キューへの追加を要求するイベント（対象トラック群, playNext: trueなら次に再生）
    /// </summary>
    public event Action<IReadOnlyList<Track>, bool>? EnqueueRequested;

    /// <summary>
    /// 統合リスニング統計リポジトリを用いて RecentViewModel を初期化します
    /// </summary>
    /// <param name="statsRepository">リスニング統計リポジトリ</param>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="clock">現在時刻プロバイダ（テスト用、省略時はDateTime.Now）</param>
    [ActivatorUtilitiesConstructor]
    public RecentViewModel(
        IListeningStatsRepository statsRepository,
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        Func<DateTime>? clock = null)
        : this(null, statsRepository ?? throw new ArgumentNullException(nameof(statsRepository)), libraryViewModel, audioService, clock)
    {
    }

    /// <summary>
    /// RecentViewModelを初期化します（旧 <see cref="IRecentTrackRepository"/> 互換コンストラクタ）
    /// </summary>
    /// <param name="repository">再生履歴リポジトリ</param>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="clock">現在時刻プロバイダ（テスト用、省略時はDateTime.Now）</param>
    internal RecentViewModel(
        IRecentTrackRepository repository,
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        Func<DateTime>? clock = null)
        : this(repository ?? throw new ArgumentNullException(nameof(repository)), null, libraryViewModel, audioService, clock)
    {
    }

    private RecentViewModel(
        IRecentTrackRepository? repository,
        IListeningStatsRepository? statsRepository,
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        Func<DateTime>? clock)
    {
        _repository = repository;
        _statsRepository = statsRepository;
        _libraryViewModel = libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _clock = clock ?? (() => DateTime.Now);

        PlayFromHistoryCommand = new RelayCommand(_ => PlayFromHistory(), _ => FilteredEntries.Count > 0);
        PlayEntryCommand = new RelayCommand(PlayEntry);
        PlayNextCommand = new RelayCommand(param => EnqueueEntry(param, playNext: true));
        EnqueueTrackCommand = new RelayCommand(param => EnqueueEntry(param, playNext: false));
        ToggleFavoriteCommand = new RelayCommand(param =>
        {
            var track = ExtractTrack(param);
            if (track != null)
            {
                _libraryViewModel.ToggleFavoriteCommand.Execute(track);
            }
        });
        RemoveEntryCommand = new RelayCommand(RemoveEntry);
        ClearHistoryCommand = new RelayCommand(_ => ClearHistory(), _ => _allEntries.Count > 0);
        ClearSearchCommand = new RelayCommand(_ => SearchKeyword = string.Empty);

        LoadHistory();

        _audioService.TrackChanged += OnAudioTrackChanged;
        _libraryViewModel.Tracks.CollectionChanged += OnLibraryTracksChanged;
        _libraryViewModel.LibrarySynced += OnLibrarySynced;
    }

    /// <summary>
    /// リポジトリから再生履歴を読み込み、表示リストを初期化します
    /// </summary>
    public void LoadHistory()
    {
        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_statsRepository != null)
                {
                    DisplayHistory(ExtractRecentEntries(_statsRepository.Load()));
                }
                else if (_repository != null)
                {
                    DisplayHistory(_repository.Load());
                }
            }
            catch (Exception ex) when (IsHistoryUnavailable(ex))
            {
                System.Diagnostics.Trace.TraceWarning(ex.Message);
            }
        });
    }

    /// <summary>
    /// 再生された楽曲を履歴の先頭に追加・更新し、表示側の上限を維持しつつ永続化します
    /// </summary>
    /// <param name="track">再生されたトラック</param>
    public void RecordTrack(Track? track)
    {
        if (_disposed || track == null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return;
        }

        var playedAt = _clock();
        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            var libraryTrack = FindLibraryTrack(track.FilePath) ?? track;
            if (_statsRepository != null)
            {
                UpdateStatsHistory(
                    data =>
                    {
                        TrackListeningStat? existing = data.FindTrackStat(libraryTrack.FilePath);
                        if (existing == null)
                        {
                            existing = TrackListeningStat.FromTrack(libraryTrack);
                            data.TrackStats.Add(existing);
                        }

                        existing.RecordRecentPlayed(libraryTrack, playedAt);
                    },
                    entries =>
                    {
                        entries.RemoveAll(e => string.Equals(e.FilePath, libraryTrack.FilePath, StringComparison.OrdinalIgnoreCase));
                        entries.Insert(0, new RecentTrackEntry(libraryTrack, playedAt));
                        if (entries.Count > MaxHistoryCount)
                        {
                            entries.RemoveRange(MaxHistoryCount, entries.Count - MaxHistoryCount);
                        }
                    });
            }
            else
            {
                UpdateHistory(entries =>
                {
                    entries.RemoveAll(e => string.Equals(e.FilePath, libraryTrack.FilePath, StringComparison.OrdinalIgnoreCase));
                    entries.Insert(0, new RecentTrackEntry(libraryTrack, playedAt));

                    if (entries.Count > MaxHistoryCount)
                    {
                        entries.RemoveRange(MaxHistoryCount, entries.Count - MaxHistoryCount);
                    }
                });
            }
        });
    }

    /// <summary>
    /// 表示中の履歴リスト先頭から連続再生を開始します
    /// </summary>
    public void PlayFromHistory()
    {
        if (FilteredEntries.Count == 0)
        {
            return;
        }

        var tracks = FilteredEntries.Select(e => e.Track).ToList();
        var first = tracks[0];
        _audioService.SetPlaylist(tracks, first);
        PlaybackRequested?.Invoke(tracks, first, "最近再生した曲", $"{tracks.Count} 曲");
        _audioService.PlayTrack(first);
    }

    /// <summary>
    /// 指定された履歴エントリ（またはトラック）を即時再生します
    /// </summary>
    /// <param name="parameter">再生対象のRecentTrackEntryまたはTrack</param>
    public void PlayEntry(object? parameter)
    {
        var track = ExtractTrack(parameter);
        if (track == null)
        {
            return;
        }

        var tracks = FilteredEntries.Count > 0
            ? FilteredEntries.Select(e => e.Track).ToList()
            : new List<Track> { track };
        if (!tracks.Contains(track))
        {
            tracks.Insert(0, track);
        }

        _audioService.SetPlaylist(tracks, track);
        PlaybackRequested?.Invoke(tracks, track, "最近再生した曲", $"{tracks.Count} 曲");
        _audioService.PlayTrack(track);
    }

    /// <summary>
    /// 指定された履歴エントリを1件削除し永続化します
    /// </summary>
    /// <param name="parameter">削除対象のRecentTrackEntryまたはTrack</param>
    public void RemoveEntry(object? parameter)
    {
        string? filePath = parameter switch
        {
            RecentTrackEntry entry => entry.FilePath,
            Track track => track.FilePath,
            _ => null,
        };

        if (_disposed || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (_statsRepository != null)
            {
                UpdateStatsHistory(
                    data =>
                    {
                        TrackListeningStat? stat = data.FindTrackStat(filePath);
                        if (stat != null)
                        {
                            stat.RecentPlayedAt = null;
                        }
                    },
                    entries => entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                UpdateHistory(entries => entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));
            }
        });
    }

    /// <summary>
    /// すべての再生履歴を削除し永続化します
    /// </summary>
    public void ClearHistory()
    {
        if (_disposed)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (_statsRepository != null)
            {
                UpdateStatsHistory(
                    data =>
                    {
                        foreach (TrackListeningStat stat in data.TrackStats)
                        {
                            stat.RecentPlayedAt = null;
                        }
                    },
                    entries => entries.Clear());
            }
            else
            {
                UpdateHistory(entries => entries.Clear());
            }
        });
    }

    private static bool IsHistoryUnavailable(Exception ex) =>
        ex is System.IO.IOException or UnauthorizedAccessException or TimeoutException or System.Text.Json.JsonException or OverflowException;

    private void UpdateStatsHistory(Action<ListeningStatsData> updateStats, Action<List<RecentTrackEntry>> fallbackUpdate)
    {
        try
        {
            ListeningStatsData updated = _statsRepository!.Update(updateStats);
            DisplayHistory(ExtractRecentEntries(updated));
        }
        catch (Exception ex) when (IsHistoryUnavailable(ex))
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
            fallbackUpdate(_allEntries);
            ApplyFilterCore();
        }
    }

    private void UpdateHistory(Action<List<RecentTrackEntry>> update)
    {
        try
        {
            DisplayHistory(_repository!.Update(update));
        }
        catch (Exception ex) when (IsHistoryUnavailable(ex))
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
            update(_allEntries);
            ApplyFilterCore();
        }
    }

    private static List<RecentTrackEntry> ExtractRecentEntries(ListeningStatsData data)
    {
        return data.TrackStats
            .Where(stat => stat.RecentPlayedAt.HasValue)
            .OrderByDescending(stat => stat.RecentPlayedAt!.Value)
            .Select(stat => stat.ToRecentTrackEntry()!)
            .ToList();
    }

    private void DisplayHistory(IReadOnlyList<RecentTrackEntry> loaded)
    {
        var libraryTrackMap = BuildLibraryTrackMap();
        _allEntries.Clear();
        foreach (var entry in loaded.Take(MaxHistoryCount))
        {
            _allEntries.Add(ResolveLibraryEntry(entry, libraryTrackMap));
        }

        ApplyFilterCore();
    }

    private void EnqueueEntry(object? parameter, bool playNext)
    {
        var track = ExtractTrack(parameter);
        if (track == null)
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

    private void ApplyFilter()
    {
        RunOnUiThread(ApplyFilterCore);
    }

    private void ApplyFilterCore()
    {
        if (_disposed)
        {
            return;
        }

        IEnumerable<RecentTrackEntry> query = _allEntries;

        if (!string.IsNullOrWhiteSpace(_searchKeyword))
        {
            string keyword = _searchKeyword.Trim();
            query = query.Where(e =>
                (!string.IsNullOrEmpty(e.Title) && e.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Artist) && e.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Album) && e.Album.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        var filtered = query.ToList();

        FilteredEntries.Clear();
        RecentTracks.Clear();
        RecentTrackEntry? nextSelected = null;
        foreach (var entry in filtered)
        {
            FilteredEntries.Add(entry);
            RecentTracks.Add(entry.Track);

            if (_selectedEntry != null)
            {
                if (ReferenceEquals(entry, _selectedEntry) ||
                    (nextSelected == null && string.Equals(entry.FilePath, _selectedEntry.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    nextSelected = entry;
                }
            }
        }

        if (!ReferenceEquals(_selectedEntry, nextSelected))
        {
            SelectedEntry = nextSelected;
        }

        SummaryText = string.Format(CultureInfo.InvariantCulture, "最近再生した {0:N0} 曲", filtered.Count);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasHistory));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnAudioTrackChanged(Track? track)
    {
        if (track != null)
        {
            RecordTrack(track);
        }
    }

    private void OnLibraryTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleLibrarySync();
    }

    private void OnLibrarySynced(LibrarySyncResult result)
    {
        if (result.UpdatedCount > 0)
        {
            ApplyFilter();
        }
    }

    private void ScheduleLibrarySync()
    {
        if (_disposed || _allEntries.Count == 0)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            SyncAllEntriesWithLibrary();
            return;
        }

        // 同一フレーム内の連続的な追加・削除を1回の同期へコアレッシングする（O(N×M) 防止）
        if (Interlocked.Exchange(ref _isLibrarySyncPending, 1) == 0)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Interlocked.Exchange(ref _isLibrarySyncPending, 0);
                if (!_disposed)
                {
                    SyncAllEntriesWithLibrary();
                }
            }));
        }
    }

    private void SyncAllEntriesWithLibrary()
    {
        if (_disposed || _allEntries.Count == 0)
        {
            return;
        }

        var libraryTrackMap = BuildLibraryTrackMap();
        if (libraryTrackMap.Count == 0)
        {
            return;
        }

        bool updated = false;
        for (int i = 0; i < _allEntries.Count; i++)
        {
            var current = _allEntries[i];
            var resolved = ResolveLibraryEntry(current, libraryTrackMap);
            if (!ReferenceEquals(current, resolved))
            {
                _allEntries[i] = resolved;
                updated = true;
            }
        }

        if (updated)
        {
            ApplyFilterCore();
        }
    }

    private Dictionary<string, Track> BuildLibraryTrackMap()
    {
        var map = new Dictionary<string, Track>(_libraryViewModel.Tracks.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var track in _libraryViewModel.Tracks)
        {
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                map.TryAdd(track.FilePath, track);
            }
        }

        return map;
    }

    private static RecentTrackEntry ResolveLibraryEntry(RecentTrackEntry entry, Dictionary<string, Track> libraryTrackMap)
    {
        if (libraryTrackMap.TryGetValue(entry.FilePath, out var libraryTrack) &&
            !ReferenceEquals(entry.Track, libraryTrack))
        {
            return entry.WithTrack(libraryTrack);
        }

        return entry;
    }

    private Track? FindLibraryTrack(string filePath)
    {
        return _libraryViewModel.Tracks.FirstOrDefault(
            t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    private static Track? ExtractTrack(object? parameter)
    {
        return parameter switch
        {
            RecentTrackEntry entry => entry.Track,
            Track track => track,
            _ => null,
        };
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
            _audioService.TrackChanged -= OnAudioTrackChanged;
            _libraryViewModel.Tracks.CollectionChanged -= OnLibraryTracksChanged;
            _libraryViewModel.LibrarySynced -= OnLibrarySynced;
        }
    }
}
