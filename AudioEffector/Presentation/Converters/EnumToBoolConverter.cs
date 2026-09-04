using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 列挙型の値とパラメータの一致判定を行い、ブール値に変換するコンバーター
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    /// <summary>
    /// 列挙値とパラメータを比較してブール値を返します
    /// </summary>
    /// <param name="value">比較元の列挙値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">比較対象の文字列パラメータ（カンマ区切りで複数指定可能）</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>一致する場合はtrue、それ以外はfalse</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        if (parameter is string paramStr && paramStr.Contains(','))
        {
            var valStr = value.ToString();
            var targets = paramStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var target in targets)
            {
                if (string.Equals(valStr, target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        if (parameter is string singleStr)
        {
            return string.Equals(value.ToString(), singleStr, StringComparison.OrdinalIgnoreCase);
        }

        return value.Equals(parameter);
    }

    /// <summary>
    /// ブール値から列挙値への逆変換を行います
    /// </summary>
    /// <param name="value">変換元のブール値</param>
    /// <param name="targetType">ターゲットの列挙型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>変換後の列挙値</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
        {
            if (parameter is string paramStr)
            {
                var firstParam = paramStr.Split(',')[0].Trim();
                if (Enum.TryParse(targetType, firstParam, true, out var result))
                {
                    return result;
                }
            }
            return parameter;
        }
        return Binding.DoNothing;
    }
}
