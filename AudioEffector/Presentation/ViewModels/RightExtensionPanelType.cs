namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 右側タブ下部拡張領域に表示する排他パネルの種別を表します
/// </summary>
public enum RightExtensionPanelType
{
    /// <summary>
    /// いずれの拡張パネルも表示しない状態
    /// </summary>
    None = 0,

    /// <summary>
    /// 歌詞表示パネル
    /// </summary>
    Lyrics = 1,

    /// <summary>
    /// LRC 自動生成パネル
    /// </summary>
    LrcGenerator = 2,
}
