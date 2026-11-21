namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 「すべての曲」ビューのソート選択肢を表すレコード
/// </summary>
public record AllSongsSortOption
{
    /// <summary>
    /// ソート処理で使用する内部キー（Title / Artist / Album / Duration / Genre）
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// UIに表示する日本語ラベル
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// AllSongsSortOptionを初期化します
    /// </summary>
    /// <param name="key">ソート処理で使用する内部キー</param>
    /// <param name="displayName">UIに表示する日本語ラベル</param>
    public AllSongsSortOption(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }
}
