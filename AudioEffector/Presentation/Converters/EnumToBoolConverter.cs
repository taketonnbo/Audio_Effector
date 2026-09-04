using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 列挙型の値とパラメータの一致判定を行い、ブール値に変換するコンバーター。
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
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
