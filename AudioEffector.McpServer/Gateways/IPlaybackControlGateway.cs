using AudioEffector.Domain.ValueObjects;
using AudioEffector.McpServer.Dtos;

namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// 起動中の Audio Effector（WPF 本体）へリアルタイム再生制御コマンドを送信するゲートウェイインターフェースです。
/// </summary>
public interface IPlaybackControlGateway
{
    /// <summary>
    /// 起動中の Audio Effector 本体から現在の再生状態スナップショットを取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>現在の再生状態または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackStateResponse>> GetPlaybackStateAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 起動中の Audio Effector 本体から現在の再生キュー（現在トラック・予約キュー・アルバム残り曲キュー）を取得します。
    /// </summary>
    /// <param name="limit">予約キュー・アルバム残り曲キューそれぞれで返却する最大トラック件数（1〜200、既定値: 50）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>現在の再生キューまたは標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackQueueResponse>> GetPlaybackQueueAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 起動中の Audio Effector 本体の再生を一時停止します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>一時停止後の再生状態または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackActionResponse>> PausePlaybackAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 起動中の Audio Effector 本体の一時停止中の再生を再開します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>再開後の再生状態または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackActionResponse>> ResumePlaybackAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 起動中の Audio Effector 本体の再生を停止します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>停止後の再生状態または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackActionResponse>> StopPlaybackAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 起動中の Audio Effector 本体で次の楽曲へ移動します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>移動後の再生状態およびトラック情報、または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackActionResponse>> NextTrackAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 起動中の Audio Effector 本体で前の楽曲（または現在曲の先頭）へ戻ります。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>移動後の再生状態およびトラック情報、または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlaybackActionResponse>> PreviousTrackAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたトラック ID または音声ファイルパスの楽曲を Audio Effector 本体で再生開始します。
    /// </summary>
    /// <param name="trackId">再生対象トラックの ID（省略可）。</param>
    /// <param name="filePath">再生対象音声ファイルの絶対パス（<paramref name="trackId"/> 未指定時に使用）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>再生開始結果または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlayTrackResponse>> PlayTrackAsync(
        TrackId? trackId,
        string? filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたアルバムを Audio Effector 本体で再生開始し、残りの収録曲をキューへ展開します。
    /// </summary>
    /// <param name="albumId">再生対象アルバムの ID（省略可。<paramref name="albumName"/> と両方指定時は優先）。</param>
    /// <param name="albumName">再生対象アルバム名（<paramref name="albumId"/> 未指定時に使用）。</param>
    /// <param name="artist">同名アルバム絞り込み用アーティスト名（省略可）。</param>
    /// <param name="startTrackId">開始トラック ID（省略可。<paramref name="startTrackIndex"/> より優先）。</param>
    /// <param name="startTrackIndex">開始トラックの 0 始まりインデックス（省略可）。</param>
    /// <param name="shuffle">シャッフル再生の有効／無効（省略時は現在設定を維持）。</param>
    /// <param name="repeat">リピート再生の有効／無効（省略時は現在設定を維持）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>コレクション再生開始結果または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlayCollectionResponse>> PlayAlbumAsync(
        AlbumId? albumId,
        string? albumName,
        string? artist,
        TrackId? startTrackId,
        int? startTrackIndex,
        bool? shuffle,
        bool? repeat,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたユーザー作成プレイリストを Audio Effector 本体で再生開始し、残りの収録曲をキューへ展開します。
    /// </summary>
    /// <param name="playlistId">再生対象プレイリストの ID（省略可。<paramref name="playlistName"/> と両方指定時は優先）。</param>
    /// <param name="playlistName">再生対象プレイリスト名（<paramref name="playlistId"/> 未指定時に使用）。</param>
    /// <param name="startTrackId">開始トラック ID（省略可。<paramref name="startTrackIndex"/> より優先）。</param>
    /// <param name="startTrackIndex">開始トラックの 0 始まりインデックス（省略可）。</param>
    /// <param name="shuffle">シャッフル再生の有効／無効（省略時は現在設定を維持）。</param>
    /// <param name="repeat">リピート再生の有効／無効（省略時は現在設定を維持）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>コレクション再生開始結果または標準エラーコードを含む処理結果。</returns>
    Task<PlaybackControlResult<PlayCollectionResponse>> PlayPlaylistAsync(
        PlaylistId? playlistId,
        string? playlistName,
        TrackId? startTrackId,
        int? startTrackIndex,
        bool? shuffle,
        bool? repeat,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 再生制御ゲートウェイの実行結果（成功データまたは標準 MCP エラー情報）を表します。
/// </summary>
/// <typeparam name="T">成功時のデータ型。</typeparam>
public sealed class PlaybackControlResult<T>
{
    private PlaybackControlResult(bool isSuccess, T? value, string? errorCode, string? errorMessage, object? details)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Details = details;
    }

    /// <summary>
    /// 操作が成功したかどうかを取得します。
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 成功時の応答データを取得します。
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// 失敗時の標準エラーコードを取得します。
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// 失敗時のクライアント公開用エラーメッセージを取得します。
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 失敗時の補助詳細情報を取得します。
    /// </summary>
    public object? Details { get; }

    /// <summary>
    /// 成功結果を生成します。
    /// </summary>
    /// <param name="value">成功データ。</param>
    /// <returns>成功を表す <see cref="PlaybackControlResult{T}"/>。</returns>
    public static PlaybackControlResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PlaybackControlResult<T>(true, value, null, null, null);
    }

    /// <summary>
    /// 失敗結果を生成します。
    /// </summary>
    /// <param name="errorCode">標準エラーコード。</param>
    /// <param name="errorMessage">クライアント公開用エラーメッセージ。</param>
    /// <param name="details">補助詳細情報（任意）。</param>
    /// <returns>失敗を表す <see cref="PlaybackControlResult{T}"/>。</returns>
    public static PlaybackControlResult<T> Failure(string errorCode, string errorMessage, object? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new PlaybackControlResult<T>(false, default, errorCode, errorMessage, details);
    }
}
