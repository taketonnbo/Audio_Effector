using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private readonly object _syncLock = new();

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
    public JsonRecentTrackRepository(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
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
            if (!File.Exists(_filePath))
            {
                return Array.Empty<RecentTrackEntry>();
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                var dtos = JsonSerializer.Deserialize<List<RecentTrackDto?>>(json);
                if (dtos == null)
                {
                    return Array.Empty<RecentTrackEntry>();
                }

                var result = new List<RecentTrackEntry>(dtos.Count);
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

                return result;
            }
            catch
            {
                return Array.Empty<RecentTrackEntry>();
            }
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<RecentTrackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_syncLock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

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
                string tempPath = $"{_filePath}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch
            {
                // 永続化失敗時はアプリ動作を止めない
            }
        }
    }
}
