using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioEffector.Application.Common;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace AudioEffector.Infrastructure.Logging;

/// <summary>
/// NLog のログ出力レベルおよびファイル出力先ディレクトリを実行時に動的更新する構成マネージャ実装
/// </summary>
public sealed class LogConfigurationManager : ILogConfigurationManager
{
    /// <summary>
    /// サポートするログ出力レベル一覧（詳細度が高い順＋Off）
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedLogLevels =
    [
        "Trace",
        "Debug",
        "Info",
        "Warn",
        "Error",
        "Off",
    ];

    private const string DEFAULT_LOG_LEVEL = "Info";
    private const string LOG_FILE_TARGET_NAME = "logfile";
    private const string LOG_FILE_NAME = "app.log";
    private const string ARCHIVE_FILE_NAME_PATTERN = "app.{#}.log";

    private readonly object _sync = new();
    private readonly Func<LoggingConfiguration?> _configurationAccessor;
    private readonly Action _reconfigureAction;
    private string _currentLogDirectory;
    private string _currentLogLevel = DEFAULT_LOG_LEVEL;
    private bool _isCustomLogDirectory;

    /// <summary>
    /// 既定の NLog <see cref="LogManager"/> を対象として <see cref="LogConfigurationManager"/> を初期化します
    /// </summary>
    public LogConfigurationManager()
        : this(
            defaultLogDirectory: null,
            configurationAccessor: null,
            reconfigureAction: null)
    {
    }

    /// <summary>
    /// テストおよび検証用に既定ログディレクトリ・NLog 構成取得デリゲートを指定して初期化します
    /// </summary>
    /// <param name="defaultLogDirectory">既定のログディレクトリパス（未指定時は実行ディレクトリ配下の logs）</param>
    /// <param name="configurationAccessor">対象の <see cref="LoggingConfiguration"/> 取得デリゲート</param>
    /// <param name="reconfigureAction">構成反映後に呼び出す再構成アクション</param>
    internal LogConfigurationManager(
        string? defaultLogDirectory,
        Func<LoggingConfiguration?>? configurationAccessor,
        Action? reconfigureAction)
    {
        string rawDefault = string.IsNullOrWhiteSpace(defaultLogDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : defaultLogDirectory;
        DefaultLogDirectory = NormalizeDirectoryPath(rawDefault);
        _currentLogDirectory = DefaultLogDirectory;
        _configurationAccessor = configurationAccessor ?? (() => LogManager.Configuration);
        _reconfigureAction = reconfigureAction ?? LogManager.ReconfigExistingLoggers;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableLogLevels => SupportedLogLevels;

    /// <inheritdoc />
    public string DefaultLogDirectory { get; }

    /// <inheritdoc />
    public string CurrentLogDirectory
    {
        get
        {
            lock (_sync)
            {
                return _currentLogDirectory;
            }
        }
    }

    /// <inheritdoc />
    public string CurrentLogLevel
    {
        get
        {
            lock (_sync)
            {
                return _currentLogLevel;
            }
        }
    }

    /// <inheritdoc />
    public bool IsCustomLogDirectory
    {
        get
        {
            lock (_sync)
            {
                return _isCustomLogDirectory;
            }
        }
    }

    /// <inheritdoc />
    public string NormalizeLogLevel(string? logLevel)
    {
        if (string.IsNullOrWhiteSpace(logLevel))
        {
            return DEFAULT_LOG_LEVEL;
        }

        string trimmed = logLevel.Trim();
        foreach (string supported in SupportedLogLevels)
        {
            if (string.Equals(supported, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return DEFAULT_LOG_LEVEL;
    }

    /// <inheritdoc />
    public LogConfigurationApplyResult ApplyConfiguration(string? logLevel, string? logDirectoryPath)
    {
        string normalizedLevel = NormalizeLogLevel(logLevel);
        (string effectiveDirectory, bool isCustom, bool usedFallback, string? warningMessage) = ResolveWritableLogDirectory(logDirectoryPath);

        lock (_sync)
        {
            LoggingConfiguration? config = _configurationAccessor();
            if (config != null)
            {
                ApplyTargetDirectory(config, effectiveDirectory);
                ApplyLoggingLevel(config, normalizedLevel);
                _reconfigureAction();
            }

            _currentLogLevel = normalizedLevel;
            _currentLogDirectory = effectiveDirectory;
            _isCustomLogDirectory = isCustom;
        }

        return new LogConfigurationApplyResult(
            Succeeded: true,
            AppliedLogLevel: normalizedLevel,
            EffectiveLogDirectory: effectiveDirectory,
            UsedFallbackDirectory: usedFallback,
            WarningMessage: warningMessage);
    }

    private (string EffectiveDirectory, bool IsCustom, bool UsedFallback, string? WarningMessage) ResolveWritableLogDirectory(string? requestedPath)
    {
        bool requestedCustom = !string.IsNullOrWhiteSpace(requestedPath);
        if (requestedCustom)
        {
            try
            {
                string normalizedRequested = NormalizeDirectoryPath(requestedPath!);
                bool isDefault = string.Equals(normalizedRequested, DefaultLogDirectory, StringComparison.OrdinalIgnoreCase);
                string candidate = isDefault ? DefaultLogDirectory : normalizedRequested;
                EnsureDirectoryWritable(candidate);
                return (candidate, !isDefault, false, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                TryEnsureDirectoryExists(DefaultLogDirectory);
                return (
                    DefaultLogDirectory,
                    false,
                    true,
                    $"指定されたログ保存先フォルダへ書き込めないため、既定フォルダへフォールバックしました: {ex.Message}");
            }
        }

        TryEnsureDirectoryExists(DefaultLogDirectory);
        return (DefaultLogDirectory, false, false, null);
    }

    private static void ApplyTargetDirectory(LoggingConfiguration config, string effectiveDirectory)
    {
        FileTarget? fileTarget = config.FindTargetByName<FileTarget>(LOG_FILE_TARGET_NAME)
            ?? config.AllTargets.OfType<FileTarget>().FirstOrDefault();

        if (fileTarget == null)
        {
            return;
        }

        fileTarget.FileName = Layout.FromLiteral(Path.Combine(effectiveDirectory, LOG_FILE_NAME));

        // ArchiveFileName は NLog の {#} プレースホルダを活かしつつディレクトリ名内の ${ をエスケープする
        string escapedDir = effectiveDirectory.Replace("${", "$${", StringComparison.Ordinal);
        fileTarget.ArchiveFileName = new SimpleLayout(Path.Combine(escapedDir, ARCHIVE_FILE_NAME_PATTERN));
    }

    private static void ApplyLoggingLevel(LoggingConfiguration config, string normalizedLevel)
    {
        LogLevel nlogLevel = ParseNLogLevel(normalizedLevel);

        foreach (LoggingRule rule in config.LoggingRules)
        {
            if (nlogLevel == LogLevel.Off)
            {
                rule.DisableLoggingForLevels(LogLevel.Trace, LogLevel.Fatal);
            }
            else
            {
                rule.SetLoggingLevels(nlogLevel, LogLevel.Fatal);
            }
        }
    }

    private static LogLevel ParseNLogLevel(string normalizedLevel) => normalizedLevel switch
    {
        "Trace" => LogLevel.Trace,
        "Debug" => LogLevel.Debug,
        "Info" => LogLevel.Info,
        "Warn" => LogLevel.Warn,
        "Error" => LogLevel.Error,
        "Off" => LogLevel.Off,
        _ => LogLevel.Info,
    };

    private static void EnsureDirectoryWritable(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        string probePath = Path.Combine(directoryPath, $".log_write_probe_{Guid.NewGuid():N}.tmp");
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

    private static void TryEnsureDirectoryExists(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
        }
        catch
        {
            // 既定フォルダ作成時の例外でアプリ全体をクラッシュさせない
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path.Trim());
        string? root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
