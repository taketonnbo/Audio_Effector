using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AudioEffector
{
    /// <summary>
    /// アイテムのインデックス（0始まり）に応じて、ゼブラストライプ用の背景ブラシを返すコンバーター。
    /// 偶数行: Transparent
    /// 奇数行: テーマに応じてダークテーマでは淡い白透過（約6%）、ライトテーマでは淡い黒透過（乗算シャドウ約5%）
    /// </summary>
    public class IndexToZebraBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush EvenBrush = Brushes.Transparent;
        private static readonly SolidColorBrush DarkOddBrush = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); // 約6.2%白 (上品で淡いハイライト)
        private static readonly SolidColorBrush LightOddBrush = new SolidColorBrush(Color.FromArgb(12, 0, 0, 0));       // 約4.7%黒 (乗算シャドウ)

        static IndexToZebraBackgroundConverter()
        {
            DarkOddBrush.Freeze();
            LightOddBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                if (index % 2 == 0)
                {
                    return EvenBrush;
                }

                // テーマリソースの ZebraOddBackgroundBrush を優先解決
                if (Application.Current != null && Application.Current.TryFindResource("ZebraOddBackgroundBrush") is SolidColorBrush resBrush)
                {
                    return resBrush;
                }

                // テーマの明度を判定（ライトテーマの場合は黒透過、ダークテーマの場合は白透過）
                if (Application.Current != null && Application.Current.TryFindResource("TextForegroundColor") is Color textColor)
                {
                    if (textColor.R < 128 && textColor.G < 128 && textColor.B < 128)
                    {
                        return LightOddBrush;
                    }
                }

                return DarkOddBrush;
            }

            return EvenBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
