using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 複数の文字列候補から最初に見つかった非空白の値を返し、すべて未設定の場合は既定文字列へフォールバックするマルチ値コンバーター
/// 主に正方形ミニプレイヤーのアルバム名表示（アルバム名 → アーティスト名 → 既定文言）に使用します
/// </summary>
public sealed class AlbumDisplayFallbackConverter : IMultiValueConverter
{
    private const string DEFAULT_FALLBACK_TEXT = "Unknown Album";

    /// <summary>
    /// 複数の候補値から最初に見つかった非空白文字列を返します
    /// </summary>
    /// <param name="values">優先順位順に並べた文字列候補</param>
    /// <param name="targetType">ターゲットプロパティの型</param>
    /// <param name="parameter">すべての候補が空の場合に使用するフォールバック文字列（省略時は "Unknown Album"）</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>解決された表示用文字列</returns>
    public object Convert(object[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values != null)
        {
            foreach (object? value in values)
            {
                if (value == null || ReferenceEquals(value, DependencyProperty.UnsetValue))
                {
                    continue;
                }

                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return parameter is string fallback && !string.IsNullOrWhiteSpace(fallback)
            ? fallback
            : DEFAULT_FALLBACK_TEXT;
    }

    /// <summary>
    /// 一方向バインディング専用のため逆変換はサポートしません
    /// </summary>
    /// <param name="value">変換対象の値</param>
    /// <param name="targetTypes">ターゲット型配列</param>
    /// <param name="parameter">パラメータ</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>常に <see cref="NotSupportedException"/> をスローします</returns>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
