namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// <see cref="IAudioService.NextAsync"/> の実行結果を表します。
/// </summary>
public enum NextTrackResult
{
    /// <summary>
    /// 次の楽曲（またはリピートによる先頭曲）の再生開始に成功しました。
    /// </summary>
    Started,

    /// <summary>
    /// キュー末尾に到達し、<see cref="IAudioService.PlaylistEnded"/> ハンドラ（次アルバム自動遷移等）によって新しい再生へ移行しました。
    /// </summary>
    DelegatedToPlaylistEnded,

    /// <summary>
    /// 次の楽曲が存在せず、キュー末尾で再生を停止しました。
    /// </summary>
    StoppedAtEnd,

    /// <summary>
    /// 次の楽曲は存在しましたが、ファイル欠損・デコード失敗または出力デバイス初期化失敗により再生開始に失敗しました。
    /// </summary>
    Failed,
}
