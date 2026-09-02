using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// プレイリスト情報をローカルJSONファイルおよびメモリキャッシュで管理するリポジトリ具象クラス
/// </summary>
public class JsonPlaylistRepository : IPlaylistRepository
{
    private readonly string _filePath;
    private readonly Dictionary<PlaylistId, UserPlaylist> _playlists = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isLoaded;

    /// <summary>
    /// 指定されたJSONファイルパスでJsonPlaylistRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">JSONファイル保存先パス（未指定時はAppData内のplaylists.json）</param>
    public JsonPlaylistRepository(string? filePath = null)
    {
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
        if (_isLoaded) return;

        if (!File.Exists(_filePath))
        {
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<PlaylistDto>>(json);
            if (dtos != null)
            {
                _playlists.Clear();
                foreach (var dto in dtos)
                {
                    var playlist = dto.ToEntity();
                    _playlists[playlist.Id] = playlist;
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

        var dtos = _playlists.Values.Select(PlaylistDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });

        string tempPath = $"{_filePath}.tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// 指定されたIDのプレイリストを取得します
    /// </summary>
    /// <param name="id">プレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プレイリストエンティティ（存在しない場合はnull）</returns>
    public async Task<UserPlaylist?> GetByIdAsync(PlaylistId id, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
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
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
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

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _playlists[playlist.Id] = playlist;
            await SaveToFileAsync(cancellationToken);
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
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_playlists.Remove(id))
            {
                await SaveToFileAsync(cancellationToken);
            }
        }
        finally
        {
            _semaphore.Release();
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
