using System;

namespace AudioEffector.ViewModels
{
    public class BandViewModel : ViewModelBase
    {
        private float _gain;
        public float Frequency { get; set; }
        public int Index { get; set; }
        public Action<int, float> OnGainChanged { get; set; }

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
        
        public string Label => Frequency >= 1000 ? $"{Frequency/1000:0.#}k" : $"{Frequency}";
    }
}
