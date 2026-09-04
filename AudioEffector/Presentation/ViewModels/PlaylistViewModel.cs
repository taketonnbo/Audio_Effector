using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Presentation.Views;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// プレイリストの作成、一覧表示、楽曲の追加・削除・並び替えを担当するViewModel
/// </summary>
public class PlaylistViewModel : ViewModelBase, IHandle<PlaylistUpdatedEvent>
{
    private readonly PlaylistApplicationService? _playlistService;
    private readonly IAudioService? _audioService;
    private readonly LibraryApplicationService? _libraryService;
    private readonly IEventBus? _eventBus;

    private UserPlaylist? _selectedPlaylist;
    private Track? _selectedTrack;
    private string _currentPlaylistName = string.Empty;
    private ImageSource? _playlistBackgroundImage;
    private bool _isPlaylistSelectorVisible;
    private bool _isPlaylistTracksVisible;
    private bool _isFavoritesView;

    #region Public Properties

    /// <summary>
    /// プレイリストコレクション
    /// </summary>
    public ObservableCollection<UserPlaylist> Playlists { get; } = [];

    /// <summary>
    /// 選択中のプレイリスト内の楽曲コレクション
    /// </summary>
    public ObservableCollection<Track> PlaylistTracks { get; } = [];

    /// <summary>
    /// 選択中のプレイリスト
    /// </summary>
    public UserPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (SetProperty(ref _selectedPlaylist, value))
            {
                if (value != null)
                {
                    CurrentPlaylistName = value.Name;
                    _ = LoadPlaylistTracksAsync(value);
                }
            }
        }
    }

    /// <summary>
    /// 選択中のトラック
    /// </summary>
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set => SetProperty(ref _selectedTrack, value);
    }

    /// <summary>
    /// 現在選択・表示されているプレイリスト名
    /// </summary>
    public string CurrentPlaylistName
    {
        get => _currentPlaylistName;
        set => SetProperty(ref _currentPlaylistName, value);
    }

    /// <summary>
    /// プレイリストビューの背景画像
    /// </summary>
    public ImageSource? PlaylistBackgroundImage
    {
        get => _playlistBackgroundImage;
        set => SetProperty(ref _playlistBackgroundImage, value);
    }

    /// <summary>
    /// プレイリスト選択画面が表示されているかどうか
    /// </summary>
    public bool IsPlaylistSelectorVisible
    {
        get => _isPlaylistSelectorVisible;
        set
        {
            if (SetProperty(ref _isPlaylistSelectorVisible, value))
            {
                OnPropertyChanged(nameof(IsPlaylistSectionActive));
            }
        }
    }

    /// <summary>
    /// プレイリストトラック一覧が表示されているかどうか
    /// </summary>
    public bool IsPlaylistTracksVisible
    {
        get => _isPlaylistTracksVisible;
        set
        {
            if (SetProperty(ref _isPlaylistTracksVisible, value))
            {
                OnPropertyChanged(nameof(IsPlaylistSectionActive));
            }
        }
    }

    /// <summary>
    /// 現在お気に入り画面を表示しているかどうか
    /// </summary>
    public bool IsFavoritesView
    {
        get => _isFavoritesView;
        set
        {
            if (SetProperty(ref _isFavoritesView, value))
            {
                OnPropertyChanged(nameof(IsPlaylistSectionActive));
            }
        }
    }

    /// <summary>
    /// プレイリストセクションがアクティブかどうか
    /// </summary>
    public bool IsPlaylistSectionActive => IsPlaylistSelectorVisible || (IsPlaylistTracksVisible && !IsFavoritesView);

    #endregion

    #region Commands

    /// <summary>
    /// プレイリスト新規作成コマンド
    /// </summary>
    public ICommand CreatePlaylistCommand { get; }

    /// <summary>
    /// プレイリスト削除コマンド
    /// </summary>
    public ICommand DeletePlaylistCommand { get; }

    /// <summary>
    /// プレイリスト再生コマンド
    /// </summary>
    public ICommand PlayPlaylistCommand { get; }

    /// <summary>
    /// プレイリストシャッフル再生コマンド
    /// </summary>
    public ICommand ShufflePlayPlaylistCommand { get; }

    /// <summary>
    /// プレイリスト名変更コマンド
    /// </summary>
    public ICommand RenamePlaylistCommand { get; }

    /// <summary>
    /// 楽曲削除コマンド
    /// </summary>
    public ICommand RemoveTrackCommand { get; }

    /// <summary>
    /// プレイリストから楽曲を削除するコマンド
    /// </summary>
    public ICommand RemoveFromPlaylistCommand { get; }

    /// <summary>
    /// プレイリスト表示コマンド
    /// </summary>
    public ICommand ShowPlaylistCommand { get; }

    /// <summary>
    /// プレイリスト選択画面表示コマンド
    /// </summary>
    public ICommand ShowPlaylistSelectorCommand { get; }

    /// <summary>
    /// 楽曲をプレイリストに追加するコマンド
    /// </summary>
    public ICommand AddToPlaylistCommand { get; }

    /// <summary>
    /// 選択中のトラック群をプレイリストに追加するコマンド
    /// </summary>
    public ICommand AddSelectedToPlaylistCommand { get; }

    /// <summary>
    /// アルバムをプレイリストに追加するダイアログ表示コマンド
    /// </summary>
    public ICommand ShowAddAlbumToPlaylistDialogCommand { get; }

    /// <summary>
    /// プレイリスト追加ダイアログ表示コマンド
    /// </summary>
    public ICommand ShowAddToPlaylistDialogCommand { get; }

    #endregion

    #region Events for View Coordination

    /// <summary>
    /// プレイリストが選択された際に発行されるイベント
    /// </summary>
    public event Action<UserPlaylist>? PlaylistSelected;

    /// <summary>
    /// プレイリストセレクター表示が要求された際に発行されるイベント
    /// </summary>
    public event Action? PlaylistSelectorRequested;

    /// <summary>
    /// トラック追加が要求された際に発行されるイベント
    /// </summary>
    public event Action<UserPlaylist>? AddToPlaylistRequested;

    /// <summary>
    /// 選択トラック群追加が要求された際に発行されるイベント
    /// </summary>
    public event Action<Track?>? AddSelectedToPlaylistRequested;

    /// <summary>
    /// アルバム追加が要求された際に発行されるイベント
    /// </summary>
    public event Action<Album>? AddAlbumToPlaylistRequested;

    /// <summary>
    /// プレイリスト追加ダイアログ表示が要求された際に発行されるイベント
    /// </summary>
    public event Action? AddToPlaylistDialogRequested;

    #endregion

    /// <summary>
    /// PlaylistViewModelを初期化します
    /// </summary>
    /// <param name="playlistService">プレイリストアプリケーションサービス（null許容）</param>
    /// <param name="audioService">オーディオ再生サービス（null許容）</param>
    /// <param name="eventBus">イベントバス（null許容）</param>
    /// <param name="libraryService">ライブラリアプリケーションサービス（null許容）</param>
    public PlaylistViewModel(
        PlaylistApplicationService? playlistService = null,
        IAudioService? audioService = null,
        IEventBus? eventBus = null,
        LibraryApplicationService? libraryService = null)
    {
        _playlistService = playlistService;
        _audioService = audioService;
        _eventBus = eventBus;
        _libraryService = libraryService;

        CreatePlaylistCommand = new RelayCommand(ExecuteCreatePlaylist);
        DeletePlaylistCommand = new RelayCommand(ExecuteDeletePlaylist);
        PlayPlaylistCommand = new RelayCommand(ExecutePlayPlaylist);
        ShufflePlayPlaylistCommand = new RelayCommand(ExecuteShufflePlayPlaylist);
        RenamePlaylistCommand = new RelayCommand(ExecuteRenamePlaylist);
        RemoveTrackCommand = new RelayCommand(async t => await ExecuteRemoveTrackAsync(t));
        RemoveFromPlaylistCommand = new RelayCommand(async t => await ExecuteRemoveTrackAsync(t));

        ShowPlaylistCommand = new RelayCommand(p =>
        {
            if (p is UserPlaylist pl)
            {
                SelectedPlaylist = pl;
                IsFavoritesView = false;
                IsPlaylistTracksVisible = true;
                IsPlaylistSelectorVisible = false;
                PlaylistSelected?.Invoke(pl);
            }
        });

        ShowPlaylistSelectorCommand = new RelayCommand(_ =>
        {
            IsPlaylistSelectorVisible = true;
            IsPlaylistTracksVisible = false;
            IsFavoritesView = false;
            PlaylistSelectorRequested?.Invoke();
        });

        AddToPlaylistCommand = new RelayCommand(p =>
        {
            if (p is UserPlaylist pl)
            {
                AddToPlaylistRequested?.Invoke(pl);
            }
        });

        AddSelectedToPlaylistCommand = new RelayCommand(p =>
        {
            AddSelectedToPlaylistRequested?.Invoke(p as Track);
        });

        ShowAddAlbumToPlaylistDialogCommand = new RelayCommand(p =>
        {
            if (p is Album album)
            {
                AddAlbumToPlaylistRequested?.Invoke(album);
            }
        });

        ShowAddToPlaylistDialogCommand = new RelayCommand(_ =>
        {
            AddToPlaylistDialogRequested?.Invoke();
        });

        _eventBus?.Subscribe<PlaylistUpdatedEvent>(HandleAsync);

        LoadPlaylists();
    }

    #region Playlist Operations

    /// <summary>
    /// プレイリスト一覧をストレージから読み込みます
    /// </summary>
    public void LoadPlaylists()
    {
        if (_playlistService == null) return;

        try
        {
            var loaded = _playlistService.LoadPlaylists();
            Playlists.Clear();
            foreach (var p in loaded)
            {
                UpdatePlaylistThumbnails(p);
                Playlists.Add(p);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading playlists: {ex.Message}");
        }
    }

    /// <summary>
    /// プレイリストのサムネイル表示用トラックパスを最大4件設定します
    /// </summary>
    /// <param name="playlist">対象プレイリスト</param>
    public void UpdatePlaylistThumbnails(UserPlaylist playlist)
    {
        if (playlist == null) return;

        var distinctAlbumPaths = new List<string>();
        var processedAlbums = new HashSet<string>();

        foreach (var path in playlist.TrackPaths)
        {
            if (distinctAlbumPaths.Count >= 4) break;

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !processedAlbums.Contains(directory))
                {
                    processedAlbums.Add(directory);
                    distinctAlbumPaths.Add(path);
                }
            }
            catch { }
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            playlist.ThumbnailTrackPaths.Clear();
            foreach (var p in distinctAlbumPaths)
            {
                playlist.ThumbnailTrackPaths.Add(p);
            }
        });
    }

    private void ExecuteCreatePlaylist(object? parameter)
    {
        string? name = parameter as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            var dialog = new Views.InputBox("New Playlist", "Enter playlist name:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                name = dialog.InputText.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var newPlaylist = new UserPlaylist { Name = name };
            Playlists.Add(newPlaylist);
            _playlistService?.SavePlaylists(Playlists.ToList());
            SelectedPlaylist = newPlaylist;
        }
    }

    private void ExecuteDeletePlaylist(object? parameter)
    {
        var playlist = (parameter as UserPlaylist) ?? SelectedPlaylist;
        if (playlist != null)
        {
            if (MessageBox.Show($"Are you sure you want to delete playlist '{playlist.Name}'?", "Delete Playlist", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Playlists.Remove(playlist);
                _playlistService?.SavePlaylists(Playlists.ToList());
                if (SelectedPlaylist == playlist)
                {
                    SelectedPlaylist = null;
                    PlaylistTracks.Clear();
                    CurrentPlaylistName = string.Empty;
                }
            }
        }
    }

    private void ExecuteRenamePlaylist(object? parameter)
    {
        var playlist = (parameter as UserPlaylist) ?? SelectedPlaylist;
        if (playlist != null)
        {
            var inputBox = new Views.InputBox("新しい名前を入力してください:", playlist.Name);
            if (inputBox.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputBox.InputText))
            {
                playlist.Name = inputBox.InputText.Trim();
                _playlistService?.SavePlaylists(Playlists.ToList());
                CurrentPlaylistName = playlist.Name;
                OnPropertyChanged(nameof(Playlists));
            }
        }
    }

    private void ExecutePlayPlaylist(object? parameter)
    {
        var playlist = (parameter as UserPlaylist) ?? SelectedPlaylist;
        if (playlist == null || _audioService == null) return;

        var tracks = (SelectedPlaylist == playlist && PlaylistTracks.Count > 0)
            ? PlaylistTracks.ToList()
            : (_playlistService?.GetPlaylistTracksAsync(playlist.Id).GetAwaiter().GetResult())?.ToList();

        if (tracks != null && tracks.Count > 0)
        {
            _audioService.SetPlaylist(tracks);
            _audioService.PlayTrack(tracks.First());
        }
    }

    private void ExecuteShufflePlayPlaylist(object? parameter)
    {
        var playlist = (parameter as UserPlaylist) ?? SelectedPlaylist;
        if (playlist == null || _audioService == null) return;

        var tracks = (SelectedPlaylist == playlist && PlaylistTracks.Count > 0)
            ? PlaylistTracks.ToList()
            : (_playlistService?.GetPlaylistTracksAsync(playlist.Id).GetAwaiter().GetResult())?.ToList();

        if (tracks != null && tracks.Count > 0)
        {
            var shuffled = tracks.OrderBy(_ => Guid.NewGuid()).ToList();
            _audioService.SetPlaylist(shuffled);
            _audioService.PlayTrack(shuffled.First());
        }
    }

    private async Task ExecuteRemoveTrackAsync(object? parameter)
    {
        if (parameter is Track track && SelectedPlaylist != null)
        {
            if (MessageBox.Show($"Remove '{track.Title}' from playlist?", "Remove Song", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                int index = PlaylistTracks.IndexOf(track);
                if (index >= 0)
                {
                    if (_playlistService != null)
                    {
                        await _playlistService.RemoveTrackAtAsync(SelectedPlaylist.Id, index);
                    }
                    PlaylistTracks.RemoveAt(index);
                    SelectedPlaylist.TrackPaths = PlaylistTracks.Select(t => t.FilePath).ToList();
                    UpdatePlaylistThumbnails(SelectedPlaylist);
                    _playlistService?.SavePlaylists(Playlists.ToList());
                }
            }
        }
    }

    #endregion

    /// <summary>
    /// プレイリストの楽曲一覧を非同期で読み込みます
    /// </summary>
    /// <param name="playlist">対象プレイリスト</param>
    /// <returns>非同期タスク</returns>
    public async Task LoadPlaylistTracksAsync(UserPlaylist playlist)
    {
        if (playlist == null || _playlistService == null) return;

        var tracks = await _playlistService.GetPlaylistTracksAsync(playlist.Id);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            PlaylistTracks.Clear();
            foreach (var t in tracks)
            {
                PlaylistTracks.Add(t);
            }
        });
    }

    /// <summary>
    /// プレイリスト更新イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">プレイリスト更新イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(PlaylistUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (SelectedPlaylist?.Id == domainEvent.Playlist.Id)
            {
                _ = LoadPlaylistTracksAsync(domainEvent.Playlist);
            }
        });
        return Task.CompletedTask;
    }
}
