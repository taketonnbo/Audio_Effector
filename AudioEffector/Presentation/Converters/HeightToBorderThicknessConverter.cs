using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 高さに応じて境界線の太さを計算するコンバーター。
/// 高さが大きいほど太くなります（最大3.0）。
/// </summary>
public class HeightToBorderThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            // 最大高さ600に対して、最大太さ3になるように計算
            double thickness = (height / 600.0) * 3.0;

            // 0から3の範囲に制限
            if (thickness < 0) thickness = 0;
            if (thickness > 3) thickness = 3;

            return new Thickness(thickness);
        }
        return new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
