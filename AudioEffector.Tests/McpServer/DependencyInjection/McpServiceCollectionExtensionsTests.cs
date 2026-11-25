using System.IO;
using System.Xml.Linq;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Repositories;
using AudioEffector.McpServer;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.DependencyInjection;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace AudioEffector.Tests.McpServer.DependencyInjection;

public sealed class McpServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _tempDataDir;

    public McpServiceCollectionExtensionsTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorMcpTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDataDir))
        {
            Directory.Delete(_tempDataDir, recursive: true);
        }
    }

    /// <summary>
    /// AddAudioEffectorMcpCore により WPF 非依存で主要アプリケーションサービス・リポジトリ・IUiDispatcher を解決できることを検証します。
    /// </summary>
    [Fact]
    public void AddAudioEffectorMcpCore_データディレクトリ指定時_主要サービスをWPF非依存で解決できる()
    {
        // Arrange
        var sut = new ServiceCollection();
        var options = new McpServerOptions
        {
            DataDirectory = _tempDataDir,
            MinLogLevel = LogLevel.Warning,
        };

        // Act
        sut.AddAudioEffectorMcpCore(options);
        using ServiceProvider provider = sut.BuildServiceProvider();

        var dataDirectoryProvider = provider.GetRequiredService<IDataDirectoryProvider>();
        var uiDispatcher = provider.GetRequiredService<IUiDispatcher>();
        var trackRepository = provider.GetRequiredService<ITrackRepository>();
        var playlistRepository = provider.GetRequiredService<IPlaylistRepository>();
        var favoriteRepository = provider.GetRequiredService<IFavoriteRepository>();
        var settingsRepository = provider.GetRequiredService<ISettingsRepository>();
        var recentTrackRepository = provider.GetRequiredService<IRecentTrackRepository>();
        var libraryService = provider.GetRequiredService<LibraryApplicationService>();
        var playlistService = provider.GetRequiredService<PlaylistApplicationService>();
        var equalizerService = provider.GetRequiredService<EqualizerApplicationService>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        var ipcReadinessProbe = provider.GetRequiredService<IIpcReadinessProbe>();
        var appLauncher = provider.GetRequiredService<IAppLauncher>();

        // Assert
        Assert.True(dataDirectoryProvider.IsCustomDirectory);
        Assert.Equal(_tempDataDir, dataDirectoryProvider.DataDirectory);
        Assert.IsType<ImmediateUiDispatcher>(uiDispatcher);
        Assert.NotNull(trackRepository);
        Assert.NotNull(playlistRepository);
        Assert.NotNull(favoriteRepository);
        Assert.NotNull(settingsRepository);
        Assert.NotNull(recentTrackRepository);
        Assert.NotNull(libraryService);
        Assert.NotNull(playlistService);
        Assert.NotNull(equalizerService);
        Assert.NotNull(settingsService);
        Assert.IsType<NamedPipeIpcReadinessProbe>(ipcReadinessProbe);
        Assert.IsType<WpfAppLauncher>(appLauncher);
    }

    /// <summary>
    /// StdioMcpHostRunner.BuildHost で構築されたホストのコンソールロガーが全ログレベルを stderr へ強制していることを検証します。
    /// </summary>
    [Fact]
    public void BuildHost_既定設定_全ログレベルがstderr出力へ強制される()
    {
        // Arrange
        var options = new McpServerOptions
        {
            DataDirectory = _tempDataDir,
            MinLogLevel = LogLevel.Debug,
        };

        // Act
        using IHost sut = StdioMcpHostRunner.BuildHost(options);
        var consoleOptions = sut.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value;

        // Assert
        Assert.Equal(LogLevel.Trace, consoleOptions.LogToStandardErrorThreshold);
    }

    /// <summary>
    /// StdioMcpHostRunner.RunAsync がキャンセル要求時に例外を投げず終了コード 0 でグレースフルシャットダウンすることを検証します。
    /// </summary>
    [Fact]
    public async Task RunAsync_キャンセル要求時_例外なく終了コード0でシャットダウンする()
    {
        // Arrange
        var sut = new StdioMcpHostRunner();
        var options = new McpServerOptions
        {
            DataDirectory = _tempDataDir,
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        int exitCode = await sut.RunAsync(options, cts.Token);

        // Assert
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// nlog.mcp.config が logs/mcp-server.log へ分離されており stdout を汚染する Console ターゲットを含まないことを検証します。
    /// </summary>
    [Fact]
    public void NLogMcpConfig_出力設定確認_Consoleターゲットを含まず専用ログファイルへ分離されている()
    {
        // Arrange
        string configPath = Path.Combine(AppContext.BaseDirectory, Program.NLOG_CONFIG_FILE_NAME);

        // Act
        Assert.True(File.Exists(configPath), $"設定ファイルが見つかりません: {configPath}");
        XDocument doc = XDocument.Load(configPath);
        XNamespace ns = "http://www.nlog-project.org/schemas/NLog.xsd";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var targets = doc.Descendants(ns + "target").ToList();

        // Assert
        Assert.Contains(targets, t => string.Equals(t.Attribute("fileName")?.Value, "logs/mcp-server.log", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, t =>
        {
            string? type = t.Attribute(xsi + "type")?.Value;
            return string.Equals(type, "Console", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "ColoredConsole", StringComparison.OrdinalIgnoreCase);
        });
    }
}
