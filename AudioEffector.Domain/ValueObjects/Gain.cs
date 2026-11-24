using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// イコライザー等のゲイン値（dB単位）を表す値オブジェクト
/// </summary>
public readonly record struct Gain : IEquatable<Gain>
{
    /// <summary>
    /// 最小許容ゲイン値（dB）
    /// </summary>
    public const float MIN_GAIN_DB = -12.0f;

    /// <summary>
    /// 最大許容ゲイン値（dB）
    /// </summary>
    public const float MAX_GAIN_DB = 12.0f;

    /// <summary>
    /// デフォルトゲイン値（0dB）
    /// </summary>
    public const float DEFAULT_GAIN_DB = 0.0f;

    /// <summary>
    /// ゲイン値（dB）
    /// </summary>
    public float Value { get; }

    /// <summary>
    /// 0dBを表すゲインインスタンス
    /// </summary>
    public static Gain Zero => new(DEFAULT_GAIN_DB);

    private Gain(float value)
    {
        Value = Math.Clamp(value, MIN_GAIN_DB, MAX_GAIN_DB);
    }

    /// <summary>
    /// デシベル値からGainオブジェクトを生成します（範囲外はクランプされます）
    /// </summary>
    /// <param name="db">デシベル値（-12.0〜+12.0）</param>
    /// <returns>生成されたGainオブジェクト</returns>
    public static Gain FromDecibels(float db)
    {
        return new Gain(db);
    }

    /// <summary>
    /// デシベル値から線形増幅率（リニア倍率）を計算します
    /// </summary>
    /// <returns>リニア倍率</returns>
    public float ToLinearGain()
    {
        return MathF.Pow(10.0f, Value / 20.0f);
    }

    /// <summary>
    /// 文字列形式（例: "+3.0 dB"）に変換します
    /// </summary>
    /// <returns>フォーマット済み文字列</returns>
    public override string ToString()
    {
        return Value >= 0 ? $"+{Value:F1} dB" : $"{Value:F1} dB";
    }
}
