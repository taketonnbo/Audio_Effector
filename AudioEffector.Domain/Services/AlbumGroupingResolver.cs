using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Services;

/// <summary>
/// 楽曲群から作品単位のアルバム識別情報（AlbumIdおよびアルバムアーティスト）を決定論的に解決するドメインサービス。
/// 共通のAlbumArtistタグを最優先し、未設定のコンピレーション／客演アルバムに対しても
/// 同一ディレクトリ・トラック番号構成・フォルダ名に基づくフォールバック集約を提供します。
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
    /// 指定された楽曲コレクション全体の作品単位アルバム識別情報を解決し、各トラックの <see cref="Track.AlbumId"/> へ反映します。
    /// </summary>
    /// <param name="tracks">対象のトラック一覧</param>
    /// <returns>トラックIDをキーとする解決済みアルバム識別情報マップ</returns>
    public static IReadOnlyDictionary<TrackId, ResolvedAlbumIdentity> ResolveAndApply(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var resolved = Resolve(tracks);
        foreach (var track in tracks)
        {
            if (track != null && resolved.TryGetValue(track.Id, out var identity))
            {
                track.SetResolvedAlbumId(identity.AlbumId);
            }
        }

        return resolved;
    }

    /// <summary>
    /// 指定された楽曲コレクション全体の作品単位アルバム識別情報を解決します。
    /// </summary>
    /// <param name="tracks">対象のトラック一覧</param>
    /// <returns>トラックIDをキーとする解決済みアルバム識別情報マップ</returns>
    public static IReadOnlyDictionary<TrackId, ResolvedAlbumIdentity> Resolve(IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var result = new Dictionary<TrackId, ResolvedAlbumIdentity>(tracks.Count);
        if (tracks.Count == 0)
        {
            return result;
        }

        // 同一ディレクトリ＋同一アルバム名ごとにコンピレーション／共通AlbumArtist補完を評価
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

        var folderAlbumArtistOverride = new Dictionary<(string Directory, string NormalizedAlbum), string>();
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
                folderAlbumArtistOverride[kvp.Key] = explicitAlbumArtists[0]!;
                continue;
            }

            if (explicitAlbumArtists.Count > 1)
            {
                // 複数の異なる明示的AlbumArtistが混在する場合は別作品として扱う
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
                folderAlbumArtistOverride[kvp.Key] = VariousArtistsName;
            }
        }

        foreach (var track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            string albumName = NormalizeAlbumName(track.Album);
            string resolvedAlbumArtist;

            if (!string.IsNullOrWhiteSpace(track.AlbumArtist))
            {
                resolvedAlbumArtist = track.AlbumArtist.Trim();
            }
            else
            {
                string directory = GetNormalizedDirectory(track.FilePath);
                var folderKey = (directory, albumName.ToLowerInvariant());
                if (!string.IsNullOrEmpty(directory) &&
                    folderAlbumArtistOverride.TryGetValue(folderKey, out string? overrideArtist))
                {
                    resolvedAlbumArtist = overrideArtist;
                }
                else
                {
                    resolvedAlbumArtist = NormalizeArtistName(track.Artist);
                }
            }

            var albumId = AlbumId.FromAlbumKey(albumName, resolvedAlbumArtist);
            result[track.Id] = new ResolvedAlbumIdentity(albumId, resolvedAlbumArtist);
        }

        return result;
    }

    private static bool IsCompilationFolderGroup(string directory, string normalizedAlbum, List<Track> groupTracks)
    {
        // 条件A: 親フォルダ名にアルバム名が含まれている（アルバム専用ディレクトリ）
        string folderName = Path.GetFileName(directory);
        if (!string.IsNullOrWhiteSpace(folderName) &&
            folderName.Contains(normalizedAlbum, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 条件B: 全曲に正のトラック番号が振られており重複がない（1枚のディスク／作品の連番構成）
        var seenTrackNumbers = new HashSet<uint>();
        bool allHaveUniquePositiveTrackNumbers = true;
        foreach (var track in groupTracks)
        {
            if (track.TrackNumber == 0 || !seenTrackNumbers.Add(track.TrackNumber))
            {
                allHaveUniquePositiveTrackNumbers = false;
                break;
            }
        }

        return allHaveUniquePositiveTrackNumbers;
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
/// <param name="AlbumId">作品単位の決定論的アルバムID</param>
/// <param name="AlbumArtist">アルバム表示および識別に使用するアーティスト名</param>
public readonly record struct ResolvedAlbumIdentity(AlbumId AlbumId, string AlbumArtist);
