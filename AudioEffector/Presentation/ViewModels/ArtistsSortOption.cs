namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 「アーティスト」ビューのソート選択肢を表すレコード
/// </summary>
public record ArtistsSortOption
{
    /// <summary>
    /// ソート処理で使用する内部キー（Name / TrackCount / AlbumCount）
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// UIに表示する日本語ラベル
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// ArtistsSortOptionを初期化します
    /// </summary>
    /// <param name="key">ソート処理で使用する内部キー</param>
    /// <param name="displayName">UIに表示する日本語ラベル</param>
    public ArtistsSortOption(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }
}
