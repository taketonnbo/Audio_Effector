using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.ViewModels
{
    /// <summary>
    /// すべてのViewModelの基底クラス。
    /// INotifyPropertyChangedを実装し、プロパティ変更通知機能を提供します。
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// プロパティ変更イベントを発行します。
        /// </summary>
        /// <param name="name">変更されたプロパティ名（CallerMemberNameにより自動設定）。</param>
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
