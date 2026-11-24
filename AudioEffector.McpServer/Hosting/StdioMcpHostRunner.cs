using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.Hosting;

/// <summary>
/// 標準入出力（<c>stdio</c>）トランスポートで MCP サーバホストを起動・実行するランナーです。
/// すべてのログ出力を <c>stderr</c> へ強制し、<c>stdout</c> の JSON-RPC 2.0 ストリーム汚染を防止します。
/// </summary>
public sealed class StdioMcpHostRunner : IMcpHostRunner
{
    /// <summary>
    /// <c>stdio</c> トランスポートで構成された <see cref="IHost"/> を構築します。
    /// </summary>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <returns>構築済みの <see cref="IHost"/> インスタンス。</returns>
    public static IHost BuildHost(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Array.Empty<string>(),
        });

        // stdout 汚染防止: 既存プロバイダを全クリアし、全ログレベルを stderr 出力へ強制する
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(options.MinLogLevel);
        builder.Logging.AddConsole(consoleOptions =>
        {
            consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddAudioEffectorMcpCore(options);
        builder.Services.AddMcpServer().WithStdioServerTransport();

        return builder.Build();
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(McpServerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        using IHost host = BuildHost(options);
        try
        {
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // SIGINT / SIGTERM / 外部キャンセルによるグレースフルシャットダウン
            return 0;
        }
    }
}
