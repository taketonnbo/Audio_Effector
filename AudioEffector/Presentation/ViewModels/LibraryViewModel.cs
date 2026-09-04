using System;
using System.Collections.Generic;
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
    private readonly LibraryApplicationService? _libraryService;
    private readonly IAudioService? _audioService;

    private Track? _selectedTrack;
    private Album? _selectedAlbum;
    private string _searchKeyword = string.Empty;
    private bool _isScanning;
    private double _scanProgress;
    private bool _isLibraryVisible = true;
    private bool _isGridView = true;
    private bool _isAscending = true;
    private string _selectedSortOption = "Title";
    private bool _isSelectionMode;

    /// <summary>
    /// 全楽曲コレクション
    /// </summary>
    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>
    /// アルバムコレクション
    /// </summary>
    public ObservableCollection<Album> Albums { get; } = [];

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
    /// ライブラリビューが表示されているかどうか
    /// </summary>
    public bool IsLibraryVisible
    {
        get => _isLibraryVisible;
        set => SetProperty(ref _isLibraryVisible, value);
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
        set => SetProperty(ref _isAscending, value);
    }

    /// <summary>
    /// 選択中のソートオプション
    /// </summary>
    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set => SetProperty(ref _selectedSortOption, value);
    }

    /// <summary>
    /// 利用可能なソートオプション一覧
    /// </summary>
    public List<string> SortOptions { get; } = ["Title", "Artist", "Album", "Year", "Duration", "TrackNumber", "DateAdded"];

    /// <summary>
    /// トラック選択モードが有効かどうか
    /// </summary>
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set => SetProperty(ref _isSelectionMode, value);
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

    #region Commands

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
    /// 表示ビュー（グリッド/リスト）切り替えコマンド
    /// </summary>
    public ICommand ToggleViewCommand { get; }

    /// <summary>
    /// ソート順（昇順/降順）切り替えコマンド
    /// </summary>
    public ICommand ToggleSortDirectionCommand { get; }

    /// <summary>
    /// トラック選択モード切り替えコマンド
    /// </summary>
    public ICommand ToggleSelectionModeCommand { get; }

    /// <summary>
    /// アルバム再生コマンド
    /// </summary>
    public ICommand PlayAlbumCommand { get; }

    /// <summary>
    /// 次に再生するアルバムとしてキューに追加するコマンド
    /// </summary>
    public ICommand PlayNextAlbumCommand { get; }

    /// <summary>
    /// アルバムをキューの末尾に追加するコマンド
    /// </summary>
    public ICommand EnqueueAlbumCommand { get; }

    /// <summary>
    /// アルバム削除コマンド
    /// </summary>
    public ICommand DeleteAlbumCommand { get; }

    #endregion

    #region Events for View Coordination

    /// <summary>
    /// アルバム再生要求イベント
    /// </summary>
    public event Action<Album>? PlayAlbumRequested;

    /// <summary>
    /// アルバム次再生要求イベント
    /// </summary>
    public event Action<Album>? PlayNextAlbumRequested;

    /// <summary>
    /// アルバムエンキュー要求イベント
    /// </summary>
    public event Action<Album>? EnqueueAlbumRequested;

    /// <summary>
    /// アルバム削除要求イベント
    /// </summary>
    public event Action<Album>? DeleteAlbumRequested;

    /// <summary>
    /// お気に入り切り替え要求イベント
    /// </summary>
    public event Action<Track>? ToggleFavoriteRequested;

    #endregion

    /// <summary>
    /// LibraryViewModelを初期化します
    /// </summary>
    /// <param name="libraryService">ライブラリアプリケーションサービス（null許容）</param>
    /// <param name="audioService">オーディオ再生サービス（null許容）</param>
    public LibraryViewModel(
        LibraryApplicationService? libraryService = null,
        IAudioService? audioService = null)
    {
        _libraryService = libraryService;
        _audioService = audioService;

        ScanFolderCommand = new RelayCommand(async folder =>
        {
            if (folder is string folderPath && !string.IsNullOrWhiteSpace(folderPath))
            {
                await ScanFolderAsync(folderPath);
            }
        });

        PlayTrackCommand = new RelayCommand(track =>
        {
            if (track is Track t && _audioService != null)
            {
                _audioService.SetPlaylist(Tracks.ToList());
                _audioService.PlayTrack(t);
            }
        });

        ToggleFavoriteCommand = new RelayCommand(track =>
        {
            if (track is Track t)
            {
                ToggleFavoriteRequested?.Invoke(t);
            }
        });

        ToggleViewCommand = new RelayCommand(_ => IsGridView = !IsGridView);
        ToggleSortDirectionCommand = new RelayCommand(_ => IsAscending = !IsAscending);
        ToggleSelectionModeCommand = new RelayCommand(_ => IsSelectionMode = !IsSelectionMode);

        PlayAlbumCommand = new RelayCommand(p =>
        {
            if (p is Album album)
            {
                PlayAlbumRequested?.Invoke(album);
            }
        });

        PlayNextAlbumCommand = new RelayCommand(p =>
        {
            if (p is Album album)
            {
                PlayNextAlbumRequested?.Invoke(album);
            }
        });

        EnqueueAlbumCommand = new RelayCommand(p =>
        {
            if (p is Album album)
            {
                EnqueueAlbumRequested?.Invoke(album);
            }
        });

        DeleteAlbumCommand = new RelayCommand(p =>
        {
            if (p is Album album)
            {
                DeleteAlbumRequested?.Invoke(album);
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
        if (_libraryService == null) return;

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
        if (_libraryService == null) return;

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
        if (_libraryService == null) return;

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
