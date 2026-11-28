namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// LRC 自動生成の実行モードを表す列挙体です
/// </summary>
public enum LrcGenerationMode
{
    /// <summary>
    /// ユーザーが指定した歌詞テキスト（.txt）と音声解析結果を強制アライメントして LRC を生成するモード
    /// </summary>
    TextAlignment = 0,

    /// <summary>
    /// 歌詞テキストを用いず、楽曲音声から直接文字起こしして LRC を生成するモード
    /// </summary>
    AudioTranscription = 1,
}
