using System.IO;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioEffector.Tests.McpServer.Hosting;

public sealed class StdioMcpHostRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public StdioMcpHostRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorStdioHostTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// クライアントの作業ディレクトリに破損した appsettings.json が存在しても、
    /// ContentRootPath が AppContext.BaseDirectory に固定されているため例外なくホスト構築できることを検証します。
    /// </summary>
    [Fact]
    public void BuildHost_作業ディレクトリに破損したappsettingsJsonが存在する場合_BaseDirectoryを参照し例外なく構築できる()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), "{ invalid json !!!");
        string originalCurrentDirectory = Environment.CurrentDirectory;
        var options = new McpServerOptions
        {
            DataDirectory = _tempDir,
        };

        try
        {
            Environment.CurrentDirectory = _tempDir;

            // Act
            using IHost sut = StdioMcpHostRunner.BuildHost(options);
            var env = sut.Services.GetRequiredService<IHostEnvironment>();

            // Assert
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(env.ContentRootPath)));
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    /// <summary>
    /// 親プロセスから継承された Logging__* 環境変数が存在する場合でも、
    /// McpServerOptions.MinLogLevel が下限として優先適用されることを検証します。
    /// </summary>
    [Fact]
    public void BuildHost_親環境変数にLogging設定がある場合_MinLogLevelが優先適用される()
    {
        // Arrange
        const string defaultEnvKey = "Logging__LogLevel__Default";
        const string lifetimeEnvKey = "Logging__LogLevel__Microsoft.Hosting.Lifetime";
        string? originalDefault = Environment.GetEnvironmentVariable(defaultEnvKey);
        string? originalLifetime = Environment.GetEnvironmentVariable(lifetimeEnvKey);

        try
        {
            Environment.SetEnvironmentVariable(defaultEnvKey, "Information");
            Environment.SetEnvironmentVariable(lifetimeEnvKey, "Information");

            var noneOptions = new McpServerOptions
            {
                DataDirectory = _tempDir,
                MinLogLevel = LogLevel.None,
            };
            var errorOptions = new McpServerOptions
            {
                DataDirectory = _tempDir,
                MinLogLevel = LogLevel.Error,
            };

            // Act
            using IHost noneHost = StdioMcpHostRunner.BuildHost(noneOptions);
            using IHost errorHost = StdioMcpHostRunner.BuildHost(errorOptions);
            ILogger noneLogger = noneHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.Hosting.Lifetime");
            ILogger errorLogger = errorHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.Hosting.Lifetime");

            // Assert
            Assert.False(noneLogger.IsEnabled(LogLevel.Information));
            Assert.False(noneLogger.IsEnabled(LogLevel.Critical));
            Assert.False(errorLogger.IsEnabled(LogLevel.Warning));
            Assert.True(errorLogger.IsEnabled(LogLevel.Error));
        }
        finally
        {
            Environment.SetEnvironmentVariable(defaultEnvKey, originalDefault);
            Environment.SetEnvironmentVariable(lifetimeEnvKey, originalLifetime);
        }
    }
}
