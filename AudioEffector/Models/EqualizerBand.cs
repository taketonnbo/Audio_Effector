namespace AudioEffector.Models
{
    /// <summary>
    /// イコライザーのバンドを表すクラス。
    /// 特定の周波数帯域のゲインを管理します。
    /// </summary>
    public class EqualizerBand
    {
        /// <summary>
        /// 周波数（Hz）。
        /// </summary>
        public float Frequency { get; set; }

        /// <summary>
        /// ゲイン（dB）。
        /// </summary>
        public float Gain { get; set; }

        /// <summary>
        /// 帯域幅（Q値）。デフォルトは0.8f。
        /// </summary>
        public float Bandwidth { get; set; } = 0.8f;
    }
}
