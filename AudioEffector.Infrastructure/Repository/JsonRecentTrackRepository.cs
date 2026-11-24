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
/// 最近再生した曲の履歴をローカルJSONファイル（RecentTracks.json）へ永続化するリポジトリ実装
/// </summary>
public sealed class JsonRecentTrackRepository : IRecentTrackRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly object _syncLock = new();
    private List<RecentTrackEntry> _cachedEntries = new();
    private FileMetadataSnapshot _lastLoadedMetadata = FileMetadataSnapshot.Missing;
    private bool _isLoaded;

    private sealed class RecentTrackDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public DateTime PlayedAt { get; set; }
    }

    /// <summary>
    /// 指定されたJSONファイルパスでJsonRecentTrackRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">保存先ファイルパス（未指定時はAppData内のRecentTracks.json）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonRecentTrackRepository(string? filePath = null, IJsonFileLockHelper? fileLockHelper = null)
    {
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            _filePath = Path.Combine(appDir, "RecentTracks.json");
        }
        else
        {
            _filePath = filePath;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecentTrackEntry> Load()
    {
        lock (_syncLock)
        {
            FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(_filePath);
            if (_isLoaded && currentMetadata == _lastLoadedMetadata)
            {
                return _cachedEntries.ToList();
            }

            using IDisposable fileLock = _fileLockHelper.AcquireLock(_filePath);

            try
            {
                _cachedEntries = ReadEntries();
            }
            catch (Exception ex) when (ex is JsonException or OverflowException)
            {
                _cachedEntries = new();
            }
            _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(_filePath);
            _isLoaded = true;
            return _cachedEntries.ToList();
        }
    }

    private List<RecentTrackEntry> ReadEntries()
    {
        if (!File.Exists(_filePath))
        {
            return new();
        }

        string json = File.ReadAllText(_filePath);
        var dtos = JsonSerializer.Deserialize<List<RecentTrackDto?>>(json);
        var result = new List<RecentTrackEntry>(dtos?.Count ?? 0);
        if (dtos != null)
        {
            foreach (var dto in dtos)
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.FilePath))
                {
                    continue;
                }

                var track = new Track
                {
                    FilePath = dto.FilePath,
                    Title = string.IsNullOrWhiteSpace(dto.Title) ? Path.GetFileNameWithoutExtension(dto.FilePath) : dto.Title,
                    Artist = string.IsNullOrWhiteSpace(dto.Artist) ? "Unknown Artist" : dto.Artist,
                    Album = string.IsNullOrWhiteSpace(dto.Album) ? "Unknown Album" : dto.Album,
                    Duration = TimeSpan.FromSeconds(Math.Max(0, dto.DurationSeconds)),
                };
                result.Add(new RecentTrackEntry(track, dto.PlayedAt));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<RecentTrackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_syncLock)
        {
            using var fileLock = _fileLockHelper.AcquireLock(_filePath);
            WriteEntries(entries);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecentTrackEntry> Update(Action<List<RecentTrackEntry>> update)
    {
        lock (_syncLock)
        {
            using var fileLock = _fileLockHelper.AcquireLock(_filePath);
            var entries = ReadEntries();
            update(entries);
            WriteEntries(entries);
            return entries;
        }
    }

    private void WriteEntries(IReadOnlyList<RecentTrackEntry> entries)
    {
        var dtos = entries.Select(e => new RecentTrackDto
        {
            FilePath = e.FilePath,
            Title = e.Title,
            Artist = e.Artist,
            Album = e.Album,
            DurationSeconds = e.Duration.TotalSeconds,
            PlayedAt = e.PlayedAt,
        }).ToList();

        string json = JsonSerializer.Serialize(dtos, JsonOptions);
        _fileLockHelper.WriteAllTextAtomic(_filePath, json);

        _cachedEntries = entries.ToList();
        _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        _isLoaded = true;
    }
}
