using AudioEffector.McpServer.Configuration;

namespace AudioEffector.McpServer.Hosting;

/// <summary>
/// MCP サーバのトランスポート別ホスト起動・実行を抽象化するインターフェースです。
/// </summary>
public interface IMcpHostRunner
{
    /// <summary>
    /// 指定されたオプションで MCP サーバホストを起動し、停止するまで実行します。
    /// </summary>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <param name="cancellationToken">ホスト停止を通知するキャンセルトークン。</param>
    /// <returns>プロセス終了コード（正常終了時は <c>0</c>）。</returns>
    Task<int> RunAsync(McpServerOptions options, CancellationToken cancellationToken = default);
}
