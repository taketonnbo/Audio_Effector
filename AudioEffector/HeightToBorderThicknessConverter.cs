using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector
{
    public class HeightToBorderThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double height)
            {
                // MaxHeight is 600, MaxThickness is 3
                double thickness = (height / 600.0) * 3.0;

                // Clamp between 0 and 3
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
}
