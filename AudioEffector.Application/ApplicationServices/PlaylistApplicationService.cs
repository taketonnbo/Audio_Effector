using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly IPlaylistRepository _playlistRepository;
    private readonly ITrackRepository _trackRepository;
    private readonly IEventBus _eventBus;
    private readonly string _playlistsFilePath;

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

        var appDataPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioEffector");
        System.IO.Directory.CreateDirectory(appDataPath);
        _playlistsFilePath = System.IO.Path.Combine(appDataPath, "playlists.json");
    }

    /// <summary>
    /// プレイリスト一覧をローカルJSONファイルから読み込みます
    /// </summary>
    /// <returns></returns>
    public List<UserPlaylist> LoadPlaylists()
    {
        if (!System.IO.File.Exists(_playlistsFilePath))
            return new List<UserPlaylist>();

        try
        {
            var json = System.IO.File.ReadAllText(_playlistsFilePath);
            return System.Text.Json.JsonSerializer.Deserialize<List<UserPlaylist>>(json) ?? new List<UserPlaylist>();
        }
        catch
        {
            return new List<UserPlaylist>();
        }
    }

    /// <summary>
    /// プレイリスト一覧をローカルJSONファイルへ保存します
    /// </summary>
    /// <param name="playlists">保存するプレイリスト一覧</param>
    public void SavePlaylists(List<UserPlaylist> playlists)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(playlists, _jsonOptions);
            System.IO.File.WriteAllText(_playlistsFilePath, json);
        }
        catch
        {
            // Silent fail
        }
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
        var playlist = await _playlistRepository.UpdateAsync(playlistId, current =>
        {
            current.AddTrack(trackId);
            return true;
        }, cancellationToken);
        if (playlist == null) return null;

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
        bool removed = false;
        var playlist = await _playlistRepository.UpdateAsync(playlistId, current => removed = current.RemoveAt(index), cancellationToken);
        if (removed && playlist != null)
        {
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
        var playlist = await _playlistRepository.UpdateAsync(playlistId, current =>
        {
            current.Reorder(oldIndex, newIndex);
            return true;
        }, cancellationToken);
        if (playlist == null) return null;

        await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken);
        return playlist;
    }

    /// <summary>
    /// キーワードおよびページネーションを指定してプレイリストを検索します。
    /// </summary>
    /// <param name="query">検索条件およびページネーション指定。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>一致総件数およびページ適用後のプレイリスト一覧を含む検索結果。</returns>
    public async Task<PlaylistSearchResult> SearchPlaylistsAsync(
        PlaylistSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit < PlaylistSearchQuery.MIN_LIMIT || query.Limit > PlaylistSearchQuery.MAX_LIMIT)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Limit,
                $"limit は {PlaylistSearchQuery.MIN_LIMIT}〜{PlaylistSearchQuery.MAX_LIMIT} の範囲で指定してください。");
        }

        if (query.Offset < PlaylistSearchQuery.DEFAULT_OFFSET)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Offset,
                $"offset は {PlaylistSearchQuery.DEFAULT_OFFSET} 以上で指定してください。");
        }

        string? trimmedKeyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();

        IReadOnlyList<UserPlaylist> allPlaylists = await _playlistRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? Array.Empty<UserPlaylist>();

        if (allPlaylists.Count == 0)
        {
            return new PlaylistSearchResult(Array.Empty<UserPlaylist>(), 0, query.Offset, query.Limit);
        }

        IEnumerable<UserPlaylist> filtered = allPlaylists;

        if (trimmedKeyword is not null)
        {
            filtered = filtered.Where(p => p.Name.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id.Value)
            .ToList();

        int totalCount = ordered.Count;
        var paged = ordered
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToList();

        return new PlaylistSearchResult(paged, totalCount, query.Offset, query.Limit);
    }

    /// <summary>
    /// プレイリストに属するトラックエンティティのリストを順序通りに取得します
    /// </summary>
    /// <param name="playlistId">プレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トラックエンティティのリスト</returns>
    public async Task<IReadOnlyList<Track>> GetPlaylistTracksAsync(PlaylistId playlistId, CancellationToken cancellationToken = default)
    {
        var playlist = await _playlistRepository.GetByIdAsync(playlistId, cancellationToken).ConfigureAwait(false);
        if (playlist == null) return Array.Empty<Track>();

        return await ResolvePlaylistTracksAsync(playlist, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定されたプレイリストの <see cref="UserPlaylist.TrackIds"/>（未設定時は旧互換 <see cref="UserPlaylist.TrackPaths"/>）から収録トラック詳細を順序通りに解決します。
    /// </summary>
    /// <param name="playlist">対象のプレイリスト。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>解決されたトラックエンティティの読み取り専用リスト。</returns>
    public async Task<IReadOnlyList<Track>> ResolvePlaylistTracksAsync(
        UserPlaylist playlist,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        IReadOnlyDictionary<PlaylistId, IReadOnlyList<Track>> map = await ResolvePlaylistTracksMapAsync(
            [playlist],
            cancellationToken).ConfigureAwait(false);

        return map.TryGetValue(playlist.Id, out IReadOnlyList<Track>? tracks)
            ? tracks
            : Array.Empty<Track>();
    }

    /// <summary>
    /// 複数のプレイリストに対して収録トラック詳細を一括解決し、プレイリスト ID ごとのマップを返します。
    /// </summary>
    /// <param name="playlists">対象のプレイリスト一覧。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>プレイリスト ID をキー、解決されたトラック一覧を値とする読み取り専用辞書。</returns>
    public async Task<IReadOnlyDictionary<PlaylistId, IReadOnlyList<Track>>> ResolvePlaylistTracksMapAsync(
        IReadOnlyList<UserPlaylist> playlists,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlists);

        var result = new Dictionary<PlaylistId, IReadOnlyList<Track>>(playlists.Count);
        if (playlists.Count == 0)
        {
            return result;
        }

        IReadOnlyList<Track> allTracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false)
            ?? Array.Empty<Track>();

        var tracksById = new Dictionary<TrackId, Track>(allTracks.Count);
        var tracksByPath = new Dictionary<string, Track>(allTracks.Count, StringComparer.OrdinalIgnoreCase);

        foreach (Track track in allTracks)
        {
            tracksById.TryAdd(track.Id, track);
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                tracksByPath.TryAdd(track.FilePath, track);
            }
        }

        foreach (UserPlaylist playlist in playlists)
        {
            var resolved = new List<Track>();

            if (playlist.TrackIds.Count > 0)
            {
                foreach (TrackId trackId in playlist.TrackIds)
                {
                    if (tracksById.TryGetValue(trackId, out Track? track))
                    {
                        resolved.Add(track);
                    }
                }
            }
            else if (playlist.TrackPaths is { Count: > 0 })
            {
                foreach (string trackPath in playlist.TrackPaths)
                {
                    if (!string.IsNullOrWhiteSpace(trackPath) && tracksByPath.TryGetValue(trackPath, out Track? track))
                    {
                        resolved.Add(track);
                    }
                }
            }

            result[playlist.Id] = resolved;
        }

        return result;
    }
}
