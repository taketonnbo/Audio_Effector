using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 値が0（または非常に小さい値）の場合に透明度（Opacity）を0にするコンバーター。
/// それ以外の場合は1（不透明）を返します。
/// </summary>
public class ZeroToTransparentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && d <= 0.01)
        {
            return 0.0; // Transparent
        }
        return 1.0; // Opaque
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
