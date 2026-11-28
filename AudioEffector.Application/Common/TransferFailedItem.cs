namespace AudioEffector.Application.Common;

/// <summary>
/// 機器転送または逆転送において個別エラーが発生した 1 曲分の失敗情報を表します
/// </summary>
public sealed class TransferFailedItem
{
    /// <summary>
    /// 楽曲タイトルを取得します
    /// </summary>
    public string TrackTitle { get; }

    /// <summary>
    /// 対象ファイル名を取得します
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// エラー理由メッセージを取得します
    /// </summary>
    public string ErrorReason { get; }

    /// <summary>
    /// <see cref="TransferFailedItem"/> のインスタンスを初期化します
    /// </summary>
    /// <param name="trackTitle">楽曲タイトル</param>
    /// <param name="fileName">対象ファイル名</param>
    /// <param name="errorReason">エラー理由メッセージ</param>
    public TransferFailedItem(string trackTitle, string fileName, string errorReason)
    {
        TrackTitle = trackTitle ?? string.Empty;
        FileName = fileName ?? string.Empty;
        ErrorReason = errorReason ?? string.Empty;
    }
}
