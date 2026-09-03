using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// スペクトラムアナライザーのバーを表すViewModelクラス。
/// </summary>
public class SpectrumBarItem : INotifyPropertyChanged
{
    private double _value;

    /// <summary>
    /// バーの高さ（値）。
    /// </summary>
    public double Value
    {
        get => _value;
        set
        {
            if (System.Math.Abs(_value - value) > 0.5)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    private double _peakValue;

    /// <summary>
    /// ピークホールドの高さ（値）。
    /// </summary>
    public double PeakValue
    {
        get => _peakValue;
        set
        {
            if (System.Math.Abs(_peakValue - value) > 0.5)
            {
                _peakValue = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// ピークホールドの滞空カウント。
    /// </summary>
    public int PeakHoldCount { get; set; }

    /// <summary>
    /// プロパティ変更イベント。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// PropertyChangedイベントを発行します。
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
