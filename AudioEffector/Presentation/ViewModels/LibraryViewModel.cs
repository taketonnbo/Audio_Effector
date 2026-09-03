using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 楽曲ライブラリの楽曲一覧、アルバム一覧、フォルダスキャン、検索、お気に入り切り替えを担当するViewModel
/// </summary>
public class LibraryViewModel : ViewModelBase
{
    private readonly LibraryApplicationService _libraryService;
    private readonly AudioApplicationService _audioService;

    private Track? _selectedTrack;
    private Album? _selectedAlbum;
    private string _searchKeyword = string.Empty;
    private bool _isScanning;
    private double _scanProgress;

    /// <summary>
    /// 全楽曲コレクション
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = new();

    /// <summary>
    /// アルバムコレクション
    /// </summary>
    public ObservableCollection<Album> Albums { get; } = new();

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
    /// フォルダスキャン中かどうか
    /// </summary>
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
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
    /// フォルダスキャンコマンド
    /// </summary>
    public ICommand ScanFolderCommand { get; }

    /// <summary>
    /// トラック再生コマンド
    /// </summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>
    /// お気に入り切り替えコマンド
    /// </summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>
    /// LibraryViewModelを初期化します
    /// </summary>
    /// <param name="libraryService">ライブラリアプリケーションサービス</param>
    /// <param name="audioService">オーディオ再生アプリケーションサービス</param>
    public LibraryViewModel(
        LibraryApplicationService libraryService,
        AudioApplicationService audioService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

        ScanFolderCommand = new RelayCommand(async folder =>
        {
            if (folder is string folderPath && !string.IsNullOrWhiteSpace(folderPath))
            {
                await ScanFolderAsync(folderPath);
            }
        });

        PlayTrackCommand = new RelayCommand(async track =>
        {
            if (track is Track t)
            {
                await _audioService.SetQueueAndPlayAsync(Tracks, Tracks.IndexOf(t));
            }
        });

        ToggleFavoriteCommand = new RelayCommand(async track =>
        {
            if (track is Track t)
            {
                await ToggleFavoriteAsync(t);
            }
        });
    }

    /// <summary>
    /// 指定されたフォルダから楽曲をスキャンしてライブラリを更新します
    /// </summary>
    /// <param name="folderPath">スキャン対象フォルダパス</param>
    /// <returns>非同期タスク</returns>
    public async Task ScanFolderAsync(string folderPath)
    {
        IsScanning = true;
        ScanProgress = 0.0;

        var progress = new Progress<double>(p => ScanProgress = p);

        try
        {
            var scannedTracks = await _libraryService.ScanFolderAsync(folderPath, progress);
            var albums = await _libraryService.GetAllAlbumsAsync();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Tracks.Clear();
                foreach (var t in scannedTracks)
                {
                    Tracks.Add(t);
                }

                Albums.Clear();
                foreach (var a in albums)
                {
                    Albums.Add(a);
                }
            });
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// キーワードでトラックを検索します
    /// </summary>
    /// <param name="keyword">検索キーワード</param>
    /// <returns>非同期タスク</returns>
    public async Task SearchAsync(string keyword)
    {
        var results = await _libraryService.SearchTracksAsync(keyword);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Tracks.Clear();
            foreach (var t in results)
            {
                Tracks.Add(t);
            }
        });
    }

    /// <summary>
    /// トラックのお気に入り状態を切り替えます
    /// </summary>
    /// <param name="track">対象トラック</param>
    /// <returns>非同期タスク</returns>
    public async Task ToggleFavoriteAsync(Track track)
    {
        var updated = await _libraryService.ToggleFavoriteAsync(track.Id);
        if (updated != null)
        {
            int index = Tracks.IndexOf(track);
            if (index >= 0)
            {
                Tracks[index] = updated;
            }
        }
    }
}
