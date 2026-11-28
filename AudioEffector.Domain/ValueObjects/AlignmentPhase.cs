namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// LRC 自動生成・文字起こし処理の現在フェーズを表す列挙体です
/// </summary>
public enum AlignmentPhase
{
    /// <summary>
    /// 楽曲音声を解析用 16kHz モノラル PCM へデコード中
    /// </summary>
    DecodingAudio = 0,

    /// <summary>
    /// ローカル音声認識モデルを読み込み中
    /// </summary>
    LoadingModel = 1,

    /// <summary>
    /// 音声認識・文字起こしを実行中
    /// </summary>
    Transcribing = 2,

    /// <summary>
    /// 認識セグメントと歌詞テキストのタイミング合わせ（強制アライメント）を実行中
    /// </summary>
    AligningLyrics = 3,

    /// <summary>
    /// タイムタグ付き LRC ドキュメントを整形中
    /// </summary>
    FormattingLrc = 4,

    /// <summary>
    /// 全処理が完了
    /// </summary>
    Completed = 5,
}
