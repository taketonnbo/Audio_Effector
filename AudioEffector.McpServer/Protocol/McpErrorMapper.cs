using AudioEffector.Application.Common;
using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.Protocol;

/// <summary>
/// 実行時例外および <see cref="Result"/> 失敗を標準 MCP エラー情報へ変換します。
/// スタックトレースや機密ファイルパス等の内部診断情報はクライアントへ露出せず、ログへ記録します。
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

    private static readonly Action<ILogger, string, string, Exception?> LogFailureResultWarningDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1007, "McpFailureResultWarning"),
            "MCP ツール処理が失敗結果を返しました（Code: {ErrorCode}, Detail: {DiagnosticMessage}）。");

    private static readonly Action<ILogger, string, string, Exception?> LogFailureResultErrorDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1008, "McpFailureResultError"),
            "MCP ツール処理で内部エラー結果が発生しました（Code: {ErrorCode}, Detail: {DiagnosticMessage}）。");

    private static readonly Action<ILogger, Exception?> LogIpcTimeoutDelegate =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1009, "McpIpcTimeout"),
            "MCP ツールで Audio Effector 本体との通信または起動待機がタイムアウトしました。");

    private static readonly Action<ILogger, Exception?> LogInternalErrorDelegate =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1099, "McpInternalError"),
            "MCP ツール実行中に予期しない内部エラーが発生しました。");

    /// <summary>
    /// 指定された標準エラーコードに対応するクライアント公開用の既定メッセージを返します。
    /// </summary>
    /// <param name="errorCode">標準エラーコード（<see cref="McpErrorCodes"/> の定数）。</param>
    /// <returns>内部診断情報を含まない安全な公開メッセージ。</returns>
    public static string GetPublicMessage(string errorCode)
    {
        return errorCode switch
        {
            McpErrorCodes.INVALID_PARAMETER => "指定されたパラメータが不正です。",
            McpErrorCodes.NOT_FOUND => "指定された対象が見つかりません。",
            McpErrorCodes.FILE_NOT_FOUND => "指定されたファイルまたはディレクトリが見つかりません。",
            McpErrorCodes.WPF_APP_NOT_RUNNING => "Audio Effector 本体が起動していないため、操作を実行できません。本体を起動してから再試行してください。",
            McpErrorCodes.IPC_CONNECTION_FAILED => "Audio Effector 本体との通信に失敗しました。しばらく待ってから再試行してください。",
            McpErrorCodes.CONFLICT_LOCK_TIMEOUT => "データファイルのロック取得がタイムアウトしました。しばらく待ってから再試行してください。",
            McpErrorCodes.OPERATION_CANCELLED => "処理がキャンセルされました。",
            _ => "内部エラーが発生しました。詳細はサーバログを確認してください。",
        };
    }

    /// <summary>
    /// 例外を標準エラーコードおよびクライアント向け安全メッセージへ変換し、元の診断情報はログへ記録します。
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
                    GetPublicMessage(McpErrorCodes.OPERATION_CANCELLED));

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
                    GetPublicMessage(McpErrorCodes.INVALID_PARAMETER),
                    details);

            case KeyNotFoundException keyNotFoundEx:
                if (logger is not null)
                {
                    LogNotFoundDelegate(logger, keyNotFoundEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.NOT_FOUND,
                    GetPublicMessage(McpErrorCodes.NOT_FOUND));

            case FileNotFoundException fileNotFoundEx:
                if (logger is not null)
                {
                    LogFileNotFoundDelegate(logger, fileNotFoundEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.FILE_NOT_FOUND,
                    GetPublicMessage(McpErrorCodes.FILE_NOT_FOUND));

            case DirectoryNotFoundException dirNotFoundEx:
                if (logger is not null)
                {
                    LogDirectoryNotFoundDelegate(logger, dirNotFoundEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.FILE_NOT_FOUND,
                    GetPublicMessage(McpErrorCodes.FILE_NOT_FOUND));

            case FileLockTimeoutException lockTimeoutEx:
                if (logger is not null)
                {
                    LogFileLockTimeoutDelegate(logger, lockTimeoutEx.Timeout.TotalMilliseconds, lockTimeoutEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.CONFLICT_LOCK_TIMEOUT,
                    GetPublicMessage(McpErrorCodes.CONFLICT_LOCK_TIMEOUT));

            case TimeoutException timeoutEx:
                if (logger is not null)
                {
                    LogIpcTimeoutDelegate(logger, timeoutEx);
                }

                return new McpErrorInfo(
                    McpErrorCodes.IPC_CONNECTION_FAILED,
                    GetPublicMessage(McpErrorCodes.IPC_CONNECTION_FAILED));

            default:
                if (logger is not null)
                {
                    LogInternalErrorDelegate(logger, exception);
                }

                return new McpErrorInfo(
                    McpErrorCodes.INTERNAL_ERROR,
                    GetPublicMessage(McpErrorCodes.INTERNAL_ERROR));
        }
    }

    /// <summary>
    /// 失敗した <see cref="Result"/> を <see cref="McpErrorInfo"/> へ変換します。
    /// 元の診断メッセージ（<see cref="Result.ErrorMessage"/>）はクライアントへ露出せずログへ記録し、エラーコード別の公開用既定メッセージを返します。
    /// </summary>
    /// <param name="result">失敗状態の結果オブジェクト。</param>
    /// <param name="errorCode">適用する標準エラーコード（既定値: <see cref="McpErrorCodes.INTERNAL_ERROR"/>）。</param>
    /// <param name="details">補助詳細情報（任意）。</param>
    /// <param name="logger">診断ログ記録先（任意）。</param>
    /// <returns>変換された <see cref="McpErrorInfo"/>。</returns>
    public static McpErrorInfo MapFailureResult(
        Result result,
        string errorCode = McpErrorCodes.INTERNAL_ERROR,
        object? details = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            throw new ArgumentException("成功状態の Result はエラーへ変換できません。", nameof(result));
        }

        string resolvedCode = string.IsNullOrWhiteSpace(errorCode)
            ? McpErrorCodes.INTERNAL_ERROR
            : errorCode;

        if (logger is not null)
        {
            string diagnostic = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "(詳細メッセージなし)"
                : result.ErrorMessage;

            if (string.Equals(resolvedCode, McpErrorCodes.INTERNAL_ERROR, StringComparison.Ordinal))
            {
                LogFailureResultErrorDelegate(logger, resolvedCode, diagnostic, null);
            }
            else
            {
                LogFailureResultWarningDelegate(logger, resolvedCode, diagnostic, null);
            }
        }

        return new McpErrorInfo(resolvedCode, GetPublicMessage(resolvedCode), details);
    }
}

/// <summary>
/// クライアントへ返却する構造化エラー情報を表します。
/// </summary>
/// <param name="Code">標準エラーコード（<see cref="McpErrorCodes"/> の定数）。</param>
/// <param name="Message">クライアント向けの安全なエラーメッセージ。</param>
/// <param name="Details">補助詳細情報（任意）。</param>
public sealed record McpErrorInfo(string Code, string Message, object? Details = null);
