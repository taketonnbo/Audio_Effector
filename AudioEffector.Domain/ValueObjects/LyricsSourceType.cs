namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 歌詞データの取得元ソース種別を表します。
/// </summary>
public enum LyricsSourceType
{
    /// <summary>
    /// 歌詞なし、または取得元未特定。
    /// </summary>
    None = 0,

    /// <summary>
    /// 音声ファイルと同階層・同名の外部 .lrc ファイル。
    /// </summary>
    LrcFile = 1,

    /// <summary>
    /// 音声ファイル内部に埋め込まれた歌詞メタデータタグ。
    /// </summary>
    EmbeddedTag = 2,
}
