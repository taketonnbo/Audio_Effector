using System.Text.Json;
using AudioEffector.Application.Common;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using AppResult = AudioEffector.Application.Common.Result;

namespace AudioEffector.McpServer.Protocol;

/// <summary>
/// MCP ツールの正常応答およびエラー応答を統一 JSON エンベロープ形式の <see cref="CallToolResult"/> へ変換するフォーマッタです。
/// </summary>
public static class McpResponseFormatter
{
    /// <summary>
    /// 正常応答データオブジェクトを <c>{"ok": true, "data": ...}</c> 形式の JSON 文字列へシリアライズします。
    /// </summary>
    /// <typeparam name="T">応答データの型。</typeparam>
    /// <param name="data">応答データ。</param>
    /// <returns>シリアライズされた JSON 文字列。</returns>
    public static string SerializeSuccess<T>(T data)
    {
        var envelope = new SuccessEnvelope<T>(true, data);
        return JsonSerializer.Serialize(envelope, McpJsonOptions.Default);
    }

    /// <summary>
    /// 正常応答データオブジェクトから <see cref="CallToolResult"/>（<c>IsError = false</c>）を生成します。
    /// </summary>
    /// <typeparam name="T">応答データの型。</typeparam>
    /// <param name="data">応答データ。</param>
    /// <returns>正常応答を表す <see cref="CallToolResult"/>。</returns>
    public static CallToolResult Success<T>(T data)
    {
        string json = SerializeSuccess(data);
        return CreateTextResult(json, isError: false);
    }

    /// <summary>
    /// 指定されたエラーコード・メッセージ・詳細情報から <see cref="CallToolResult"/>（<c>IsError = true</c>）を生成します。
    /// </summary>
    /// <param name="code">標準エラーコード（<see cref="McpErrorCodes"/>）。</param>
    /// <param name="message">クライアント向けエラーメッセージ。</param>
    /// <param name="details">補助詳細情報（任意）。</param>
    /// <returns>エラー応答を表す <see cref="CallToolResult"/>。</returns>
    public static CallToolResult Error(string code, string message, object? details = null)
    {
        string resolvedCode = string.IsNullOrWhiteSpace(code)
            ? McpErrorCodes.INTERNAL_ERROR
            : code;
        string resolvedMessage = string.IsNullOrWhiteSpace(message)
            ? "エラーが発生しました。"
            : message;

        var envelope = new ErrorEnvelope(false, new McpErrorInfo(resolvedCode, resolvedMessage, details));
        string json = JsonSerializer.Serialize(envelope, McpJsonOptions.Default);
        return CreateTextResult(json, isError: true);
    }

    /// <summary>
    /// 戻り値なしの <see cref="AppResult"/> を <see cref="CallToolResult"/> へ変換します。
    /// </summary>
    /// <param name="result">アプリケーション層の処理結果。</param>
    /// <param name="successData">成功時に <c>data</c> へ格納するオブジェクト（未指定時は <c>{"status": "ok"}</c>）。</param>
    /// <param name="errorCode">失敗時に適用する標準エラーコード。</param>
    /// <param name="details">失敗時の補助詳細情報（任意）。</param>
    /// <returns>変換された <see cref="CallToolResult"/>。</returns>
    public static CallToolResult FromResult(
        AppResult result,
        object? successData = null,
        string errorCode = McpErrorCodes.INTERNAL_ERROR,
        object? details = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return Success(successData ?? new { status = "ok" });
        }

        McpErrorInfo error = McpErrorMapper.MapFailureResult(result, errorCode, details);
        return Error(error.Code, error.Message, error.Details);
    }

    /// <summary>
    /// 値付き <see cref="Result{T}"/> を <see cref="CallToolResult"/> へ変換します。
    /// </summary>
    /// <typeparam name="T">成功時の値型。</typeparam>
    /// <param name="result">アプリケーション層の処理結果。</param>
    /// <param name="errorCode">失敗時に適用する標準エラーコード。</param>
    /// <param name="details">失敗時の補助詳細情報（任意）。</param>
    /// <returns>変換された <see cref="CallToolResult"/>。</returns>
    public static CallToolResult FromResult<T>(
        Result<T> result,
        string errorCode = McpErrorCodes.INTERNAL_ERROR,
        object? details = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return Success(result.Value);
        }

        McpErrorInfo error = McpErrorMapper.MapFailureResult(result, errorCode, details);
        return Error(error.Code, error.Message, error.Details);
    }

    /// <summary>
    /// 実行時例外を標準エラーコードへ変換し、<see cref="CallToolResult"/>（<c>IsError = true</c>）を生成します。
    /// </summary>
    /// <param name="exception">発生した例外。</param>
    /// <param name="logger">詳細ログ記録先（任意）。</param>
    /// <returns>エラー応答を表す <see cref="CallToolResult"/>。</returns>
    public static CallToolResult FromException(Exception exception, ILogger? logger = null)
    {
        McpErrorInfo error = McpErrorMapper.MapException(exception, logger);
        return Error(error.Code, error.Message, error.Details);
    }

    private static CallToolResult CreateTextResult(string json, bool isError)
    {
        return new CallToolResult
        {
            IsError = isError,
            Content =
            [
                new TextContentBlock
                {
                    Text = json,
                },
            ],
        };
    }

    private sealed record SuccessEnvelope<T>(bool Ok, T Data);

    private sealed record ErrorEnvelope(bool Ok, McpErrorInfo Error);
}
