using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// アプリケーション設定キーと値をローカルJSONファイルおよびメモリディクショナリで管理するリポジトリ具象クラス
/// </summary>
public class JsonSettingsRepository : ISettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isLoaded;
    private bool _disposed;

    /// <summary>
    /// 指定されたJSONファイルパスでJsonSettingsRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">JSONファイル保存先パス（未指定時はAppData内のsettings.json）</param>
    public JsonSettingsRepository(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "settings.json");
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
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                _settings.Clear();
                foreach (var (k, v) in dict)
                {
                    _settings[k] = v;
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

        string json = JsonSerializer.Serialize(_settings, _jsonOptions);

        string tempPath = $"{_filePath}.tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>
    /// 指定されたキーの設定値を取得します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="defaultValue">デフォルト値</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値文字列</returns>
    public async Task<string?> GetValueAsync(string key, string? defaultValue = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _settings.TryGetValue(key, out var val) ? val : defaultValue;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたキーの設定値を保存します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="value">設定値文字列</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SetValueAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (value == null)
            {
                _settings.Remove(key);
            }
            else
            {
                _settings[key] = value;
            }

            await SaveToFileAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// すべての設定キーと値のディクショナリを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値ディクショナリ</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return new Dictionary<string, string>(_settings);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたキーの設定を削除します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_settings.Remove(key))
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
