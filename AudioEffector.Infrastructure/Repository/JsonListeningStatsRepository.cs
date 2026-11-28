using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// 楽曲別累計統計および日時毎の再生履歴をローカル JSON ファイル（ListeningStats.json）へ永続化するリポジトリ実装。
/// 旧 RecentTracks.json からの自動マイグレーション、データディレクトリ動的変更追従、プロセス間ファイルロック、
/// 原子的書き込み、および <see cref="IRecentTrackRepository"/> 互換ビューを提供します。
/// </summary>
public sealed class JsonListeningStatsRepository : IListeningStatsRepository, IRecentTrackRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Func<string> _filePathResolver;
    private readonly Func<string> _legacyRecentTracksFilePathResolver;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly object _syncLock = new();
    private readonly HashSet<string> _legacyMigrationAttemptedPaths = new(StringComparer.OrdinalIgnoreCase);
    private ListeningStatsData _cachedData = new();
    private string? _lastLoadedFilePath;
    private FileMetadataSnapshot _lastLoadedMetadata = FileMetadataSnapshot.Missing;
    private bool _isLoaded;

    private sealed class ListeningStatsFileDto
    {
        public int SchemaVersion { get; set; } = ListeningStatsData.CurrentSchemaVersion;
        public List<TrackListeningStatDto?>? TrackStats { get; set; }
        public List<TrackPlayEventDto?>? PlayEvents { get; set; }
    }

    private sealed class TrackListeningStatDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public int PlayCount { get; set; }
        public double TotalListeningSeconds { get; set; }
        public DateTime LastPlayedAt { get; set; }
        public DateTime? RecentPlayedAt { get; set; }
    }

    private sealed class TrackPlayEventDto
    {
        public DateTime PlayedAt { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public double PlayedDurationSeconds { get; set; }
    }

    private sealed class LegacyRecentTrackDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public DateTime PlayedAt { get; set; }
    }

    /// <summary>
    /// 指定された JSON ファイルパスで <see cref="JsonListeningStatsRepository"/> を初期化します
    /// </summary>
    /// <param name="filePath">保存先ファイルパス（未指定時は AppData 内の ListeningStats.json）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    /// <param name="legacyRecentTracksFilePath">移行元の旧 RecentTracks.json パス（未指定時は同一ディレクトリの RecentTracks.json）</param>
    public JsonListeningStatsRepository(
        string? filePath = null,
        IJsonFileLockHelper? fileLockHelper = null,
        string? legacyRecentTracksFilePath = null)
    {
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;

        string resolvedFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            resolvedFilePath = Path.Combine(appDir, "ListeningStats.json");
        }
        else
        {
            resolvedFilePath = filePath;
        }

        string resolvedLegacyPath;
        if (string.IsNullOrWhiteSpace(legacyRecentTracksFilePath))
        {
            string? directory = Path.GetDirectoryName(resolvedFilePath);
            resolvedLegacyPath = string.IsNullOrWhiteSpace(directory)
                ? "RecentTracks.json"
                : Path.Combine(directory, "RecentTracks.json");
        }
        else
        {
            resolvedLegacyPath = legacyRecentTracksFilePath;
        }

        _filePathResolver = () => resolvedFilePath;
        _legacyRecentTracksFilePathResolver = () => resolvedLegacyPath;
    }

    /// <summary>
    /// データディレクトリプロバイダを指定して <see cref="JsonListeningStatsRepository"/> を初期化します（保存先変更へ動的追従）
    /// </summary>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonListeningStatsRepository(
        IDataDirectoryProvider dataDirectoryProvider,
        IJsonFileLockHelper? fileLockHelper = null)
    {
        ArgumentNullException.ThrowIfNull(dataDirectoryProvider);
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;
        _filePathResolver = () => dataDirectoryProvider.GetFilePath("ListeningStats.json");
        _legacyRecentTracksFilePathResolver = () => dataDirectoryProvider.GetFilePath("RecentTracks.json");
    }

    private bool IsCacheFresh(string filePath, FileMetadataSnapshot currentMetadata) =>
        _isLoaded
        && string.Equals(_lastLoadedFilePath, filePath, StringComparison.OrdinalIgnoreCase)
        && currentMetadata == _lastLoadedMetadata;

    /// <inheritdoc />
    public ListeningStatsData Load()
    {
        lock (_syncLock)
        {
            string filePath = _filePathResolver();
            FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(filePath);
            if (IsCacheFresh(filePath, currentMetadata))
            {
                return _cachedData.Clone();
            }

            using IDisposable fileLock = _fileLockHelper.AcquireLock(filePath);
            _cachedData = ReadOrMigrateDataLocked(filePath, _legacyRecentTracksFilePathResolver());
            _lastLoadedFilePath = filePath;
            _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(filePath);
            _isLoaded = true;
            return _cachedData.Clone();
        }
    }

    /// <inheritdoc />
    public void Save(ListeningStatsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_syncLock)
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = _fileLockHelper.AcquireLock(filePath);
            WriteDataLocked(filePath, data);
        }
    }

    /// <inheritdoc />
    public ListeningStatsData Update(Action<ListeningStatsData> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_syncLock)
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = _fileLockHelper.AcquireLock(filePath);
            ListeningStatsData data = ReadOrMigrateDataLocked(filePath, _legacyRecentTracksFilePathResolver());
            update(data);
            WriteDataLocked(filePath, data);
            return _cachedData.Clone();
        }
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        lock (_syncLock)
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = _fileLockHelper.AcquireLock(filePath);
            _legacyMigrationAttemptedPaths.Add(filePath);
            WriteDataLocked(filePath, new ListeningStatsData());
        }
    }

    /// <inheritdoc />
    IReadOnlyList<RecentTrackEntry> IRecentTrackRepository.Load()
    {
        ListeningStatsData data = Load();
        return ExtractRecentEntries(data);
    }

    /// <inheritdoc />
    void IRecentTrackRepository.Save(IReadOnlyList<RecentTrackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Update(data => SyncRecentEntries(data, entries));
    }

    /// <inheritdoc />
    IReadOnlyList<RecentTrackEntry> IRecentTrackRepository.Update(Action<List<RecentTrackEntry>> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_syncLock)
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = _fileLockHelper.AcquireLock(filePath);
            ListeningStatsData data = ReadOrMigrateDataLocked(filePath, _legacyRecentTracksFilePathResolver());
            List<RecentTrackEntry> entries = ExtractRecentEntries(data);
            update(entries);
            SyncRecentEntries(data, entries);
            WriteDataLocked(filePath, data);
            return entries;
        }
    }

    private ListeningStatsData ReadOrMigrateDataLocked(string filePath, string legacyFilePath)
    {
        if (File.Exists(filePath))
        {
            return ReadDataFromFile(filePath);
        }

        if (!_legacyMigrationAttemptedPaths.Contains(filePath) && File.Exists(legacyFilePath))
        {
            _legacyMigrationAttemptedPaths.Add(filePath);
            ListeningStatsData? migrated = TryMigrateFromLegacyRecentTracks(legacyFilePath);
            if (migrated != null)
            {
                WriteDataLocked(filePath, migrated);
                return migrated;
            }
        }

        return new ListeningStatsData();
    }

    private static ListeningStatsData ReadDataFromFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            ListeningStatsFileDto? dto = JsonSerializer.Deserialize<ListeningStatsFileDto>(json);
            if (dto == null)
            {
                return new ListeningStatsData();
            }

            var statsByPath = new Dictionary<string, TrackListeningStat>(StringComparer.OrdinalIgnoreCase);
            var statsList = new List<TrackListeningStat>(dto.TrackStats?.Count ?? 0);
            if (dto.TrackStats != null)
            {
                foreach (TrackListeningStatDto? item in dto.TrackStats)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
                    {
                        continue;
                    }

                    var stat = new TrackListeningStat(
                        item.FilePath,
                        item.Title,
                        item.Artist,
                        item.Album,
                        TimeSpan.FromSeconds(SanitizeNonNegative(item.DurationSeconds)),
                        Math.Max(0, item.PlayCount),
                        TimeSpan.FromSeconds(SanitizeNonNegative(item.TotalListeningSeconds)),
                        item.LastPlayedAt,
                        item.RecentPlayedAt);

                    if (!statsByPath.ContainsKey(stat.FilePath))
                    {
                        statsByPath[stat.FilePath] = stat;
                        statsList.Add(stat);
                    }
                }
            }

            var eventsList = new List<TrackPlayEvent>(dto.PlayEvents?.Count ?? 0);
            if (dto.PlayEvents != null)
            {
                foreach (TrackPlayEventDto? item in dto.PlayEvents)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
                    {
                        continue;
                    }

                    eventsList.Add(new TrackPlayEvent(
                        item.PlayedAt,
                        item.FilePath,
                        item.Title,
                        item.Artist,
                        item.Album,
                        TimeSpan.FromSeconds(SanitizeNonNegative(item.PlayedDurationSeconds))));
                }
            }

            return new ListeningStatsData(statsList, eventsList, dto.SchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or OverflowException)
        {
            return new ListeningStatsData();
        }
    }

    private static ListeningStatsData? TryMigrateFromLegacyRecentTracks(string legacyFilePath)
    {
        try
        {
            string json = File.ReadAllText(legacyFilePath);
            List<LegacyRecentTrackDto?>? legacyDtos = JsonSerializer.Deserialize<List<LegacyRecentTrackDto?>>(json);
            if (legacyDtos == null || legacyDtos.Count == 0)
            {
                return null;
            }

            var data = new ListeningStatsData();
            var statsMap = new Dictionary<string, TrackListeningStat>(StringComparer.OrdinalIgnoreCase);

            foreach (LegacyRecentTrackDto? dto in legacyDtos)
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.FilePath))
                {
                    continue;
                }

                TimeSpan duration = TimeSpan.FromSeconds(SanitizeNonNegative(dto.DurationSeconds));
                string title = string.IsNullOrWhiteSpace(dto.Title)
                    ? Path.GetFileNameWithoutExtension(dto.FilePath)
                    : dto.Title;
                string artist = string.IsNullOrWhiteSpace(dto.Artist) ? "Unknown Artist" : dto.Artist;
                string album = string.IsNullOrWhiteSpace(dto.Album) ? "Unknown Album" : dto.Album;

                if (!statsMap.TryGetValue(dto.FilePath, out TrackListeningStat? stat))
                {
                    stat = new TrackListeningStat(
                        dto.FilePath,
                        title,
                        artist,
                        album,
                        duration,
                        playCount: 1,
                        totalListeningDuration: duration,
                        lastPlayedAt: dto.PlayedAt,
                        recentPlayedAt: dto.PlayedAt);
                    statsMap[dto.FilePath] = stat;
                    data.TrackStats.Add(stat);
                }
                else
                {
                    stat.PlayCount++;
                    stat.TotalListeningDuration += duration;
                    if (dto.PlayedAt > stat.LastPlayedAt)
                    {
                        stat.LastPlayedAt = dto.PlayedAt;
                    }

                    if (!stat.RecentPlayedAt.HasValue || dto.PlayedAt > stat.RecentPlayedAt.Value)
                    {
                        stat.RecentPlayedAt = dto.PlayedAt;
                    }
                }

                data.PlayEvents.Add(new TrackPlayEvent(
                    dto.PlayedAt,
                    dto.FilePath,
                    title,
                    artist,
                    album,
                    duration));
            }

            return data.TrackStats.Count > 0 ? data : null;
        }
        catch (Exception ex) when (ex is JsonException or OverflowException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteDataLocked(string filePath, ListeningStatsData data)
    {
        var dto = new ListeningStatsFileDto
        {
            SchemaVersion = data.SchemaVersion > 0 ? data.SchemaVersion : ListeningStatsData.CurrentSchemaVersion,
            TrackStats = data.TrackStats.Select(stat => new TrackListeningStatDto
            {
                FilePath = stat.FilePath,
                Title = stat.Title,
                Artist = stat.Artist,
                Album = stat.Album,
                DurationSeconds = SanitizeNonNegative(stat.Duration.TotalSeconds),
                PlayCount = Math.Max(0, stat.PlayCount),
                TotalListeningSeconds = SanitizeNonNegative(stat.TotalListeningDuration.TotalSeconds),
                LastPlayedAt = stat.LastPlayedAt,
                RecentPlayedAt = stat.RecentPlayedAt,
            }).ToList<TrackListeningStatDto?>(),
            PlayEvents = data.PlayEvents.Select(ev => new TrackPlayEventDto
            {
                PlayedAt = ev.PlayedAt,
                FilePath = ev.FilePath,
                Title = ev.Title,
                Artist = ev.Artist,
                Album = ev.Album,
                PlayedDurationSeconds = SanitizeNonNegative(ev.PlayedDuration.TotalSeconds),
            }).ToList<TrackPlayEventDto?>(),
        };

        string json = JsonSerializer.Serialize(dto, JsonOptions);
        _fileLockHelper.WriteAllTextAtomic(filePath, json);

        _cachedData = data.Clone();
        _lastLoadedFilePath = filePath;
        _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(filePath);
        _isLoaded = true;
    }

    private static List<RecentTrackEntry> ExtractRecentEntries(ListeningStatsData data)
    {
        return data.TrackStats
            .Where(stat => stat.RecentPlayedAt.HasValue)
            .OrderByDescending(stat => stat.RecentPlayedAt!.Value)
            .Select(stat => stat.ToRecentTrackEntry()!)
            .ToList();
    }

    private static void SyncRecentEntries(ListeningStatsData data, IReadOnlyList<RecentTrackEntry> entries)
    {
        foreach (TrackListeningStat stat in data.TrackStats)
        {
            stat.RecentPlayedAt = null;
        }

        foreach (RecentTrackEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                continue;
            }

            TrackListeningStat? existing = data.FindTrackStat(entry.FilePath);
            if (existing == null)
            {
                existing = TrackListeningStat.FromTrack(entry.Track);
                existing.RecentPlayedAt = entry.PlayedAt;
                data.TrackStats.Add(existing);
            }
            else if (!existing.RecentPlayedAt.HasValue || entry.PlayedAt > existing.RecentPlayedAt.Value)
            {
                existing.RecordRecentPlayed(entry.Track, entry.PlayedAt);
            }
        }
    }

    private static double SanitizeNonNegative(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            return 0;
        }

        return seconds;
    }
}
