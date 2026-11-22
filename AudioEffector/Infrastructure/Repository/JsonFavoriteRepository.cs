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

    /// <summary>
    /// JSONファイルの初回読み込みを行います。一時的なI/O失敗時は_isLoadedをfalseのまま維持しfalseを返します
    /// </summary>
    private async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return true;
        }

        if (!File.Exists(_filePath))
        {
            _isLoaded = true;
            return true;
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

                            // 起動時UIブロック回避のため同期TagLib I/Oは行わず高速移行し、後続のReconcileでメタデータ補完する
                            var migratedEntry = FavoriteTrackEntry.FromLegacyFilePath(rawValue.Trim());
                            AddWithoutDuplicate(_entries, migratedEntry);
                            migratedFromLegacy = true;
                        }
                    }
                }
            }

            _isLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は _isLoaded を確定させず再スローし、既存データの誤上書きを防止
            throw;
        }
        catch (JsonException)
        {
            // 破損JSONの場合は空コレクションとして安全に起動確定
            _entries.Clear();
            _isLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 一時的なファイルロック・アクセス障害時は _isLoaded を false のまま維持して次回再試行可能にする
            return false;
        }

        if (migratedFromLegacy)
        {
            try
            {
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 自動マイグレーション保存失敗時もアプリ起動は継続
            }
        }

        return true;
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
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 一時ファイル削除失敗時は元の例外を優先
            }

            throw;
        }
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false))
            {
                return Array.Empty<FavoriteTrackEntry>();
            }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entry);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 一時的I/O失敗で既存データを読み込めなかった場合は、既存ファイルの上書き破壊を防ぐため保存を行わない
            if (!await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(track);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entries);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            _isLoaded = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ContainsAsync(Track track, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(track);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

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
