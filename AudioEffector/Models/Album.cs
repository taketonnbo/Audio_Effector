using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AudioEffector.Models
{
    /// <summary>
    /// アルバムを表すクラス。
    /// プロパティ値の変更通知をサポートするためにINotifyPropertyChangedインターフェースを実装しています。
    /// </summary>
    public class Album : INotifyPropertyChanged
    {
        /// <summary>
        /// アルバムのタイトルを取得または設定します。
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// アーティスト名を取得または設定します。
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// アルバムのカバー画像を取得または設定します。
        /// </summary>
        public BitmapImage CoverImage { get; set; }

        /// <summary>
        /// アルバムに含まれるトラックのリストを取得または設定します。
        /// </summary>
        public List<Track> Tracks { get; set; } = new List<Track>();

        /// <summary>
        /// アルバムのリリース年を取得または設定します。
        /// </summary>
        public uint Year { get; set; }

        private bool _isSelected;
        /// <summary>
        /// アルバムが選択されているかどうかを示す値を取得または設定します。
        /// この値を設定すると、アルバム内のすべてのトラックの選択状態も更新されます。
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();

                    // 選択状態をトラックに伝播させる
                    if (Tracks != null)
                    {
                        foreach (var track in Tracks)
                        {
                            track.IsSelected = value;
                        }
                    }
                }
            }
        }

        private bool _isOnDevice;
        /// <summary>
        /// アルバムがデバイス上に存在するかどうかを示す値を取得または設定します。
        /// </summary>
        public bool IsOnDevice
        {
            get => _isOnDevice;
            set
            {
                if (_isOnDevice != value)
                {
                    _isOnDevice = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// プロパティ値が変更されたときに発生するイベントです。
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// PropertyChangedイベントを発行します。
        /// </summary>
        /// <param name="propertyName">変更されたプロパティの名前（呼び出し元から自動的に取得されます）。</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
