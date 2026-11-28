using System;
using System.IO;
using AudioEffector.Infrastructure.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Logging;

/// <summary>
/// <see cref="LogConfigurationManager"/> による NLog ログ出力レベル・出力先ディレクトリの動的更新とエラー時フォールバックを検証するテストクラス
/// </summary>
public sealed class LogConfigurationManagerTests : IDisposable
{
    private readonly string _tempRoot;

    public LogConfigurationManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"AudioEffector_LogConfig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    private sealed class NLogConfigScope : IDisposable
    {
        public LoggingConfiguration Config { get; }
        public FileTarget FileTarget { get; }
        public DebuggerTarget DebugTarget { get; }
        public LoggingRule Rule { get; }

        public NLogConfigScope()
        {
            Config = new LoggingConfiguration();
            FileTarget = new FileTarget("logfile")
            {
                FileName = "logs/app.log",
                ArchiveFileName = "logs/app.{#}.log",
            };
            DebugTarget = new DebuggerTarget("debugger");
            Config.AddTarget(FileTarget);
            Config.AddTarget(DebugTarget);

            Rule = new LoggingRule("*", LogLevel.Info, FileTarget);
            Rule.Targets.Add(DebugTarget);
            Config.LoggingRules.Add(Rule);
        }

        public void Dispose()
        {
            FileTarget.Dispose();
            DebugTarget.Dispose();
        }
    }

    /// <summary>
    /// Trace / Debug / Info / Warn / Error / Off の各レベルを指定した場合、NLog の LoggingRule 最小レベルが動的に更新され再構成アクションが呼ばれることを検証します。
    /// </summary>
    [Theory]
    [InlineData("Trace", true, true, true, true, true)]
    [InlineData("Debug", false, true, true, true, true)]
    [InlineData("Info", false, false, true, true, true)]
    [InlineData("Warn", false, false, false, true, true)]
    [InlineData("Error", false, false, false, false, true)]
    [InlineData("Off", false, false, false, false, false)]
    public void ApplyConfiguration_各ログレベル指定時_LoggingRuleの有効レベルが動的更新されること(
        string level,
        bool expectTrace,
        bool expectDebug,
        bool expectInfo,
        bool expectWarn,
        bool expectError)
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogs");
        using var scope = new NLogConfigScope();
        int reconfigCount = 0;
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => reconfigCount++);

        // Act
        var result = sut.ApplyConfiguration(level, null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.UsedFallbackDirectory);
        Assert.Equal(level, result.AppliedLogLevel);
        Assert.Equal(level, sut.CurrentLogLevel);
        Assert.Equal(1, reconfigCount);
        Assert.Equal(expectTrace, scope.Rule.IsLoggingEnabledForLevel(LogLevel.Trace));
        Assert.Equal(expectDebug, scope.Rule.IsLoggingEnabledForLevel(LogLevel.Debug));
        Assert.Equal(expectInfo, scope.Rule.IsLoggingEnabledForLevel(LogLevel.Info));
        Assert.Equal(expectWarn, scope.Rule.IsLoggingEnabledForLevel(LogLevel.Warn));
        Assert.Equal(expectError, scope.Rule.IsLoggingEnabledForLevel(LogLevel.Error));
    }

    /// <summary>
    /// カスタムログディレクトリを指定した場合、ディレクトリが自動作成され FileTarget の FileName と ArchiveFileName が更新されることを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_カスタムログ保存先指定時_FileTarget出力先とアーカイブパスが更新されること()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogs2");
        string customLogsDir = Path.Combine(_tempRoot, "CustomLogs");
        using var scope = new NLogConfigScope();
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => { });

        // Act
        var result = sut.ApplyConfiguration("Debug", customLogsDir);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.UsedFallbackDirectory);
        Assert.True(sut.IsCustomLogDirectory);
        Assert.Equal(customLogsDir, result.EffectiveLogDirectory);
        Assert.Equal(customLogsDir, sut.CurrentLogDirectory);
        Assert.True(Directory.Exists(customLogsDir));

        string renderedFileName = scope.FileTarget.FileName.Render(LogEventInfo.CreateNullEvent());
        Assert.Equal(Path.Combine(customLogsDir, "app.log"), renderedFileName);
        Assert.NotNull(scope.FileTarget.ArchiveFileName);
        string renderedArchiveFileName = scope.FileTarget.ArchiveFileName!.Render(LogEventInfo.CreateNullEvent());
        Assert.StartsWith(customLogsDir, renderedArchiveFileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".log", renderedArchiveFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 不正なログレベル文字列を指定した場合、既定の Info へ安全にフォールバックされることを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_不正なログレベル文字列指定時_Infoへフォールバックすること()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogs3");
        using var scope = new NLogConfigScope();
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => { });

        // Act
        var result = sut.ApplyConfiguration("UnknownLevel", null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("Info", result.AppliedLogLevel);
        Assert.False(scope.Rule.IsLoggingEnabledForLevel(LogLevel.Debug));
        Assert.True(scope.Rule.IsLoggingEnabledForLevel(LogLevel.Info));
    }

    /// <summary>
    /// 無効なパスをログ保存先に指定した場合、例外でクラッシュせず既定ディレクトリへフォールバックすることを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_無効な保存先パス指定時_既定ログディレクトリへフォールバックすること()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogs4");
        using var scope = new NLogConfigScope();
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => { });

        // Act
        var result = sut.ApplyConfiguration("Warn", "Z:\0invalid\\logs");

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.UsedFallbackDirectory);
        Assert.False(sut.IsCustomLogDirectory);
        Assert.Equal(defaultLogsDir, result.EffectiveLogDirectory);
        Assert.False(string.IsNullOrWhiteSpace(result.WarningMessage));
        Assert.Equal(Path.Combine(defaultLogsDir, "app.log"), scope.FileTarget.FileName.Render(LogEventInfo.CreateNullEvent()));
    }

    /// <summary>
    /// Off から Debug へ復帰させた際、すべての有効レベル（Debug 以上）が再度有効化されることを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_OffからDebugへ復帰時_Debug以上のレベルが再有効化されること()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogsOffResume");
        using var scope = new NLogConfigScope();
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => { });

        // Act
        sut.ApplyConfiguration("Off", null);
        bool enabledDuringOff = scope.Rule.IsLoggingEnabledForLevel(LogLevel.Error);
        var resumed = sut.ApplyConfiguration("Debug", null);

        // Assert
        Assert.False(enabledDuringOff);
        Assert.True(resumed.Succeeded);
        Assert.Equal("Debug", sut.CurrentLogLevel);
        Assert.False(scope.Rule.IsLoggingEnabledForLevel(LogLevel.Trace));
        Assert.True(scope.Rule.IsLoggingEnabledForLevel(LogLevel.Debug));
        Assert.True(scope.Rule.IsLoggingEnabledForLevel(LogLevel.Error));
    }

    /// <summary>
    /// ディレクトリ名に NLog レイアウト記法（${...}）を含むパスを指定した場合でも、例外を送出せず安全に適用されることを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_レイアウト記法を含むパス指定時_NLog例外なくリテラル適用されること()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogsDollar");
        string dollarLogsDir = Path.Combine(_tempRoot, "Logs_${custom_var}");
        using var scope = new NLogConfigScope();
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => { });

        // Act
        var result = sut.ApplyConfiguration("Info", dollarLogsDir);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.UsedFallbackDirectory);
        Assert.Equal(dollarLogsDir, result.EffectiveLogDirectory);
        Assert.Equal(Path.Combine(dollarLogsDir, "app.log"), scope.FileTarget.FileName.Render(LogEventInfo.CreateNullEvent()));
        Assert.Equal(Path.Combine(dollarLogsDir, "app.log"), scope.FileTarget.ArchiveFileName!.Render(LogEventInfo.CreateNullEvent()));
    }

    /// <summary>
    /// LoggingConfiguration が null の環境でも例外を投げずに状態更新できることを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_NLog構成がnullの場合_例外なく状態更新されること()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogsNullCfg");
        int reconfigCount = 0;
        var sut = new LogConfigurationManager(defaultLogsDir, () => null, () => reconfigCount++);

        // Act
        var result = sut.ApplyConfiguration("Trace", null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("Trace", sut.CurrentLogLevel);
        Assert.Equal(defaultLogsDir, sut.CurrentLogDirectory);
        Assert.Equal(0, reconfigCount);
    }

    /// <summary>
    /// fallbackToDefaultOnError に false を指定して無効パスを渡した場合、現行のカスタムディレクトリを変更せず失敗を返すことを検証します。
    /// </summary>
    [Fact]
    public void ApplyConfiguration_フォールバック無効で無効パス指定時_現行設定を維持して失敗を返すこと()
    {
        // Arrange
        string defaultLogsDir = Path.Combine(_tempRoot, "DefaultLogsNoFallback");
        string validCustomDir = Path.Combine(_tempRoot, "ValidCustomLogs");
        using var scope = new NLogConfigScope();
        var sut = new LogConfigurationManager(defaultLogsDir, () => scope.Config, () => { });
        sut.ApplyConfiguration("Debug", validCustomDir);

        // Act
        var result = sut.ApplyConfiguration("Debug", "Z:\0invalid\\logs", fallbackToDefaultOnError: false);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.UsedFallbackDirectory);
        Assert.True(sut.IsCustomLogDirectory);
        Assert.Equal(validCustomDir, sut.CurrentLogDirectory);
        Assert.Equal(Path.Combine(validCustomDir, "app.log"), scope.FileTarget.FileName.Render(LogEventInfo.CreateNullEvent()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // 一時ディレクトリ削除失敗はテスト結果へ影響させない
            }
        }
    }
}
