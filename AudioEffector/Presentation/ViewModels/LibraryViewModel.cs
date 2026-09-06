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
    private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);

    private Track? _selectedTrack;
    private Album? _selectedAlbum;
    private Album? _expandedAlbum;
    private string _searchKeyword = string.Empty;
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
        set => SetProperty(ref _isLoading, value);
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

    /// <summary>
    /// LibraryViewModelを初期化します
    /// </summary>
    /// <param name="libraryService">ライブラリアプリケーションサービス</param>
    /// <param name="audioService">再生制御サービス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="playlistViewModel">プレイリスト専用ViewModel</param>
    public LibraryViewModel(
        LibraryApplicationService libraryService,
        IAudioService audioService,
        ISettingsService settingsService,
        PlaylistViewModel? playlistViewModel = null)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        Playlist = playlistViewModel;

        // お気に入りの読み込み
        var loadedFavorites = _libraryService.LoadFavorites();
        foreach (var path in loadedFavorites)
        {
            _favoritePaths.Add(path);
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
    /// 指定されたフォルダー（または保存されたパス）からライブラリをロードします。
    /// </summary>
    /// <param name="rootFolder">スキャン対象フォルダーパス（省略時は設定から読み込み）</param>
    public async void LoadLibrary(string? rootFolder = null)
    {
        if (string.IsNullOrEmpty(rootFolder))
        {
            var settings = _settingsService.LoadSettings();
            rootFolder = settings.LastLibraryPath;
        }

        if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder)) return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;

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

                var scannedTracks = new System.Collections.Concurrent.ConcurrentBag<Track>();

                Parallel.ForEach(files, (file, loopState) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        loopState.Stop();
                        return;
                    }

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
                        track.Title = tfile.Tag.Title ?? track.Title;
                        track.Artist = tfile.Tag.FirstPerformer ?? "Unknown Artist";
                        track.Album = tfile.Tag.Album ?? "Unknown Album";
                        track.Duration = tfile.Properties.Duration;
                        track.Year = tfile.Tag.Year;
                        track.TrackNumber = tfile.Tag.Track;

                        track.Bitrate = tfile.Properties.AudioBitrate;
                        track.SampleRate = tfile.Properties.AudioSampleRate;
                        track.BitsPerSample = tfile.Properties.BitsPerSample;
                        string ext = Path.GetExtension(file).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                        track.Format = ext.TrimStart('.').ToUpper(System.Globalization.CultureInfo.InvariantCulture);

                        track.IsLossless = LosslessAudioExtensions.Contains(ext);
                        track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;
                    }
                    catch { }

                    if (_favoritePaths.Contains(track.FilePath))
                    {
                        track.IsFavorite = true;
                    }

                    scannedTracks.Add(track);
                });

                if (token.IsCancellationRequested) return;

                RunOnUiThread(() =>
                {
                    if (token.IsCancellationRequested) return;

                    var allTrackList = scannedTracks.ToList();
                    Tracks.Clear();
                    foreach (var t in allTrackList)
                    {
                        Tracks.Add(t);
                    }

                    var grouped = allTrackList.GroupBy(t => t.Album);
                    Albums.Clear();
                    foreach (var g in grouped)
                    {
                        uint albumYear = g.Select(t => t.Year).Where(y => y > 0).GroupBy(y => y).OrderByDescending(z => z.Count()).FirstOrDefault()?.Key ?? 0;

                        Albums.Add(new Album
                        {
                            Title = g.Key,
                            Artist = g.First().Artist,
                            CoverImage = null,
                            Tracks = g.OrderBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList(),
                            Year = albumYear
                        });
                    }

                    SortLibrary();
                    _audioService.SetPlaylist(allTrackList);
                });
            }, token);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は正常終了
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
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
                    if (_favoritePaths.Contains(track.FilePath))
                    {
                        _favoritePaths.Remove(track.FilePath);
                    }
                }

                _libraryService.SaveFavorites(_favoritePaths.ToList());
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
            track.IsFavorite = !track.IsFavorite;

            if (track.IsFavorite)
            {
                if (!_favoritePaths.Contains(track.FilePath))
                {
                    _favoritePaths.Add(track.FilePath);
                }
            }
            else
            {
                _favoritePaths.Remove(track.FilePath);
            }

            _libraryService.SaveFavorites(_favoritePaths.ToList());
            FavoriteToggled?.Invoke(track);
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
                if (_favoritePaths.Contains(track.FilePath))
                {
                    _favoritePaths.Remove(track.FilePath);
                    _libraryService.SaveFavorites(_favoritePaths.ToList());
                }

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
        LoadLibrary(folderPath);
        await Task.CompletedTask;
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
