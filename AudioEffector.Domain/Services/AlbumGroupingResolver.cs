using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Services;

/// <summary>
/// 楽曲群を作品単位のアルバムグループへ集約し、各グループに一意の <see cref="AlbumId"/> を割り当てるドメインサービス。
/// 既存の <see cref="AlbumId"/> がある場合はそれを引き継ぎ、未採番の新規グループには <see cref="AlbumId.New"/> を採番します。
/// </summary>
public static class AlbumGroupingResolver
{
    /// <summary>
    /// コンピレーション作品のフォールバック用アルバムアーティスト名
    /// </summary>
    public const string VariousArtistsName = "Various Artists";

    private const string UnknownAlbumName = "Unknown Album";
    private const string UnknownArtistName = "Unknown Artist";

    /// <summary>
    /// 楽曲コレクションを作品単位でグループ化し、既存アルバムIDを引き継ぎつつ各トラックへ共通の <see cref="Track.AlbumId"/> を割り当てます。
    /// </summary>
    /// <param name="tracks">対象のトラック一覧</param>
    /// <param name="existingAlbumIdsByKey">既存の作品キー（正規化済みアルバム名, 正規化済み作品アーティスト名）と既存AlbumIdの対応マップ（任意）</param>
    /// <returns>トラックIDをキーとする解決済みアルバム識別情報マップ</returns>
    public static IReadOnlyDictionary<TrackId, ResolvedAlbumIdentity> ResolveAndApply(
        IReadOnlyList<Track> tracks,
        IReadOnlyDictionary<(string NormalizedAlbum, string NormalizedArtist), AlbumId>? existingAlbumIdsByKey = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var resolved = Resolve(tracks, existingAlbumIdsByKey);
        foreach (var track in tracks)
        {
            if (track != null && resolved.TryGetValue(track.Id, out var identity))
            {
                track.AlbumId = identity.AlbumId;
            }
        }

        return resolved;
    }

    /// <summary>
    /// アルバム名とアーティスト名から、既存アルバムID引き継ぎ用の正規化作品キーを生成します。
    /// </summary>
    /// <param name="albumName">アルバム名</param>
    /// <param name="artistName">アルバムアーティスト名</param>
    /// <returns>小文字化・トリム済みの作品キー</returns>
    public static (string NormalizedAlbum, string NormalizedArtist) CreateGroupKey(string? albumName, string? artistName)
        => (NormalizeAlbumName(albumName).ToLowerInvariant(), NormalizeArtistName(artistName).ToLowerInvariant());

    /// <summary>
    /// 指定された楽曲コレクション全体の作品単位アルバム識別情報を解決します。
    /// </summary>
    /// <param name="tracks">対象のトラック一覧</param>
    /// <param name="existingAlbumIdsByKey">既存の作品キーと既存AlbumIdの対応マップ（任意）</param>
    /// <returns>トラックIDをキーとする解決済みアルバム識別情報マップ</returns>
    public static IReadOnlyDictionary<TrackId, ResolvedAlbumIdentity> Resolve(
        IReadOnlyList<Track> tracks,
        IReadOnlyDictionary<(string NormalizedAlbum, string NormalizedArtist), AlbumId>? existingAlbumIdsByKey = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var result = new Dictionary<TrackId, ResolvedAlbumIdentity>(tracks.Count);
        if (tracks.Count == 0)
        {
            return result;
        }

        var folderAlbumArtistOverride = BuildFolderAlbumArtistOverrides(tracks);

        // 作品キーごとにトラックを束ねる
        var groupsByKey = new Dictionary<(string NormalizedAlbum, string NormalizedArtist), (string DisplayArtist, List<Track> GroupTracks)>();
        foreach (var track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            string albumName = NormalizeAlbumName(track.Album);
            string resolvedAlbumArtist = ResolveAlbumArtistForTrack(track, albumName, folderAlbumArtistOverride);
            var groupKey = CreateGroupKey(albumName, resolvedAlbumArtist);

            if (!groupsByKey.TryGetValue(groupKey, out var entry))
            {
                entry = (resolvedAlbumArtist, new List<Track>());
                groupsByKey[groupKey] = entry;
            }

            entry.GroupTracks.Add(track);
        }

        // 各作品グループへ単一の AlbumId を決定（2パス: 先に既存キーが残るグループへ既存IDを予約し、残りを解決）
        var usedAlbumIds = new HashSet<AlbumId>();
        var resolvedIdByGroupKey = new Dictionary<(string NormalizedAlbum, string NormalizedArtist), AlbumId>(groupsByKey.Count);

        // Pass 1: existingAlbumIdsByKey に一致する既存作品グループへ既存 ID を先に予約
        if (existingAlbumIdsByKey != null)
        {
            foreach (var kvp in groupsByKey)
            {
                if (existingAlbumIdsByKey.TryGetValue(kvp.Key, out var existingId) &&
                    usedAlbumIds.Add(existingId))
                {
                    resolvedIdByGroupKey[kvp.Key] = existingId;
                }
            }
        }

        // Pass 2: 残りの新規グループを解決（Pass 1 で残存既存作品に予約済みの ID は奪わない）
        foreach (var kvp in groupsByKey)
        {
            if (resolvedIdByGroupKey.ContainsKey(kvp.Key))
            {
                continue;
            }

            var (_, groupTracks) = kvp.Value;

            // グループ内トラックが持つ既存の AlbumId のうち、未使用のものを優先して引き継ぐ（最頻出優先）
            AlbumId? candidateFromTracks = groupTracks
                .GroupBy(t => t.AlbumId)
                .OrderByDescending(g => g.Count())
                .Select(g => (AlbumId?)g.Key)
                .FirstOrDefault(id => id.HasValue && !usedAlbumIds.Contains(id.Value));

            AlbumId resolvedId = candidateFromTracks ?? AlbumId.New();
            usedAlbumIds.Add(resolvedId);
            resolvedIdByGroupKey[kvp.Key] = resolvedId;
        }

        foreach (var kvp in groupsByKey)
        {
            var resolvedId = resolvedIdByGroupKey[kvp.Key];
            var (displayArtist, groupTracks) = kvp.Value;
            foreach (var track in groupTracks)
            {
                result[track.Id] = new ResolvedAlbumIdentity(resolvedId, displayArtist);
            }
        }

        return result;
    }

    private static Dictionary<(string Directory, string NormalizedAlbum), string> BuildFolderAlbumArtistOverrides(IReadOnlyList<Track> tracks)
    {
        var byDirectoryAndAlbum = new Dictionary<(string Directory, string NormalizedAlbum), List<Track>>();
        foreach (var track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            string albumName = NormalizeAlbumName(track.Album);
            string directory = GetNormalizedDirectory(track.FilePath);
            if (!string.IsNullOrEmpty(directory) &&
                !string.Equals(albumName, UnknownAlbumName, StringComparison.OrdinalIgnoreCase))
            {
                var key = (directory, albumName.ToLowerInvariant());
                if (!byDirectoryAndAlbum.TryGetValue(key, out var list))
                {
                    list = new List<Track>();
                    byDirectoryAndAlbum[key] = list;
                }

                list.Add(track);
            }
        }

        var overrides = new Dictionary<(string Directory, string NormalizedAlbum), string>();
        foreach (var kvp in byDirectoryAndAlbum)
        {
            var groupTracks = kvp.Value;
            if (groupTracks.Count < 2)
            {
                continue;
            }

            // 1. 同一フォルダ・同一アルバム内に共通の明示的AlbumArtist（1種類のみ）がある場合は未設定曲へ補完
            var explicitAlbumArtists = groupTracks
                .Select(t => t.AlbumArtist?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (explicitAlbumArtists.Count == 1)
            {
                overrides[kvp.Key] = explicitAlbumArtists[0]!;
                continue;
            }

            if (explicitAlbumArtists.Count > 1)
            {
                continue;
            }

            // 2. 全曲AlbumArtist未設定かつ複数演奏者が存在する場合、コンピレーション作品かを判定
            var distinctPerformers = groupTracks
                .Select(t => NormalizeArtistName(t.Artist))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctPerformers.Count < 2)
            {
                continue;
            }

            if (IsCompilationFolderGroup(kvp.Key.Directory, kvp.Key.NormalizedAlbum, groupTracks))
            {
                overrides[kvp.Key] = VariousArtistsName;
            }
        }

        return overrides;
    }

    private static string ResolveAlbumArtistForTrack(
        Track track,
        string albumName,
        Dictionary<(string Directory, string NormalizedAlbum), string> folderAlbumArtistOverride)
    {
        if (!string.IsNullOrWhiteSpace(track.AlbumArtist))
        {
            return track.AlbumArtist.Trim();
        }

        string directory = GetNormalizedDirectory(track.FilePath);
        var folderKey = (directory, albumName.ToLowerInvariant());
        if (!string.IsNullOrEmpty(directory) &&
            folderAlbumArtistOverride.TryGetValue(folderKey, out string? overrideArtist))
        {
            return overrideArtist;
        }

        return NormalizeArtistName(track.Artist);
    }

    private static bool IsCompilationFolderGroup(string directory, string normalizedAlbum, List<Track> groupTracks)
    {
        string folderName = Path.GetFileName(directory);
        if (!string.IsNullOrWhiteSpace(folderName) &&
            folderName.Contains(normalizedAlbum, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var seenTrackNumbers = new HashSet<uint>();
        foreach (var track in groupTracks)
        {
            if (track.TrackNumber == 0 || !seenTrackNumbers.Add(track.TrackNumber))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeAlbumName(string? album)
        => string.IsNullOrWhiteSpace(album) ? UnknownAlbumName : album.Trim();

    private static string NormalizeArtistName(string? artist)
        => string.IsNullOrWhiteSpace(artist) ? UnknownArtistName : artist.Trim();

    private static string GetNormalizedDirectory(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            return string.IsNullOrWhiteSpace(dir)
                ? string.Empty
                : dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// 解決されたアルバム識別情報（作品単位のAlbumIdおよび表示用アルバムアーティスト名）
/// </summary>
/// <param name="AlbumId">作品単位の一意なアルバムID</param>
/// <param name="AlbumArtist">アルバム表示に使用するアーティスト名</param>
public readonly record struct ResolvedAlbumIdentity(AlbumId AlbumId, string AlbumArtist);
