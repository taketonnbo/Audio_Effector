using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// イコライザーの周波数バンド（中心周波数、ゲイン、帯域幅）を表す値オブジェクト
/// </summary>
public readonly record struct FrequencyBand : IEquatable<FrequencyBand>
{
    /// <summary>
    /// 中心周波数（Hz）
    /// </summary>
    public float CenterFrequency { get; }

    /// <summary>
    /// ゲイン設定値
    /// </summary>
    public Gain Gain { get; }

    /// <summary>
    /// 帯域幅（オクターブまたはQ値）
    /// </summary>
    public float Bandwidth { get; }

    /// <summary>
    /// 指定された中心周波数・ゲイン・帯域幅で周波数バンドを初期化します
    /// </summary>
    /// <param name="centerFrequency">中心周波数（Hz）</param>
    /// <param name="gain">ゲイン</param>
    /// <param name="bandwidth">帯域幅（デフォルト: 1.0f）</param>
    public FrequencyBand(float centerFrequency, Gain gain, float bandwidth = 1.0f)
    {
        if (centerFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(centerFrequency), "中心周波数は0より大きい必要があります");
        }

        CenterFrequency = centerFrequency;
        Gain = gain;
        Bandwidth = bandwidth > 0 ? bandwidth : 1.0f;
    }

    /// <summary>
    /// 新しいゲインを適用した周波数バンドを返します
    /// </summary>
    /// <param name="newGain">新しいゲイン</param>
    /// <returns>更新された周波数バンド</returns>
    public FrequencyBand WithGain(Gain newGain)
    {
        return new FrequencyBand(CenterFrequency, newGain, Bandwidth);
    }

    /// <summary>
    /// 周波数ラベル（例: "1.0 kHz" または "60 Hz"）を取得します
    /// </summary>
    /// <returns>周波数表示用文字列</returns>
    public string GetFrequencyLabel()
    {
        return CenterFrequency >= 1000.0f
            ? $"{CenterFrequency / 1000.0f:F1} kHz"
            : $"{CenterFrequency:F0} Hz";
    }

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>バンド情報の文字列</returns>
    public override string ToString()
    {
        return $"{GetFrequencyLabel()}: {Gain}";
    }
}
