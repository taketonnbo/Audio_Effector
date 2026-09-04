using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// ブール値を反転させるコンバーター
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    /// <summary>
    /// ブール値を反転した値に変換します
    /// </summary>
    /// <param name="value">変換対象のブール値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>反転されたブール値</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }

    /// <summary>
    /// 反転されたブール値から元の値への逆変換を行います
    /// </summary>
    /// <param name="value">逆変換対象のブール値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>再反転されたブール値</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}
