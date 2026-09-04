using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// アプリケーション設定（テーマ、ウィンドウ状態、各種オプション）の読み込み・保存を統括するアプリケーションサービス
/// </summary>
public class SettingsApplicationService : ISettingsService
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly string _settingsFilePath;

    /// <summary>
    /// SettingsApplicationServiceを初期化します
    /// </summary>
    /// <param name="settingsRepository">設定リポジトリ</param>
    public SettingsApplicationService(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));

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

    /// <summary>
    /// アプリケーション設定全体を読み込みます
    /// </summary>
    /// <returns></returns>
    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// アプリケーション設定全体を保存します
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Silent fail
        }
    }

    /// <summary>
    /// アプリケーション設定全体を非同期で読み込みます
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// アプリケーション設定全体を非同期で保存します
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
        }
        catch
        {
            // Silent fail
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

        string? rawValue = await _settingsRepository.GetValueAsync(key, null, cancellationToken);
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

        await _settingsRepository.SetValueAsync(key, stringValue, cancellationToken);
    }

    /// <summary>
    /// すべての設定キーと値のディクショナリを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値ディクショナリ</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _settingsRepository.GetAllAsync(cancellationToken);
    }
}
