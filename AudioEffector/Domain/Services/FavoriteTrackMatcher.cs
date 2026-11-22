using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Services;

/// <summary>
/// お気に入りエントリと楽曲エンティティ間のメタデータベース照合およびフォールバック解決を行うドメインサービス
/// </summary>
public static class FavoriteTrackMatcher
{
    /// <summary>
    /// MP3とFLAC間の差し替え等による軽微な再生時間差を許容するしきい値（秒）
    /// </summary>
    public const double DURATION_TOLERANCE_SECONDS = 5.0;

    /// <summary>
    /// お気に入りエントリ一覧とライブラリトラック一覧を一括照合し、各トラックのIsFavorite反映とエントリ自動更新を行います
    /// </summary>
    /// <param name="entries">永続化されているお気に入りエントリ一覧</param>
    /// <param name="tracks">ライブラリ上のトラック一覧</param>
    /// <param name="fallbackTrackLoader">ライブラリ内に存在しないエントリをLastKnownFilePathから補完読み込みするためのデリゲート（任意）</param>
    /// <returns>照合および更新結果</returns>
    public static FavoriteReconciliationResult Reconcile(
        IReadOnlyList<FavoriteTrackEntry> entries,
        IReadOnlyList<Track> tracks,
        Func<string, Track?>? fallbackTrackLoader = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tracks);

        var updatedEntries = entries.ToList();
        var matchedTrackByEntryIndex = new Track?[entries.Count];
        var claimedTrackIndices = new HashSet<int>();
        bool hasEntryChanges = false;

        // パス優先フェーズ: タグ未設定エントリや旧形式（パスのみ）エントリの誤マッチを防ぐため、
        // LastKnownFilePath が完全一致するトラックを先に確保する
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.HasSufficientMetadata || string.IsNullOrWhiteSpace(entry.LastKnownFilePath))
            {
                continue;
            }

            int pathIndex = FindPathMatchTrackIndex(entry.LastKnownFilePath, tracks, claimedTrackIndices);
            if (pathIndex >= 0)
            {
                matchedTrackByEntryIndex[i] = tracks[pathIndex];
                claimedTrackIndices.Add(pathIndex);
            }
        }

        // 第1優先フェーズ: 有効メタデータ (Title, Artist, Album) 一致による解決
        for (int i = 0; i < entries.Count; i++)
        {
            if (matchedTrackByEntryIndex[i] != null)
            {
                continue;
            }

            var entry = entries[i];
            if (!entry.HasSufficientMetadata)
            {
                continue;
            }

            int bestIndex = FindBestMetadataMatchTrackIndex(entry, tracks, claimedTrackIndices);
            if (bestIndex >= 0)
            {
                matchedTrackByEntryIndex[i] = tracks[bestIndex];
                claimedTrackIndices.Add(bestIndex);
            }
        }

        // 第2優先フェーズ（フォールバック）: タグ編集直後の LastKnownFilePath 一致、
        // および移動されたタグ未設定曲の (Title, DurationSeconds) 照合
        for (int i = 0; i < entries.Count; i++)
        {
            if (matchedTrackByEntryIndex[i] != null)
            {
                continue;
            }

            var entry = entries[i];
            int fallbackIndex = FindPathMatchTrackIndex(entry.LastKnownFilePath, tracks, claimedTrackIndices);
            if (fallbackIndex < 0 && !entry.HasSufficientMetadata && !string.IsNullOrWhiteSpace(entry.Title))
            {
                fallbackIndex = FindUntaggedMetadataMatchTrackIndex(entry, tracks, claimedTrackIndices);
            }

            if (fallbackIndex >= 0)
            {
                matchedTrackByEntryIndex[i] = tracks[fallbackIndex];
                claimedTrackIndices.Add(fallbackIndex);
            }
        }

        // ライブラリ内の全トラックへ IsFavorite を反映
        var matchedTrackSet = new HashSet<Track>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < matchedTrackByEntryIndex.Length; i++)
        {
            var matchedTrack = matchedTrackByEntryIndex[i];
            if (matchedTrack != null)
            {
                matchedTrackSet.Add(matchedTrack);
            }
        }

        foreach (var track in tracks)
        {
            bool isFavorite = matchedTrackSet.Contains(track);
            if (track.IsFavorite != isFavorite)
            {
                track.SetFavorite(isFavorite);
            }
        }

        // エントリの自動更新およびお気に入り一覧の構築
        var resolvedTracks = new List<Track>(entries.Count);
        var addedTrackSet = new HashSet<Track>(ReferenceEqualityComparer.Instance);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var matchedTrack = matchedTrackByEntryIndex[i];

            if (matchedTrack == null &&
                fallbackTrackLoader != null &&
                !string.IsNullOrWhiteSpace(entry.LastKnownFilePath))
            {
                var loadedTrack = fallbackTrackLoader(entry.LastKnownFilePath);
                if (loadedTrack != null)
                {
                    loadedTrack.SetFavorite(true);
                    matchedTrack = loadedTrack;
                }
            }

            if (matchedTrack != null)
            {
                if (RequiresEntryUpdate(entry, matchedTrack))
                {
                    updatedEntries[i] = entry.WithUpdatedTrack(matchedTrack);
                    hasEntryChanges = true;
                }

                if (addedTrackSet.Add(matchedTrack))
                {
                    resolvedTracks.Add(matchedTrack);
                }
            }
        }

        return new FavoriteReconciliationResult(updatedEntries, resolvedTracks, hasEntryChanges);
    }

    /// <summary>
    /// 指定されたトラックに最もよく一致するお気に入りエントリを検索します
    /// </summary>
    /// <param name="track">照合対象のトラック</param>
    /// <param name="entries">検索対象のお気に入りエントリ一覧</param>
    /// <returns>一致したお気に入りエントリ（見つからない場合はnull）</returns>
    public static FavoriteTrackEntry? FindBestMatchingEntry(Track track, IReadOnlyList<FavoriteTrackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return null;
        }

        // 第1優先: 有効メタデータ (Title, Artist, Album) 一致
        FavoriteTrackEntry? bestMetadataMatch = null;
        int bestMetadataRank = int.MaxValue;
        double bestDurationDiff = double.MaxValue;

        foreach (var entry in entries)
        {
            if (!entry.HasSufficientMetadata || !IsMetadataCompatible(entry, track))
            {
                continue;
            }

            int rank = CalculateMatchRank(entry, track, out double durationDiff);
            if (rank < bestMetadataRank || (rank == bestMetadataRank && durationDiff < bestDurationDiff))
            {
                bestMetadataMatch = entry;
                bestMetadataRank = rank;
                bestDurationDiff = durationDiff;
            }
        }

        if (bestMetadataMatch != null)
        {
            return bestMetadataMatch;
        }

        // 第2優先: ファイルパス一致フォールバック（タグ未設定曲・タグ編集直後・旧形式）
        foreach (var entry in entries)
        {
            if (IsFilePathMatch(entry.LastKnownFilePath, track.FilePath))
            {
                return entry;
            }
        }

        // 第3優先: タグ未設定エントリのタイトル＋再生時間一致
        FavoriteTrackEntry? bestUntaggedMatch = null;
        double bestUntaggedDiff = double.MaxValue;
        foreach (var entry in entries)
        {
            if (entry.HasSufficientMetadata || !IsUntaggedMetadataCompatible(entry, track, out double diff))
            {
                continue;
            }

            if (diff < bestUntaggedDiff)
            {
                bestUntaggedMatch = entry;
                bestUntaggedDiff = diff;
            }
        }

        return bestUntaggedMatch;
    }

    /// <summary>
    /// 指定されたトラックが削除・解除対象としてお気に入りエントリに一致するかを判定します
    /// </summary>
    /// <param name="entry">判定対象のお気に入りエントリ</param>
    /// <param name="track">解除対象のトラック</param>
    /// <returns>一致する場合はtrue</returns>
    public static bool IsMatchForRemoval(FavoriteTrackEntry entry, Track track)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(track);

        if (IsFilePathMatch(entry.LastKnownFilePath, track.FilePath))
        {
            return true;
        }

        if (entry.HasSufficientMetadata)
        {
            return IsMetadataCompatible(entry, track);
        }

        return IsUntaggedMetadataCompatible(entry, track, out _);
    }

    /// <summary>
    /// 2つのお気に入りエントリが同一楽曲を表すかどうかを判定します
    /// </summary>
    /// <param name="left">比較元のエントリ</param>
    /// <param name="right">比較先のエントリ</param>
    /// <returns>同一楽曲を表す場合はtrue</returns>
    public static bool AreSameEntry(FavoriteTrackEntry left, FavoriteTrackEntry right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.HasSufficientMetadata && right.HasSufficientMetadata)
        {
            if (AreCoreMetadataEqual(left.Title, left.Artist, left.Album, right.Title, right.Artist, right.Album) &&
                AreTrackNumbersCompatible(left.TrackNumber, right.TrackNumber) &&
                AreDurationsCompatible(left.DurationSeconds, right.DurationSeconds, IsFilePathMatch(left.LastKnownFilePath, right.LastKnownFilePath)))
            {
                return true;
            }
        }

        if (IsFilePathMatch(left.LastKnownFilePath, right.LastKnownFilePath))
        {
            return true;
        }

        if (!left.HasSufficientMetadata && !right.HasSufficientMetadata &&
            !string.IsNullOrWhiteSpace(left.Title) &&
            AreCoreMetadataEqual(left.Title, left.Artist, left.Album, right.Title, right.Artist, right.Album) &&
            AreTrackNumbersCompatible(left.TrackNumber, right.TrackNumber) &&
            left.DurationSeconds > 0 && right.DurationSeconds > 0 &&
            Math.Abs(left.DurationSeconds - right.DurationSeconds) <= DURATION_TOLERANCE_SECONDS)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 解決済みトラックの情報に基づき、お気に入りエントリの更新（パスまたはメタデータ）が必要かを判定します
    /// </summary>
    /// <param name="entry">現在のお気に入りエントリ</param>
    /// <param name="track">照合された最新のトラック</param>
    /// <returns>更新が必要な場合はtrue</returns>
    public static bool RequiresEntryUpdate(FavoriteTrackEntry entry, Track track)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(track);

        var updated = entry.WithUpdatedTrack(track);
        return !string.Equals(entry.Title, updated.Title, StringComparison.Ordinal) ||
               !string.Equals(entry.Artist, updated.Artist, StringComparison.Ordinal) ||
               !string.Equals(entry.Album, updated.Album, StringComparison.Ordinal) ||
               entry.TrackNumber != updated.TrackNumber ||
               Math.Abs(entry.DurationSeconds - updated.DurationSeconds) > 0.0005 ||
               !string.Equals(entry.LastKnownFilePath, updated.LastKnownFilePath, StringComparison.OrdinalIgnoreCase);
    }

    private static int FindPathMatchTrackIndex(string? filePath, IReadOnlyList<Track> tracks, HashSet<int> claimedTrackIndices)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return -1;
        }

        for (int i = 0; i < tracks.Count; i++)
        {
            if (claimedTrackIndices.Contains(i))
            {
                continue;
            }

            if (IsFilePathMatch(filePath, tracks[i].FilePath))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindBestMetadataMatchTrackIndex(FavoriteTrackEntry entry, IReadOnlyList<Track> tracks, HashSet<int> claimedTrackIndices)
    {
        int bestIndex = -1;
        int bestRank = int.MaxValue;
        double bestDurationDiff = double.MaxValue;

        for (int i = 0; i < tracks.Count; i++)
        {
            if (claimedTrackIndices.Contains(i))
            {
                continue;
            }

            var track = tracks[i];
            if (!IsMetadataCompatible(entry, track))
            {
                continue;
            }

            int rank = CalculateMatchRank(entry, track, out double durationDiff);
            if (rank < bestRank || (rank == bestRank && durationDiff < bestDurationDiff))
            {
                bestIndex = i;
                bestRank = rank;
                bestDurationDiff = durationDiff;
            }
        }

        return bestIndex;
    }

    private static int FindUntaggedMetadataMatchTrackIndex(FavoriteTrackEntry entry, IReadOnlyList<Track> tracks, HashSet<int> claimedTrackIndices)
    {
        int bestIndex = -1;
        double bestDurationDiff = double.MaxValue;

        for (int i = 0; i < tracks.Count; i++)
        {
            if (claimedTrackIndices.Contains(i))
            {
                continue;
            }

            if (IsUntaggedMetadataCompatible(entry, tracks[i], out double diff) && diff < bestDurationDiff)
            {
                bestIndex = i;
                bestDurationDiff = diff;
            }
        }

        return bestIndex;
    }

    private static bool IsMetadataCompatible(FavoriteTrackEntry entry, Track track)
    {
        if (!AreCoreMetadataEqual(entry.Title, entry.Artist, entry.Album, track.Title, track.Artist, track.Album))
        {
            return false;
        }

        if (!AreTrackNumbersCompatible(entry.TrackNumber, track.TrackNumber))
        {
            return false;
        }

        bool pathMatches = IsFilePathMatch(entry.LastKnownFilePath, track.FilePath);
        return AreDurationsCompatible(entry.DurationSeconds, track.Duration.TotalSeconds, pathMatches);
    }

    private static bool IsUntaggedMetadataCompatible(FavoriteTrackEntry entry, Track track, out double durationDiff)
    {
        durationDiff = double.MaxValue;

        if (string.IsNullOrWhiteSpace(entry.Title))
        {
            return false;
        }

        if (!AreCoreMetadataEqual(entry.Title, entry.Artist, entry.Album, track.Title, track.Artist, track.Album))
        {
            return false;
        }

        if (!AreTrackNumbersCompatible(entry.TrackNumber, track.TrackNumber))
        {
            return false;
        }

        double trackDuration = FavoriteTrackEntry.NormalizeDuration(track.Duration.TotalSeconds);
        if (entry.DurationSeconds <= 0 || trackDuration <= 0)
        {
            return false;
        }

        durationDiff = Math.Abs(entry.DurationSeconds - trackDuration);
        return durationDiff <= DURATION_TOLERANCE_SECONDS;
    }

    private static int CalculateMatchRank(FavoriteTrackEntry entry, Track track, out double durationDiff)
    {
        bool pathMatches = IsFilePathMatch(entry.LastKnownFilePath, track.FilePath);
        bool trackNumberMatches = entry.TrackNumber > 0 && track.TrackNumber > 0 && entry.TrackNumber == track.TrackNumber;
        double trackDuration = FavoriteTrackEntry.NormalizeDuration(track.Duration.TotalSeconds);
        durationDiff = (entry.DurationSeconds > 0 && trackDuration > 0)
            ? Math.Abs(entry.DurationSeconds - trackDuration)
            : double.MaxValue;

        if (pathMatches)
        {
            return 0;
        }

        if (trackNumberMatches)
        {
            return 1;
        }

        return 2;
    }

    private static bool AreCoreMetadataEqual(
        string leftTitle,
        string leftArtist,
        string leftAlbum,
        string rightTitle,
        string rightArtist,
        string rightAlbum)
    {
        return string.Equals(FavoriteTrackEntry.NormalizeText(leftTitle), FavoriteTrackEntry.NormalizeText(rightTitle), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(FavoriteTrackEntry.NormalizeText(leftArtist), FavoriteTrackEntry.NormalizeText(rightArtist), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(FavoriteTrackEntry.NormalizeText(leftAlbum), FavoriteTrackEntry.NormalizeText(rightAlbum), StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreTrackNumbersCompatible(uint leftTrackNumber, uint rightTrackNumber)
    {
        return leftTrackNumber == 0 || rightTrackNumber == 0 || leftTrackNumber == rightTrackNumber;
    }

    private static bool AreDurationsCompatible(double leftSeconds, double rightSeconds, bool allowMismatchWhenSamePath)
    {
        if (allowMismatchWhenSamePath)
        {
            return true;
        }

        double normalizedLeft = FavoriteTrackEntry.NormalizeDuration(leftSeconds);
        double normalizedRight = FavoriteTrackEntry.NormalizeDuration(rightSeconds);
        if (normalizedLeft <= 0 || normalizedRight <= 0)
        {
            return true;
        }

        return Math.Abs(normalizedLeft - normalizedRight) <= DURATION_TOLERANCE_SECONDS;
    }

    private static bool IsFilePathMatch(string? leftPath, string? rightPath)
    {
        string left = FavoriteTrackEntry.NormalizeText(leftPath);
        string right = FavoriteTrackEntry.NormalizeText(rightPath);
        return !string.IsNullOrEmpty(left) &&
               !string.IsNullOrEmpty(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// お気に入りエントリとライブラリ内トラックの照合結果を表すレコード
/// </summary>
/// <param name="UpdatedEntries">最新パスやメタデータで更新されたお気に入りエントリ一覧</param>
/// <param name="MatchedTracks">お気に入り登録順に解決されたトラック一覧</param>
/// <param name="HasEntryChanges">エントリのパスまたはメタデータに自動更新が発生したかどうか</param>
public sealed record FavoriteReconciliationResult(
    IReadOnlyList<FavoriteTrackEntry> UpdatedEntries,
    IReadOnlyList<Track> MatchedTracks,
    bool HasEntryChanges);
