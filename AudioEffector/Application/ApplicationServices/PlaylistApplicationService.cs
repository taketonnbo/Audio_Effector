using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// プレイリストの作成、削除、楽曲の追加・削除・並び替えを統括するアプリケーションサービス
/// </summary>
public class PlaylistApplicationService
{
    private readonly IPlaylistRepository _playlistRepository;
    private readonly ITrackRepository _trackRepository;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// PlaylistApplicationServiceを初期化します
    /// </summary>
    /// <param name="playlistRepository">プレイリストリポジトリ</param>
    /// <param name="trackRepository">トラックリポジトリ</param>
    /// <param name="eventBus">イベントバス</param>
    public PlaylistApplicationService(
        IPlaylistRepository playlistRepository,
        ITrackRepository trackRepository,
        IEventBus eventBus)
    {
        _playlistRepository = playlistRepository ?? throw new ArgumentNullException(nameof(playlistRepository));
        _trackRepository = trackRepository ?? throw new ArgumentNullException(nameof(trackRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// すべてのプレイリストを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プレイリストのリスト</returns>
    public async Task<IReadOnlyList<UserPlaylist>> GetAllPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        return await _playlistRepository.GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// 新しいプレイリストを作成します
    /// </summary>
    /// <param name="name">プレイリスト名</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>作成されたプレイリスト</returns>
    public async Task<UserPlaylist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        var playlist = new UserPlaylist(PlaylistId.New(), name);
        await _playlistRepository.SaveAsync(playlist, cancellationToken);
        await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken);
        return playlist;
    }

    /// <summary>
    /// 指定されたIDのプレイリストを削除します
    /// </summary>
    /// <param name="id">プレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task DeletePlaylistAsync(PlaylistId id, CancellationToken cancellationToken = default)
    {
        var playlist = await _playlistRepository.GetByIdAsync(id, cancellationToken);
        await _playlistRepository.DeleteAsync(id, cancellationToken);
        if (playlist != null)
        {
            await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken);
        }
    }

    /// <summary>
    /// プレイリストにトラックを追加します
    /// </summary>
    /// <param name="playlistId">プレイリストID</param>
    /// <param name="trackId">追加するトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>追加後のプレイリスト（存在しない場合はnull）</returns>
    public async Task<UserPlaylist?> AddTrackAsync(PlaylistId playlistId, TrackId trackId, CancellationToken cancellationToken = default)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId, cancellationToken);
        if (playlist == null) return null;

        playlist.AddTrack(trackId);
        await _playlistRepository.SaveAsync(playlist, cancellationToken);
        await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken);
        return playlist;
    }

    /// <summary>
    /// プレイリストから指定インデックスのトラックを削除します
    /// </summary>
    /// <param name="playlistId">プレイリストID</param>
    /// <param name="index">削除するトラックのインデックス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除後のプレイリスト（存在しない場合はnull）</returns>
    public async Task<UserPlaylist?> RemoveTrackAtAsync(PlaylistId playlistId, int index, CancellationToken cancellationToken = default)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId, cancellationToken);
        if (playlist == null) return null;

        if (playlist.RemoveAt(index))
        {
            await _playlistRepository.SaveAsync(playlist, cancellationToken);
            await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken);
        }

        return playlist;
    }

    /// <summary>
    /// プレイリスト内のトラック順序を並び替えます
    /// </summary>
    /// <param name="playlistId">プレイリストID</param>
    /// <param name="oldIndex">移動元インデックス</param>
    /// <param name="newIndex">移動先インデックス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>並び替え後のプレイリスト（存在しない場合はnull）</returns>
    public async Task<UserPlaylist?> ReorderTrackAsync(
        PlaylistId playlistId,
        int oldIndex,
        int newIndex,
        CancellationToken cancellationToken = default)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId, cancellationToken);
        if (playlist == null) return null;

        playlist.Reorder(oldIndex, newIndex);
        await _playlistRepository.SaveAsync(playlist, cancellationToken);
        await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken);
        return playlist;
    }

    /// <summary>
    /// プレイリストに属するトラックエンティティのリストを順序通りに取得します
    /// </summary>
    /// <param name="playlistId">プレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トラックエンティティのリスト</returns>
    public async Task<IReadOnlyList<Track>> GetPlaylistTracksAsync(PlaylistId playlistId, CancellationToken cancellationToken = default)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId, cancellationToken);
        if (playlist == null) return Array.Empty<Track>();

        var result = new List<Track>();
        foreach (var trackId in playlist.TrackIds)
        {
            var track = await _trackRepository.GetByIdAsync(trackId, cancellationToken);
            if (track != null)
            {
                result.Add(track);
            }
        }

        return result;
    }
}
