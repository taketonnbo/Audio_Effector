using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 楽曲ライブラリのフォルダスキャン、検索、アルバム集約、お気に入り管理を統括するアプリケーションサービス
/// </summary>
public class LibraryApplicationService
{
    private readonly ITrackRepository _trackRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly TagLibMetadataExtractor _metadataExtractor;
    private readonly IEventBus _eventBus;
    private readonly string _favoritesFilePath;

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
        TagLibMetadataExtractor metadataExtractor,
        IEventBus eventBus)
    {
        _trackRepository = trackRepository ?? throw new ArgumentNullException(nameof(trackRepository));
        _favoriteRepository = favoriteRepository ?? throw new ArgumentNullException(nameof(favoriteRepository));
        _metadataExtractor = metadataExtractor ?? throw new ArgumentNullException(nameof(metadataExtractor));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        var appDataPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioEffector");
        System.IO.Directory.CreateDirectory(appDataPath);
        _favoritesFilePath = System.IO.Path.Combine(appDataPath, "favorites.json");
    }

    /// <summary>
    /// お気に入りリストをJSONファイルから読み込みます
    /// </summary>
    /// <returns></returns>
    public List<string> LoadFavorites()
    {
        if (File.Exists(_favoritesFilePath))
        {
            try
            {
                string json = File.ReadAllText(_favoritesFilePath);
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { }
        }
        return new List<string>();
    }

    /// <summary>
    /// お気に入りリストをJSONファイルへ保存します
    /// </summary>
    public void SaveFavorites(List<string> favorites)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(favorites);
            File.WriteAllText(_favoritesFilePath, json);
        }
        catch { }
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
        var favoriteIds = await _favoriteRepository.GetFavoriteIdsAsync(cancellationToken);

        for (int i = 0; i < totalCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string file = allFiles[i];
            var audioPath = AudioPath.Create(file);

            // 既存登録があるかを確認
            var existingTrack = await _trackRepository.GetByPathAsync(audioPath, cancellationToken);
            if (existingTrack != null)
            {
                bool isFav = favoriteIds.Contains(existingTrack.Id);
                existingTrack.SetFavorite(isFav);
                scannedTracks.Add(existingTrack);
            }
            else
            {
                var newTrack = await _metadataExtractor.ExtractMetadataAsync(audioPath, cancellationToken);
                if (newTrack != null)
                {
                    bool isFav = favoriteIds.Contains(newTrack.Id);
                    if (isFav)
                    {
                        newTrack.SetFavorite(true);
                    }

                    scannedTracks.Add(newTrack);
                }
            }

            progress?.Report((double)(i + 1) / totalCount);
        }

        await _trackRepository.SaveRangeAsync(scannedTracks, cancellationToken);
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
        return await _trackRepository.SearchAsync(keyword, cancellationToken);
    }

    /// <summary>
    /// ライブラリ内のすべてのトラックからアルバムを集約して取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>アルバムのリスト</returns>
    public async Task<IReadOnlyList<Album>> GetAllAlbumsAsync(CancellationToken cancellationToken = default)
    {
        var tracks = await _trackRepository.GetAllAsync(cancellationToken);
        var favoriteIds = await _favoriteRepository.GetFavoriteIdsAsync(cancellationToken);

        foreach (var t in tracks)
        {
            t.SetFavorite(favoriteIds.Contains(t.Id));
        }

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
    /// 指定されたトラックのお気に入り状態を切り替えます
    /// </summary>
    /// <param name="trackId">トラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>切り替え後のトラック（存在しない場合はnull）</returns>
    public async Task<Track?> ToggleFavoriteAsync(TrackId trackId, CancellationToken cancellationToken = default)
    {
        var track = await _trackRepository.GetByIdAsync(trackId, cancellationToken);
        if (track == null) return null;

        bool newFavoriteState = !track.IsFavorite;
        if (newFavoriteState)
        {
            await _favoriteRepository.AddAsync(trackId, cancellationToken);
        }
        else
        {
            await _favoriteRepository.RemoveAsync(trackId, cancellationToken);
        }

        track.SetFavorite(newFavoriteState);
        await _trackRepository.SaveAsync(track, cancellationToken);

        return track;
    }

    /// <summary>
    /// お気に入りに登録されているすべてのトラックを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>お気に入りトラックのリスト</returns>
    public async Task<IReadOnlyList<Track>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var favoriteIds = await _favoriteRepository.GetFavoriteIdsAsync(cancellationToken);
        var allTracks = await _trackRepository.GetAllAsync(cancellationToken);

        var favorites = new List<Track>();
        foreach (var t in allTracks)
        {
            if (favoriteIds.Contains(t.Id))
            {
                t.SetFavorite(true);
                favorites.Add(t);
            }
        }

        return favorites;
    }
}
