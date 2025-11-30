using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector
{
    public class BoolToVisConverter : IValueConverter
    {
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
