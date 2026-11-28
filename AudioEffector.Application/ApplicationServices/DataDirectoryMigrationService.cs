using System;
using System.Collections.Generic;
using System.IO;
using AudioEffector.Application.Common;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 各種データファイル（プリセット・プレイリスト・お気に入り・再生履歴・ライブラリ）の保存先ディレクトリ変更およびデータ移行を安全に実行するアプリケーションサービス
/// </summary>
public sealed class DataDirectoryMigrationService : IDataDirectoryMigrationService
{
    /// <summary>
    /// 保存先ディレクトリ管理対象のデータファイル名一覧（settings.json は既定フォルダに残すため含まない）
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultManagedFileNames =
    [
        "tracks.json",
        "playlists.json",
        "favorites.json",
        "ListeningStats.json",
        "RecentTracks.json",
        "presets.json",
    ];

    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly ISettingsService _settingsService;
    private readonly IJsonFileLockHelper _fileLockHelper;

    /// <summary>
    /// <see cref="DataDirectoryMigrationService"/> を初期化します
    /// </summary>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ</param>
    /// <param name="settingsService">アプリケーション設定サービス</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー</param>
    public DataDirectoryMigrationService(
        IDataDirectoryProvider dataDirectoryProvider,
        ISettingsService settingsService,
        IJsonFileLockHelper fileLockHelper)
    {
        _dataDirectoryProvider = dataDirectoryProvider ?? throw new ArgumentNullException(nameof(dataDirectoryProvider));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _fileLockHelper = fileLockHelper ?? throw new ArgumentNullException(nameof(fileLockHelper));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ManagedFileNames => DefaultManagedFileNames;

    /// <inheritdoc />
    public bool HasExistingDataFiles(string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return false;
        }

        foreach (string fileName in ManagedFileNames)
        {
            string path = Path.Combine(sourceDirectory, fileName);
            if (File.Exists(path))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool IsSameDirectory(string? pathA, string? pathB)
    {
        if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
        {
            return false;
        }

        try
        {
            string normalizedA = DataDirectoryProvider.NormalizeDirectoryPath(pathA);
            string normalizedB = DataDirectoryProvider.NormalizeDirectoryPath(pathB);
            return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public DataDirectoryChangeResult ChangeDataDirectory(string? targetDirectory, DataMigrationMode migrationMode)
    {
        string sourceDirectory = _dataDirectoryProvider.DataDirectory;
        string defaultDirectory = _dataDirectoryProvider.DefaultDataDirectory;
        string? previousPersistedValue = _dataDirectoryProvider.IsCustomDirectory ? sourceDirectory : null;

        string resolvedTarget;
        bool isDefaultTarget;
        try
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                resolvedTarget = defaultDirectory;
                isDefaultTarget = true;
            }
            else
            {
                resolvedTarget = DataDirectoryProvider.NormalizeDirectoryPath(targetDirectory);
                isDefaultTarget = string.Equals(resolvedTarget, defaultDirectory, StringComparison.OrdinalIgnoreCase);
                if (isDefaultTarget)
                {
                    resolvedTarget = defaultDirectory;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DataDirectoryChangeResult(
                Succeeded: false,
                Changed: false,
                EffectivePath: sourceDirectory,
                MigratedFileCount: 0,
                ErrorMessage: $"指定されたフォルダパスが無効です: {ex.Message}");
        }

        if (IsSameDirectory(sourceDirectory, resolvedTarget))
        {
            return new DataDirectoryChangeResult(
                Succeeded: true,
                Changed: false,
                EffectivePath: sourceDirectory,
                MigratedFileCount: 0);
        }

        // 移行先ディレクトリの作成および書き込み可能性の事前検証
        try
        {
            EnsureDirectoryWritable(resolvedTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new DataDirectoryChangeResult(
                Succeeded: false,
                Changed: false,
                EffectivePath: sourceDirectory,
                MigratedFileCount: 0,
                ErrorMessage: $"指定されたフォルダへのアクセスまたは書き込みに失敗しました: {ex.Message}");
        }

        int migratedCount = 0;
        var movedSourceFiles = new List<string>();
        var createdDestinationFiles = new List<string>();
        var overwrittenBackups = new List<(string DestinationPath, string BackupPath)>();

        if (migrationMode is DataMigrationMode.Copy or DataMigrationMode.Move)
        {
            try
            {
                foreach (string fileName in ManagedFileNames)
                {
                    string sourcePath = Path.Combine(sourceDirectory, fileName);
                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(resolvedTarget, fileName);
                    bool destinationExisted = File.Exists(destinationPath);
                    string? backupPath = null;
                    if (destinationExisted)
                    {
                        backupPath = $"{destinationPath}.bak_{Guid.NewGuid():N}";
                        File.Copy(destinationPath, backupPath, overwrite: true);
                        overwrittenBackups.Add((destinationPath, backupPath));
                    }

                    CopyFileWithLocks(sourcePath, destinationPath);
                    if (!destinationExisted)
                    {
                        createdDestinationFiles.Add(destinationPath);
                    }

                    migratedCount++;
                    if (migrationMode == DataMigrationMode.Move)
                    {
                        movedSourceFiles.Add(sourcePath);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                RollbackCopiedFiles(createdDestinationFiles, overwrittenBackups);
                return new DataDirectoryChangeResult(
                    Succeeded: false,
                    Changed: false,
                    EffectivePath: sourceDirectory,
                    MigratedFileCount: 0,
                    ErrorMessage: $"データファイルの移行に失敗しました: {ex.Message}");
            }
        }

        string? persistedValue = isDefaultTarget ? null : resolvedTarget;
        bool settingsUpdated = false;
        try
        {
            _settingsService.UpdateSettings(settings => settings.DataDirectoryPath = persistedValue);
            settingsUpdated = true;
            _dataDirectoryProvider.UpdateDataDirectory(persistedValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or NotSupportedException or ArgumentException)
        {
            if (settingsUpdated)
            {
                try
                {
                    _settingsService.UpdateSettings(settings => settings.DataDirectoryPath = previousPersistedValue);
                }
                catch
                {
                    // 設定ロールバック失敗時もアプリクラッシュは防止する
                }
            }

            RollbackCopiedFiles(createdDestinationFiles, overwrittenBackups);
            return new DataDirectoryChangeResult(
                Succeeded: false,
                Changed: false,
                EffectivePath: sourceDirectory,
                MigratedFileCount: 0,
                ErrorMessage: $"データ保存先設定の反映に失敗しました: {ex.Message}");
        }

        CleanupBackups(overwrittenBackups);

        // Move モード時は切替確定後に元ファイルを安全に削除する（削除失敗時は新先データを保護しつつ警告メッセージを返す）
        string? cleanupWarning = null;
        if (migrationMode == DataMigrationMode.Move && movedSourceFiles.Count > 0)
        {
            var failedDeletions = new List<string>();
            foreach (string sourcePath in movedSourceFiles)
            {
                try
                {
                    using IDisposable sourceLock = _fileLockHelper.AcquireLock(sourcePath);
                    if (File.Exists(sourcePath))
                    {
                        File.Delete(sourcePath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
                {
                    failedDeletions.Add(Path.GetFileName(sourcePath));
                }
            }

            if (failedDeletions.Count > 0)
            {
                cleanupWarning = $"保存先の切り替えと移行は完了しましたが、元のファイルの一部を削除できませんでした: {string.Join(", ", failedDeletions)}";
            }
        }

        return new DataDirectoryChangeResult(
            Succeeded: true,
            Changed: true,
            EffectivePath: _dataDirectoryProvider.DataDirectory,
            MigratedFileCount: migratedCount,
            ErrorMessage: cleanupWarning);
    }

    private void CopyFileWithLocks(string sourcePath, string destinationPath)
    {
        // デッドロック防止のためパス辞書順でロックを取得する
        bool sourceFirst = string.Compare(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) <= 0;
        string firstLockPath = sourceFirst ? sourcePath : destinationPath;
        string secondLockPath = sourceFirst ? destinationPath : sourcePath;

        using IDisposable firstLock = _fileLockHelper.AcquireLock(firstLockPath);
        using IDisposable secondLock = _fileLockHelper.AcquireLock(secondLockPath);

        if (!File.Exists(sourcePath))
        {
            return;
        }

        string content = File.ReadAllText(sourcePath);
        _fileLockHelper.WriteAllTextAtomic(destinationPath, content);
    }

    private static void RollbackCopiedFiles(
        List<string> createdDestinationFiles,
        List<(string DestinationPath, string BackupPath)> overwrittenBackups)
    {
        foreach (string createdPath in createdDestinationFiles)
        {
            try
            {
                if (File.Exists(createdPath))
                {
                    File.Delete(createdPath);
                }
            }
            catch
            {
                // ロールバック時の個別削除失敗は他ファイルの復元継続を妨げない
            }
        }

        foreach ((string destinationPath, string backupPath) in overwrittenBackups)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, destinationPath, overwrite: true);
                    File.Delete(backupPath);
                }
            }
            catch
            {
                // ロールバック時の個別復元失敗は他ファイルの復元継続を妨げない
            }
        }
    }

    private static void CleanupBackups(List<(string DestinationPath, string BackupPath)> overwrittenBackups)
    {
        foreach ((_, string backupPath) in overwrittenBackups)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch
            {
                // 一時バックアップの掃除失敗は切替成功へ影響させない
            }
        }
    }

    private static void EnsureDirectoryWritable(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        string probePath = Path.Combine(directoryPath, $".write_probe_{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probePath))
            {
            }
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }
}
