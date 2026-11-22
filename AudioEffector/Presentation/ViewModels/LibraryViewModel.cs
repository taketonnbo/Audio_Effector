using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Infrastructure.Library;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 楽曲ライブラリの楽曲一覧、アルバム一覧、フォルダスキャン、ソート、お気に入り切り替えを担当するViewModel
/// </summary>
public sealed class LibraryViewModel : ViewModelBase, IDisposable
{
    private static readonly string[] SupportedAudioExtensions = [".mp3", ".wav", ".aiff", ".wma", ".m4a", ".mp4", ".flac", ".aac", ".ogg", ".opus", ".alac"];
    private static readonly string[] LosslessAudioExtensions = [".flac", ".wav", ".aiff", ".alac"];

    private readonly LibraryApplicationService _libraryService;
    private readonly IAudioService _audioService;
    private readonly ISettingsService _settingsService;
    private readonly Func<string, Track> _trackMetadataLoader;
    private readonly object _favoritesLock = new();
    private readonly object _favoritesSaveLock = new();
    private readonly List<FavoriteTrackEntry> _favoriteEntries = new();
    private readonly Dictionary<string, DateTime> _trackLastWriteTimes = new(StringComparer.OrdinalIgnoreCase);
    private long _favoritesVersion;
    private long _lastSavedFavoritesVersion;

    private Track? _selectedTrack;
    private Album? _selectedAlbum;
    private Album? _expandedAlbum;
    private string _searchKeyword = string.Empty;
    private string? _currentLibraryPath;
    private LibrarySyncResult? _lastSyncResult;
    private bool _isLoading;
    private double _scanProgress;
    private bool _isGridView = true;
    private bool _isAscending = true;
    private string _selectedSortOption = "Artist";
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// 全楽曲コレクション
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = new();

    /// <summary>
    /// アルバムコレクション
    /// </summary>
    public ObservableCollection<Album> Albums { get; } = new();

    /// <summary>
    /// プレイリスト専用ViewModelへの参照（ダイアログ呼び出し等用）
    /// </summary>
    public PlaylistViewModel? Playlist { get; }

    /// <summary>
    /// 選択中のトラック
    /// </summary>
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set => SetProperty(ref _selectedTrack, value);
    }

    /// <summary>
    /// 選択中のアルバム
    /// </summary>
    public Album? SelectedAlbum
    {
        get => _selectedAlbum;
        set => SetProperty(ref _selectedAlbum, value);
    }

    /// <summary>
    /// 現在収録曲トレイを展開しているアルバム
    /// </summary>
    public Album? ExpandedAlbum
    {
        get => _expandedAlbum;
        set => SetProperty(ref _expandedAlbum, value);
    }

    /// <summary>
    /// 検索キーワード
    /// </summary>
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                _ = SearchAsync(value);
            }
        }
    }

    /// <summary>
    /// ライブラリ読み込み・スキャン中かどうか
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsScanning));
            }
        }
    }

    /// <summary>
    /// 互換用：フォルダスキャン中かどうか
    /// </summary>
    public bool IsScanning
    {
        get => _isLoading;
        set => IsLoading = value;
    }

    /// <summary>
    /// スキャン進捗率（0.0〜1.0）
    /// </summary>
    public double ScanProgress
    {
        get => _scanProgress;
        set => SetProperty(ref _scanProgress, value);
    }

    /// <summary>
    /// 現在スキャン対象となっているライブラリフォルダパス
    /// </summary>
    public string? CurrentLibraryPath
    {
        get => _currentLibraryPath;
        private set => SetProperty(ref _currentLibraryPath, value);
    }

    /// <summary>
    /// 直近のライブラリスキャン・差分同期結果
    /// </summary>
    public LibrarySyncResult? LastSyncResult
    {
        get => _lastSyncResult;
        private set => SetProperty(ref _lastSyncResult, value);
    }

    /// <summary>
    /// グリッド表示モードかどうか
    /// </summary>
    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            if (SetProperty(ref _isGridView, value))
            {
                OnPropertyChanged(nameof(IsListView));
                CloseExpandedAlbum();
            }
        }
    }

    /// <summary>
    /// リスト表示モードかどうか
    /// </summary>
    public bool IsListView => !IsGridView;

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
                SortLibrary();
            }
        }
    }

    /// <summary>
    /// ソート順選択肢一覧
    /// </summary>
    public List<string> SortOptions { get; } = new() { "Artist", "Album" };

    /// <summary>
    /// 選択中のソート順（Artist / Album）
    /// </summary>
    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                SortLibrary();
            }
        }
    }

    // コマンド定義
    /// <summary>フォルダスキャンコマンド</summary>
    public ICommand ScanFolderCommand { get; }

    /// <summary>グリッド/リスト表示切り替えコマンド</summary>
    public ICommand ToggleViewCommand { get; }

    /// <summary>ソート昇順/降順切り替えコマンド</summary>
    public ICommand ToggleSortDirectionCommand { get; }

    /// <summary>アルバム全体再生コマンド</summary>
    public ICommand PlayAlbumCommand { get; }

    /// <summary>アルバム収録曲トレイ展開切り替えコマンド（排他制御）</summary>
    public ICommand ToggleAlbumTracksCommand { get; }

    /// <summary>アルバムを次に再生するコマンド</summary>
    public ICommand PlayNextAlbumCommand { get; }

    /// <summary>アルバムをキュー末尾に追加するコマンド</summary>
    public ICommand EnqueueAlbumCommand { get; }

    /// <summary>アルバム削除コマンド</summary>
    public ICommand DeleteAlbumCommand { get; }

    /// <summary>トラック再生コマンド</summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>トラックを次に再生するコマンド</summary>
    public ICommand PlayNextCommand { get; }

    /// <summary>トラックをキューに追加するコマンド</summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>お気に入り切り替えコマンド</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>トラック削除コマンド</summary>
    public ICommand DeleteTrackCommand { get; }

    /// <summary>アルバム詳細情報表示コマンド（#168）</summary>
    public ICommand ShowAlbumInfoCommand { get; }

    /// <summary>トラックプロパティ表示コマンド</summary>
    public ICommand ShowTrackPropertiesCommand { get; }

    /// <summary>ファイルの場所を開くコマンド</summary>
    public ICommand OpenFileLocationCommand { get; }

    // イベント定義
    /// <summary>再生要求イベント（対象トラック群, 開始トラック, リスト名, サブタイトル）</summary>
    public event Action<IReadOnlyList<Track>, Track?, string, string>? PlaybackRequested;

    /// <summary>キュー追加要求イベント（対象トラック群, playNext: trueなら次に再生、falseなら末尾追加）</summary>
    public event Action<IReadOnlyList<Track>, bool>? EnqueueRequested;

    /// <summary>お気に入り変更イベント</summary>
    public event Action<Track>? FavoriteToggled;

    /// <summary>トラック削除通知イベント</summary>
    public event Action<Track>? TrackRemoved;

    /// <summary>アルバム削除通知イベント</summary>
    public event Action<Album>? AlbumRemoved;

    /// <summary>ライブラリスキャン・差分同期完了通知イベント</summary>
    public event Action<LibrarySyncResult>? LibrarySynced;

    /// <summary>
    /// LibraryViewModelを初期化します
    /// </summary>
    /// <param name="libraryService">ライブラリアプリケーションサービス</param>
    /// <param name="audioService">再生制御サービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="playlistViewModel">プレイリスト専用ViewModel</param>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public LibraryViewModel(
        LibraryApplicationService libraryService,
        IAudioService audioService,
        ISettingsService settingsService,
        PlaylistViewModel? playlistViewModel = null)
        : this(libraryService, audioService, settingsService, playlistViewModel, trackMetadataLoader: null)
    {
    }

    /// <summary>
    /// カスタムメタデータローダーを指定してLibraryViewModelを初期化します（主に単体テスト用）
    /// </summary>
    /// <param name="libraryService">ライブラリアプリケーションサービス</param>
    /// <param name="audioService">再生制御サービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="playlistViewModel">プレイリスト専用ViewModel</param>
    /// <param name="trackMetadataLoader">ファイルパスからTrackを生成するメタデータローダー（省略時はTagLibを使用）</param>
    public LibraryViewModel(
        LibraryApplicationService libraryService,
        IAudioService audioService,
        ISettingsService settingsService,
        PlaylistViewModel? playlistViewModel,
        Func<string, Track>? trackMetadataLoader)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _trackMetadataLoader = trackMetadataLoader ?? ExtractTrackFromFile;
        Playlist = playlistViewModel;
        _currentLibraryPath = _settingsService.LoadSettings()?.LastLibraryPath;

        // お気に入りの読み込み
        var loadedFavorites = _libraryService.LoadFavoriteEntries();
        lock (_favoritesLock)
        {
            _favoriteEntries.AddRange(loadedFavorites);
        }

        ScanFolderCommand = new RelayCommand(folder =>
        {
            if (folder is string folderPath && !string.IsNullOrWhiteSpace(folderPath))
            {
                LoadLibrary(folderPath);
            }
        });

        ToggleViewCommand = new RelayCommand(_ => IsGridView = !IsGridView);
        ToggleSortDirectionCommand = new RelayCommand(_ => IsAscending = !IsAscending);

        PlayAlbumCommand = new RelayCommand(PlayAlbum);
        ToggleAlbumTracksCommand = new RelayCommand(param =>
        {
            if (param is Album album)
            {
                ToggleAlbumTracks(album);
            }
        });
        PlayNextAlbumCommand = new RelayCommand(PlayNextAlbum);
        EnqueueAlbumCommand = new RelayCommand(EnqueueAlbum);
        DeleteAlbumCommand = new RelayCommand(DeleteAlbum);

        PlayTrackCommand = new RelayCommand(PlayTrack);
        PlayNextCommand = new RelayCommand(PlayNextTrack);
        EnqueueTrackCommand = new RelayCommand(EnqueueTrack);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        DeleteTrackCommand = new RelayCommand(DeleteTrack);

        ShowTrackPropertiesCommand = new RelayCommand(ShowTrackProperties);
        OpenFileLocationCommand = new RelayCommand(OpenFileLocation);
        ShowAlbumInfoCommand = new RelayCommand(param =>
        {
            if (param is Album album)
            {
                ShowAlbumInfo(album);
            }
        });
    }

    /// <summary>
    /// 指定されたフォルダー（または保存されたパス）からライブラリをロード・差分同期します。
    /// </summary>
    /// <param name="rootFolder">スキャン対象フォルダーパス（省略時は現在のパスまたは設定から読み込み）</param>
    public async void LoadLibrary(string? rootFolder = null)
    {
        try
        {
            await LoadLibraryAsync(rootFolder);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は正常終了
        }
    }

    /// <summary>
    /// 現在設定されているライブラリフォルダを再スキャンし、追加・削除・更新された楽曲およびアルバムを差分同期します。
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>差分同期の実行結果</returns>
    public Task<LibrarySyncResult> RescanCurrentFolderAsync(CancellationToken cancellationToken = default)
    {
        string? targetFolder = !string.IsNullOrWhiteSpace(CurrentLibraryPath)
            ? CurrentLibraryPath
            : _settingsService.LoadSettings()?.LastLibraryPath;
        return LoadLibraryAsync(targetFolder, cancellationToken);
    }

    /// <summary>
    /// 指定されたフォルダー（または保存されたパス）からライブラリを非同期でスキャンし、既存コレクションと差分同期します。
    /// </summary>
    /// <param name="rootFolder">スキャン対象フォルダーパス（省略時は現在のパスまたは設定から読み込み）</param>
    /// <param name="cancellationToken">外部キャンセレーショントークン</param>
    /// <returns>差分同期の実行結果</returns>
    public async Task<LibrarySyncResult> LoadLibraryAsync(string? rootFolder = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            var settings = _settingsService.LoadSettings();
            rootFolder = !string.IsNullOrWhiteSpace( settings?.LastLibraryPath)
                ? settings.LastLibraryPath
                : CurrentLibraryPath;
        }

        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return new LibrarySyncResult(0, 0, 0, Tracks.Count, Albums.Count);
        }

        var previousCts = _loadCts;
        previousCts?.Cancel();
        previousCts?.Dispose();

        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts = currentCts;
        var token = currentCts.Token;

        CurrentLibraryPath = rootFolder;
        ScanProgress = 0.0;
        IsLoading = true;

        bool detectModified = _settingsService.LoadSettings()?.DetectMetadataChangesOnRescan ?? false;

        Dictionary<string, Track> existingTracksByPath = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DateTime> previousWriteTimes = new(StringComparer.OrdinalIgnoreCase);
        RunOnUiThread(() =>
        {
            foreach (var track in Tracks)
            {
                if (!string.IsNullOrWhiteSpace(track.FilePath))
                {
                    existingTracksByPath.TryAdd(track.FilePath, track);
                }
            }

            foreach (var kvp in _trackLastWriteTimes)
            {
                previousWriteTimes[kvp.Key] = kvp.Value;
            }
        });

        LibrarySyncResult syncResult = new(0, 0, 0, Tracks.Count, Albums.Count);

        try
        {
            await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return;

                List<string> files;
                try
                {
                    files = Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                        .Where(f => SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLower(System.Globalization.CultureInfo.InvariantCulture)))
                        .ToList();
                }
                catch
                {
                    files = new List<string>();
                }

                if (token.IsCancellationRequested) return;

                var diskFileSet = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                var removedTracks = existingTracksByPath.Values
                    .Where(t => !diskFileSet.Contains(t.FilePath))
                    .ToList();

                var addedPaths = new List<string>();
                var modifiedPaths = new List<string>();
                var currentWriteTimes = new Dictionary<string, DateTime>(files.Count, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    DateTime lastWriteUtc = SafeGetLastWriteTimeUtc(file);
                    currentWriteTimes[file] = lastWriteUtc;

                    if (!existingTracksByPath.ContainsKey(file))
                    {
                        addedPaths.Add(file);
                    }
                    else if (detectModified &&
                             (!previousWriteTimes.TryGetValue(file, out DateTime prevUtc) || prevUtc != lastWriteUtc))
                    {
                        modifiedPaths.Add(file);
                    }
                }

                if (token.IsCancellationRequested) return;

                var pathsToParse = new List<string>(addedPaths.Count + modifiedPaths.Count);
                pathsToParse.AddRange(addedPaths);
                pathsToParse.AddRange(modifiedPaths);

                var parsedTracksByPath = new System.Collections.Concurrent.ConcurrentDictionary<string, Track>(StringComparer.OrdinalIgnoreCase);

                if (pathsToParse.Count > 0)
                {
                    int totalToParse = pathsToParse.Count;
                    int processedCount = 0;
                    long lastReportTick = Environment.TickCount64;

                    Parallel.ForEach(pathsToParse, (file, loopState) =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            loopState.Stop();
                            return;
                        }

                        var parsed = _trackMetadataLoader(file);
                        parsedTracksByPath[file] = parsed;

                        int current = Interlocked.Increment(ref processedCount);
                        long nowTick = Environment.TickCount64;
                        long prevTick = Interlocked.Read(ref lastReportTick);
                        if (current == totalToParse || nowTick - prevTick >= 40)
                        {
                            Interlocked.Exchange(ref lastReportTick, nowTick);
                            double progressValue = (double)current / totalToParse;
                            RunOnUiThread(() =>
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    ScanProgress = progressValue;
                                }
                            });
                        }
                    });
                }
                else
                {
                    RunOnUiThread(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ScanProgress = 1.0;
                        }
                    });
                }

                if (token.IsCancellationRequested) return;

                var addedTracks = new List<Track>(addedPaths.Count);
                foreach (string path in addedPaths)
                {
                    if (parsedTracksByPath.TryGetValue(path, out var addedTrack))
                    {
                        addedTracks.Add(addedTrack);
                    }
                }

                long? favoriteSaveVersion = null;

                RunOnUiThread(() =>
                {
                    if (token.IsCancellationRequested) return;

                    // 1. 削除されたトラックを除去
                    foreach (var removed in removedTracks)
                    {
                        Tracks.Remove(removed);
                        if (ReferenceEquals(SelectedTrack, removed))
                        {
                            SelectedTrack = null;
                        }
                        TrackRemoved?.Invoke(removed);
                    }

                    // 2. 更新された既存トラックのメタデータをインプレース反映（インスタンス参照を維持）
                    int updatedCount = 0;
                    foreach (string modPath in modifiedPaths)
                    {
                        if (existingTracksByPath.TryGetValue(modPath, out var existingTrack) &&
                            parsedTracksByPath.TryGetValue(modPath, out var parsedTrack))
                        {
                            CopyTrackMetadata(parsedTrack, existingTrack);
                            updatedCount++;
                        }
                    }

                    // 3. 新規追加されたトラックを登録
                    foreach (var added in addedTracks)
                    {
                        Tracks.Add(added);
                    }

                    // 4. ファイル更新日時キャッシュを同期（実際に解析した時点のタイムスタンプのみを更新し、スキップ時は前回解析日時を維持）
                    foreach (var removed in removedTracks)
                    {
                        _trackLastWriteTimes.Remove(removed.FilePath);
                    }
                    foreach (string addedPath in addedPaths)
                    {
                        if (currentWriteTimes.TryGetValue(addedPath, out DateTime writeUtc))
                        {
                            _trackLastWriteTimes[addedPath] = writeUtc;
                        }
                    }
                    foreach (string modPath in modifiedPaths)
                    {
                        if (currentWriteTimes.TryGetValue(modPath, out DateTime writeUtc))
                        {
                            _trackLastWriteTimes[modPath] = writeUtc;
                        }
                    }

                    // 5. アルバムコレクションを差分同期（既存Albumインスタンスを維持しつつ追加・削除・空アルバム除去）
                    bool hasTrackDiff = removedTracks.Count > 0 || addedTracks.Count > 0 || updatedCount > 0 || (Tracks.Count > 0 && Albums.Count == 0);
                    if (hasTrackDiff)
                    {
                        SyncAlbumsFromTracks();
                    }

                    // 6. お気に入り状態の再照合（フォルダ移動・パス変更時もメタデータ一致でパスを自動更新）
                    favoriteSaveVersion = ReconcileFavoritesInMemory(Tracks.ToList(), token);

                    ScanProgress = 1.0;
                    syncResult = new LibrarySyncResult(
                        AddedCount: addedTracks.Count,
                        RemovedCount: removedTracks.Count,
                        UpdatedCount: updatedCount,
                        TotalTrackCount: Tracks.Count,
                        TotalAlbumCount: Albums.Count);
                    LastSyncResult = syncResult;
                    LibrarySynced?.Invoke(syncResult);
                });

                if (favoriteSaveVersion.HasValue && !token.IsCancellationRequested)
                {
                    PersistFavorites(favoriteSaveVersion.Value, token);
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は正常終了
        }
        finally
        {
            if (ReferenceEquals(_loadCts, currentCts))
            {
                IsLoading = false;
            }
            currentCts.Dispose();
            if (ReferenceEquals(_loadCts, currentCts))
            {
                _loadCts = null;
            }
        }

        return syncResult;
    }

    private static Track ExtractTrackFromFile(string file)
    {
        var track = new Track
        {
            FilePath = file,
            Title = Path.GetFileNameWithoutExtension(file),
            Artist = "Unknown Artist",
            Album = "Unknown Album"
        };

        try
        {
            using var tfile = TagLib.File.Create(file);
            var (title, artist, album, genre) = TagLibMetadataExtractor.ExtractDisplayTags(tfile.Tag, file);
            track.Title = title;
            track.Artist = artist;
            track.Album = album;
            track.Duration = tfile.Properties.Duration;
            track.Year = tfile.Tag.Year;
            track.TrackNumber = tfile.Tag.Track;
            track.Genre = genre;

            track.Bitrate = tfile.Properties.AudioBitrate;
            track.SampleRate = tfile.Properties.AudioSampleRate;
            track.BitsPerSample = tfile.Properties.BitsPerSample;
            string ext = Path.GetExtension(file).ToLower(System.Globalization.CultureInfo.InvariantCulture);
            track.Format = ext.TrimStart('.').ToUpper(System.Globalization.CultureInfo.InvariantCulture);

            track.IsLossless = LosslessAudioExtensions.Contains(ext);
            track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;
        }
        catch
        {
            // タグ読み込み失敗時はファイル名ベースの既定値を維持
        }

        return track;
    }

    private static DateTime SafeGetLastWriteTimeUtc(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void CopyTrackMetadata(Track source, Track destination)
    {
        destination.Title = source.Title;
        destination.Artist = source.Artist;
        destination.Album = source.Album;
        destination.Duration = source.Duration;
        destination.Year = source.Year;
        destination.TrackNumber = source.TrackNumber;
        destination.Genre = source.Genre;
        destination.Bitrate = source.Bitrate;
        destination.SampleRate = source.SampleRate;
        destination.BitsPerSample = source.BitsPerSample;
        destination.Format = source.Format;
        destination.IsLossless = source.IsLossless;
        destination.IsHiRes = source.IsHiRes;
    }

    private void SyncAlbumsFromTracks()
    {
        var grouped = Tracks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Album) ? "Unknown Album" : t.Album, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingAlbumsByTitle = new Dictionary<string, Album>(Albums.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var album in Albums)
        {
            existingAlbumsByTitle.TryAdd(album.Title, album);
        }

        var activeAlbumTitles = new HashSet<string>(grouped.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

        // 収録曲がゼロになったアルバムを除去
        var albumsToRemove = Albums.Where(a => !activeAlbumTitles.Contains(a.Title)).ToList();
        foreach (var emptyAlbum in albumsToRemove)
        {
            if (ReferenceEquals(ExpandedAlbum, emptyAlbum))
            {
                CloseExpandedAlbum();
            }
            if (ReferenceEquals(SelectedAlbum, emptyAlbum))
            {
                SelectedAlbum = null;
            }
            Albums.Remove(emptyAlbum);
            AlbumRemoved?.Invoke(emptyAlbum);
        }

        // 既存アルバムの更新および新規アルバムの追加
        foreach (var group in grouped)
        {
            var orderedTracks = group
                .OrderBy(t => t.TrackNumber)
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            uint albumYear = orderedTracks
                .Select(t => t.Year)
                .Where(y => y > 0)
                .GroupBy(y => y)
                .OrderByDescending(z => z.Count())
                .FirstOrDefault()?.Key ?? 0;
            string representativeArtist = orderedTracks[0].Artist;

            if (existingAlbumsByTitle.TryGetValue(group.Key, out var existingAlbum))
            {
                existingAlbum.Title = group.Key;
                existingAlbum.Artist = representativeArtist;
                existingAlbum.Year = albumYear;
                if (!AreTrackListsIdentical(existingAlbum.Tracks, orderedTracks))
                {
                    existingAlbum.Tracks = orderedTracks;
                }
            }
            else
            {
                Albums.Add(new Album
                {
                    Title = group.Key,
                    Artist = representativeArtist,
                    CoverImage = null,
                    Tracks = orderedTracks,
                    Year = albumYear
                });
            }
        }

        SortAlbumsIfOrderChanged();
    }

    private static bool AreTrackListsIdentical(List<Track> current, List<Track> target)
    {
        if (current.Count != target.Count)
        {
            return false;
        }

        for (int i = 0; i < current.Count; i++)
        {
            if (!ReferenceEquals(current[i], target[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void SortAlbumsIfOrderChanged()
    {
        if (Albums.Count <= 1)
        {
            return;
        }

        var sorted = GetSortedAlbumList(Albums);
        bool orderChanged = false;
        for (int i = 0; i < Albums.Count; i++)
        {
            if (!ReferenceEquals(Albums[i], sorted[i]))
            {
                orderChanged = true;
                break;
            }
        }

        if (!orderChanged)
        {
            return;
        }

        Albums.Clear();
        foreach (var album in sorted)
        {
            Albums.Add(album);
        }
    }

    private List<Album> GetSortedAlbumList(IEnumerable<Album> source)
    {
        return SelectedSortOption switch
        {
            "Artist" => IsAscending ? source.OrderBy(a => a.Artist).ToList() : source.OrderByDescending(a => a.Artist).ToList(),
            "Album" => IsAscending ? source.OrderBy(a => a.Title).ToList() : source.OrderByDescending(a => a.Title).ToList(),
            _ => source.ToList()
        };
    }

    /// <summary>
    /// ライブラリをソート条件と昇順/降順に従って並び替えます。
    /// </summary>
    public void SortLibrary()
    {
        CloseExpandedAlbum();
        if (!Albums.Any()) return;

        var sorted = Albums.ToList();
        switch (SelectedSortOption)
        {
            case "Artist":
                sorted = IsAscending ? sorted.OrderBy(a => a.Artist).ToList() : sorted.OrderByDescending(a => a.Artist).ToList();
                break;
            case "Album":
                sorted = IsAscending ? sorted.OrderBy(a => a.Title).ToList() : sorted.OrderByDescending(a => a.Title).ToList();
                break;
        }

        Albums.Clear();
        foreach (var album in sorted)
        {
            Albums.Add(album);
        }
    }

    /// <summary>
    /// アルバム全体を再生します
    /// </summary>
    /// <param name="album">再生対象のアルバム</param>
    public void PlayAlbum(Album album)
    {
        if (album == null || album.Tracks.Count == 0) return;

        var tracks = album.Tracks.ToList();
        Track startTrack;
        if (_audioService.IsShuffleEnabled)
        {
            var rng = new Random();
            startTrack = tracks[rng.Next(tracks.Count)];
        }
        else
        {
            startTrack = tracks.First();
        }

        startTrack.IsPlaying = true;
        PlaybackRequested?.Invoke(tracks, startTrack, album.Title, album.Artist);
        _audioService.SetPlaylist(tracks, startTrack);
        _audioService.PlayTrack(startTrack);
    }

    /// <summary>
    /// アルバム全体を再生します（コマンドパラメータ用）
    /// </summary>
    private void PlayAlbum(object? parameter)
    {
        if (parameter is Album album)
        {
            PlayAlbum(album);
        }
    }

    /// <summary>
    /// アルバムを次に再生するようキューに追加します
    /// </summary>
    private void PlayNextAlbum(object? parameter)
    {
        if (parameter is Album album && album.Tracks.Count > 0)
        {
            EnqueueRequested?.Invoke(album.Tracks, true);
        }
    }

    /// <summary>
    /// アルバムをキュー末尾に追加します
    /// </summary>
    private void EnqueueAlbum(object? parameter)
    {
        if (parameter is Album album && album.Tracks.Count > 0)
        {
            EnqueueRequested?.Invoke(album.Tracks, false);
        }
    }

    /// <summary>
    /// アルバムをライブラリから削除します
    /// </summary>
    /// <summary>
    /// アルバム収録曲トレイの展開・折りたたみを切り替えます（排他制御）。
    /// </summary>
    /// <param name="album">対象のアルバム</param>
    public void ToggleAlbumTracks(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);

        if (_expandedAlbum == album)
        {
            album.IsTracksExpanded = false;
            ExpandedAlbum = null;
        }
        else
        {
            if (_expandedAlbum != null)
            {
                _expandedAlbum.IsTracksExpanded = false;
            }
            album.IsTracksExpanded = true;
            ExpandedAlbum = album;
        }
    }

    /// <summary>
    /// 展開中のアルバム収録曲トレイを安全に閉じます。
    /// </summary>
    public void CloseExpandedAlbum()
    {
        if (_expandedAlbum != null)
        {
            _expandedAlbum.IsTracksExpanded = false;
            ExpandedAlbum = null;
        }
    }

    /// <summary>
    /// アルバムの詳細情報を表示します（#168: 後続Issueにてアルバム詳細画面遷移を実装）。
    /// </summary>
    /// <param name="album">対象アルバム</param>
    public void ShowAlbumInfo(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        // TODO: 後続Issueにてアルバム詳細画面への遷移・表示を実装
    }

    private void DeleteAlbum(object? parameter)
    {
        if (parameter is Album album)
        {
            var result = MessageBox.Show(
                $"Remove album '{album.Title}' from Library?\n(Files will NOT be deleted from disk)",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var tracksToRemove = album.Tracks.ToList();
                Albums.Remove(album);

                foreach (var track in tracksToRemove)
                {
                    Tracks.Remove(track);
                }

                RemoveFavoriteEntries(tracksToRemove);
                AlbumRemoved?.Invoke(album);
            }
        }
    }

    /// <summary>
    /// トラックを再生します
    /// </summary>
    private void PlayTrack(object? parameter)
    {
        if (parameter is Track track)
        {
            // 所属アルバムを探してキューをセット
            var album = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
            if (album != null)
            {
                int trackIndex = album.Tracks.IndexOf(track);
                var candidateTracks = trackIndex >= 0
                    ? album.Tracks.Skip(trackIndex).ToList()
                    : album.Tracks.ToList();

                PlaybackRequested?.Invoke(candidateTracks, track, album.Title, album.Artist);
                _audioService.SetPlaylist(candidateTracks, track);
            }
            else
            {
                PlaybackRequested?.Invoke([track], track, track.Album, track.Artist);
                _audioService.SetPlaylist([track], track);
            }
            _audioService.PlayTrack(track);
        }
    }

    /// <summary>
    /// トラックを次に再生するようキューに追加します
    /// </summary>
    private void PlayNextTrack(object? parameter)
    {
        if (parameter is Track track)
        {
            EnqueueRequested?.Invoke([track], true);
        }
    }

    /// <summary>
    /// トラックをキュー末尾に追加します
    /// </summary>
    private void EnqueueTrack(object? parameter)
    {
        if (parameter is Track track)
        {
            EnqueueRequested?.Invoke([track], false);
        }
    }

    /// <summary>
    /// トラックのお気に入り状態を切り替えます
    /// </summary>
    /// <param name="parameter">対象トラック（Track型）</param>
    public void ToggleFavorite(object? parameter)
    {
        if (parameter is Track track)
        {
            bool newFavoriteState = !track.IsFavorite;
            track.IsFavorite = newFavoriteState;

            long targetVersion;
            FavoriteTrackEntry referenceEntry = FavoriteTrackEntry.FromTrack(track);

            lock (_favoritesLock)
            {
                if (newFavoriteState)
                {
                    var existing = FavoriteTrackMatcher.FindBestMatchingEntry(track, _favoriteEntries);
                    if (existing != null)
                    {
                        int existingIndex = _favoriteEntries.IndexOf(existing);
                        _favoriteEntries[existingIndex] = existing.WithUpdatedTrack(track);
                    }
                    else
                    {
                        _favoriteEntries.Add(referenceEntry);
                    }
                }
                else
                {
                    _favoriteEntries.RemoveAll(entry => FavoriteTrackMatcher.IsMatchForRemoval(entry, track));
                }

                targetVersion = ++_favoritesVersion;
            }

            // ライブラリ内の同一楽曲インスタンスへお気に入り状態を同期
            foreach (var libraryTrack in Tracks)
            {
                if (!ReferenceEquals(libraryTrack, track) &&
                    FavoriteTrackMatcher.IsMatchForRemoval(referenceEntry, libraryTrack))
                {
                    libraryTrack.SetFavorite(newFavoriteState);
                }
            }

            PersistFavorites(targetVersion);
            FavoriteToggled?.Invoke(track);
        }
    }

    /// <summary>
    /// 現在のライブラリ内トラック一覧へお気に入り状態を照合・反映し、パスやメタデータの差分があれば永続化します
    /// </summary>
    public void SyncFavoritesWithLibraryTracks()
    {
        ReconcileFavoriteEntries(Tracks.ToList());
    }

    /// <summary>
    /// 指定されたトラックがお気に入り登録されているかどうかをメタデータおよびパスで判定します
    /// </summary>
    /// <param name="track">判定対象のトラック</param>
    /// <returns>お気に入り登録されている場合はtrue</returns>
    public bool IsTrackFavorite(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        List<FavoriteTrackEntry> snapshot;
        lock (_favoritesLock)
        {
            snapshot = _favoriteEntries.ToList();
        }

        return FavoriteTrackMatcher.FindBestMatchingEntry(track, snapshot) != null;
    }

    /// <summary>
    /// お気に入り一覧表示用のトラックリストをライブラリ内のメタデータ一致（およびフォールバック読込）で解決して取得します
    /// </summary>
    /// <param name="fallbackTrackLoader">ライブラリ内に存在しない楽曲をLastKnownFilePathから読み込むフォールバックローダー（任意）</param>
    /// <returns>お気に入り登録順に解決されたトラック一覧</returns>
    public IReadOnlyList<Track> GetFavoriteTracks(Func<string, Track?>? fallbackTrackLoader = null)
    {
        var result = ReconcileFavoriteEntries(Tracks.ToList(), fallbackTrackLoader);
        return result.MatchedTracks;
    }

    private FavoriteReconciliationResult ReconcileFavoriteEntries(
        IReadOnlyList<Track> tracks,
        Func<string, Track?>? fallbackTrackLoader = null,
        CancellationToken cancellationToken = default)
    {
        var (result, targetVersion) = ReconcileFavoritesCore(tracks, fallbackTrackLoader, cancellationToken);
        if (targetVersion.HasValue)
        {
            PersistFavorites(targetVersion.Value, cancellationToken);
        }

        return result;
    }

    private long? ReconcileFavoritesInMemory(
        IReadOnlyList<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        var (_, targetVersion) = ReconcileFavoritesCore(tracks, fallbackTrackLoader: null, cancellationToken);
        return targetVersion;
    }

    private (FavoriteReconciliationResult Result, long? TargetVersion) ReconcileFavoritesCore(
        IReadOnlyList<Track> tracks,
        Func<string, Track?>? fallbackTrackLoader = null,
        CancellationToken cancellationToken = default)
    {
        List<FavoriteTrackEntry> snapshot;
        lock (_favoritesLock)
        {
            snapshot = _favoriteEntries.ToList();
        }

        var result = FavoriteTrackMatcher.Reconcile(snapshot, tracks, fallbackTrackLoader);
        long? targetVersion = null;
        if (result.HasEntryChanges && !cancellationToken.IsCancellationRequested)
        {
            lock (_favoritesLock)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    bool mergedAny = false;
                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        if (!ReferenceEquals(snapshot[i], result.UpdatedEntries[i]))
                        {
                            int currentIndex = _favoriteEntries.FindIndex(e => ReferenceEquals(e, snapshot[i]));
                            if (currentIndex >= 0)
                            {
                                _favoriteEntries[currentIndex] = result.UpdatedEntries[i];
                                mergedAny = true;
                            }
                        }
                    }

                    if (mergedAny)
                    {
                        targetVersion = ++_favoritesVersion;
                    }
                }
            }
        }

        return (result, targetVersion);
    }

    private void RemoveFavoriteEntries(IEnumerable<Track> removedTracks)
    {
        var targets = removedTracks.ToList();
        if (targets.Count == 0)
        {
            return;
        }

        long? targetVersion = null;
        lock (_favoritesLock)
        {
            int removedCount = _favoriteEntries.RemoveAll(entry =>
                targets.Any(track => FavoriteTrackMatcher.IsMatchForRemoval(entry, track)));
            if (removedCount > 0)
            {
                targetVersion = ++_favoritesVersion;
            }
        }

        if (targetVersion.HasValue)
        {
            PersistFavorites(targetVersion.Value);
        }
    }

    /// <summary>
    /// お気に入りエントリの最新スナップショットを直列化して永続化します（古いスナップショットによるLost Updateを防止）
    /// </summary>
    private void PersistFavorites(long expectedVersion, CancellationToken cancellationToken = default)
    {
        lock (_favoritesSaveLock)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            List<FavoriteTrackEntry> latestSnapshot;
            lock (_favoritesLock)
            {
                if (_lastSavedFavoritesVersion >= expectedVersion && _lastSavedFavoritesVersion >= _favoritesVersion)
                {
                    return;
                }

                latestSnapshot = _favoriteEntries.ToList();
                _lastSavedFavoritesVersion = _favoritesVersion;
            }

            _libraryService.SaveFavoriteEntries(latestSnapshot);
        }
    }

    /// <summary>
    /// トラックをライブラリから削除します
    /// </summary>
    private void DeleteTrack(object? parameter)
    {
        if (parameter is Track track)
        {
            var result = MessageBox.Show(
                $"Remove '{track.Title}' from Library?\n(File will NOT be deleted from disk)",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var album = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                if (album != null)
                {
                    album.Tracks.Remove(track);
                    if (album.Tracks.Count == 0)
                    {
                        Albums.Remove(album);
                    }
                }

                Tracks.Remove(track);
                RemoveFavoriteEntries([track]);

                TrackRemoved?.Invoke(track);
            }
        }
    }

    private static void ShowTrackProperties(object? parameter)
    {
        if (parameter is Track track)
        {
            long fileSize = 0;
            try
            {
                if (File.Exists(track.FilePath))
                {
                    fileSize = new FileInfo(track.FilePath).Length;
                }
            }
            catch { }

            var info = $"Title: {track.Title}\n" +
                       $"Artist: {track.Artist}\n" +
                       $"Album: {track.Album}\n" +
                       $"Duration: {track.Duration}\n" +
                       $"File Size: {fileSize / 1024 / 1024.0:F2} MB\n\n" +
                       $"File Path:\n{track.FilePath}";

            MessageBox.Show(info, "Track Properties", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void OpenFileLocation(object? parameter)
    {
        if (parameter is Track track && File.Exists(track.FilePath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
        }
    }

    /// <summary>
    /// 指定されたフォルダから楽曲をスキャンしてライブラリを更新します（非同期API互換）
    /// </summary>
    /// <param name="folderPath">スキャン対象フォルダーパス</param>
    /// <returns>非同期タスク</returns>
    public async Task ScanFolderAsync(string folderPath)
    {
        await LoadLibraryAsync(folderPath);
    }

    /// <summary>
    /// キーワードでトラックを検索します
    /// </summary>
    /// <param name="keyword">検索キーワード</param>
    /// <returns>非同期タスク</returns>
    public async Task SearchAsync(string keyword)
    {
        var results = await _libraryService.SearchTracksAsync(keyword);
        RunOnUiThread(() =>
        {
            Tracks.Clear();
            foreach (var t in results)
            {
                Tracks.Add(t);
            }
        });
    }

    /// <summary>
    /// トラックのお気に入り状態を非同期で切り替えます（非同期API互換）
    /// </summary>
    /// <param name="track">対象トラック</param>
    /// <returns>非同期タスク</returns>
    public async Task ToggleFavoriteAsync(Track track)
    {
        ToggleFavorite(track);
        await Task.CompletedTask;
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
            try
            {
                dispatcher.Invoke(action);
            }
            catch (TaskCanceledException)
            {
                action();
            }
        }
    }

    private bool _disposed;

    /// <summary>
    /// アンマネージ リソースの解放および破棄を行います。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _disposed = true;
    }
}

/// <summary>
/// ライブラリスキャンおよび差分同期の実行結果を表すレコード
/// </summary>
/// <param name="AddedCount">新規追加された楽曲数</param>
/// <param name="RemovedCount">削除された楽曲数</param>
/// <param name="UpdatedCount">メタデータ再解析により更新された楽曲数</param>
/// <param name="TotalTrackCount">同期完了後の総楽曲数</param>
/// <param name="TotalAlbumCount">同期完了後の総アルバム数</param>
public sealed record LibrarySyncResult(
    int AddedCount,
    int RemovedCount,
    int UpdatedCount,
    int TotalTrackCount,
    int TotalAlbumCount);
