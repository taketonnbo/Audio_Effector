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
    /// <summary>
    /// プレイリスト名の最大文字数。
    /// </summary>
    public const int MAX_PLAYLIST_NAME_LENGTH = 200;

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
            bool allIdsHavePath = true;
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
                else
                {
                    allIdsHavePath = false;
                }
            }

            // ID が正本で全曲分のパスを確定できない場合は、不完全なパス列を残さない（TrackIds を正本として維持する）
            if (!allIdsHavePath)
            {
                mergedPaths.Clear();
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
                existingPathSet.Add(track.FilePath);
                if (keepPathAsSourceOfTruth || !hasExistingIds || mergedPaths.Count == mergedIds.Count - 1)
                {
                    mergedPaths.Add(track.FilePath);
                }
            }

            addedTracks.Add(track);
        }

        if (addedTracks.Count > 0)
        {
            if (hasExistingIds && mergedPaths.Count != mergedIds.Count)
            {
                mergedPaths.Clear();
            }

            playlist.ReplaceTracks(mergedIds, mergedPaths);
        }

        return addedTracks;
    }

    /// <summary>
    /// プレイリストに保存されている未解決トラック参照を保持したまま、指定された楽曲に対応する参照のみを取り除きます。
    /// </summary>
    /// <param name="playlist">編集対象のプレイリスト。</param>
    /// <param name="trackToRemove">削除する楽曲。</param>
    /// <returns>参照が削除された場合は <see langword="true"/>、一致する参照がなかった場合は <see langword="false"/>。</returns>
    public static bool RemoveTrackPreservingUnresolved(UserPlaylist playlist, Track trackToRemove)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(trackToRemove);

        var updatedIds = playlist.TrackIds.ToList();
        var updatedPaths = playlist.TrackPaths?.ToList() ?? new List<string>();
        bool removed = false;

        if (updatedIds.Count > 0)
        {
            int idIndex = updatedIds.FindIndex(id => id == trackToRemove.Id);
            if (idIndex >= 0)
            {
                bool pathsAligned = updatedPaths.Count == updatedIds.Count;
                updatedIds.RemoveAt(idIndex);
                if (pathsAligned)
                {
                    updatedPaths.RemoveAt(idIndex);
                }
                else if (!string.IsNullOrWhiteSpace(trackToRemove.FilePath))
                {
                    int pathIndex = updatedPaths.FindIndex(p => string.Equals(p, trackToRemove.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (pathIndex >= 0)
                    {
                        updatedPaths.RemoveAt(pathIndex);
                    }
                }

                removed = true;
            }
        }
        else if (updatedPaths.Count > 0 && !string.IsNullOrWhiteSpace(trackToRemove.FilePath))
        {
            int pathIndex = updatedPaths.FindIndex(p => string.Equals(p, trackToRemove.FilePath, StringComparison.OrdinalIgnoreCase));
            if (pathIndex >= 0)
            {
                updatedPaths.RemoveAt(pathIndex);
                removed = true;
            }
        }

        if (removed)
        {
            if (updatedIds.Count > 0 && updatedPaths.Count != updatedIds.Count)
            {
                updatedPaths.Clear();
            }

            playlist.ReplaceTracks(updatedIds, updatedPaths);
        }

        return removed;
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
    public Task<UserPlaylist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        return CreatePlaylistAsync(name, initialTrackIds: null, cancellationToken);
    }

    /// <summary>
    /// 任意の初期トラック ID 一覧を含めて新しいプレイリストを作成・永続化します。
    /// </summary>
    /// <param name="name">プレイリスト名（前後空白はトリムされ、空文字および上限超過は <see cref="ArgumentException"/>）。</param>
    /// <param name="initialTrackIds">初期登録するトラック ID 一覧（省略または空の場合は空のプレイリストを作成）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成されたプレイリスト。</returns>
    public async Task<UserPlaylist> CreatePlaylistAsync(
        string name,
        IReadOnlyList<TrackId>? initialTrackIds,
        CancellationToken cancellationToken = default)
    {
        PlaylistCreationResult result = await CreatePlaylistWithTracksAsync(
            name,
            initialTrackIds,
            initialFilePaths: null,
            cancellationToken).ConfigureAwait(false);
        return result.Playlist;
    }

    /// <summary>
    /// 指定されたプレイリスト名および任意の初期トラック（トラック ID・ファイルパス）から新しいプレイリストを作成・永続化します。
    /// 指定されたトラックが 1 件でもライブラリに存在しない場合は <see cref="KeyNotFoundException"/> をスローし、不完全なプレイリストを保存しません。
    /// </summary>
    /// <param name="name">プレイリスト名（前後空白はトリムされます）。</param>
    /// <param name="initialTrackIds">初期登録するトラック ID 一覧（任意）。</param>
    /// <param name="initialFilePaths">初期登録する音声ファイル絶対パス一覧（任意）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>作成されたプレイリスト・解決済み収録トラック一覧・重複スキップ一覧を含む結果。</returns>
    public async Task<PlaylistCreationResult> CreatePlaylistWithTracksAsync(
        string name,
        IReadOnlyList<TrackId>? initialTrackIds = null,
        IReadOnlyList<string>? initialFilePaths = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = ValidateAndNormalizePlaylistName(name);

        bool hasTrackIds = initialTrackIds is { Count: > 0 };
        bool hasFilePaths = initialFilePaths is { Count: > 0 };

        var resolvedTracks = new List<Track>();
        var skippedItems = new List<string>();

        if (hasTrackIds || hasFilePaths)
        {
            IReadOnlyList<Track> allTracks = await _trackRepository
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? Array.Empty<Track>();

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

            var addedTrackIds = new HashSet<TrackId>();

            if (hasTrackIds)
            {
                foreach (TrackId trackId in initialTrackIds!)
                {
                    if (!tracksById.TryGetValue(trackId, out Track? track))
                    {
                        throw new KeyNotFoundException($"指定されたトラック ID '{trackId}' はライブラリに存在しません。");
                    }

                    if (!addedTrackIds.Add(track.Id))
                    {
                        skippedItems.Add(trackId.ToString());
                        continue;
                    }

                    resolvedTracks.Add(track);
                }
            }

            if (hasFilePaths)
            {
                foreach (string rawPath in initialFilePaths!)
                {
                    if (string.IsNullOrWhiteSpace(rawPath))
                    {
                        throw new ArgumentException("初期トラックのファイルパスに空の値は指定できません。", nameof(initialFilePaths));
                    }

                    string trimmedPath = rawPath.Trim();
                    if (!tracksByPath.TryGetValue(trimmedPath, out Track? track))
                    {
                        throw new KeyNotFoundException($"指定されたファイルパス '{trimmedPath}' に一致するトラックはライブラリに存在しません。");
                    }

                    if (!addedTrackIds.Add(track.Id))
                    {
                        skippedItems.Add(trimmedPath);
                        continue;
                    }

                    resolvedTracks.Add(track);
                }
            }
        }

        DateTime nowUtc = DateTime.UtcNow;
        var playlist = new UserPlaylist(
            id: PlaylistId.New(),
            name: normalizedName,
            trackIds: resolvedTracks.Select(t => t.Id),
            createdAt: nowUtc,
            updatedAt: nowUtc);

        if (resolvedTracks.Count > 0 && resolvedTracks.All(t => !string.IsNullOrWhiteSpace(t.FilePath)))
        {
            playlist.TrackPaths = resolvedTracks.Select(t => t.FilePath).ToList();
        }

        await _playlistRepository.SaveAsync(playlist, cancellationToken).ConfigureAwait(false);
        await _eventBus.PublishAsync(new PlaylistUpdatedEvent(playlist), cancellationToken).ConfigureAwait(false);

        return PlaylistCreationResult.Create(playlist, resolvedTracks, skippedItems);
    }

    private static string ValidateAndNormalizePlaylistName(string name, string paramName = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("プレイリスト名を指定してください。", paramName);
        }

        string trimmed = name.Trim();
        if (trimmed.Length > MAX_PLAYLIST_NAME_LENGTH)
        {
            throw new ArgumentException(
                $"プレイリスト名は {MAX_PLAYLIST_NAME_LENGTH} 文字以内で指定してください。",
                paramName);
        }

        return trimmed;
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
    /// 指定された ID のプレイリストを単一ロック区間内で削除・永続化し、削除前の最新プレイリスト情報を返します。
    /// 対象プレイリストが存在しない場合は削除処理およびイベント発行を行わず <see langword="null"/> を返します。
    /// </summary>
    /// <param name="id">削除対象のプレイリスト ID。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>削除されたプレイリストエンティティ（対象が存在しない場合は <see langword="null"/>）。</returns>
    public async Task<UserPlaylist?> DeletePlaylistWithResultAsync(PlaylistId id, CancellationToken cancellationToken = default)
    {
        UserPlaylist? deleted = await _playlistRepository.DeleteAndReturnAsync(id, cancellationToken).ConfigureAwait(false);
        if (deleted is null)
        {
            return null;
        }

        await _eventBus.PublishAsync(new PlaylistUpdatedEvent(deleted), cancellationToken).ConfigureAwait(false);
        return deleted;
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
    /// 指定されたプレイリストへ複数トラックを一括追加し、1 回の排他ロックで原子的に永続化します。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID。</param>
    /// <param name="trackIds">追加するトラック ID 一覧。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>更新結果（対象プレイリストが存在しない場合は <see langword="null"/>）。</returns>
    public Task<PlaylistUpdateResult?> AddTracksAsync(
        PlaylistId playlistId,
        IReadOnlyList<TrackId> trackIds,
        CancellationToken cancellationToken = default)
    {
        return AddTracksAsync(playlistId, trackIds, filePaths: null, cancellationToken);
    }

    /// <summary>
    /// 指定されたプレイリストへ複数トラック（トラック ID・ファイルパス）を一括追加し、1 回の排他ロックで原子的に永続化します。
    /// 指定されたトラックが 1 件でもライブラリに存在しない場合は <see cref="KeyNotFoundException"/> をスローし、プレイリストを変更しません。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID。</param>
    /// <param name="trackIds">追加するトラック ID 一覧（任意）。</param>
    /// <param name="filePaths">追加する音声ファイルの絶対パス一覧（任意）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>更新結果（対象プレイリストが存在しない場合は <see langword="null"/>）。</returns>
    public async Task<PlaylistUpdateResult?> AddTracksAsync(
        PlaylistId playlistId,
        IReadOnlyList<TrackId>? trackIds,
        IReadOnlyList<string>? filePaths,
        CancellationToken cancellationToken = default)
    {
        bool hasTrackIds = trackIds is { Count: > 0 };
        bool hasFilePaths = filePaths is { Count: > 0 };
        if (!hasTrackIds && !hasFilePaths)
        {
            throw new ArgumentException("追加する trackIds または filePaths を 1 件以上指定してください。", nameof(trackIds));
        }

        (Dictionary<TrackId, Track> tracksById, Dictionary<string, Track> tracksByPath) =
            await LoadTrackLookupsAsync(cancellationToken).ConfigureAwait(false);

        var candidates = new List<(Track Track, string RawIdentifier)>();
        if (hasTrackIds)
        {
            foreach (TrackId trackId in trackIds!)
            {
                if (!tracksById.TryGetValue(trackId, out Track? track))
                {
                    throw new KeyNotFoundException($"指定されたトラック ID '{trackId}' はライブラリに存在しません。");
                }

                candidates.Add((track, trackId.ToString()));
            }
        }

        if (hasFilePaths)
        {
            foreach (string rawPath in filePaths!)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    throw new ArgumentException("追加トラックのファイルパスに空の値は指定できません。", nameof(filePaths));
                }

                string trimmedPath = rawPath.Trim();
                if (!tracksByPath.TryGetValue(trimmedPath, out Track? track))
                {
                    throw new KeyNotFoundException($"指定されたファイルパス '{trimmedPath}' に一致するトラックはライブラリに存在しません。");
                }

                candidates.Add((track, trimmedPath));
            }
        }

        int addedCount = 0;
        var skippedItems = new List<string>();

        UserPlaylist? updatedPlaylist = await _playlistRepository.UpdateAsync(
            playlistId,
            current =>
            {
                addedCount = 0;
                skippedItems.Clear();

                bool hasExistingIds = current.TrackIds.Count > 0;
                bool hasExistingPaths = current.TrackPaths is { Count: > 0 };

                var mergedIds = new List<TrackId>();
                var mergedPaths = new List<string>();
                var existingIdSet = new HashSet<TrackId>();
                var existingPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool keepPathAsSourceOfTruth = false;

                if (hasExistingIds)
                {
                    bool pathsAligned = hasExistingPaths && current.TrackPaths.Count == current.TrackIds.Count;
                    bool allIdsHavePath = true;
                    for (int i = 0; i < current.TrackIds.Count; i++)
                    {
                        TrackId existingId = current.TrackIds[i];
                        mergedIds.Add(existingId);
                        existingIdSet.Add(existingId);

                        if (tracksById.TryGetValue(existingId, out Track? resolved)
                            && !string.IsNullOrWhiteSpace(resolved.FilePath))
                        {
                            mergedPaths.Add(resolved.FilePath);
                            existingPathSet.Add(resolved.FilePath);
                        }
                        else if (pathsAligned && !string.IsNullOrWhiteSpace(current.TrackPaths[i]))
                        {
                            mergedPaths.Add(current.TrackPaths[i]);
                            existingPathSet.Add(current.TrackPaths[i]);
                        }
                        else
                        {
                            allIdsHavePath = false;
                        }
                    }

                    if (!allIdsHavePath)
                    {
                        mergedPaths.Clear();
                    }
                }
                else if (hasExistingPaths)
                {
                    var resolvedIdsForAllPaths = new List<TrackId>(current.TrackPaths.Count);
                    bool allPathsResolved = true;

                    foreach (string rawPath in current.TrackPaths)
                    {
                        if (string.IsNullOrWhiteSpace(rawPath))
                        {
                            continue;
                        }

                        mergedPaths.Add(rawPath);
                        existingPathSet.Add(rawPath);

                        if (tracksByPath.TryGetValue(rawPath, out Track? resolved))
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

                foreach ((Track track, string rawIdentifier) in candidates)
                {
                    bool duplicateById = existingIdSet.Contains(track.Id);
                    bool duplicateByPath = !string.IsNullOrWhiteSpace(track.FilePath)
                        && existingPathSet.Contains(track.FilePath);
                    if (duplicateById || duplicateByPath)
                    {
                        skippedItems.Add(rawIdentifier);
                        continue;
                    }

                    if (!keepPathAsSourceOfTruth)
                    {
                        mergedIds.Add(track.Id);
                    }

                    existingIdSet.Add(track.Id);
                    if (!string.IsNullOrWhiteSpace(track.FilePath))
                    {
                        existingPathSet.Add(track.FilePath);
                        if (keepPathAsSourceOfTruth || !hasExistingIds || mergedPaths.Count == mergedIds.Count - 1)
                        {
                            mergedPaths.Add(track.FilePath);
                        }
                    }

                    addedCount++;
                }

                if (addedCount == 0)
                {
                    return false;
                }

                if (!keepPathAsSourceOfTruth && mergedPaths.Count != mergedIds.Count)
                {
                    mergedPaths.Clear();
                }

                current.ReplaceTracks(mergedIds, mergedPaths);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (updatedPlaylist is null)
        {
            return null;
        }

        if (addedCount > 0)
        {
            await _eventBus.PublishAsync(new PlaylistUpdatedEvent(updatedPlaylist), cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<Track> resolvedTracks = ResolveTracksFromLookups(updatedPlaylist, tracksById, tracksByPath);
        return PlaylistUpdateResult.Create(updatedPlaylist, resolvedTracks, addedCount, skippedItems);
    }

    /// <summary>
    /// 指定されたプレイリストから、インデックス配列またはトラック ID 配列に一致する楽曲を一括削除します。
    /// 複数インデックス同時指定時は降順処理によりインデックスずれを防止します。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID。</param>
    /// <param name="indices">削除する 0 始まりインデックス一覧（任意）。</param>
    /// <param name="trackIds">削除するトラック ID 一覧（任意）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>更新結果（対象プレイリストが存在しない場合は <see langword="null"/>）。</returns>
    public async Task<PlaylistUpdateResult?> RemoveTracksAsync(
        PlaylistId playlistId,
        IReadOnlyList<int>? indices,
        IReadOnlyList<TrackId>? trackIds,
        CancellationToken cancellationToken = default)
    {
        bool hasIndices = indices is { Count: > 0 };
        bool hasTrackIds = trackIds is { Count: > 0 };
        if (!hasIndices && !hasTrackIds)
        {
            throw new ArgumentException("削除する indices または trackIds を 1 件以上指定してください。", nameof(indices));
        }

        if (hasIndices)
        {
            foreach (int index in indices!)
            {
                if (index < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(indices), index, "indices には 0 以上のインデックスを指定してください。");
                }
            }
        }

        (Dictionary<TrackId, Track> tracksById, Dictionary<string, Track> tracksByPath) =
            await LoadTrackLookupsAsync(cancellationToken).ConfigureAwait(false);

        int removedCount = 0;
        var skippedItems = new List<string>();

        UserPlaylist? updatedPlaylist = await _playlistRepository.UpdateAsync(
            playlistId,
            current =>
            {
                removedCount = 0;
                skippedItems.Clear();

                bool hasIds = current.TrackIds.Count > 0;
                int currentCount = hasIds ? current.TrackIds.Count : (current.TrackPaths?.Count ?? 0);

                if (hasIndices)
                {
                    foreach (int index in indices!)
                    {
                        if (index < 0 || index >= currentCount)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(indices),
                                index,
                                $"インデックス {index} はプレイリストの収録範囲（0〜{Math.Max(0, currentCount - 1)}）外です。");
                        }
                    }
                }

                if (hasIds)
                {
                    var updatedIds = current.TrackIds.ToList();
                    var updatedPaths = current.TrackPaths?.ToList() ?? new List<string>();
                    bool pathsAligned = updatedPaths.Count == updatedIds.Count;

                    if (hasIndices)
                    {
                        foreach (int index in indices!.Distinct().OrderByDescending(i => i))
                        {
                            updatedIds.RemoveAt(index);
                            if (pathsAligned)
                            {
                                updatedPaths.RemoveAt(index);
                            }

                            removedCount++;
                        }
                    }

                    if (hasTrackIds)
                    {
                        foreach (TrackId targetId in trackIds!)
                        {
                            bool matchedAny = false;
                            for (int i = updatedIds.Count - 1; i >= 0; i--)
                            {
                                if (updatedIds[i] == targetId)
                                {
                                    updatedIds.RemoveAt(i);
                                    if (pathsAligned)
                                    {
                                        updatedPaths.RemoveAt(i);
                                    }

                                    removedCount++;
                                    matchedAny = true;
                                }
                            }

                            if (!matchedAny)
                            {
                                skippedItems.Add(targetId.ToString());
                            }
                        }
                    }

                    if (removedCount == 0)
                    {
                        return false;
                    }

                    if (updatedIds.Count == 0)
                    {
                        updatedPaths.Clear();
                    }
                    else if (!pathsAligned || updatedPaths.Count != updatedIds.Count)
                    {
                        updatedPaths.Clear();
                    }

                    current.ReplaceTracks(updatedIds, updatedPaths);
                    return true;
                }
                else
                {
                    var updatedPaths = current.TrackPaths?.ToList() ?? new List<string>();

                    if (hasIndices)
                    {
                        foreach (int index in indices!.Distinct().OrderByDescending(i => i))
                        {
                            updatedPaths.RemoveAt(index);
                            removedCount++;
                        }
                    }

                    if (hasTrackIds)
                    {
                        foreach (TrackId targetId in trackIds!)
                        {
                            if (!tracksById.TryGetValue(targetId, out Track? resolved)
                                || string.IsNullOrWhiteSpace(resolved.FilePath))
                            {
                                skippedItems.Add(targetId.ToString());
                                continue;
                            }

                            bool matchedAny = false;
                            for (int i = updatedPaths.Count - 1; i >= 0; i--)
                            {
                                if (string.Equals(updatedPaths[i], resolved.FilePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    updatedPaths.RemoveAt(i);
                                    removedCount++;
                                    matchedAny = true;
                                }
                            }

                            if (!matchedAny)
                            {
                                skippedItems.Add(targetId.ToString());
                            }
                        }
                    }

                    if (removedCount == 0)
                    {
                        return false;
                    }

                    current.ReplaceTracks(Array.Empty<TrackId>(), updatedPaths);
                    return true;
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (updatedPlaylist is null)
        {
            return null;
        }

        if (removedCount > 0)
        {
            await _eventBus.PublishAsync(new PlaylistUpdatedEvent(updatedPlaylist), cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<Track> resolvedTracks = ResolveTracksFromLookups(updatedPlaylist, tracksById, tracksByPath);
        return PlaylistUpdateResult.Create(updatedPlaylist, resolvedTracks, removedCount, skippedItems);
    }

    /// <summary>
    /// プレイリスト内のトラック順序を並び替え、<see cref="UserPlaylist.TrackIds"/> と <see cref="UserPlaylist.TrackPaths"/> の整合性を維持して永続化します。
    /// 範囲外インデックスが指定された場合は <see cref="ArgumentOutOfRangeException"/> をスローします。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID。</param>
    /// <param name="fromIndex">移動元インデックス（0 始まり）。</param>
    /// <param name="toIndex">移動先インデックス（0 始まり）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>更新結果（対象プレイリストが存在しない場合は <see langword="null"/>）。</returns>
    public async Task<PlaylistUpdateResult?> ReorderPlaylistTrackAsync(
        PlaylistId playlistId,
        int fromIndex,
        int toIndex,
        CancellationToken cancellationToken = default)
    {
        if (fromIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex), fromIndex, "fromIndex は 0 以上で指定してください。");
        }

        if (toIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toIndex), toIndex, "toIndex は 0 以上で指定してください。");
        }

        int movedCount = 0;

        UserPlaylist? updatedPlaylist = await _playlistRepository.UpdateAsync(
            playlistId,
            current =>
            {
                movedCount = 0;
                bool hasIds = current.TrackIds.Count > 0;
                int currentCount = hasIds ? current.TrackIds.Count : (current.TrackPaths?.Count ?? 0);

                if (fromIndex >= currentCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(fromIndex),
                        fromIndex,
                        $"fromIndex ({fromIndex}) はプレイリストの収録範囲外です。");
                }

                if (toIndex >= currentCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(toIndex),
                        toIndex,
                        $"toIndex ({toIndex}) はプレイリストの収録範囲外です。");
                }

                if (fromIndex == toIndex)
                {
                    return false;
                }

                if (hasIds)
                {
                    var updatedIds = current.TrackIds.ToList();
                    var updatedPaths = current.TrackPaths?.ToList() ?? new List<string>();
                    bool pathsAligned = updatedPaths.Count == updatedIds.Count;

                    TrackId movedId = updatedIds[fromIndex];
                    updatedIds.RemoveAt(fromIndex);
                    updatedIds.Insert(toIndex, movedId);

                    if (pathsAligned)
                    {
                        string movedPath = updatedPaths[fromIndex];
                        updatedPaths.RemoveAt(fromIndex);
                        updatedPaths.Insert(toIndex, movedPath);
                    }
                    else
                    {
                        updatedPaths.Clear();
                    }

                    current.ReplaceTracks(updatedIds, updatedPaths);
                }
                else
                {
                    var updatedPaths = current.TrackPaths!.ToList();
                    string movedPath = updatedPaths[fromIndex];
                    updatedPaths.RemoveAt(fromIndex);
                    updatedPaths.Insert(toIndex, movedPath);
                    current.ReplaceTracks(Array.Empty<TrackId>(), updatedPaths);
                }

                movedCount = 1;
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (updatedPlaylist is null)
        {
            return null;
        }

        if (movedCount > 0)
        {
            await _eventBus.PublishAsync(new PlaylistUpdatedEvent(updatedPlaylist), cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<Track> resolvedTracks = await ResolvePlaylistTracksAsync(updatedPlaylist, cancellationToken).ConfigureAwait(false);
        return PlaylistUpdateResult.Create(updatedPlaylist, resolvedTracks, movedCount);
    }

    /// <summary>
    /// 指定されたプレイリストの名称を変更して永続化し、<see cref="PlaylistUpdatedEvent"/> を発行します。
    /// </summary>
    /// <param name="playlistId">編集対象のプレイリスト ID。</param>
    /// <param name="newName">新しいプレイリスト名（前後空白はトリムされ、空文字および上限超過は <see cref="ArgumentException"/>）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>更新結果（対象プレイリストが存在しない場合は <see langword="null"/>）。</returns>
    public async Task<PlaylistUpdateResult?> RenamePlaylistAsync(
        PlaylistId playlistId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = ValidateAndNormalizePlaylistName(newName, nameof(newName));
        int changedCount = 0;

        UserPlaylist? updatedPlaylist = await _playlistRepository.UpdateAsync(
            playlistId,
            current =>
            {
                changedCount = 0;
                if (string.Equals(current.Name, normalizedName, StringComparison.Ordinal))
                {
                    return false;
                }

                current.Rename(normalizedName);
                changedCount = 1;
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (updatedPlaylist is null)
        {
            return null;
        }

        if (changedCount > 0)
        {
            await _eventBus.PublishAsync(new PlaylistUpdatedEvent(updatedPlaylist), cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<Track> resolvedTracks = await ResolvePlaylistTracksAsync(updatedPlaylist, cancellationToken).ConfigureAwait(false);
        return PlaylistUpdateResult.Create(updatedPlaylist, resolvedTracks, changedCount);
    }

    private async Task<(Dictionary<TrackId, Track> TracksById, Dictionary<string, Track> TracksByPath)> LoadTrackLookupsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Track> allTracks = await _trackRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? Array.Empty<Track>();

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

        return (tracksById, tracksByPath);
    }

    private static List<Track> ResolveTracksFromLookups(
        UserPlaylist playlist,
        Dictionary<TrackId, Track> tracksById,
        Dictionary<string, Track> tracksByPath)
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

        return resolved;
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
