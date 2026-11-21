namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 「すべての曲」ビューのソート選択肢を表すレコード
/// </summary>
/// <param name="Key">ソート処理で使用する内部キー（Title / Artist / Album / Duration / Genre）</param>
/// <param name="DisplayName">UIに表示する日本語ラベル</param>
public sealed record AllSongsSortOption(string Key, string DisplayName);
