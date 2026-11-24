using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Hosting;
using NLog;

namespace AudioEffector.McpServer;

/// <summary>
/// <c>AudioEffector.McpServer</c> コンソールホストのエントリポイントです。
/// </summary>
public static class Program
{
    /// <summary>
    /// MCP サーバ専用 NLog 設定ファイル名。
    /// </summary>
    public const string NLOG_CONFIG_FILE_NAME = "nlog.mcp.config";

    /// <summary>
    /// 引数解析エラー時の終了コード。
    /// </summary>
    public const int EXIT_CODE_INVALID_ARGUMENTS = 1;

    /// <summary>
    /// アプリケーションのメインエントリポイントです。
    /// </summary>
    /// <param name="args">コマンドライン引数。</param>
    /// <returns>プロセス終了コード。</returns>
    public static async Task<int> Main(string[] args)
    {
        // DI 構築および NLogService 解決前に専用 NLog 設定を明示ロードし、WPF 本体 (logs/app.log) とのロック競合を防ぐ
        string nlogConfigPath = Path.Combine(AppContext.BaseDirectory, NLOG_CONFIG_FILE_NAME);
        if (File.Exists(nlogConfigPath))
        {
            LogManager.Setup().LoadConfigurationFromFile(nlogConfigPath);
        }

        try
        {
            return await RunWithArgsAsync(args, Console.Error, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            LogManager.Shutdown();
        }
    }

    /// <summary>
    /// コマンドライン引数を解析し、対応する <see cref="IMcpHostRunner"/> を選択・実行します。
    /// </summary>
    /// <param name="args">コマンドライン引数。</param>
    /// <param name="errorWriter">エラーメッセージ出力先（通常は <see cref="Console.Error"/>）。</param>
    /// <param name="cancellationToken">停止通知用キャンセルトークン。</param>
    /// <param name="getEnvironmentVariable">環境変数取得デリゲート（テスト用差し替え可）。</param>
    /// <returns>プロセス終了コード。</returns>
    internal static async Task<int> RunWithArgsAsync(
        string[] args,
        TextWriter errorWriter,
        CancellationToken cancellationToken,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!McpServerOptions.TryParse(args, out McpServerOptions? options, out string? errorMessage, getEnvironmentVariable)
            || options is null)
        {
            await errorWriter.WriteLineAsync(errorMessage ?? "コマンドライン引数の解析に失敗しました。").ConfigureAwait(false);
            await errorWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
            return EXIT_CODE_INVALID_ARGUMENTS;
        }

        IMcpHostRunner runner = CreateRunner(options.Transport, errorWriter);
        return await runner.RunAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// トランスポート種別に応じた <see cref="IMcpHostRunner"/> を生成します。
    /// </summary>
    /// <param name="transport">通信トランスポート種別。</param>
    /// <param name="errorWriter">エラーメッセージ出力先。</param>
    /// <returns>対応するホストランナー。</returns>
    internal static IMcpHostRunner CreateRunner(McpTransportMode transport, TextWriter? errorWriter = null)
    {
        return transport switch
        {
            McpTransportMode.Stdio => new StdioMcpHostRunner(),
            McpTransportMode.Http => new HttpMcpHostRunner(errorWriter),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "サポートされていないトランスポートです。"),
        };
    }
}
