using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// プレイリストの作成、一覧表示、楽曲の追加・削除・並び替えを担当するViewModel
/// </summary>
public class PlaylistViewModel : ViewModelBase, IHandle<PlaylistUpdatedEvent>
{
    private readonly PlaylistApplicationService _playlistService;
    private readonly AudioApplicationService _audioService;
    private readonly IEventBus _eventBus;

    private UserPlaylist? _selectedPlaylist;
    private Track? _selectedTrack;

    /// <summary>
    /// プレイリストコレクション
    /// </summary>
    public ObservableCollection<UserPlaylist> Playlists { get; } = new();

    /// <summary>
    /// 選択中のプレイリスト内の楽曲コレクション
    /// </summary>
    public ObservableCollection<Track> PlaylistTracks { get; } = new();

    /// <summary>
    /// 選択中のプレイリスト
    /// </summary>
    public UserPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (SetProperty(ref _selectedPlaylist, value) && value != null)
            {
                _ = LoadPlaylistTracksAsync(value);
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
    /// 楽曲削除コマンド
    /// </summary>
    public ICommand RemoveTrackCommand { get; }

    /// <summary>
    /// PlaylistViewModelを初期化します
    /// </summary>
    /// <param name="playlistService">プレイリストアプリケーションサービス</param>
    /// <param name="audioService">オーディオ再生アプリケーションサービス</param>
    /// <param name="eventBus">イベントバス</param>
    public PlaylistViewModel(
        PlaylistApplicationService playlistService,
        AudioApplicationService audioService,
        IEventBus eventBus)
    {
        _playlistService = playlistService ?? throw new ArgumentNullException(nameof(playlistService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        CreatePlaylistCommand = new RelayCommand(async name =>
        {
            if (name is string n && !string.IsNullOrWhiteSpace(n))
            {
                var created = await _playlistService.CreatePlaylistAsync(n);
                Playlists.Add(created);
                SelectedPlaylist = created;
            }
        });

        DeletePlaylistCommand = new RelayCommand(async playlist =>
        {
            if (playlist is UserPlaylist p)
            {
                await _playlistService.DeletePlaylistAsync(p.Id);
                Playlists.Remove(p);
                if (SelectedPlaylist == p)
                {
                    SelectedPlaylist = null;
                    PlaylistTracks.Clear();
                }
            }
        });

        PlayPlaylistCommand = new RelayCommand(async _ =>
        {
            if (PlaylistTracks.Count > 0)
            {
                await _audioService.SetQueueAndPlayAsync(PlaylistTracks, 0);
            }
        });

        RemoveTrackCommand = new RelayCommand(async track =>
        {
            if (SelectedPlaylist != null && track is Track t)
            {
                int index = PlaylistTracks.IndexOf(t);
                if (index >= 0)
                {
                    await _playlistService.RemoveTrackAtAsync(SelectedPlaylist.Id, index);
                    PlaylistTracks.RemoveAt(index);
                }
            }
        });

        _eventBus.Subscribe<PlaylistUpdatedEvent>(HandleAsync);
    }

    /// <summary>
    /// プレイリストの楽曲一覧を非同期で読み込みます
    /// </summary>
    /// <param name="playlist">対象プレイリスト</param>
    /// <returns>非同期タスク</returns>
    public async Task LoadPlaylistTracksAsync(UserPlaylist playlist)
    {
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
