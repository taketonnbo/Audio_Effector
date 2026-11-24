using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly Dictionary<PlaylistId, UserPlaylist> _playlists = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
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

        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "playlists.json");
        }
        else
        {
            _filePath = filePath;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        if (_isLoaded && currentMetadata == _lastLoadedMetadata)
        {
            return;
        }

        using IDisposable fileLock = await _fileLockHelper
            .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await ReloadFromDiskCoreAsync(forceReload: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadFromDiskCoreAsync(bool forceReload, CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        if (!forceReload && _isLoaded && currentMetadata == _lastLoadedMetadata)
        {
            return;
        }

        if (!currentMetadata.Exists)
        {
            _playlists.Clear();
            _lastLoadedMetadata = FileMetadataSnapshot.Missing;
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var dtos = JsonSerializer.Deserialize<List<PlaylistDto>>(json);
            _playlists.Clear();
            if (dtos != null)
            {
                foreach (var dto in dtos)
                {
                    var playlist = dto.ToEntity();
                    _playlists[playlist.Id] = playlist;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 読み込みエラー時は空のまま開始
            _playlists.Clear();
        }

        _lastLoadedMetadata = currentMetadata;
        _isLoaded = true;
    }

    private async Task SaveToFileCoreAsync(CancellationToken cancellationToken)
    {
        var dtos = _playlists.Values.Select(PlaylistDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos, _jsonOptions);

        await _fileLockHelper.WriteAllTextAtomicAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(_filePath);
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
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
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
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
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
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            _playlists[playlist.Id] = playlist;
            await SaveToFileCoreAsync(cancellationToken).ConfigureAwait(false);
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
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            if (_playlists.Remove(id))
            {
                await SaveToFileCoreAsync(cancellationToken).ConfigureAwait(false);
            }
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
        public List<Guid> TrackIds { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static PlaylistDto FromEntity(UserPlaylist entity) => new()
        {
            Id = entity.Id.Value,
            Name = entity.Name,
            TrackIds = entity.TrackIds.Select(t => t.Value).ToList(),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        public UserPlaylist ToEntity() => new(
            id: PlaylistId.From(Id),
            name: Name,
            trackIds: TrackIds.Select(TrackId.From),
            createdAt: CreatedAt,
            updatedAt: UpdatedAt);
    }
}
