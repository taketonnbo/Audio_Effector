using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 値が0（または非常に小さい値）の場合に透明度（Opacity）を0にするコンバーター
/// </summary>
public class ZeroToTransparentConverter : IValueConverter
{
    /// <summary>
    /// 数値に応じて不透明度（Opacity）に変換します
    /// </summary>
    /// <param name="value">判定対象の数値（double）</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>0.01以下の場合は0.0、それ以外の場合は1.0</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && d <= 0.01)
        {
            return 0.0; // Transparent
        }
        return 1.0; // Opaque
    }

    /// <summary>
    /// 不透明度から元の数値への逆変換を行います（未サポート）
    /// </summary>
    /// <param name="value">変換元の値</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>変換後の値</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
