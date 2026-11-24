using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// アプリケーション設定キーと値をローカルJSONファイルおよびメモリディクショナリで管理するリポジトリ具象クラス
/// </summary>
public class JsonSettingsRepository : ISettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private SettingsFileDocument _document = new();
    private FileMetadataSnapshot _lastLoadedMetadata = FileMetadataSnapshot.Missing;
    private bool _isLoaded;
    private bool _disposed;

    /// <summary>
    /// 指定されたJSONファイルパスでJsonSettingsRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">JSONファイル保存先パス（未指定時はAppData内のsettings.json）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonSettingsRepository(string? filePath = null, IJsonFileLockHelper? fileLockHelper = null)
    {
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;

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
            _document = new SettingsFileDocument();
            _settings.Clear();
            _lastLoadedMetadata = FileMetadataSnapshot.Missing;
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            _document = SettingsFileDocument.Parse(json);
            _settings.Clear();
            foreach (var (k, v) in _document.Values)
            {
                _settings[k] = v;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch when (!forceReload)
        {
            // 読み込みエラー時は空のまま開始
            _settings.Clear();
        }

        _lastLoadedMetadata = currentMetadata;
        _isLoaded = true;
    }

    private async Task SaveToFileCoreAsync(CancellationToken cancellationToken)
    {
        _isLoaded = false;
        _document.Values = _settings;
        string json = JsonSerializer.Serialize(_document, _jsonOptions);
        await _fileLockHelper.WriteAllTextAtomicAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        _isLoaded = true;
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

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
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
    public Task SetValueAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        return UpdateValueAsync(key, _ => value, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateValueAsync(string key, Func<string?, string?> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(key);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            _settings.TryGetValue(key, out var previous);
            var value = update(previous);
            if (value == previous) return false;

            if (value == null)
            {
                _settings.Remove(key);
            }
            else
            {
                _settings[key] = value;
            }

            await SaveToFileCoreAsync(cancellationToken).ConfigureAwait(false);
            return true;
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
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
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

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            if (_settings.Remove(key))
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
}
