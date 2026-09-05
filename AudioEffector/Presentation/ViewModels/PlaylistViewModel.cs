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
/// プレイリストの作成、一覧表示、楽曲の追加・削除・再生を担当するViewModel
/// </summary>
public sealed class PlaylistViewModel : ViewModelBase, IDisposable, IHandle<PlaylistUpdatedEvent>
{
    private readonly PlaylistApplicationService _playlistService;
    private readonly LibraryApplicationService _libraryService;
    private readonly IAudioService _audioService;
    private readonly IEventBus _eventBus;
    private CancellationTokenSource? _trackLoadCancellation;
    private long _trackLoadVersion;
    private UserPlaylist? _selectedPlaylist;
    private Track? _selectedTrack;
    private string _currentPlaylistName = string.Empty;
    private ImageSource? _playlistBackgroundImage;
    private bool _isFavoritesView;
    private bool _disposed;

    /// <summary>
    /// ユーザーが作成したプレイリストのコレクション
    /// </summary>
    public ObservableCollection<UserPlaylist> UserPlaylists { get; } = [];

    /// <summary>
    /// 現在表示しているプレイリストまたはお気に入りの楽曲コレクション
    /// </summary>
    public ObservableCollection<Track> PlaylistTracks { get; } = [];

    /// <summary>
    /// 選択中のプレイリスト
    /// </summary>
    public UserPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set => _ = SelectPlaylistAsync(value);
    }

    /// <summary>
    /// 現在表示中のプレイリスト
    /// </summary>
    public UserPlaylist? CurrentViewingPlaylist => SelectedPlaylist;

    /// <summary>
    /// 選択中のトラック
    /// </summary>
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set => SetProperty(ref _selectedTrack, value);
    }

    /// <summary>
    /// 現在表示しているプレイリスト名
    /// </summary>
    public string CurrentPlaylistName
    {
        get => _currentPlaylistName;
        private set => SetProperty(ref _currentPlaylistName, value);
    }

    /// <summary>
    /// プレイリスト画面の背景画像
    /// </summary>
    public ImageSource? PlaylistBackgroundImage
    {
        get => _playlistBackgroundImage;
        private set => SetProperty(ref _playlistBackgroundImage, value);
    }

    /// <summary>
    /// お気に入り一覧を表示中かどうか
    /// </summary>
    public bool IsFavoritesView
    {
        get => _isFavoritesView;
        private set => SetProperty(ref _isFavoritesView, value);
    }

    /// <summary>プレイリストを作成するコマンド</summary>
    public ICommand CreatePlaylistCommand { get; }

    /// <summary>プレイリストを削除するコマンド</summary>
    public ICommand DeletePlaylistCommand { get; }

    /// <summary>プレイリストを再生するコマンド</summary>
    public ICommand PlayPlaylistCommand { get; }

    /// <summary>プレイリストをシャッフル再生するコマンド</summary>
    public ICommand ShufflePlayPlaylistCommand { get; }

    /// <summary>プレイリスト名を変更するコマンド</summary>
    public ICommand RenamePlaylistCommand { get; }

    /// <summary>表示中の一覧から楽曲を削除するコマンド</summary>
    public ICommand RemoveFromPlaylistCommand { get; }

    /// <summary>プレイリストの楽曲一覧を表示するコマンド</summary>
    public ICommand ShowPlaylistCommand { get; }

    /// <summary>プレイリスト選択画面を表示するコマンド</summary>
    public ICommand ShowPlaylistSelectorCommand { get; }

    /// <summary>楽曲追加先プレイリストを選択するコマンド</summary>
    public ICommand ShowAddToPlaylistDialogCommand { get; }

    /// <summary>選択中の楽曲をプレイリストへ追加するコマンド</summary>
    public ICommand AddSelectedToPlaylistCommand { get; }

    /// <summary>アルバムをプレイリストへ追加するコマンド</summary>
    public ICommand ShowAddAlbumToPlaylistDialogCommand { get; }

    /// <summary>
    /// プレイリスト画面内の操作によって画面遷移が必要になったことを通知します。
    /// </summary>
    public event Action<ViewType>? ViewRequested;

    /// <summary>
    /// プレイリスト再生開始時に再生リスト表示の更新を要求します。
    /// </summary>
    public event Action<IReadOnlyList<Track>, string, string>? PlaybackRequested;

    /// <summary>
    /// お気に入り画面からの削除をライブラリ側へ要求します。
    /// </summary>
    public event Action<Track>? FavoriteRemovalRequested;

    /// <summary>
    /// プレイリストViewModelを初期化します。
    /// </summary>
    /// <param name="playlistService">プレイリストアプリケーションサービス</param>
    /// <param name="libraryService">ライブラリアプリケーションサービス</param>
    /// <param name="audioService">既存再生サービス</param>
    /// <param name="eventBus">イベントバス</param>
    public PlaylistViewModel(
        PlaylistApplicationService playlistService,
        LibraryApplicationService libraryService,
        IAudioService audioService,
        IEventBus eventBus)
    {
        _playlistService = playlistService ?? throw new ArgumentNullException(nameof(playlistService));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
        DeletePlaylistCommand = new RelayCommand(DeletePlaylist);
        PlayPlaylistCommand = new RelayCommand(parameter => _ = PlayPlaylistAsync(parameter as UserPlaylist, shuffle: false));
        ShufflePlayPlaylistCommand = new RelayCommand(parameter => _ = PlayPlaylistAsync(parameter as UserPlaylist, shuffle: true));
        RenamePlaylistCommand = new RelayCommand(RenamePlaylist);
        RemoveFromPlaylistCommand = new RelayCommand(parameter => _ = RemoveTrackAsync(parameter as Track));
        ShowPlaylistCommand = new RelayCommand(parameter =>
        {
            if (parameter is UserPlaylist playlist)
            {
                ViewRequested?.Invoke(ViewType.PlaylistTracks);
                _ = SelectPlaylistAsync(playlist);
            }
        });
        ShowPlaylistSelectorCommand = new RelayCommand(_ =>
        {
            IsFavoritesView = false;
            ViewRequested?.Invoke(ViewType.Playlists);
        });
        ShowAddToPlaylistDialogCommand = new RelayCommand(parameter =>
        {
            if (parameter is Track track)
            {
                ShowAddTracksDialog([track]);
            }
        });
        AddSelectedToPlaylistCommand = new RelayCommand(AddSelectedTracksToPlaylist);
        ShowAddAlbumToPlaylistDialogCommand = new RelayCommand(parameter =>
        {
            if (parameter is Album album && album.Tracks.Count > 0)
            {
                ShowAddTracksDialog(album.Tracks);
            }
        });

        _eventBus.Subscribe<PlaylistUpdatedEvent>(HandleAsync);
    }

    /// <summary>
    /// 永続化済みプレイリストを読み込みます。
    /// </summary>
    public void LoadPlaylists()
    {
        var loadedPlaylists = _playlistService.LoadPlaylists();
        RunOnUiThread(() =>
        {
            UserPlaylists.Clear();
            foreach (var playlist in loadedPlaylists)
            {
                UpdatePlaylistThumbnails(playlist);
                UserPlaylists.Add(playlist);
            }
        });
    }

    /// <summary>
    /// 対象プレイリストを選択し、所属トラックを読み込みます。
    /// </summary>
    /// <param name="playlist">選択するプレイリスト。選択解除時はnull</param>
    /// <returns>トラック読み込み処理</returns>
    public Task SelectPlaylistAsync(UserPlaylist? playlist)
    {
        if (!SetProperty(ref _selectedPlaylist, playlist, nameof(SelectedPlaylist)))
        {
            return playlist == null ? Task.CompletedTask : LoadPlaylistTracksAsync(playlist);
        }

        OnPropertyChanged(nameof(CurrentViewingPlaylist));
        IsFavoritesView = false;
        CurrentPlaylistName = playlist?.Name ?? string.Empty;

        if (playlist == null)
        {
            CancelPendingTrackLoad();
            RunOnUiThread(PlaylistTracks.Clear);
            return Task.CompletedTask;
        }

        return LoadPlaylistTracksAsync(playlist);
    }

    /// <summary>
    /// お気に入り一覧をプレイリスト用の共通ビューに表示します。
    /// </summary>
    /// <param name="tracks">表示するお気に入りトラック</param>
    /// <param name="backgroundImage">背景画像</param>
    public void ShowFavorites(IEnumerable<Track> tracks, ImageSource? backgroundImage)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        CancelPendingTrackLoad();

        if (_selectedPlaylist != null)
        {
            _selectedPlaylist = null;
            OnPropertyChanged(nameof(SelectedPlaylist));
            OnPropertyChanged(nameof(CurrentViewingPlaylist));
        }

        IsFavoritesView = true;
        CurrentPlaylistName = "Favorites";
        PlaylistBackgroundImage = backgroundImage;
        ReplaceDisplayedTracks(tracks);
    }

    /// <summary>
    /// 再生中のジャケットをプレイリスト背景へ反映します。
    /// </summary>
    /// <param name="image">背景画像</param>
    public void SetBackgroundImage(ImageSource? image)
    {
        if (!IsFavoritesView)
        {
            PlaylistBackgroundImage = image;
        }
    }

    /// <summary>
    /// お気に入り表示中の背景画像を更新します。
    /// </summary>
    /// <param name="image">背景画像</param>
    public void SetFavoritesBackgroundImage(ImageSource? image)
    {
        if (IsFavoritesView)
        {
            PlaylistBackgroundImage = image;
        }
    }

    /// <summary>
    /// お気に入り表示中のコレクションへ楽曲を追加します。
    /// </summary>
    /// <param name="track">追加する楽曲</param>
    public void AddFavoriteTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (IsFavoritesView && !PlaylistTracks.Any(item => PathsEqual(item.FilePath, track.FilePath)))
        {
            RunOnUiThread(() => PlaylistTracks.Add(track));
        }
    }

    /// <summary>
    /// 表示中コレクションから楽曲を取り除きます。
    /// </summary>
    /// <param name="track">削除する楽曲</param>
    public void RemoveDisplayedTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        RunOnUiThread(() =>
        {
            var displayedTrack = PlaylistTracks.FirstOrDefault(item => PathsEqual(item.FilePath, track.FilePath));
            if (displayedTrack != null)
            {
                PlaylistTracks.Remove(displayedTrack);
            }
        });
    }

    /// <summary>
    /// プレイリストのトラックを非同期で読み込みます。後から開始した選択を古い結果で上書きしません。
    /// </summary>
    /// <param name="playlist">読み込み対象のプレイリスト</param>
    /// <returns>トラック読み込み処理</returns>
    public async Task LoadPlaylistTracksAsync(UserPlaylist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _trackLoadCancellation, cancellation);
        previousCancellation?.Cancel();
        long loadVersion = Interlocked.Increment(ref _trackLoadVersion);

        try
        {
            var tracks = await ResolveTracksAsync(playlist, cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                loadVersion != Volatile.Read(ref _trackLoadVersion) ||
                !ReferenceEquals(SelectedPlaylist, playlist))
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (!cancellation.IsCancellationRequested &&
                    loadVersion == Volatile.Read(ref _trackLoadVersion) &&
                    ReferenceEquals(SelectedPlaylist, playlist))
                {
                    ReplaceDisplayedTracks(tracks);
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 新しい選択が開始されたため、古い読み込み結果を破棄します。
        }
        finally
        {
            Interlocked.CompareExchange(ref _trackLoadCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// プレイリスト更新イベントを処理します。
    /// </summary>
    /// <param name="domainEvent">更新イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新処理</returns>
    public Task HandleAsync(PlaylistUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (SelectedPlaylist?.Id == domainEvent.Playlist.Id)
        {
            return SelectPlaylistAsync(domainEvent.Playlist);
        }

        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<Track>> ResolveTracksAsync(UserPlaylist playlist, CancellationToken cancellationToken)
    {
        if (playlist.TrackPaths.Count == 0)
        {
            return await _playlistService.GetPlaylistTracksAsync(playlist.Id, cancellationToken);
        }

        var tracks = new List<Track>(playlist.TrackPaths.Count);
        foreach (string path in playlist.TrackPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = await _libraryService.GetTrackByPathAsync(path, cancellationToken);
            if (track != null)
            {
                tracks.Add(track);
            }
        }

        return tracks;
    }

    private void CreatePlaylist(object? parameter)
    {
        string? name = parameter as string;
        if (string.IsNullOrWhiteSpace(name))
        {
            var inputBox = new InputBox("Enter playlist name:");
            SetDialogOwner(inputBox);
            if (inputBox.ShowDialog() != true)
            {
                return;
            }

            name = inputBox.InputText;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var playlist = new UserPlaylist { Name = name.Trim() };
        UserPlaylists.Add(playlist);
        SavePlaylists();
    }

    private void DeletePlaylist(object? parameter)
    {
        var playlist = parameter as UserPlaylist ?? SelectedPlaylist;
        if (playlist == null ||
            MessageBox.Show($"Are you sure you want to delete playlist '{playlist.Name}'?", "Delete Playlist", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        UserPlaylists.Remove(playlist);
        SavePlaylists();
        if (ReferenceEquals(SelectedPlaylist, playlist))
        {
            _ = SelectPlaylistAsync(null);
            ViewRequested?.Invoke(ViewType.Playlists);
        }
    }

    private void RenamePlaylist(object? parameter)
    {
        var playlist = parameter as UserPlaylist ?? SelectedPlaylist;
        if (playlist == null)
        {
            return;
        }

        var inputBox = new InputBox("新しい名前を入力してください:", playlist.Name);
        SetDialogOwner(inputBox);
        if (inputBox.ShowDialog() != true || string.IsNullOrWhiteSpace(inputBox.InputText))
        {
            return;
        }

        playlist.Name = inputBox.InputText.Trim();
        int index = UserPlaylists.IndexOf(playlist);
        if (index >= 0)
        {
            UserPlaylists[index] = playlist;
        }

        if (ReferenceEquals(SelectedPlaylist, playlist))
        {
            CurrentPlaylistName = playlist.Name;
        }

        SavePlaylists();
    }

    private async Task PlayPlaylistAsync(UserPlaylist? requestedPlaylist, bool shuffle)
    {
        var playlist = requestedPlaylist ?? SelectedPlaylist;
        if (playlist == null)
        {
            return;
        }

        IReadOnlyList<Track> resolvedTracks = ReferenceEquals(SelectedPlaylist, playlist) && PlaylistTracks.Count > 0
            ? PlaylistTracks.ToList()
            : await ResolveTracksAsync(playlist, CancellationToken.None);
        var tracks = shuffle ? resolvedTracks.OrderBy(_ => Guid.NewGuid()).ToList() : resolvedTracks.ToList();
        if (tracks.Count == 0)
        {
            return;
        }

        _audioService.SetPlaylist(tracks);
        PlaybackRequested?.Invoke(tracks, playlist.Name, shuffle ? "Playlist (Shuffled)" : "Playlist");
        _audioService.PlayTrack(tracks[0]);
    }

    private async Task RemoveTrackAsync(Track? track)
    {
        if (track == null)
        {
            return;
        }

        if (IsFavoritesView)
        {
            FavoriteRemovalRequested?.Invoke(track);
            return;
        }

        var playlist = SelectedPlaylist;
        if (playlist == null ||
            MessageBox.Show($"Remove '{track.Title}' from playlist?", "Remove Song", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        int index = PlaylistTracks.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        PlaylistTracks.RemoveAt(index);
        playlist.TrackPaths = PlaylistTracks.Select(item => item.FilePath).ToList();
        UpdatePlaylistThumbnails(playlist);
        SavePlaylists();
        await Task.CompletedTask;
    }

    private void AddSelectedTracksToPlaylist(object? parameter)
    {
        IEnumerable<Track> tracks = parameter switch
        {
            Track track => [track],
            Album album => album.Tracks,
            IEnumerable<Track> trackList => trackList,
            _ => PlaylistTracks.Where(track => track.IsSelected)
        };

        var selectedTracks = tracks.Distinct().ToList();
        if (selectedTracks.Count == 0)
        {
            MessageBox.Show("No tracks selected.", "Add to Playlist");
            return;
        }

        ShowAddTracksDialog(selectedTracks);
    }

    private void ShowAddTracksDialog(IEnumerable<Track> requestedTracks)
    {
        var tracks = requestedTracks.Distinct().ToList();
        if (tracks.Count == 0)
        {
            return;
        }

        var dialog = new PlaylistSelectionDialog(UserPlaylists, tracks[0]);
        SetDialogOwner(dialog);
        if (dialog.ShowDialog() != true || dialog.SelectedPlaylist == null)
        {
            return;
        }

        int addedCount = AddTracks(dialog.SelectedPlaylist, tracks);
        if (tracks.Count == 1)
        {
            string message = addedCount == 1
                ? $"Added '{tracks[0].Title}' to '{dialog.SelectedPlaylist.Name}'"
                : $"'{tracks[0].Title}' is already in '{dialog.SelectedPlaylist.Name}'";
            MessageBox.Show(message, addedCount == 1 ? "Track Added" : "Already Added");
        }
        else
        {
            MessageBox.Show(
                addedCount > 0
                    ? $"Added {addedCount} tracks to '{dialog.SelectedPlaylist.Name}'"
                    : "All selected tracks are already in the playlist.",
                addedCount > 0 ? "Tracks Added" : "No Tracks Added");
        }

        foreach (var track in tracks)
        {
            track.IsSelected = false;
        }
    }

    private int AddTracks(UserPlaylist playlist, IEnumerable<Track> requestedTracks)
    {
        int addedCount = 0;
        foreach (var track in requestedTracks)
        {
            if (playlist.TrackPaths.Any(path => PathsEqual(path, track.FilePath)))
            {
                continue;
            }

            playlist.TrackPaths.Add(track.FilePath);
            addedCount++;
            if (ReferenceEquals(SelectedPlaylist, playlist) && !IsFavoritesView)
            {
                PlaylistTracks.Add(track);
            }
        }

        if (addedCount > 0)
        {
            UpdatePlaylistThumbnails(playlist);
            SavePlaylists();
        }

        return addedCount;
    }

    private void SavePlaylists() => _playlistService.SavePlaylists(UserPlaylists.ToList());

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private void UpdatePlaylistThumbnails(UserPlaylist playlist)
    {
        var thumbnailPaths = playlist.TrackPaths
            .Select(path => new { Path = path, Directory = SafeGetDirectoryName(path) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Directory))
            .GroupBy(item => item.Directory!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Path)
            .Take(4)
            .ToList();

        RunOnUiThread(() =>
        {
            playlist.ThumbnailTrackPaths.Clear();
            foreach (string path in thumbnailPaths)
            {
                playlist.ThumbnailTrackPaths.Add(path);
            }
        });
    }

    private static string? SafeGetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch
        {
            return null;
        }
    }

    private void ReplaceDisplayedTracks(IEnumerable<Track> tracks)
    {
        PlaylistTracks.Clear();
        foreach (var track in tracks)
        {
            PlaylistTracks.Add(track);
        }
    }

    private void CancelPendingTrackLoad()
    {
        Interlocked.Increment(ref _trackLoadVersion);
        var cancellation = Interlocked.Exchange(ref _trackLoadCancellation, null);
        cancellation?.Cancel();
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static void SetDialogOwner(Window dialog)
    {
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
    }

    /// <summary>
    /// イベント購読と進行中の読み込みを解放します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CancelPendingTrackLoad();
        _eventBus.Unsubscribe<PlaylistUpdatedEvent>(HandleAsync);
        _disposed = true;
    }
}
