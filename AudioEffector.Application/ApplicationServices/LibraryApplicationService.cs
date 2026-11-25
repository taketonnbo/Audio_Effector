using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 楽曲ライブラリのフォルダスキャン、検索、アルバム集約、お気に入り管理を統括するアプリケーションサービス
/// </summary>
public class LibraryApplicationService
{
    /// <summary>
    /// ライブラリスキャン対象としてサポートする音声ファイル拡張子一覧（大文字・小文字を区別しない）
    /// </summary>
    public static IReadOnlySet<string> SupportedAudioExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".aiff", ".wma", ".m4a", ".mp4", ".flac", ".aac", ".ogg", ".opus", ".alac"
    };

    private readonly ITrackRepository _trackRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// LibraryApplicationServiceを初期化します
    /// </summary>
    /// <param name="trackRepository">トラックリポジトリ</param>
    /// <param name="favoriteRepository">お気に入りリポジトリ</param>
    /// <param name="metadataExtractor">メタデータ抽出器</param>
    /// <param name="eventBus">イベントバス</param>
    public LibraryApplicationService(
        ITrackRepository trackRepository,
        IFavoriteRepository favoriteRepository,
        IMetadataExtractor metadataExtractor,
        IEventBus eventBus)
    {
        _trackRepository = trackRepository ?? throw new ArgumentNullException(nameof(trackRepository));
        _favoriteRepository = favoriteRepository ?? throw new ArgumentNullException(nameof(favoriteRepository));
        _metadataExtractor = metadataExtractor ?? throw new ArgumentNullException(nameof(metadataExtractor));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// お気に入りエントリ一覧をリポジトリから非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>お気に入りエントリの読み取り専用リスト</returns>
    public async Task<IReadOnlyList<FavoriteTrackEntry>> GetFavoriteEntriesAsync(CancellationToken cancellationToken = default)
    {
        return await _favoriteRepository.GetAllAsync(cancellationToken).ConfigureAwait(false)
            ?? Array.Empty<FavoriteTrackEntry>();
    }

    /// <summary>
    /// お気に入りエントリ一覧をリポジトリから同期的に読み込みます
    /// </summary>
    /// <returns>お気に入りエントリのリスト</returns>
    public IReadOnlyList<FavoriteTrackEntry> LoadFavoriteEntries()
    {
        try
        {
            return GetFavoriteEntriesAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return Array.Empty<FavoriteTrackEntry>();
        }
    }

    /// <summary>
    /// お気に入りエントリ一覧をリポジトリへ非同期で一括保存します
    /// </summary>
    /// <param name="entries">保存するお気に入りエントリ一覧</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveFavoriteEntriesAsync(IReadOnlyList<FavoriteTrackEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        await _favoriteRepository.SaveAllAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// お気に入りエントリ一覧をリポジトリへ同期的に保存します
    /// </summary>
    /// <param name="entries">保存するお気に入りエントリ一覧</param>
    public void SaveFavoriteEntries(IReadOnlyList<FavoriteTrackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        try
        {
            _favoriteRepository.SaveAllAsync(entries).GetAwaiter().GetResult();
        }
        catch
        {
            // 永続化失敗時はアプリ動作を止めない
        }
    }

    /// <summary>指定された楽曲のお気に入り状態を項目単位で保存します</summary>
    /// <param name="track">対象の楽曲</param>
    /// <param name="entry">登録する項目（解除時はnull）</param>
    public void SetFavorite(Track track, FavoriteTrackEntry? entry)
    {
        try
        {
            var save = entry != null
                ? _favoriteRepository.AddOrUpdateAsync(entry)
                : _favoriteRepository.RemoveAsync(track);
            save.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
        }
    }

    /// <summary>画面で再照合したメタデータの差分を保存します</summary>
    /// <param name="changes">お気に入りの変更項目</param>
    public void SaveFavoriteMetadata(IReadOnlyList<FavoriteMetadataChange> changes)
    {
        if (changes.Count == 0) return;
        try
        {
            _favoriteRepository.UpdateMetadataAsync(changes).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
        }
    }

    /// <summary>
    /// お気に入りエントリと指定されたトラック一覧を照合してIsFavoriteを反映し、パスやメタデータの差分があれば永続化します
    /// </summary>
    /// <param name="tracks">照合対象のトラック一覧</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>照合および更新結果</returns>
    public async Task<FavoriteReconciliationResult> ReconcileFavoritesAsync(
        IReadOnlyList<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var entries = await GetFavoriteEntriesAsync(cancellationToken).ConfigureAwait(false);
        var result = FavoriteTrackMatcher.Reconcile(entries, tracks);
        if (result.HasEntryChanges)
        {
            var changes = entries.Zip(result.UpdatedEntries, (original, updated) => new FavoriteMetadataChange(original, updated))
                .Where(change => !ReferenceEquals(change.Original, change.Updated)).ToList();
            await _favoriteRepository.UpdateMetadataAsync(changes, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 指定されたフォルダから再帰的に音声ファイルをスキャンし、ライブラリに登録します
    /// </summary>
    /// <param name="folderPath">スキャン対象のフォルダパス</param>
    /// <param name="progress">進捗通知（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>スキャン・登録された全トラックのリスト</returns>
    public async Task<IReadOnlyList<Track>> ScanFolderAsync(
        string folderPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return Array.Empty<Track>();
        }

        var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedAudioExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        int totalCount = allFiles.Count;
        var scannedTracks = new List<Track>();

        for (int i = 0; i < totalCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string file = allFiles[i];
            var audioPath = AudioPath.Create(file);

            // 既存登録があるかを確認
            var existingTrack = await _trackRepository.GetByPathAsync(audioPath, cancellationToken).ConfigureAwait(false);
            if (existingTrack != null)
            {
                scannedTracks.Add(existingTrack);
            }
            else
            {
                var newTrack = await _metadataExtractor.ExtractMetadataAsync(audioPath, cancellationToken).ConfigureAwait(false);
                if (newTrack != null)
                {
                    scannedTracks.Add(newTrack);
                }
            }

            progress?.Report((double)(i + 1) / totalCount);
        }

        await ReconcileFavoritesAsync(scannedTracks, cancellationToken).ConfigureAwait(false);
        await _trackRepository.SaveRangeAsync(scannedTracks, cancellationToken).ConfigureAwait(false);
        return scannedTracks;
    }

    /// <summary>
    /// リポジトリに永続化されている既存トラックのファイルパスとトラック ID の対応マップを取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>ファイルパス（大文字・小文字を区別しない）をキーとする既存トラック ID マップ。</returns>
    public async Task<IReadOnlyDictionary<string, TrackId>> GetTrackIdsByPathAsync(CancellationToken cancellationToken = default)
    {
        var existingTracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false)
            ?? Array.Empty<Track>();

        var result = new Dictionary<string, TrackId>(existingTracks.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var track in existingTracks)
        {
            if (!string.IsNullOrWhiteSpace(track.FilePath))
            {
                result.TryAdd(track.FilePath, track.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// 現在のライブラリ楽曲一覧（追加・タグ更新・削除・フォルダ切替後）をリポジトリへ完全同期します。
    /// 同一ファイルパスの既存トラックが存在する場合は、既存の <see cref="TrackId"/> を維持したうえで最新メタデータを保存します。
    /// </summary>
    /// <param name="tracks">同期対象の確定済み楽曲一覧。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>非同期タスク。</returns>
    public async Task SyncLibraryTracksAsync(IReadOnlyList<Track> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var existingIdsByPath = await GetTrackIdsByPathAsync(cancellationToken).ConfigureAwait(false);
        var normalizedTracks = new List<Track>(tracks.Count);

        foreach (var track in tracks)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
            {
                continue;
            }

            if (existingIdsByPath.TryGetValue(track.FilePath, out TrackId existingId) && !track.Id.Equals(existingId))
            {
                normalizedTracks.Add(new Track(
                    id: existingId,
                    filePath: AudioPath.Create(track.FilePath),
                    title: track.Title,
                    artist: track.Artist,
                    album: track.Album,
                    duration: track.Duration,
                    year: track.Year,
                    trackNumber: track.TrackNumber,
                    bitrate: track.Bitrate,
                    sampleRate: track.SampleRate,
                    bitsPerSample: track.BitsPerSample,
                    format: track.Format,
                    genre: track.Genre,
                    isFavorite: track.IsFavorite,
                    isLossless: track.IsLossless,
                    isHiRes: track.IsHiRes));
            }
            else
            {
                normalizedTracks.Add(track);
            }
        }

        await _trackRepository.ReplaceAllAsync(normalizedTracks, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 現在のライブラリ楽曲一覧をリポジトリへ同期的に保存します（UI 削除コマンド等からの呼び出し用）。
    /// </summary>
    /// <param name="tracks">同期対象の確定済み楽曲一覧。</param>
    public void SyncLibraryTracks(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        try
        {
            SyncLibraryTracksAsync(tracks).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
        }
    }

    /// <summary>
    /// キーワードでライブラリ内のトラックを検索します
    /// </summary>
    /// <param name="keyword">検索キーワード</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>一致したトラックのリスト</returns>
    public async Task<IReadOnlyList<Track>> SearchTracksAsync(string keyword, CancellationToken cancellationToken = default)
    {
        return await _trackRepository.SearchAsync(keyword, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// キーワード・メタデータフィルタ・お気に入り条件およびページネーションを指定してライブラリ内のトラックを検索します。
    /// お気に入りリポジトリとの最新照合（読み取り専用）を行い、各トラックの <see cref="Track.IsFavorite"/> を反映します。
    /// </summary>
    /// <param name="query">複合検索条件およびページネーション指定。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>一致総件数およびページ適用後のトラック一覧を含む検索結果。</returns>
    public async Task<TrackSearchResult> SearchTracksAsync(
        TrackSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit < TrackSearchQuery.MIN_LIMIT || query.Limit > TrackSearchQuery.MAX_LIMIT)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Limit,
                $"limit は {TrackSearchQuery.MIN_LIMIT}〜{TrackSearchQuery.MAX_LIMIT} の範囲で指定してください。");
        }

        if (query.Offset < TrackSearchQuery.DEFAULT_OFFSET)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Offset,
                $"offset は {TrackSearchQuery.DEFAULT_OFFSET} 以上で指定してください。");
        }

        string? trimmedKeyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        string? trimmedArtist = string.IsNullOrWhiteSpace(query.Artist) ? null : query.Artist.Trim();
        string? trimmedAlbum = string.IsNullOrWhiteSpace(query.Album) ? null : query.Album.Trim();
        string? trimmedGenre = string.IsNullOrWhiteSpace(query.Genre) ? null : query.Genre.Trim();

        // 検索条件で候補集合を狭める前に全曲でお気に入り照合を行い、メタデータ優先・パスフォールバックの対応先を一貫して確定させる
        IReadOnlyList<Track> allTracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (allTracks.Count == 0)
        {
            return new TrackSearchResult(Array.Empty<Track>(), 0, query.Offset, query.Limit);
        }

        // お気に入り永続化データ（favorites.json）を正本として最新の IsFavorite 状態をメモリ上で反映する（読み取り専用のため書き込みは行わない）
        var favoriteEntries = await GetFavoriteEntriesAsync(cancellationToken).ConfigureAwait(false);
        var reconciliation = FavoriteTrackMatcher.Reconcile(favoriteEntries, allTracks);
        var favoriteTrackIds = reconciliation.MatchedTracks
            .Select(t => t.Id)
            .ToHashSet();

        IEnumerable<Track> filtered = allTracks;

        if (trimmedKeyword is not null)
        {
            filtered = filtered.Where(t =>
                t.Title.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase));
        }

        if (trimmedArtist is not null)
        {
            filtered = filtered.Where(t => t.Artist.Contains(trimmedArtist, StringComparison.OrdinalIgnoreCase));
        }

        if (trimmedAlbum is not null)
        {
            filtered = filtered.Where(t => t.Album.Contains(trimmedAlbum, StringComparison.OrdinalIgnoreCase));
        }

        if (trimmedGenre is not null)
        {
            filtered = filtered.Where(t => t.Genre.Contains(trimmedGenre, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsFavoriteOnly)
        {
            filtered = filtered.Where(t => favoriteTrackIds.Contains(t.Id));
        }

        var ordered = filtered
            .OrderBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TrackNumber)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id.Value)
            .ToList();

        int totalCount = ordered.Count;
        var paged = ordered
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToList();

        return new TrackSearchResult(paged, totalCount, query.Offset, query.Limit);
    }

    /// <summary>
    /// ファイルパスからトラックを取得します。未登録の場合はファイルのメタデータを読み込みます。
    /// </summary>
    /// <param name="filePath">対象オーディオファイルのパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>取得したトラック。ファイルを解決できない場合はnull</returns>
    public async Task<Track?> GetTrackByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var audioPath = AudioPath.Create(filePath);
        var track = await _trackRepository.GetByPathAsync(audioPath, cancellationToken).ConfigureAwait(false);
        return track ?? await _metadataExtractor.ExtractMetadataAsync(audioPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ライブラリ内のすべてのトラックからアルバムを集約して取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>アルバムのリスト</returns>
    public async Task<IReadOnlyList<Album>> GetAllAlbumsAsync(CancellationToken cancellationToken = default)
    {
        var tracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        await ReconcileFavoritesAsync(tracks, cancellationToken).ConfigureAwait(false);

        var grouped = tracks
            .GroupBy(t => t.AlbumId)
            .Select(g =>
            {
                var orderedTracks = g.OrderBy(t => t.TrackNumber).ToList();
                var representative = orderedTracks[0];
                return new Album(
                    name: representative.Album,
                    artist: representative.Artist,
                    year: 0,
                    tracks: orderedTracks,
                    id: g.Key);
            })
            .ToList();

        return grouped;
    }

    /// <summary>
    /// 指定されたアルバムIDに一致するアルバムを取得します
    /// </summary>
    /// <param name="albumId">アルバムID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>一致したアルバム（存在しない場合はnull）</returns>
    public async Task<Album?> GetAlbumByIdAsync(AlbumId albumId, CancellationToken cancellationToken = default)
    {
        var tracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var matchingTracks = tracks
            .Where(t => t.AlbumId == albumId)
            .OrderBy(t => t.TrackNumber)
            .ToList();

        if (matchingTracks.Count == 0)
        {
            return null;
        }

        var representative = matchingTracks[0];
        return new Album(
            name: representative.Album,
            artist: representative.Artist,
            year: 0,
            tracks: matchingTracks,
            id: albumId);
    }

    /// <summary>
    /// 指定されたトラックIDのお気に入り状態を切り替えます
    /// </summary>
    /// <param name="trackId">トラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>切り替え後のトラック（存在しない場合はnull）</returns>
    public async Task<Track?> ToggleFavoriteAsync(TrackId trackId, CancellationToken cancellationToken = default)
    {
        var track = await _trackRepository.GetByIdAsync(trackId, cancellationToken).ConfigureAwait(false);
        if (track == null) return null;

        await ToggleFavoriteAsync(track, cancellationToken).ConfigureAwait(false);
        await _trackRepository.SaveAsync(track, cancellationToken).ConfigureAwait(false);

        return track;
    }

    /// <summary>
    /// 指定されたトラックのお気に入り状態を切り替え、リポジトリへ永続化します
    /// </summary>
    /// <param name="track">対象トラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>切り替え後のお気に入り状態</returns>
    public async Task<bool> ToggleFavoriteAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        bool newFavoriteState = !track.IsFavorite;
        if (newFavoriteState)
        {
            await _favoriteRepository.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(track), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _favoriteRepository.RemoveAsync(track, cancellationToken).ConfigureAwait(false);
        }

        track.SetFavorite(newFavoriteState);
        return newFavoriteState;
    }

    /// <summary>
    /// お気に入りに登録されているすべてのトラックを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>お気に入りトラックのリスト</returns>
    public async Task<IReadOnlyList<Track>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var allTracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var result = await ReconcileFavoritesAsync(allTracks, cancellationToken).ConfigureAwait(false);
        return result.MatchedTracks;
    }
}
