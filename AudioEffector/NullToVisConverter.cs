using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioEffector
{
    /// <summary>
    /// nullまたは空文字列の場合にVisibility.Collapsedを返すコンバーター。
    /// それ以外の場合はVisibleを返します。
    /// </summary>
    public class NullToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is string s && string.IsNullOrEmpty(s)) return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
