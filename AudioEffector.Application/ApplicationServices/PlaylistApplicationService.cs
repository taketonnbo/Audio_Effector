using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
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
    private readonly IFavoriteRepository? _favoriteRepository;
    private bool _isLastLoadFailed;

    /// <summary>
    /// PlaylistApplicationServiceを初期化します
    /// </summary>
    /// <param name="playlistRepository">プレイリストリポジトリ</param>
    /// <param name="trackRepository">トラックリポジトリ</param>
    /// <param name="eventBus">イベントバス</param>
    /// <param name="favoriteRepository">お気に入りリポジトリ（収録トラック詳細取得時にお気に入り状態を最新化する場合に指定）</param>
    public PlaylistApplicationService(
        IPlaylistRepository playlistRepository,
        ITrackRepository trackRepository,
        IEventBus eventBus,
        IFavoriteRepository? favoriteRepository = null)
    {
        _playlistRepository = playlistRepository ?? throw new ArgumentNullException(nameof(playlistRepository));
        _trackRepository = trackRepository ?? throw new ArgumentNullException(nameof(trackRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _favoriteRepository = favoriteRepository;
    }

    /// <summary>
    /// プレイリスト一覧をリポジトリから同期的に読み込み、成否を明示的に返します。
    /// 読み込みに失敗した場合、再読み込みが成功するまで <see cref="SavePlaylists"/> による全件置換を保護します。
    /// </summary>
    /// <returns>読み込み結果（成功時はプレイリスト一覧、失敗時はエラーメッセージ）。</returns>
    public Result<List<UserPlaylist>> TryLoadPlaylists()
    {
        try
        {
            IReadOnlyList<UserPlaylist> playlists = _playlistRepository.GetAllAsync().GetAwaiter().GetResult()
                ?? Array.Empty<UserPlaylist>();
            _isLastLoadFailed = false;
            return Result.Success(playlists.ToList());
        }
        catch (Exception ex)
        {
            _isLastLoadFailed = true;
            return Result.Failure<List<UserPlaylist>>($"プレイリストの読み込みに失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// プレイリスト一覧をリポジトリから同期的に読み込みます
    /// </summary>
    /// <returns>プレイリスト一覧（読み込み失敗時は空リスト）</returns>
    public List<UserPlaylist> LoadPlaylists()
    {
        Result<List<UserPlaylist>> result = TryLoadPlaylists();
        return result.IsSuccess && result.Value is not null
            ? result.Value
            : new List<UserPlaylist>();
    }

    /// <summary>
    /// プレイリスト一覧をリポジトリへ同期的に一括保存し、成否を返します。
    /// 直近の読み込みが失敗している場合は、既存プレイリスト消失を防ぐため全件置換を実行せず失敗を返します。
    /// </summary>
    /// <param name="playlists">保存するプレイリスト一覧</param>
    /// <returns>保存結果。</returns>
    public Result TrySavePlaylists(List<UserPlaylist> playlists)
    {
        if (playlists == null)
        {
            return Result.Failure("保存対象のプレイリスト一覧が指定されていません。");
        }

        if (_isLastLoadFailed)
        {
            return Result.Failure("直近のプレイリスト読み込みに失敗しているため、既存データ保護のため全件保存を中止しました。再読み込み後に再試行してください。");
        }

        try
        {
            _playlistRepository.ReplaceAllAsync(playlists).GetAwaiter().GetResult();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"プレイリストの保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// プレイリスト一覧をリポジトリへ同期的に一括保存します
    /// </summary>
    /// <param name="playlists">保存するプレイリスト一覧</param>
    public void SavePlaylists(List<UserPlaylist> playlists)
    {
        _ = TrySavePlaylists(playlists);
    }

    /// <summary>
    /// 単一のプレイリストをリポジトリへ同期的に保存し、成否を返します。
    /// ディスク上の最新状態へ対象 ID のみマージするため、他プレイリストを消失させません。
    /// </summary>
    /// <param name="playlist">保存対象のプレイリスト。</param>
    /// <returns>保存結果。</returns>
    public Result TrySavePlaylist(UserPlaylist playlist)
    {
        if (playlist == null)
        {
            return Result.Failure("保存対象のプレイリストが指定されていません。");
        }

        try
        {
            _playlistRepository.SaveAsync(playlist).GetAwaiter().GetResult();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"プレイリストの保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定された ID のプレイリストをリポジトリから同期的に削除し、成否を返します。
    /// </summary>
    /// <param name="playlistId">削除対象のプレイリスト ID。</param>
    /// <returns>削除結果。</returns>
    public Result TryDeletePlaylist(PlaylistId playlistId)
    {
        try
        {
            _playlistRepository.DeleteAsync(playlistId).GetAwaiter().GetResult();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"プレイリストの削除に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// プレイリストの既存収録曲（<see cref="UserPlaylist.TrackIds"/> および <see cref="UserPlaylist.TrackPaths"/>）の順序と未解決参照を保持したまま、
    /// 指定された楽曲を重複なく末尾へ追加します。お気に入りリポジトリにはアクセスせず、読み取りエラー時は <see cref="Result{T}"/> で失敗を返します。
    /// </summary>
    /// <param name="playlist">編集対象のプレイリスト。</param>
    /// <param name="tracksToAdd">追加する楽曲一覧。</param>
    /// <returns>成功時は実際に追加された楽曲一覧、失敗時はエラーメッセージを含む結果。</returns>
    public Result<IReadOnlyList<Track>> TryAddTracksPreservingOrder(UserPlaylist playlist, IEnumerable<Track> tracksToAdd)
    {
        if (playlist == null)
        {
            return Result.Failure<IReadOnlyList<Track>>("編集対象のプレイリストが指定されていません。");
        }

        if (tracksToAdd == null)
        {
            return Result.Failure<IReadOnlyList<Track>>("追加対象の楽曲一覧が指定されていません。");
        }

        try
        {
            IReadOnlyList<Track> added = AddTracksPreservingOrder(playlist, tracksToAdd);
            return Result.Success(added);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Track>>($"プレイリストへの楽曲追加準備に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// プレイリストの既存収録曲（<see cref="UserPlaylist.TrackIds"/> および <see cref="UserPlaylist.TrackPaths"/>）の順序と未解決参照を保持したまま、
    /// 指定された楽曲を重複なく末尾へ追加します。編集時の参照解決はお気に入り照合から独立して実行されます。
    /// </summary>
    /// <param name="playlist">編集対象のプレイリスト。</param>
    /// <param name="tracksToAdd">追加する楽曲一覧。</param>
    /// <returns>実際に追加された楽曲の読み取り専用リスト。</returns>
    public IReadOnlyList<Track> AddTracksPreservingOrder(UserPlaylist playlist, IEnumerable<Track> tracksToAdd)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(tracksToAdd);

        List<Track> candidatesToAdd = tracksToAdd.Where(t => t != null).ToList();
        if (candidatesToAdd.Count == 0)
        {
            return Array.Empty<Track>();
        }

        bool hasExistingIds = playlist.TrackIds.Count > 0;
        bool hasExistingPaths = playlist.TrackPaths is { Count: > 0 };

        // 既存要素がある場合のみ、お気に入り照合を行わずにトラック一覧から ID/パス対応を引く
        Dictionary<TrackId, Track>? tracksById = null;
        Dictionary<string, Track>? tracksByPath = null;
        if (hasExistingIds || hasExistingPaths)
        {
            IReadOnlyList<Track> allTracks = _trackRepository.GetAllAsync().GetAwaiter().GetResult()
                ?? Array.Empty<Track>();
            tracksById = new Dictionary<TrackId, Track>(allTracks.Count);
            tracksByPath = new Dictionary<string, Track>(allTracks.Count, StringComparer.OrdinalIgnoreCase);

            foreach (Track track in allTracks)
            {
                tracksById.TryAdd(track.Id, track);
                if (!string.IsNullOrWhiteSpace(track.FilePath))
                {
                    tracksByPath.TryAdd(track.FilePath, track);
                }
            }
        }

        var mergedIds = new List<TrackId>();
        var mergedPaths = new List<string>();
        var existingIdSet = new HashSet<TrackId>();
        var existingPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool keepPathAsSourceOfTruth = false;

        if (hasExistingIds)
        {
            bool pathsAligned = hasExistingPaths && playlist.TrackPaths.Count == playlist.TrackIds.Count;
            for (int i = 0; i < playlist.TrackIds.Count; i++)
            {
                TrackId existingId = playlist.TrackIds[i];
                mergedIds.Add(existingId);
                existingIdSet.Add(existingId);

                if (tracksById != null && tracksById.TryGetValue(existingId, out Track? resolved)
                    && !string.IsNullOrWhiteSpace(resolved.FilePath))
                {
                    mergedPaths.Add(resolved.FilePath);
                    existingPathSet.Add(resolved.FilePath);
                }
                else if (pathsAligned && !string.IsNullOrWhiteSpace(playlist.TrackPaths[i]))
                {
                    mergedPaths.Add(playlist.TrackPaths[i]);
                    existingPathSet.Add(playlist.TrackPaths[i]);
                }
            }
        }
        else if (hasExistingPaths)
        {
            // 旧 WPF 形式（TrackPaths のみ）：全件の ID を確定できない場合はパス列を正本として未解決参照と収録順を保持する
            var resolvedIdsForAllPaths = new List<TrackId>(playlist.TrackPaths.Count);
            bool allPathsResolved = true;

            foreach (string rawPath in playlist.TrackPaths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                mergedPaths.Add(rawPath);
                existingPathSet.Add(rawPath);

                if (tracksByPath != null && tracksByPath.TryGetValue(rawPath, out Track? resolved))
                {
                    resolvedIdsForAllPaths.Add(resolved.Id);
                    existingIdSet.Add(resolved.Id);
                }
                else
                {
                    allPathsResolved = false;
                }
            }

            if (allPathsResolved)
            {
                mergedIds.AddRange(resolvedIdsForAllPaths);
            }
            else
            {
                keepPathAsSourceOfTruth = true;
            }
        }

        var addedTracks = new List<Track>();
        foreach (Track track in candidatesToAdd)
        {
            bool duplicateById = existingIdSet.Contains(track.Id);
            bool duplicateByPath = !string.IsNullOrWhiteSpace(track.FilePath) && existingPathSet.Contains(track.FilePath);
            if (duplicateById || duplicateByPath)
            {
                continue;
            }

            if (!keepPathAsSourceOfTruth)
            {
                mergedIds.Add(track.Id);
            }

            existingIdSet.Add(track.Id);
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                mergedPaths.Add(track.FilePath);
                existingPathSet.Add(track.FilePath);
            }

            addedTracks.Add(track);
        }

        if (addedTracks.Count > 0)
        {
            playlist.ReplaceTracks(mergedIds, mergedPaths);
        }

        return addedTracks;
    }

    /// <summary>
    /// 指定された楽曲一覧からプレイリストの <see cref="UserPlaylist.TrackIds"/> と <see cref="UserPlaylist.TrackPaths"/> を順序通りに同期更新します。
    /// </summary>
    /// <param name="playlist">更新対象のプレイリスト。</param>
    /// <param name="tracks">確定済みの収録楽曲一覧。</param>
    public static void SyncPlaylistTracks(UserPlaylist playlist, IEnumerable<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(tracks);

        var trackList = tracks.Where(t => t != null).ToList();
        playlist.ReplaceTracks(
            trackList.Select(t => t.Id),
            trackList.Where(t => !string.IsNullOrWhiteSpace(t.FilePath)).Select(t => t.FilePath));
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

        // お気に入り永続化データ（favorites.json）を正本として全曲で読み取り専用照合を行い、収録トラックの IsFavorite を最新化する
        if (_favoriteRepository is not null && allTracks.Count > 0)
        {
            IReadOnlyList<FavoriteTrackEntry> favoriteEntries = await _favoriteRepository
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? Array.Empty<FavoriteTrackEntry>();
            FavoriteTrackMatcher.Reconcile(favoriteEntries, allTracks);
        }

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
