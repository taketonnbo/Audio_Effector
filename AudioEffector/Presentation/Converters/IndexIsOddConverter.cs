using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// インデックスが奇数（1, 3, 5...）かどうかを判定して真偽値を返すコンバーター
/// </summary>
public class IndexIsOddConverter : IValueConverter
{
    /// <summary>
    /// インデックスが奇数かどうかを判定します
    /// </summary>
    /// <param name="value">インデックス（int）</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>奇数の場合はtrue、偶数の場合はfalse</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return index % 2 == 1;
        }
        return false;
    }

    /// <summary>
    /// 真偽値からインデックスへの逆変換を行います（未サポート）
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
