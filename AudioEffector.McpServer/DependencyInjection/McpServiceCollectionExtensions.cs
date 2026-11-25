using System.Reflection;
using AudioEffector.Application;
using AudioEffector.Infrastructure;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Gateways;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.DependencyInjection;

/// <summary>
/// MCP サーバのトランスポート非依存コアサービスおよび既存 DI 基盤を一括登録する拡張メソッドを提供します。
/// </summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// MCP サーバ名。
    /// </summary>
    public const string SERVER_NAME = "AudioEffector.McpServer";

    /// <summary>
    /// AudioEffector のインフラ層・アプリケーション層および MCP サーバのツール／リソース自動登録を DI コンテナへ構成します。
    /// トランスポート（stdio / HTTP）の登録は呼び出し元のホストランナー側で行います。
    /// </summary>
    /// <param name="services">登録対象のサービスコレクション。</param>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <returns>サービス登録後のサービスコレクション。</returns>
    public static IServiceCollection AddAudioEffectorMcpCore(this IServiceCollection services, McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton<IIpcReadinessProbe, NamedPipeIpcReadinessProbe>();
        services.AddSingleton<IAppLaunchCoordinator, SessionFileAppLaunchCoordinator>();
        services.AddSingleton<IAppLauncher, WpfAppLauncher>();

        // IUiDispatcher は AddInfrastructureServices 内の TryAddSingleton により既定の ImmediateUiDispatcher が登録される
        services.AddInfrastructureServices(options.DataDirectory);
        services.AddApplicationServices();

        Assembly mcpAssembly = typeof(McpServiceCollectionExtensions).Assembly;
        string serverVersion = GetServerVersion(mcpAssembly);

        services
            .AddMcpServer(serverOptions =>
            {
                serverOptions.ServerInfo = new()
                {
                    Name = SERVER_NAME,
                    Version = serverVersion,
                };
            })
            .WithToolsFromAssembly(mcpAssembly)
            .WithResourcesFromAssembly(mcpAssembly);

        return services;
    }

    /// <summary>
    /// 指定されたアセンブリのバージョン文字列を取得します。
    /// </summary>
    /// <param name="assembly">対象アセンブリ。</param>
    /// <returns>バージョン文字列。</returns>
    internal static string GetServerVersion(Assembly assembly)
    {
        string? infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            int plusIndex = infoVersion.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
