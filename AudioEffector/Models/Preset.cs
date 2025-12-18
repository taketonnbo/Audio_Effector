using System.Collections.Generic;

namespace AudioEffector.Models
{
    /// <summary>
    /// イコライザーのプリセットを表すクラス。
    /// </summary>
    public class Preset
    {
        /// <summary>
        /// プリセット名。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 各バンドのゲイン設定値のリスト。
        /// </summary>
        public List<float> Gains { get; set; } = new List<float>();
    }
}
