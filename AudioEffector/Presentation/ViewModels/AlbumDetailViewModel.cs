using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// アルバム情報詳細画面（AlbumDetailView）のメタデータ集計・高解像度アートワーク読込・再生/管理操作を担当するViewModel
/// </summary>
public sealed class AlbumDetailViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// Hero セクション向け高解像度カバーアートの既定デコード幅（px）
    /// </summary>
    public const int DefaultArtworkDecodePixelWidth = 400;

    private readonly LibraryViewModel _libraryViewModel;
    private readonly IAudioService _audioService;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _artworkBytesLoader;

    private Album? _currentAlbum;
    private BitmapImage? _coverImage;
    private bool _isPlaying;
    private string? _loadedArtworkFilePath;
    private CancellationTokenSource? _artLoadCts;
    private int _artLoadVersion;
    private bool _disposed;

    /// <summary>
    /// 現在表示中のアルバムエンティティ
    /// </summary>
    public Album? CurrentAlbum
    {
        get => _currentAlbum;
        private set => SetProperty(ref _currentAlbum, value);
    }

    /// <summary>
    /// 収録曲コレクション
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = new();

    /// <summary>
    /// プレイリスト専用ViewModelへの参照（コンテキストメニュー・ヘッダーからのプレイリスト追加用）
    /// </summary>
    public PlaylistViewModel? Playlist { get; }

    /// <summary>
    /// アルバムタイトル
    /// </summary>
    public string AlbumTitle => _currentAlbum?.Title ?? string.Empty;

    /// <summary>
    /// アルバムアーティスト名
    /// </summary>
    public string Artist => _currentAlbum?.Artist ?? string.Empty;

    /// <summary>
    /// リリース年（Album.Year が 0 の場合は収録曲先頭の非 0 年をフォールバック採用）
    /// </summary>
    public uint Year
    {
        get
        {
            if (_currentAlbum == null)
            {
                return 0;
            }

            if (_currentAlbum.Year > 0)
            {
                return _currentAlbum.Year;
            }

            var fallbackTrack = Tracks.FirstOrDefault(t => t.Year > 0);
            return fallbackTrack?.Year ?? 0;
        }
    }

    /// <summary>
    /// 表示用リリース年文字列（0 の場合は空文字）
    /// </summary>
    public string YearDisplay => Year > 0 ? Year.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// 表示用ジャンル文字列（収録曲の非空ジャンルの最頻値、同数はトラック順先頭）
    /// </summary>
    public string GenreDisplay => ResolveMostFrequentValue(Tracks.Select(t => t.Genre));

    /// <summary>
    /// 収録曲数
    /// </summary>
    public int TrackCount => Tracks.Count;

    /// <summary>
    /// 収録曲が 1 曲以上存在するかどうか
    /// </summary>
    public bool HasTracks => Tracks.Count > 0;

    /// <summary>
    /// アルバム総再生時間の表示用文字列（例: 1:14:24 または 42:15）
    /// </summary>
    public string TotalDurationString
    {
        get
        {
            var total = Tracks.Aggregate(TimeSpan.Zero, (sum, track) => sum + track.Duration);
            return total.TotalHours >= 1
                ? total.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : total.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// リリース年・ジャンル・曲数・総再生時間を中黒区切りで連結したメタデータサマリー文字列
    /// </summary>
    public string MetadataSummary
    {
        get
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrEmpty(YearDisplay))
            {
                parts.Add(YearDisplay);
            }

            if (!string.IsNullOrEmpty(GenreDisplay))
            {
                parts.Add(GenreDisplay);
            }

            parts.Add($"{TrackCount} 曲");
            parts.Add(TotalDurationString);
            return string.Join(" • ", parts);
        }
    }

    /// <summary>
    /// 代表オーディオフォーマット文字列（収録曲の非空フォーマットの最頻値、同数はトラック順先頭）
    /// </summary>
    public string AudioFormatSummary => ResolveMostFrequentValue(Tracks.Select(t => t.Format));

    /// <summary>
    /// ハイレゾ音源が含まれているかどうか
    /// </summary>
    public bool IsHiRes => Tracks.Any(t => t.IsHiRes);

    /// <summary>
    /// ロスレス音源が含まれているかどうか
    /// </summary>
    public bool IsLossless => Tracks.Any(t => t.IsLossless);

    /// <summary>
    /// 代表音質バッジ表示ラベル（Hi-Res / Lossless / 空文字）
    /// </summary>
    public string QualityLabel => IsHiRes ? "Hi-Res" : (IsLossless ? "Lossless" : string.Empty);

    /// <summary>
    /// Hero セクション表示用の高解像度カバーアート画像（未設定・未埋め込み時は null）
    /// </summary>
    public BitmapImage? CoverImage
    {
        get => _coverImage;
        private set => SetProperty(ref _coverImage, value);
    }

    /// <summary>
    /// 現在再生中かどうか（コンテキストメニューの再生/停止表示切替用）
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>
    /// 進行中のアートワーク非同期読み込みタスク（単体テストでの待機用）
    /// </summary>
    internal Task CurrentArtLoadTask { get; private set; } = Task.CompletedTask;

    // アルバム操作コマンド
    /// <summary>アルバム全曲再生コマンド</summary>
    public ICommand PlayAllCommand { get; }

    /// <summary>アルバムシャッフル再生コマンド</summary>
    public ICommand ShufflePlayCommand { get; }

    /// <summary>アルバムを次に再生コマンド</summary>
    public ICommand PlayNextAlbumCommand { get; }

    /// <summary>アルバムをキュー末尾に追加コマンド</summary>
    public ICommand EnqueueAlbumCommand { get; }

    /// <summary>アルバム削除コマンド</summary>
    public ICommand DeleteAlbumCommand { get; }

    /// <summary>アルバム一覧へ戻るコマンド</summary>
    public ICommand BackToAlbumsCommand { get; }

    // トラック操作コマンド
    /// <summary>個別トラック再生コマンド</summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>個別トラックの再生/停止トグルコマンド（コンテキストメニュー用）</summary>
    public ICommand TogglePlayTrackCommand { get; }

    /// <summary>個別トラックを次に再生コマンド</summary>
    public ICommand PlayNextTrackCommand { get; }

    /// <summary>個別トラックをキュー末尾に追加コマンド</summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>個別トラックのお気に入り切替コマンド</summary>
    public ICommand ToggleFavoriteTrackCommand { get; }

    /// <summary>個別トラック削除コマンド</summary>
    public ICommand DeleteTrackCommand { get; }

    /// <summary>個別トラックのプロパティ表示コマンド</summary>
    public ICommand ShowTrackPropertiesCommand { get; }

    /// <summary>個別トラックのファイル場所を開くコマンド</summary>
    public ICommand OpenFileLocationCommand { get; }

    // イベント定義
    /// <summary>再生要求イベント（対象トラック群, 開始トラック, リスト名, サブタイトル）</summary>
    public event Action<IReadOnlyList<Track>, Track?, string, string>? PlaybackRequested;

    /// <summary>キュー追加要求イベント（対象トラック群, playNext: trueなら次に再生、falseなら末尾追加）</summary>
    public event Action<IReadOnlyList<Track>, bool>? EnqueueRequested;

    /// <summary>アルバム一覧へ戻る要求イベント</summary>
    public event Action? BackRequested;

    /// <summary>
    /// AlbumDetailViewModelを初期化します
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">再生制御サービス</param>
    /// <param name="playlistViewModel">プレイリスト専用ViewModel</param>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public AlbumDetailViewModel(
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        PlaylistViewModel? playlistViewModel = null)
        : this(libraryViewModel, audioService, playlistViewModel, artworkBytesLoader: null)
    {
    }

    /// <summary>
    /// カスタムアートワークローダーを指定してAlbumDetailViewModelを初期化します（主に単体テスト用）
    /// </summary>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="audioService">再生制御サービス</param>
    /// <param name="playlistViewModel">プレイリスト専用ViewModel</param>
    /// <param name="artworkBytesLoader">音声ファイルパスから埋め込みアートワークバイト列を非同期抽出するローダー</param>
    public AlbumDetailViewModel(
        LibraryViewModel libraryViewModel,
        IAudioService audioService,
        PlaylistViewModel? playlistViewModel,
        Func<string, CancellationToken, Task<byte[]?>>? artworkBytesLoader)
    {
        _libraryViewModel = libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        Playlist = playlistViewModel ?? _libraryViewModel.Playlist;
        _artworkBytesLoader = artworkBytesLoader ?? ExtractArtworkBytesAsync;

        _isPlaying = _audioService.IsPlaying;

        PlayAllCommand = new RelayCommand(_ => PlayAll(), _ => HasTracks);
        ShufflePlayCommand = new RelayCommand(_ => ShufflePlay(), _ => HasTracks);
        PlayNextAlbumCommand = new RelayCommand(_ => EnqueueAlbum(playNext: true), _ => HasTracks);
        EnqueueAlbumCommand = new RelayCommand(_ => EnqueueAlbum(playNext: false), _ => HasTracks);
        DeleteAlbumCommand = new RelayCommand(_ => DeleteCurrentAlbum(), _ => _currentAlbum != null);
        BackToAlbumsCommand = new RelayCommand(_ => BackRequested?.Invoke());

        PlayTrackCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                PlayTrack(track);
            }
        });
        TogglePlayTrackCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                TogglePlayTrack(track);
            }
        });
        PlayNextTrackCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                EnqueueRequested?.Invoke([track], true);
            }
        });
        EnqueueTrackCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                EnqueueRequested?.Invoke([track], false);
            }
        });
        ToggleFavoriteTrackCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                _libraryViewModel.ToggleFavoriteCommand.Execute(track);
            }
        });
        DeleteTrackCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                _libraryViewModel.DeleteTrackCommand.Execute(track);
            }
        });
        ShowTrackPropertiesCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                _libraryViewModel.ShowTrackPropertiesCommand.Execute(track);
            }
        });
        OpenFileLocationCommand = new RelayCommand(param =>
        {
            if (param is Track track)
            {
                _libraryViewModel.OpenFileLocationCommand.Execute(track);
            }
        });

        _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
        _libraryViewModel.TrackRemoved += OnLibraryTrackRemoved;
        _libraryViewModel.AlbumRemoved += OnLibraryAlbumRemoved;
        _libraryViewModel.LibrarySynced += OnLibrarySynced;
    }

    /// <summary>
    /// 表示対象のアルバムを設定し、メタデータおよび高解像度カバーアートの読み込みを開始します
    /// </summary>
    /// <param name="album">表示対象のアルバム（null の場合は表示状態をクリアし画像参照を解放）</param>
    public void SetAlbum(Album? album)
    {
        if (_disposed)
        {
            return;
        }

        CurrentAlbum = album;
        ReplaceTracks(album?.Tracks);
        NotifyMetadataPropertiesChanged();

        string? artworkPath = album?.RepresentativeTrackFilePath;
        StartArtworkLoad(artworkPath);
    }

    private void ReplaceTracks(IEnumerable<Track>? source)
    {
        Tracks.Clear();
        if (source == null)
        {
            return;
        }

        foreach (var track in source)
        {
            Tracks.Add(track);
        }
    }

    private void NotifyMetadataPropertiesChanged()
    {
        OnPropertyChanged(nameof(AlbumTitle));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(Year));
        OnPropertyChanged(nameof(YearDisplay));
        OnPropertyChanged(nameof(GenreDisplay));
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(TotalDurationString));
        OnPropertyChanged(nameof(MetadataSummary));
        OnPropertyChanged(nameof(AudioFormatSummary));
        OnPropertyChanged(nameof(IsHiRes));
        OnPropertyChanged(nameof(IsLossless));
        OnPropertyChanged(nameof(QualityLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    private void StartArtworkLoad(string? filePath)
    {
        CancelArtworkLoad();
        int version = Interlocked.Increment(ref _artLoadVersion);
        _loadedArtworkFilePath = filePath;
        CoverImage = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            CurrentArtLoadTask = Task.CompletedTask;
            return;
        }

        var cts = new CancellationTokenSource();
        _artLoadCts = cts;
        CurrentArtLoadTask = LoadArtworkAsync(filePath, version, cts.Token);
    }

    private async Task LoadArtworkAsync(string filePath, int version, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? imageBytes = await _artworkBytesLoader(filePath, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || _disposed || version != Volatile.Read(ref _artLoadVersion))
            {
                return;
            }

            BitmapImage? decoded = null;
            if (imageBytes is { Length: > 0 })
            {
                decoded = await Task.Run(() => DecodeFrozenBitmap(imageBytes, DefaultArtworkDecodePixelWidth), cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested || _disposed || version != Volatile.Read(ref _artLoadVersion))
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (!cancellationToken.IsCancellationRequested && !_disposed && version == Volatile.Read(ref _artLoadVersion))
                {
                    CoverImage = decoded;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // アルバム切替や破棄に伴う正常なキャンセル
        }
        catch (Exception)
        {
            // 破損画像やI/O失敗時はフォールバック表示（CoverImage = null）を維持
        }
    }

    private void CancelArtworkLoad()
    {
        if (_artLoadCts != null)
        {
            try
            {
                _artLoadCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _artLoadCts.Dispose();
            _artLoadCts = null;
        }
    }

    private static Task<byte[]?> ExtractArtworkBytesAsync(string filePath, CancellationToken cancellationToken)
    {
        return Task.Run<byte[]?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath))
            {
                return null;
            }

            using var tagFile = TagLib.File.Create(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            if (tagFile.Tag.Pictures is { Length: > 0 })
            {
                return tagFile.Tag.Pictures[0].Data.Data;
            }

            return null;
        }, cancellationToken);
    }

    private static BitmapImage? DecodeFrozenBitmap(byte[] imageBytes, int decodePixelWidth)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ResolveMostFrequentValue(IEnumerable<string?> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var orderedDistinct = new List<string>();

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string trimmed = raw.Trim();
            if (counts.TryGetValue(trimmed, out int currentCount))
            {
                counts[trimmed] = currentCount + 1;
            }
            else
            {
                counts[trimmed] = 1;
                orderedDistinct.Add(trimmed);
            }
        }

        if (orderedDistinct.Count == 0)
        {
            return string.Empty;
        }

        string best = orderedDistinct[0];
        int bestCount = counts[best];
        for (int i = 1; i < orderedDistinct.Count; i++)
        {
            string candidate = orderedDistinct[i];
            int count = counts[candidate];
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    private void PlayAll()
    {
        if (_currentAlbum == null || Tracks.Count == 0)
        {
            return;
        }

        var tracks = Tracks.ToList();
        var startTrack = tracks[0];
        startTrack.IsPlaying = true;
        PlaybackRequested?.Invoke(tracks, startTrack, AlbumTitle, Artist);
        _audioService.SetPlaylist(tracks, startTrack);
        _audioService.PlayTrack(startTrack);
    }

    private void ShufflePlay()
    {
        if (_currentAlbum == null || Tracks.Count == 0)
        {
            return;
        }

        var list = Tracks.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        var startTrack = list[0];
        startTrack.IsPlaying = true;
        _audioService.SetPlaylist(list, startTrack);

        var effectiveList = _audioService.IsShuffleEnabled
            ? BuildEffectiveShuffleList(startTrack, list, _audioService.AlbumQueue)
            : list;

        PlaybackRequested?.Invoke(effectiveList, startTrack, AlbumTitle, Artist);
        _audioService.PlayTrack(startTrack);
    }

    private static List<Track> BuildEffectiveShuffleList(Track firstTrack, List<Track> fallbackList, IReadOnlyList<Track>? albumQueue)
    {
        if (albumQueue == null || albumQueue.Count == 0)
        {
            return fallbackList;
        }

        var result = new List<Track>(albumQueue.Count + 1) { firstTrack };
        foreach (var track in albumQueue)
        {
            if (!ReferenceEquals(track, firstTrack))
            {
                result.Add(track);
            }
        }

        return result;
    }

    private void EnqueueAlbum(bool playNext)
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        EnqueueRequested?.Invoke(Tracks.ToList(), playNext);
    }

    private void DeleteCurrentAlbum()
    {
        if (_currentAlbum != null)
        {
            _libraryViewModel.DeleteAlbumCommand.Execute(_currentAlbum);
        }
    }

    private void TogglePlayTrack(Track track)
    {
        if (track.IsPlaying && _audioService.IsPlaying)
        {
            _audioService.TogglePlayPause();
            return;
        }

        PlayTrack(track);
    }

    private void PlayTrack(Track track)
    {
        var tracks = Tracks.ToList();
        int trackIndex = tracks.IndexOf(track);
        // #276: 途中曲から再生開始した際に選択曲より前の楽曲を再生キューに残さない
        var candidateTracks = trackIndex >= 0
            ? tracks.Skip(trackIndex).ToList()
            : [track];

        // #299: 右側パネルにはアルバムの全収録曲を表示し、再生キューには選択曲以降のみを渡す
        var displayTracks = tracks.Count > 0 ? tracks : [track];
        track.IsPlaying = true;
        PlaybackRequested?.Invoke(displayTracks, track, AlbumTitle, Artist);
        _audioService.SetPlaylist(candidateTracks, track);
        _audioService.PlayTrack(track);
    }

    private void OnPlaybackStateChanged(bool isPlaying)
    {
        RunOnUiThread(() => IsPlaying = isPlaying);
    }

    /// <summary>
    /// ライブラリからのトラック削除通知を受け取り、収録曲・メタデータ・代表アートワークを更新します
    /// </summary>
    /// <param name="removedTrack">削除されたトラック</param>
    internal void OnLibraryTrackRemoved(Track removedTrack)
    {
        if (_disposed || _currentAlbum == null)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_disposed || _currentAlbum == null)
            {
                return;
            }

            if (!Tracks.Remove(removedTrack))
            {
                return;
            }

            if (Tracks.Count == 0)
            {
                SetAlbum(null);
                BackRequested?.Invoke();
                return;
            }

            NotifyMetadataPropertiesChanged();

            string newArtworkPath = Tracks.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.FilePath))?.FilePath ?? string.Empty;
            if (!string.Equals(_loadedArtworkFilePath, newArtworkPath, StringComparison.OrdinalIgnoreCase))
            {
                StartArtworkLoad(newArtworkPath);
            }
        });
    }

    /// <summary>
    /// ライブラリからのアルバム削除通知を受け取り、表示中アルバムと一致する場合は表示をクリアして一覧へ復帰します
    /// </summary>
    /// <param name="removedAlbum">削除されたアルバム</param>
    internal void OnLibraryAlbumRemoved(Album removedAlbum)
    {
        if (_disposed || _currentAlbum == null)
        {
            return;
        }

        if (!_currentAlbum.Equals(removedAlbum))
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_disposed || _currentAlbum == null || !_currentAlbum.Equals(removedAlbum))
            {
                return;
            }

            SetAlbum(null);
            BackRequested?.Invoke();
        });
    }

    private void OnLibrarySynced(LibrarySyncResult syncResult)
    {
        if (_disposed || _currentAlbum == null)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_disposed || _currentAlbum == null)
            {
                return;
            }

            var updatedAlbum = _libraryViewModel.Albums.FirstOrDefault(a => a.Equals(_currentAlbum));
            if (updatedAlbum == null || updatedAlbum.Tracks.Count == 0)
            {
                SetAlbum(null);
                BackRequested?.Invoke();
                return;
            }

            CurrentAlbum = updatedAlbum;
            ReplaceTracks(updatedAlbum.Tracks);
            NotifyMetadataPropertiesChanged();

            string newArtworkPath = updatedAlbum.RepresentativeTrackFilePath;
            if (!string.Equals(_loadedArtworkFilePath, newArtworkPath, StringComparison.OrdinalIgnoreCase))
            {
                StartArtworkLoad(newArtworkPath);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.Invoke(action);
        }
        catch (TaskCanceledException)
        {
            // アプリ終了等によるキャンセル時はバックグラウンドスレッドでの UI 更新を破棄
        }
    }

    /// <summary>
    /// 進行中の非同期読み込みとイベント購読を解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelArtworkLoad();
        Interlocked.Increment(ref _artLoadVersion);

        _audioService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _libraryViewModel.TrackRemoved -= OnLibraryTrackRemoved;
        _libraryViewModel.AlbumRemoved -= OnLibraryAlbumRemoved;
        _libraryViewModel.LibrarySynced -= OnLibrarySynced;

        _coverImage = null;
        _currentAlbum = null;
    }
}
