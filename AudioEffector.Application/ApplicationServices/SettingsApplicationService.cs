using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// アプリケーション設定（テーマ、ウィンドウ状態、各種オプション）の読み込み・保存を統括するアプリケーションサービス
/// </summary>
public class SettingsApplicationService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly ISettingsRepository _settingsRepository;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly string _settingsFilePath;
    private bool _disposed;

    /// <summary>
    /// SettingsApplicationServiceを初期化します
    /// </summary>
    /// <param name="settingsRepository">設定リポジトリ</param>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ（未指定または既定時は既存互換パスを使用）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー</param>
    public SettingsApplicationService(
        ISettingsRepository settingsRepository,
        IDataDirectoryProvider? dataDirectoryProvider,
        IJsonFileLockHelper fileLockHelper)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _fileLockHelper = fileLockHelper ?? throw new ArgumentNullException(nameof(fileLockHelper));

        if (dataDirectoryProvider is { IsCustomDirectory: true })
        {
            Directory.CreateDirectory(dataDirectoryProvider.DataDirectory);
            _settingsFilePath = dataDirectoryProvider.GetFilePath("settings.json");
        }
        else
        {
#if DEBUG
            var folderName = "AudioEffector_Debug";
#else
            var folderName = "AudioEffector";
#endif
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                folderName);
            Directory.CreateDirectory(appDataPath);
            _settingsFilePath = Path.Combine(appDataPath, "settings.json");
        }
    }

    /// <summary>
    /// アプリケーション設定全体を読み込みます
    /// </summary>
    /// <returns>読み込んだアプリケーション設定</returns>
    public AppSettings LoadSettings()
    {
        try
        {
            using IDisposable fileLock = _fileLockHelper.AcquireLock(_settingsFilePath);

            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsFilePath);
            return SettingsFileDocument.Parse(json).Settings;
        }
        catch (Exception ex) when (ex is not FileLockTimeoutException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// アプリケーション設定全体を保存します
    /// </summary>
    /// <param name="settings">保存する設定情報</param>
    public void SaveSettings(AppSettings settings)
    {
        WriteSettings(document => document.Settings = settings);
    }

    /// <inheritdoc />
    public void UpdateSettings(Action<AppSettings> update)
    {
        WriteSettings(document => update(document.Settings));
    }

    private void WriteSettings(Action<SettingsFileDocument> apply)
    {
        try
        {
            using IDisposable fileLock = _fileLockHelper.AcquireLock(_settingsFilePath);
            var document = File.Exists(_settingsFilePath)
                ? SettingsFileDocument.Parse(File.ReadAllText(_settingsFilePath))
                : new SettingsFileDocument();
            apply(document);
            var json = JsonSerializer.Serialize(document, _jsonOptions);
            _fileLockHelper.WriteAllTextAtomic(_settingsFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
        }
    }

    /// <summary>
    /// アプリケーション設定全体を非同期で読み込みます
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>読み込んだ設定情報を含むタスク</returns>
    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using IDisposable fileLock = await _fileLockHelper.AcquireLockAsync(_settingsFilePath, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
            return SettingsFileDocument.Parse(json).Settings;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not FileLockTimeoutException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// アプリケーション設定全体を非同期で保存します
    /// </summary>
    /// <param name="settings">保存する設定</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>保存処理を表す非同期タスク</returns>
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        return WriteSettingsAsync(document => document.Settings = settings, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateSettingsAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
    {
        return WriteSettingsAsync(document => update(document.Settings), cancellationToken);
    }

    private async Task WriteSettingsAsync(Action<SettingsFileDocument> apply, CancellationToken cancellationToken)
    {
        try
        {
            using IDisposable fileLock = await _fileLockHelper.AcquireLockAsync(_settingsFilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            var document = File.Exists(_settingsFilePath)
                ? SettingsFileDocument.Parse(await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false))
                : new SettingsFileDocument();
            apply(document);
            var json = JsonSerializer.Serialize(document, _jsonOptions);
            await _fileLockHelper.WriteAllTextAtomicAsync(_settingsFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(ex.Message);
        }
    }

    /// <summary>
    /// 指定されたキーの設定値を取得します（型変換対応）
    /// </summary>
    /// <typeparam name="T">設定値の型</typeparam>
    /// <param name="key">設定キー</param>
    /// <param name="defaultValue">デフォルト値</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値</returns>
    public async Task<T?> GetSettingAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        string? rawValue = await _settingsRepository.GetValueAsync(key, null, cancellationToken).ConfigureAwait(false);
        if (rawValue == null)
        {
            return defaultValue;
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)rawValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(rawValue);
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 指定されたキーの設定値を保存します（型シリアライズ対応）
    /// </summary>
    /// <typeparam name="T">設定値の型</typeparam>
    /// <param name="key">設定キー</param>
    /// <param name="value">設定値</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        string? stringValue = value is string strVal
            ? strVal
            : (value != null ? JsonSerializer.Serialize(value) : null);

        await _settingsRepository.SetValueAsync(key, stringValue, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// すべての設定キーと値のディクショナリを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値ディクショナリ</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _settingsRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
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
            _disposed = true;
        }
    }
}
