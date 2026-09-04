using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// ブール値をVisibility列挙型に変換するコンバーター
/// trueの場合はVisible、falseの場合はCollapsedを返します
/// パラメータに"Invert"を指定すると、動作が反転します
/// </summary>
public class BoolToVisConverter : IValueConverter
{
    /// <summary>
    /// ブール値をVisibilityに変換します
    /// </summary>
    /// <param name="value">変換元のブール値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>変換後のVisibility</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // bool値以外が来たら隠す
        if (value is not bool bValue)
            return Visibility.Collapsed;

        // パラメータに "Invert" があったら反転（trueなら隠す、falseなら表示）
        if (parameter is string paramStr && paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            bValue = !bValue;
        }

        return bValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Visibilityからブール値への逆変換を行います（未サポート）
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
