using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AudioEffector
{
    /// <summary>
    /// リストのAlternationIndexに基づいてゼブラストライプ用の背景ブラシ（奇数行に微細な明るい背景）を返すコンバーター。
    /// </summary>
    public class IndexToZebraBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush EvenBrush = Brushes.Transparent;
        private static readonly SolidColorBrush OddBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)); // 約11%透明度の白 (視認性向上)

        static IndexToZebraBackgroundConverter()
        {
            OddBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (index % 2 == 1) ? OddBrush : EvenBrush;
            }
            return EvenBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
