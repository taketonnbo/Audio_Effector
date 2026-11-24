namespace AudioEffector.McpServer.Protocol;

/// <summary>
/// MCP ツール応答で使用する標準エラーコード定数を定義します。
/// </summary>
public static class McpErrorCodes
{
    /// <summary>
    /// 入力パラメータの不正またはバリデーション失敗。
    /// </summary>
    public const string INVALID_PARAMETER = "INVALID_PARAMETER";

    /// <summary>
    /// 指定されたエンティティや対象が存在しない。
    /// </summary>
    public const string NOT_FOUND = "NOT_FOUND";

    /// <summary>
    /// 指定されたファイルまたはディレクトリがディスク上に存在しない。
    /// </summary>
    public const string FILE_NOT_FOUND = "FILE_NOT_FOUND";

    /// <summary>
    /// 再生制御等の実行時に Audio Effector WPF 本体アプリが起動していない。
    /// </summary>
    public const string WPF_APP_NOT_RUNNING = "WPF_APP_NOT_RUNNING";

    /// <summary>
    /// WPF 本体との名前付きパイプ IPC 通信の接続失敗またはタイムアウト。
    /// </summary>
    public const string IPC_CONNECTION_FAILED = "IPC_CONNECTION_FAILED";

    /// <summary>
    /// OS ファイルロック（<c>.lock</c>）の待機タイムアウトによる競合。
    /// </summary>
    public const string CONFLICT_LOCK_TIMEOUT = "CONFLICT_LOCK_TIMEOUT";

    /// <summary>
    /// クライアントまたはホストからのキャンセル要求による処理中断。
    /// </summary>
    public const string OPERATION_CANCELLED = "OPERATION_CANCELLED";

    /// <summary>
    /// 分類されない予期しない内部エラー。
    /// </summary>
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
}
