using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioEffector.Presentation.Converters;

/// <summary>
/// 0始まりのインデックスを1始まりの表示用番号に変換するコンバーター
/// </summary>
public class IndexConverter : IValueConverter
{
    /// <summary>
    /// 0始まりのインデックスを1加算した表示値に変換します
    /// </summary>
    /// <param name="value">0始まりのインデックス（int）</param>
    /// <param name="targetType">ターゲットの型</param>
    /// <param name="parameter">変換パラメーター</param>
    /// <param name="culture">カルチャ情報</param>
    /// <returns>1加算されたインデックス値</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return index + 1;
        }
        return value;
    }

    /// <summary>
    /// 表示用番号から0始まりインデックスへの逆変換を行います（未サポート）
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
