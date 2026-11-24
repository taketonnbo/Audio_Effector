using AudioEffector.Application.Common;
using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.Protocol;

/// <summary>
/// 実行時例外および <see cref="Result"/> 失敗を標準 MCP エラー情報へ変換します。
/// スタックトレースや機密ファイルパス等の内部詳細はクライアントへ露出せず、ログへ記録します。
/// </summary>
public static class McpErrorMapper
{
    private static readonly Action<ILogger, Exception?> LogOperationCancelledDelegate =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(1001, "McpOperationCancelled"),
            "MCP ツール処理がキャンセルされました。");

    private static readonly Action<ILogger, string?, Exception?> LogInvalidParameterDelegate =
        LoggerMessage.Define<string?>(
            LogLevel.Warning,
            new EventId(1002, "McpInvalidParameter"),
            "MCP ツールでパラメータ検証エラーが発生しました（Parameter: {ParamName}）。");

    private static readonly Action<ILogger, Exception?> LogNotFoundDelegate =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1003, "McpNotFound"),
            "MCP ツールで対象が見つかりませんでした。");

    private static readonly Action<ILogger, Exception?> LogFileNotFoundDelegate =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1004, "McpFileNotFound"),
            "MCP ツールでファイルが見つかりませんでした。");

    private static readonly Action<ILogger, Exception?> LogDirectoryNotFoundDelegate =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1005, "McpDirectoryNotFound"),
            "MCP ツールでディレクトリが見つかりませんでした。");

    private static readonly Action<ILogger, double, Exception?> LogFileLockTimeoutDelegate =
        LoggerMessage.Define<double>(
            LogLevel.Warning,
            new EventId(1006, "McpFileLockTimeout"),
            "MCP ツールでファイルロック待機タイムアウトが発生しました（TimeoutMs: {TimeoutMs}）。");

    private static readonly Action<ILogger, Exception?> LogInternalErrorDelegate =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1099, "McpInternalError"),
            "MCP ツール実行中に予期しない内部エラーが発生しました。");

    /// <summary>
    /// 例外を標準エラーコードおよびクライアント向け安全メッセージへ変換し、必要に応じてログへ記録します。
    /// </summary>
    /// <param name="exception">発生した例外。</param>
    /// <param name="logger">診断ログ記録先（任意）。</param>
    /// <returns>変換された <see cref="McpErrorInfo"/>。</returns>
    public static McpErrorInfo MapException(Exception exception, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case OperationCanceledException:
                if (logger is not null)
                {
                    LogOperationCancelledDelegate(logger, exception);
                }

                return new McpErrorInfo(
                    McpErrorCodes.OPERATION_CANCELLED,
                    "処理がキャンセルされました。");

            case ArgumentException argEx:
                if (logger is not null)
                {
                    LogInvalidParameterDelegate(logger, argEx.ParamName, argEx);
                }

                object? details = string.IsNullOrWhiteSpace(argEx.ParamName)
                    ? null
                    : new { parameter = argEx.ParamName };
                return new McpErrorInfo(
                    McpErrorCodes.INVALID_PARAMETER,
                    SanitizeArgumentMessage(argEx),
                    details);

            case KeyNotFoundException keyNotFoundEx:
                if (logger is not null)
                {
                    LogNotFoundDelegate(logger, keyNotFoundEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.NOT_FOUND,
                    "指定された対象が見つかりません。");

            case FileNotFoundException fileNotFoundEx:
                if (logger is not null)
                {
                    LogFileNotFoundDelegate(logger, fileNotFoundEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.FILE_NOT_FOUND,
                    "指定されたファイルが見つかりません。");

            case DirectoryNotFoundException dirNotFoundEx:
                if (logger is not null)
                {
                    LogDirectoryNotFoundDelegate(logger, dirNotFoundEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.FILE_NOT_FOUND,
                    "指定されたディレクトリが見つかりません。");

            case FileLockTimeoutException lockTimeoutEx:
                if (logger is not null)
                {
                    LogFileLockTimeoutDelegate(logger, lockTimeoutEx.Timeout.TotalMilliseconds, lockTimeoutEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.CONFLICT_LOCK_TIMEOUT,
                    "データファイルのロック取得がタイムアウトしました。しばらく待ってから再試行してください。");

            default:
                if (logger is not null)
                {
                    LogInternalErrorDelegate(logger, exception);
                }

                return new McpErrorInfo(
                    McpErrorCodes.INTERNAL_ERROR,
                    "内部エラーが発生しました。詳細はサーバログを確認してください。");
        }
    }

    /// <summary>
    /// 失敗した <see cref="Result"/> を <see cref="McpErrorInfo"/> へ変換します。
    /// </summary>
    /// <param name="result">失敗状態の結果オブジェクト。</param>
    /// <param name="errorCode">適用する標準エラーコード（既定値: <see cref="McpErrorCodes.INTERNAL_ERROR"/>）。</param>
    /// <param name="details">補助詳細情報（任意）。</param>
    /// <returns>変換された <see cref="McpErrorInfo"/>。</returns>
    public static McpErrorInfo MapFailureResult(
        Result result,
        string errorCode = McpErrorCodes.INTERNAL_ERROR,
        object? details = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            throw new ArgumentException("成功状態の Result はエラーへ変換できません。", nameof(result));
        }

        string resolvedCode = string.IsNullOrWhiteSpace(errorCode)
            ? McpErrorCodes.INTERNAL_ERROR
            : errorCode;

        string message = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "処理に失敗しました。"
            : result.ErrorMessage;

        return new McpErrorInfo(resolvedCode, message, details);
    }

    private static string SanitizeArgumentMessage(ArgumentException exception)
    {
        string raw = exception.Message;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "指定されたパラメータが不正です。";
        }

        // .NET の ArgumentException が末尾に自動付与する " (Parameter 'xxx')" 部分を除去して簡潔なメッセージにする
        if (!string.IsNullOrWhiteSpace(exception.ParamName))
        {
            string suffix = $" (Parameter '{exception.ParamName}')";
            if (raw.EndsWith(suffix, StringComparison.Ordinal))
            {
                raw = raw[..^suffix.Length].Trim();
            }
        }

        // 複数行にわたる場合でも先頭行のみを採用し、スタック情報等の混入を防ぐ
        int newlineIndex = raw.IndexOfAny(['\r', '\n']);
        if (newlineIndex >= 0)
        {
            raw = raw[..newlineIndex].Trim();
        }

        return string.IsNullOrWhiteSpace(raw)
            ? "指定されたパラメータが不正です。"
            : raw;
    }
}

/// <summary>
/// クライアントへ返却する構造化エラー情報を表します。
/// </summary>
/// <param name="Code">標準エラーコード（<see cref="McpErrorCodes"/> の定数）。</param>
/// <param name="Message">クライアント向けの安全なエラーメッセージ。</param>
/// <param name="Details">補助詳細情報（任意）。</param>
public sealed record McpErrorInfo(string Code, string Message, object? Details = null);
