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
            await _favoriteRepository.SaveAllAsync(result.UpdatedEntries, cancellationToken).ConfigureAwait(false);
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

        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".alac"
        };

        var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
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
            .GroupBy(t => (t.Album, t.Artist))
            .Select(g => new Album(
                name: g.Key.Album,
                artist: g.Key.Artist,
                year: 0,
                tracks: g.OrderBy(t => t.TrackNumber).ToList()))
            .ToList();

        return grouped;
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
