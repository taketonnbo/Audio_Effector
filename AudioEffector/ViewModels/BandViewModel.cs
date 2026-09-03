using System;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.ViewModels
{
    /// <summary>
    /// イコライザーの各バンド（周波数帯域）を表すViewModelクラス。
    /// </summary>
    public class BandViewModel : ViewModelBase
    {
        private float _gain;

        /// <summary>
        /// バンドの周波数（Hz）。
        /// </summary>
        public float Frequency { get; set; }

        /// <summary>
        /// バンドのインデックス。
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// ゲイン値が変更された際に呼び出されるコールバック。
        /// </summary>
        public Action<int, float> OnGainChanged { get; set; }

        /// <summary>
        /// 現在のゲイン値（dB）。
        /// </summary>
        public float Gain
        {
            get => _gain;
            set
            {
                if (Math.Abs(_gain - value) > 0.01f)
                {
                    _gain = value;
                    OnPropertyChanged();
                    OnGainChanged?.Invoke(Index, _gain);
                }
            }
        }

        /// <summary>
        /// UI表示用のラベル（例: 1k, 500）。
        /// </summary>
        public string Label => Frequency >= 1000 ? $"{Frequency / 1000:0.#}k" : $"{Frequency}";
    }
}
