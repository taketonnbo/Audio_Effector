using AudioEffector.McpServer.Configuration;

namespace AudioEffector.McpServer.Hosting;

/// <summary>
/// 将来の Phase 3（Streamable HTTP トランスポート対応）に向けた拡張用スタブランナーです。
/// Phase 1 では未実装エラーを <c>stderr</c> へ出力し、終了コード <c>2</c> を返却します。
/// </summary>
public sealed class HttpMcpHostRunner : IMcpHostRunner
{
    /// <summary>
    /// HTTP トランスポート未実装時の終了コード。
    /// </summary>
    public const int EXIT_CODE_NOT_IMPLEMENTED = 2;

    private readonly TextWriter _errorWriter;

    /// <summary>
    /// <see cref="HttpMcpHostRunner"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="errorWriter">エラー出力先（未指定時は <see cref="Console.Error"/>）。</param>
    public HttpMcpHostRunner(TextWriter? errorWriter = null)
    {
        _errorWriter = errorWriter ?? Console.Error;
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(McpServerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        await _errorWriter.WriteLineAsync(
            $"HTTP トランスポート（http://{options.Host}:{options.Port}/mcp）は現在未実装です（Phase 3 で対応予定）。'--transport stdio' を使用してください。")
            .ConfigureAwait(false);
        await _errorWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

        return EXIT_CODE_NOT_IMPLEMENTED;
    }
}
