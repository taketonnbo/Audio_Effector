using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// プレイリスト情報をローカルJSONファイルおよびメモリキャッシュで管理するリポジトリ具象クラス
/// </summary>
public class JsonPlaylistRepository : IPlaylistRepository, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string _legacyPlaylistIdNamespace = "AudioEffector.Playlist.Legacy.v1";
    private readonly Func<string> _filePathResolver;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly Dictionary<PlaylistId, UserPlaylist> _playlists = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _lastLoadedFilePath;
    private FileMetadataSnapshot _lastLoadedMetadata = FileMetadataSnapshot.Missing;
    private bool _isLoaded;
    private bool _disposed;

    /// <summary>
    /// 指定されたJSONファイルパスでJsonPlaylistRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">JSONファイル保存先パス（未指定時はAppData内のplaylists.json）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonPlaylistRepository(string? filePath = null, IJsonFileLockHelper? fileLockHelper = null)
    {
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;

        string resolvedFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
            resolvedFilePath = Path.Combine(appDir, "playlists.json");
        }
        else
        {
            resolvedFilePath = filePath;
        }

        _filePathResolver = () => resolvedFilePath;
    }

    /// <summary>
    /// データディレクトリプロバイダを指定してJsonPlaylistRepositoryを初期化します（保存先変更へ動的追従）
    /// </summary>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonPlaylistRepository(IDataDirectoryProvider dataDirectoryProvider, IJsonFileLockHelper? fileLockHelper = null)
    {
        ArgumentNullException.ThrowIfNull(dataDirectoryProvider);
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;
        _filePathResolver = () => dataDirectoryProvider.GetFilePath("playlists.json");
    }

    private bool IsCacheFresh(string filePath, FileMetadataSnapshot currentMetadata) =>
        _isLoaded
        && string.Equals(_lastLoadedFilePath, filePath, StringComparison.OrdinalIgnoreCase)
        && currentMetadata == _lastLoadedMetadata;

    private async Task EnsureLoadedAsync(string filePath, CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(filePath);
        if (IsCacheFresh(filePath, currentMetadata))
        {
            return;
        }

        using IDisposable fileLock = await _fileLockHelper
            .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await ReloadFromDiskCoreAsync(filePath, forceReload: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadFromDiskCoreAsync(string filePath, bool forceReload, CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(filePath);
        if (!forceReload && IsCacheFresh(filePath, currentMetadata))
        {
            return;
        }

        if (!currentMetadata.Exists)
        {
            _playlists.Clear();
            _lastLoadedFilePath = filePath;
            _lastLoadedMetadata = FileMetadataSnapshot.Missing;
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var dtos = JsonSerializer.Deserialize<List<PlaylistDto>>(json, _jsonOptions);
            _playlists.Clear();
            if (dtos != null)
            {
                DateTime fallbackTimestampUtc = currentMetadata.LastWriteTimeUtc > DateTime.MinValue
                    ? DateTime.SpecifyKind(currentMetadata.LastWriteTimeUtc, DateTimeKind.Utc)
                    : DateTime.UtcNow;
                var legacyFingerprintCounts = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var dto in dtos)
                {
                    if (dto == null)
                    {
                        continue;
                    }

                    var playlist = dto.ToEntity(fallbackTimestampUtc, legacyFingerprintCounts);
                    _playlists[playlist.Id] = playlist;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException) when (!forceReload)
        {
            // JSON 構文破損時は空のまま開始（I/O エラーや共有違反は呼び出し元へ伝播させて読み込み失敗と区別する）
            _playlists.Clear();
        }

        _lastLoadedFilePath = filePath;
        _lastLoadedMetadata = currentMetadata;
        _isLoaded = true;
    }

    private async Task<FileMetadataSnapshot> WritePlaylistsAtomicCoreAsync(
        string filePath,
        IEnumerable<UserPlaylist> candidatePlaylists,
        CancellationToken cancellationToken)
    {
        var dtos = candidatePlaylists.Select(PlaylistDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos, _jsonOptions);

        await _fileLockHelper.WriteAllTextAtomicAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        return _fileLockHelper.GetFileMetadata(filePath);
    }

    private void CommitCache(string filePath, Dictionary<PlaylistId, UserPlaylist> candidate, FileMetadataSnapshot updatedMetadata)
    {
        _playlists.Clear();
        foreach (KeyValuePair<PlaylistId, UserPlaylist> pair in candidate)
        {
            _playlists[pair.Key] = pair.Value;
        }

        _lastLoadedFilePath = filePath;
        _lastLoadedMetadata = updatedMetadata;
        _isLoaded = true;
    }

    /// <summary>
    /// 指定されたIDのプレイリストを取得します
    /// </summary>
    /// <param name="id">プレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プレイリストエンティティ（存在しない場合はnull）</returns>
    public async Task<UserPlaylist?> GetByIdAsync(PlaylistId id, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            await EnsureLoadedAsync(filePath, cancellationToken).ConfigureAwait(false);
            return _playlists.TryGetValue(id, out var playlist) ? playlist : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// すべてのプレイリストを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プレイリストのリスト</returns>
    public async Task<IReadOnlyList<UserPlaylist>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            await EnsureLoadedAsync(filePath, cancellationToken).ConfigureAwait(false);
            return _playlists.Values.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// プレイリストを保存または更新します
    /// </summary>
    /// <param name="playlist">保存対象のプレイリスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveAsync(UserPlaylist playlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
            var candidate = new Dictionary<PlaylistId, UserPlaylist>(_playlists)
            {
                [playlist.Id] = playlist,
            };

            FileMetadataSnapshot updatedMetadata = await WritePlaylistsAtomicCoreAsync(filePath, candidate.Values, cancellationToken).ConfigureAwait(false);
            CommitCache(filePath, candidate, updatedMetadata);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// すべてのプレイリストを指定された一覧で原子的に置き換えます
    /// </summary>
    /// <param name="playlists">保存対象のプレイリスト一覧</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task ReplaceAllAsync(IReadOnlyList<UserPlaylist> playlists, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlists);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var candidate = new Dictionary<PlaylistId, UserPlaylist>(playlists.Count);
            foreach (UserPlaylist playlist in playlists)
            {
                if (playlist != null)
                {
                    candidate[playlist.Id] = playlist;
                }
            }

            FileMetadataSnapshot updatedMetadata = await WritePlaylistsAtomicCoreAsync(filePath, candidate.Values, cancellationToken).ConfigureAwait(false);
            CommitCache(filePath, candidate, updatedMetadata);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたIDのプレイリストを削除します
    /// </summary>
    /// <param name="id">削除対象のプレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task DeleteAsync(PlaylistId id, CancellationToken cancellationToken = default)
    {
        _ = await DeleteAndReturnAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserPlaylist?> DeleteAndReturnAsync(PlaylistId id, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
            var candidate = new Dictionary<PlaylistId, UserPlaylist>(_playlists);
            if (!candidate.Remove(id, out UserPlaylist? removed))
            {
                return null;
            }

            FileMetadataSnapshot updatedMetadata = await WritePlaylistsAtomicCoreAsync(filePath, candidate.Values, cancellationToken).ConfigureAwait(false);
            CommitCache(filePath, candidate, updatedMetadata);
            return removed;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<UserPlaylist?> UpdateAsync(PlaylistId id, Func<UserPlaylist, bool> update, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            using var fileLock = await _fileLockHelper.AcquireLockAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
            if (!_playlists.TryGetValue(id, out var playlist))
            {
                return null;
            }

            if (update(playlist))
            {
                try
                {
                    FileMetadataSnapshot updatedMetadata = await WritePlaylistsAtomicCoreAsync(filePath, _playlists.Values, cancellationToken).ConfigureAwait(false);
                    _lastLoadedFilePath = filePath;
                    _lastLoadedMetadata = updatedMetadata;
                    _isLoaded = true;
                }
                catch
                {
                    // update デリゲートがインメモリインスタンスを変更済みの可能性があるため、キャッシュを破棄して次回必ずディスクから再読込させる
                    _playlists.Clear();
                    _lastLoadedFilePath = null;
                    _lastLoadedMetadata = FileMetadataSnapshot.Missing;
                    _isLoaded = false;
                    throw;
                }
            }

            return playlist;
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

    private sealed class PlaylistDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Guid>? TrackIds { get; set; } = [];
        public List<string>? TrackPaths { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static PlaylistDto FromEntity(UserPlaylist entity) => new()
        {
            Id = entity.Id.Value,
            Name = entity.Name,
            TrackIds = entity.TrackIds.Select(t => t.Value).ToList(),
            TrackPaths = entity.TrackPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [],
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };

        public UserPlaylist ToEntity(DateTime fallbackTimestampUtc, Dictionary<string, int> legacyFingerprintCounts)
        {
            List<string> normalizedPaths = TrackPaths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList() ?? [];
            List<TrackId> normalizedTrackIds = TrackIds?
                .Where(g => g != Guid.Empty)
                .Select(TrackId.From)
                .ToList() ?? [];

            PlaylistId playlistId = Id != Guid.Empty
                ? PlaylistId.From(Id)
                : CreateDeterministicLegacyId(Name, normalizedPaths, legacyFingerprintCounts);

            DateTime createdAt = CreatedAt == default ? fallbackTimestampUtc : CreatedAt;
            DateTime updatedAt = UpdatedAt == default ? createdAt : UpdatedAt;

            var playlist = new UserPlaylist(
                id: playlistId,
                name: Name,
                trackIds: normalizedTrackIds,
                createdAt: createdAt,
                updatedAt: updatedAt)
            {
                TrackPaths = normalizedPaths,
            };

            return playlist;
        }

        private static PlaylistId CreateDeterministicLegacyId(
            string? name,
            IReadOnlyList<string> trackPaths,
            Dictionary<string, int> legacyFingerprintCounts)
        {
            string normalizedName = string.IsNullOrWhiteSpace(name) ? "New Playlist" : name.Trim();
            string baseKey = string.Join(
                "\n",
                [_legacyPlaylistIdNamespace, normalizedName, .. trackPaths.Select(p => p.Trim())]);

            legacyFingerprintCounts.TryGetValue(baseKey, out int occurrence);
            legacyFingerprintCounts[baseKey] = occurrence + 1;

            string uniqueKey = occurrence == 0 ? baseKey : $"{baseKey}\n#{occurrence}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(uniqueKey));
            byte[] guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);

            // RFC 4122 Version 5 / Variant 1 準拠ビットを設定
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

            var guid = new Guid(guidBytes);
            return PlaylistId.From(guid);
        }
    }
}
