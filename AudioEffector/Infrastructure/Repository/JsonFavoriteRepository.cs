using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// お気に入り楽曲IDをローカルJSONファイルおよびメモリセットで管理するリポジトリ具象クラス
/// </summary>
public class JsonFavoriteRepository : IFavoriteRepository, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly HashSet<TrackId> _favoriteIds = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isLoaded;
    private bool _disposed;

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
        if (_isLoaded) return;

        if (!File.Exists(_filePath))
        {
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var guids = JsonSerializer.Deserialize<List<Guid>>(json);
            if (guids != null)
            {
                _favoriteIds.Clear();
                foreach (var guid in guids)
                {
                    _favoriteIds.Add(TrackId.From(guid));
                }
            }
        }
        catch
        {
            // 読み込みエラー時は空のまま開始
        }

        _isLoaded = true;
    }

    private async Task SaveToFileAsync(CancellationToken cancellationToken)
    {
        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var guids = _favoriteIds.Select(t => t.Value).ToList();
        string json = JsonSerializer.Serialize(guids, _jsonOptions);

        string tempPath = $"{_filePath}.tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// お気に入りに登録されているすべてのトラックIDを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>お気に入りトラックIDの読み取り専用セット</returns>
    public async Task<IReadOnlySet<TrackId>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return new HashSet<TrackId>(_favoriteIds);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたトラックIDをお気に入りに追加します
    /// </summary>
    /// <param name="trackId">追加対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task AddAsync(TrackId trackId, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_favoriteIds.Add(trackId))
            {
                await SaveToFileAsync(cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたトラックIDをお気に入りから削除します
    /// </summary>
    /// <param name="trackId">削除対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task RemoveAsync(TrackId trackId, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_favoriteIds.Remove(trackId))
            {
                await SaveToFileAsync(cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたトラックIDがお気に入りに登録されているかを確認します
    /// </summary>
    /// <param name="trackId">確認対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>登録されている場合はtrue、それ以外はfalse</returns>
    public async Task<bool> ContainsAsync(TrackId trackId, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _favoriteIds.Contains(trackId);
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
