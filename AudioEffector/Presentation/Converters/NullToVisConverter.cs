using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// nullまたは空文字列の場合にVisibility.Collapsedを返すコンバーター
/// </summary>
public class NullToVisConverter : IValueConverter
{
    /// <summary>
    /// 値がnullまたは空文字列かどうかに応じてVisibilityに変換します
    /// </summary>
    /// <param name="value">判定対象の値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>値がnullまたは空文字列の場合はCollapsed、それ以外はVisible</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrEmpty(s)) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    /// <summary>
    /// Visibilityから元の値への逆変換を行います（未サポート）
    /// </summary>
    /// <param name="value">変換元の値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>変換後の値</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
