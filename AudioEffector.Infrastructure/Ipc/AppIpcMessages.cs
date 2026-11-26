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
/// Named Pipe IPC のエラー応答でクライアントへ安全な案内を伝えるための構造化理由コード定数を定義します。
/// </summary>
public static class AppIpcErrorReasons
{
    /// <summary>
    /// 同名アルバムが複数存在し一意に特定できない。
    /// </summary>
    public const string AMBIGUOUS_ALBUM = "AMBIGUOUS_ALBUM";

    /// <summary>
    /// 同名プレイリストが複数存在し一意に特定できない。
    /// </summary>
    public const string AMBIGUOUS_PLAYLIST = "AMBIGUOUS_PLAYLIST";
}

/// <summary>
/// MCP サーバから Audio Effector（WPF 本体）へ送信する Named Pipe IPC リクエストを表します。
/// </summary>
/// <param name="Command">実行するコマンド名（例: <c>ping</c>, <c>get_playback_state</c>, <c>play_track</c>, <c>play_album</c>, <c>play_playlist</c>）。</param>
/// <param name="TrackId">再生対象トラックの GUID 文字列（任意）。</param>
/// <param name="FilePath">再生対象音声ファイルの絶対パス（任意）。</param>
/// <param name="DeadlineUtc">クライアント側が設定した要求の有効期限（UTC、任意）。期限超過時は処理を実行しない。</param>
/// <param name="AlbumId">再生対象アルバムの GUID 文字列（任意）。</param>
/// <param name="AlbumName">再生対象アルバム名（任意）。</param>
/// <param name="Artist">同名アルバム絞り込み用アーティスト名（任意）。</param>
/// <param name="PlaylistId">再生対象プレイリストの GUID 文字列（任意）。</param>
/// <param name="PlaylistName">再生対象プレイリスト名（任意）。</param>
/// <param name="StartTrackId">コレクション再生時の開始トラック GUID 文字列（任意）。</param>
/// <param name="StartTrackIndex">コレクション再生時の開始トラック 0 始まりインデックス（任意）。</param>
/// <param name="Shuffle">シャッフル再生の有効／無効（任意、省略時は現在設定を維持）。</param>
/// <param name="Repeat">リピート再生の有効／無効（任意、省略時は現在設定を維持）。</param>
/// <param name="TrackFilePaths">カスタムデータディレクトリ使用時に MCP 側で解決済みの再生対象音声ファイルパス一覧（順序保持、任意）。</param>
/// <param name="SourceId">解決済みコレクションのソース ID（任意）。</param>
/// <param name="SourceName">解決済みコレクションのソース名（キュー見出し同期用、任意）。</param>
public sealed record AppIpcRequest(
    string Command,
    string? TrackId = null,
    string? FilePath = null,
    DateTimeOffset? DeadlineUtc = null,
    string? AlbumId = null,
    string? AlbumName = null,
    string? Artist = null,
    string? PlaylistId = null,
    string? PlaylistName = null,
    string? StartTrackId = null,
    int? StartTrackIndex = null,
    bool? Shuffle = null,
    bool? Repeat = null,
    IReadOnlyList<string>? TrackFilePaths = null,
    Guid? SourceId = null,
    string? SourceName = null);

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
/// <param name="PlaybackState">現在の再生状態（<c>playing</c> / <c>paused</c> / <c>stopped</c>）。</param>
/// <param name="Track">再生中・一時停止中または再生開始したトラック情報（停止・未選択時は <see langword="null"/>）。</param>
/// <param name="ErrorCode">失敗時の標準エラーコード（<see cref="AppIpcErrorCodes"/>）。</param>
/// <param name="Error">失敗時のエラー説明。</param>
/// <param name="SourceType">コレクション再生時のソース種別（<c>album</c> / <c>playlist</c>）。</param>
/// <param name="SourceId">コレクション再生時のソース ID（アルバム ID またはプレイリスト ID）。</param>
/// <param name="SourceName">コレクション再生時のソース名（アルバム名またはプレイリスト名）。</param>
/// <param name="TotalTracks">コレクション再生時の総トラック数（再生可能ファイル数）。</param>
/// <param name="IsShuffleEnabled">シャッフル再生の有効状態。</param>
/// <param name="IsRepeatEnabled">リピート再生の有効状態。</param>
/// <param name="ErrorReason">失敗時の構造化理由コード（<see cref="AppIpcErrorReasons"/>、任意）。</param>
/// <param name="PositionSeconds">現在の再生位置（秒）。</param>
/// <param name="PositionString">フォーマット済み現在再生位置文字列（例: <c>01:23</c>）。</param>
/// <param name="DurationSeconds">総再生時間（秒）。</param>
/// <param name="DurationString">フォーマット済み総再生時間文字列（例: <c>03:45</c>）。</param>
/// <param name="Volume">現在の音量（0.0〜1.0）。</param>
/// <param name="UserQueueCount">予約キューの件数。</param>
/// <param name="AlbumQueueCount">アルバム残り曲キューの件数。</param>
public sealed record AppIpcResponse(
    bool Ok,
    string? PlaybackState = null,
    AppIpcTrackInfo? Track = null,
    string? ErrorCode = null,
    string? Error = null,
    string? SourceType = null,
    Guid? SourceId = null,
    string? SourceName = null,
    int? TotalTracks = null,
    bool? IsShuffleEnabled = null,
    bool? IsRepeatEnabled = null,
    string? ErrorReason = null,
    double? PositionSeconds = null,
    string? PositionString = null,
    double? DurationSeconds = null,
    string? DurationString = null,
    double? Volume = null,
    int? UserQueueCount = null,
    int? AlbumQueueCount = null)
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
    /// 再生状態取得コマンド（<c>get_playback_state</c>）の成功応答を生成します。
    /// </summary>
    /// <param name="playbackState">再生状態文字列（<c>playing</c> / <c>paused</c> / <c>stopped</c>）。</param>
    /// <param name="track">再生中または一時停止中のトラック情報（停止・未選択時は <see langword="null"/>）。</param>
    /// <param name="positionSeconds">現在再生位置（秒）。</param>
    /// <param name="positionString">フォーマット済み現在再生位置文字列。</param>
    /// <param name="durationSeconds">総再生時間（秒）。</param>
    /// <param name="durationString">フォーマット済み総再生時間文字列。</param>
    /// <param name="volume">現在の音量（0.0〜1.0）。</param>
    /// <param name="isShuffleEnabled">シャッフル再生の有効状態。</param>
    /// <param name="isRepeatEnabled">リピート再生の有効状態。</param>
    /// <param name="userQueueCount">予約キュー件数。</param>
    /// <param name="albumQueueCount">アルバム残り曲キュー件数。</param>
    /// <returns>再生状態スナップショットを表す <see cref="AppIpcResponse"/>。</returns>
    public static AppIpcResponse SuccessPlaybackState(
        string playbackState,
        AppIpcTrackInfo? track,
        double positionSeconds,
        string positionString,
        double durationSeconds,
        string durationString,
        double volume,
        bool isShuffleEnabled,
        bool isRepeatEnabled,
        int userQueueCount,
        int albumQueueCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbackState);
        ArgumentException.ThrowIfNullOrWhiteSpace(positionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(durationString);
        ArgumentOutOfRangeException.ThrowIfNegative(userQueueCount);
        ArgumentOutOfRangeException.ThrowIfNegative(albumQueueCount);

        return new AppIpcResponse(
            Ok: true,
            PlaybackState: playbackState,
            Track: track,
            IsShuffleEnabled: isShuffleEnabled,
            IsRepeatEnabled: isRepeatEnabled,
            PositionSeconds: positionSeconds,
            PositionString: positionString,
            DurationSeconds: durationSeconds,
            DurationString: durationString,
            Volume: volume,
            UserQueueCount: userQueueCount,
            AlbumQueueCount: albumQueueCount);
    }

    /// <summary>
    /// アルバム・プレイリスト再生コマンドの成功応答を生成します。
    /// </summary>
    /// <param name="sourceType">再生ソース種別（<c>album</c> / <c>playlist</c>）。</param>
    /// <param name="sourceId">再生ソース ID（アルバム ID またはプレイリスト ID）。</param>
    /// <param name="sourceName">再生ソース名。</param>
    /// <param name="playbackState">再生状態文字列（例: <c>playing</c>）。</param>
    /// <param name="totalTracks">再生対象コレクションの総トラック数。</param>
    /// <param name="isShuffleEnabled">適用後のシャッフル再生有効状態。</param>
    /// <param name="isRepeatEnabled">適用後のリピート再生有効状態。</param>
    /// <param name="track">再生を開始したトラック情報。</param>
    /// <returns>コレクション再生開始結果を表す <see cref="AppIpcResponse"/>。</returns>
    public static AppIpcResponse SuccessCollectionPlayback(
        string sourceType,
        Guid sourceId,
        string sourceName,
        string playbackState,
        int totalTracks,
        bool isShuffleEnabled,
        bool isRepeatEnabled,
        AppIpcTrackInfo track)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(playbackState);
        ArgumentNullException.ThrowIfNull(track);
        return new AppIpcResponse(
            Ok: true,
            PlaybackState: playbackState,
            Track: track,
            SourceType: sourceType,
            SourceId: sourceId,
            SourceName: sourceName,
            TotalTracks: totalTracks,
            IsShuffleEnabled: isShuffleEnabled,
            IsRepeatEnabled: isRepeatEnabled);
    }

    /// <summary>
    /// エラーコードと説明を含む失敗応答を生成します。
    /// </summary>
    /// <param name="errorCode">標準エラーコード（<see cref="AppIpcErrorCodes"/>）。</param>
    /// <param name="error">エラー説明。</param>
    /// <param name="errorReason">クライアント向け案内生成用の構造化理由コード（<see cref="AppIpcErrorReasons"/>、任意）。</param>
    /// <returns>失敗を表す <see cref="AppIpcResponse"/>。</returns>
    public static AppIpcResponse Failure(string errorCode, string error, string? errorReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new AppIpcResponse(Ok: false, ErrorCode: errorCode, Error: error, ErrorReason: errorReason);
    }
}
