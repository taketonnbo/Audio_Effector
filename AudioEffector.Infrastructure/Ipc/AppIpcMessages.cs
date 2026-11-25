using System.Diagnostics.CodeAnalysis;

namespace AudioEffector.Infrastructure.Ipc;

/// <summary>
/// Named Pipe IPC 応答で使用するエラーコード定数を定義します。
/// </summary>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "Named Pipe IPC のプロトコルメッセージ型を集約するファイルのため")]
public static class AppIpcErrorCodes
{
    /// <summary>
    /// 入力パラメータの不正または必須項目未指定。
    /// </summary>
    public const string INVALID_PARAMETER = "INVALID_PARAMETER";

    /// <summary>
    /// 指定されたトラック ID に一致するトラックが存在しない。
    /// </summary>
    public const string NOT_FOUND = "NOT_FOUND";

    /// <summary>
    /// 指定された音声ファイルがディスク上に存在しない。
    /// </summary>
    public const string FILE_NOT_FOUND = "FILE_NOT_FOUND";

    /// <summary>
    /// 未対応の IPC コマンド。
    /// </summary>
    public const string UNSUPPORTED_COMMAND = "UNSUPPORTED_COMMAND";

    /// <summary>
    /// リクエスト JSON の構文不正。
    /// </summary>
    public const string INVALID_JSON = "INVALID_JSON";

    /// <summary>
    /// IPC サーバ側の予期しない内部エラー。
    /// </summary>
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
}

/// <summary>
/// MCP サーバから Audio Effector（WPF 本体）へ送信する Named Pipe IPC リクエストを表します。
/// </summary>
/// <param name="Command">実行するコマンド名（例: <c>ping</c>, <c>play_track</c>）。</param>
/// <param name="TrackId">再生対象トラックの GUID 文字列（任意）。</param>
/// <param name="FilePath">再生対象音声ファイルの絶対パス（任意）。</param>
/// <param name="DeadlineUtc">クライアント側が設定した要求の有効期限（UTC、任意）。期限超過時は処理を実行しない。</param>
public sealed record AppIpcRequest(
    string Command,
    string? TrackId = null,
    string? FilePath = null,
    DateTimeOffset? DeadlineUtc = null);

/// <summary>
/// Named Pipe IPC 応答で返却する再生中トラック情報です。
/// </summary>
/// <param name="Id">トラック ID（GUID）。</param>
/// <param name="Title">楽曲タイトル。</param>
/// <param name="Artist">アーティスト名。</param>
/// <param name="Album">アルバム名。</param>
/// <param name="DurationSeconds">再生時間（秒）。</param>
/// <param name="DurationString">フォーマット済み再生時間文字列（例: <c>03:45</c>）。</param>
/// <param name="FilePath">音声ファイルの絶対パス。</param>
public sealed record AppIpcTrackInfo(
    Guid Id,
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    string DurationString,
    string FilePath);

/// <summary>
/// Audio Effector（WPF 本体）から MCP サーバへ返却する Named Pipe IPC 応答を表します。
/// </summary>
/// <param name="Ok">コマンド実行が成功したかどうか。</param>
/// <param name="PlaybackState">現在の再生状態（例: <c>playing</c>）。</param>
/// <param name="Track">再生を開始したトラック情報。</param>
/// <param name="ErrorCode">失敗時の標準エラーコード（<see cref="AppIpcErrorCodes"/>）。</param>
/// <param name="Error">失敗時のエラー説明。</param>
public sealed record AppIpcResponse(
    bool Ok,
    string? PlaybackState = null,
    AppIpcTrackInfo? Track = null,
    string? ErrorCode = null,
    string? Error = null)
{
    /// <summary>
    /// 疎通確認（<c>ping</c>）の成功応答を生成します。
    /// </summary>
    /// <returns>成功を表す <see cref="AppIpcResponse"/>。</returns>
    public static AppIpcResponse SuccessPing() => new(Ok: true);

    /// <summary>
    /// 楽曲再生コマンドの成功応答を生成します。
    /// </summary>
    /// <param name="playbackState">再生状態文字列（例: <c>playing</c>）。</param>
    /// <param name="track">再生対象トラック情報。</param>
    /// <returns>再生開始結果を表す <see cref="AppIpcResponse"/>。</returns>
    public static AppIpcResponse SuccessPlayback(string playbackState, AppIpcTrackInfo track)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbackState);
        ArgumentNullException.ThrowIfNull(track);
        return new AppIpcResponse(Ok: true, PlaybackState: playbackState, Track: track);
    }

    /// <summary>
    /// エラーコードと説明を含む失敗応答を生成します。
    /// </summary>
    /// <param name="errorCode">標準エラーコード（<see cref="AppIpcErrorCodes"/>）。</param>
    /// <param name="error">エラー説明。</param>
    /// <returns>失敗を表す <see cref="AppIpcResponse"/>。</returns>
    public static AppIpcResponse Failure(string errorCode, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new AppIpcResponse(Ok: false, ErrorCode: errorCode, Error: error);
    }
}
