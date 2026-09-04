namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 外部デバイス上のディレクトリまたはファイルアイテムを表すクラス
/// </summary>
public class DirectoryItem
{
    /// <summary>
    /// アイテム名を取得または設定します
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// アイテムのフルパスを取得または設定します
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// フォルダーかどうかを示す値を取得または設定します
    /// </summary>
    public bool IsFolder { get; set; }
}
