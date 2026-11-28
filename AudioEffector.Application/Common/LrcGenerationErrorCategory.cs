namespace AudioEffector.Application.Common;

/// <summary>
/// LRC 自動生成機能で発生する例外の原因カテゴリを定義します
/// </summary>
public enum LrcGenerationErrorCategory
{
    /// <summary>
    /// 音声認識エンジンのネイティブライブラリ欠損または読み込み失敗
    /// </summary>
    NativeRuntimeUnavailable,

    /// <summary>
    /// 音声ファイルへのアクセス失敗（存在しない・USB 取り外し・権限不足等）
    /// </summary>
    AudioFileAccessFailed,

    /// <summary>
    /// 音声ファイルのデコード失敗・ファイル破損・未対応形式
    /// </summary>
    AudioDecodeFailed,

    /// <summary>
    /// ローカル音声認識モデルファイルの破損または読み込み失敗
    /// </summary>
    ModelLoadFailed,

    /// <summary>
    /// メモリ不足による処理中断
    /// </summary>
    OutOfMemory,

    /// <summary>
    /// ローカル音声認識モデルのダウンロード障害（通信失敗・タイムアウト・容量不足・権限不足等）
    /// </summary>
    ModelDownloadFailed,

    /// <summary>
    /// LRC ファイルの保存失敗（書き込み保護・USB 取り外し・空き容量不足等）
    /// </summary>
    FileSaveFailed,

    /// <summary>
    /// 歌詞テキストまたは既存 LRC ファイルの読み込み失敗
    /// </summary>
    FileReadFailed,

    /// <summary>
    /// 上記のいずれにも該当しない予期しない例外
    /// </summary>
    Unexpected,
}
