using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// お気に入り楽曲エントリをローカルJSONファイル（favorites.json）およびメモリキャッシュで管理するリポジトリ具象クラス
/// </summary>
public class JsonFavoriteRepository : IFavoriteRepository, IDisposable
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath;
    private readonly List<FavoriteTrackEntry> _entries = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isLoaded;
    private bool _disposed;

    private sealed class FavoriteTrackDto
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public uint TrackNumber { get; set; }
        public double DurationSeconds { get; set; }
        public string LastKnownFilePath { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; }

        public static FavoriteTrackDto FromEntity(FavoriteTrackEntry entity) => new()
        {
            Title = entity.Title,
            Artist = entity.Artist,
            Album = entity.Album,
            TrackNumber = entity.TrackNumber,
            DurationSeconds = entity.DurationSeconds,
            LastKnownFilePath = entity.LastKnownFilePath,
            AddedAt = entity.AddedAt
        };

        public FavoriteTrackEntry ToEntity() => new(
            title: Title,
            artist: Artist,
            album: Album,
            trackNumber: TrackNumber,
            durationSeconds: DurationSeconds,
            lastKnownFilePath: LastKnownFilePath,
            addedAt: AddedAt);
    }

    /// <summary>
    /// 指定されたJSONファイルパスでJsonFavoriteRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">JSONファイル保存先パス（未指定時はAppData内のfavorites.json）</param>
    public JsonFavoriteRepository(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "favorites.json");
        }
        else
        {
            _filePath = filePath;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return;
        }

        if (!File.Exists(_filePath))
        {
            _isLoaded = true;
            return;
        }

        bool migratedFromLegacy = false;

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    _entries.Clear();
                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            var dto = element.Deserialize<FavoriteTrackDto>(JSON_OPTIONS);
                            if (dto == null)
                            {
                                continue;
                            }

                            var entry = dto.ToEntity();
                            if (string.IsNullOrWhiteSpace(entry.Title) && string.IsNullOrWhiteSpace(entry.LastKnownFilePath))
                            {
                                continue;
                            }

                            AddWithoutDuplicate(_entries, entry);
                        }
                        else if (element.ValueKind == JsonValueKind.String)
                        {
                            string? rawValue = element.GetString();
                            if (string.IsNullOrWhiteSpace(rawValue) || Guid.TryParse(rawValue, out _))
                            {
                                // 旧JsonFavoriteRepositoryの孤立TrackId(Guid)配列はパス・メタデータを持たないためスキップ
                                continue;
                            }

                            var migratedEntry = CreateEntryFromLegacyPath(rawValue.Trim());
                            AddWithoutDuplicate(_entries, migratedEntry);
                            migratedFromLegacy = true;
                        }
                    }
                }
            }
        }
        catch
        {
            // 読み込みエラー（破損JSON等）時は空コレクションのまま安全に起動
            _entries.Clear();
        }

        _isLoaded = true;

        if (migratedFromLegacy)
        {
            try
            {
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 自動マイグレーション保存失敗時もアプリ起動は継続
            }
        }
    }

    private async Task SaveToFileAsync(CancellationToken cancellationToken)
    {
        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var dtos = _entries.Select(FavoriteTrackDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos, JSON_OPTIONS);

        string tempPath = $"{_filePath}.tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static FavoriteTrackEntry CreateEntryFromLegacyPath(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                string title = !string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                    ? tagFile.Tag.Title
                    : Path.GetFileNameWithoutExtension(filePath);
                string artist = !string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer)
                    ? tagFile.Tag.FirstPerformer
                    : (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstAlbumArtist) ? tagFile.Tag.FirstAlbumArtist : "Unknown Artist");
                string album = !string.IsNullOrWhiteSpace(tagFile.Tag.Album)
                    ? tagFile.Tag.Album
                    : "Unknown Album";

                return new FavoriteTrackEntry(
                    title: title,
                    artist: artist,
                    album: album,
                    trackNumber: tagFile.Tag.Track,
                    durationSeconds: tagFile.Properties.Duration.TotalSeconds,
                    lastKnownFilePath: filePath,
                    addedAt: DateTime.Now);
            }
            catch
            {
                // タグ解析失敗時はパスのみ保持し、後のライブラリ照合で補完する
            }
        }

        return FavoriteTrackEntry.FromLegacyFilePath(filePath);
    }

    private static void AddWithoutDuplicate(List<FavoriteTrackEntry> list, FavoriteTrackEntry candidate)
    {
        int existingIndex = list.FindIndex(existing => FavoriteTrackMatcher.AreSameEntry(existing, candidate));
        if (existingIndex >= 0)
        {
            var existing = list[existingIndex];
            // 既存のAddedAtを維持しつつ、より完全なメタデータを持つ方を優先する
            list[existingIndex] = !existing.HasSufficientMetadata && candidate.HasSufficientMetadata
                ? new FavoriteTrackEntry(
                    candidate.Title,
                    candidate.Artist,
                    candidate.Album,
                    candidate.TrackNumber,
                    candidate.DurationSeconds,
                    string.IsNullOrWhiteSpace(candidate.LastKnownFilePath) ? existing.LastKnownFilePath : candidate.LastKnownFilePath,
                    existing.AddedAt)
                : existing;
            return;
        }

        list.Add(candidate);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FavoriteTrackEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _entries.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddOrUpdateAsync(FavoriteTrackEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            int existingIndex = _entries.FindIndex(existing => FavoriteTrackMatcher.AreSameEntry(existing, entry));
            if (existingIndex >= 0)
            {
                var existing = _entries[existingIndex];
                var merged = new FavoriteTrackEntry(
                    title: string.IsNullOrWhiteSpace(entry.Title) ? existing.Title : entry.Title,
                    artist: string.IsNullOrWhiteSpace(entry.Artist) ? existing.Artist : entry.Artist,
                    album: string.IsNullOrWhiteSpace(entry.Album) ? existing.Album : entry.Album,
                    trackNumber: entry.TrackNumber > 0 ? entry.TrackNumber : existing.TrackNumber,
                    durationSeconds: entry.DurationSeconds > 0 ? entry.DurationSeconds : existing.DurationSeconds,
                    lastKnownFilePath: string.IsNullOrWhiteSpace(entry.LastKnownFilePath) ? existing.LastKnownFilePath : entry.LastKnownFilePath,
                    addedAt: existing.AddedAt);
                _entries[existingIndex] = merged;
            }
            else
            {
                _entries.Add(entry);
            }

            await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            int removedCount = _entries.RemoveAll(entry => FavoriteTrackMatcher.IsMatchForRemoval(entry, track));
            if (removedCount > 0)
            {
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAllAsync(IReadOnlyList<FavoriteTrackEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            _entries.Clear();
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                AddWithoutDuplicate(_entries, entry);
            }

            await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ContainsAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return FavoriteTrackMatcher.FindBestMatchingEntry(track, _entries) != null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// アンマネージドリソースおよびマネージドリソースを解放します
    /// </summary>
    /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _semaphore.Dispose();
            }
            _disposed = true;
        }
    }
}
