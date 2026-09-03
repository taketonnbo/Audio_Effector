using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// すべてのViewModelの基底クラス
/// INotifyPropertyChangedを実装し、プロパティ変更通知機能およびSetPropertyヘルパーを提供します
/// </summary>
public class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// プロパティ変更通知イベント
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// プロパティ変更イベントを発行します
    /// </summary>
    /// <param name="propertyName">変更されたプロパティ名（CallerMemberNameにより自動設定）</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// フィールド値を更新し、値が変更された場合にPropertyChangedイベントを発行します
    /// </summary>
    /// <typeparam name="T">プロパティの型</typeparam>
    /// <param name="field">バッキングフィールドへの参照</param>
    /// <param name="value">設定する新しい値</param>
    /// <param name="propertyName">プロパティ名</param>
    /// <returns>値が変更された場合はtrue、それ以外はfalse</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
