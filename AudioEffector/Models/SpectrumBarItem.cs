using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Models
{
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
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

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
}
