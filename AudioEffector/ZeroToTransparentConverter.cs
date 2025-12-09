using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector
{
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
}
