using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 音量値（0.0〜1.0）およびミュート状態を表す値オブジェクト
/// </summary>
public readonly record struct Volume : IEquatable<Volume>
{
    /// <summary>
    /// 最小音量値（無音）
    /// </summary>
    public const float MIN_VOLUME = 0.0f;

    /// <summary>
    /// 最大音量値（100%）
    /// </summary>
    public const float MAX_VOLUME = 1.0f;

    /// <summary>
    /// デフォルト音量値（50%）
    /// </summary>
    public const float DEFAULT_VOLUME = 0.5f;

    /// <summary>
    /// 設定音量値（0.0〜1.0）
    /// </summary>
    public float Value { get; }

    /// <summary>
    /// ミュート状態かどうか
    /// </summary>
    public bool IsMuted { get; }

    /// <summary>
    /// 実効音量（ミュート時は0.0、それ以外はValue）
    /// </summary>
    public float EffectiveVolume => IsMuted ? MIN_VOLUME : Value;

    /// <summary>
    /// 音量パーセント表示（0〜100%）
    /// </summary>
    public int Percent => (int)MathF.Round(Value * 100.0f);

    private Volume(float value, bool isMuted)
    {
        Value = Math.Clamp(value, MIN_VOLUME, MAX_VOLUME);
        IsMuted = isMuted;
    }

    /// <summary>
    /// float値からVolumeオブジェクトを生成します
    /// </summary>
    /// <param name="value">音量値（0.0〜1.0）</param>
    /// <param name="isMuted">ミュート状態</param>
    /// <returns>生成されたVolumeオブジェクト</returns>
    public static Volume FromFloat(float value, bool isMuted = false)
    {
        return new Volume(value, isMuted);
    }

    /// <summary>
    /// パーセント値（0〜100）からVolumeオブジェクトを生成します
    /// </summary>
    /// <param name="percent">パーセント（0〜100）</param>
    /// <param name="isMuted">ミュート状態</param>
    /// <returns>生成されたVolumeオブジェクト</returns>
    public static Volume FromPercent(int percent, bool isMuted = false)
    {
        return new Volume(percent / 100.0f, isMuted);
    }

    /// <summary>
    /// ミュート状態を切り替えた新しいVolumeオブジェクトを返します
    /// </summary>
    /// <param name="isMuted">新しいミュート状態</param>
    /// <returns>更新されたVolumeオブジェクト</returns>
    public Volume WithMute(bool isMuted)
    {
        return new Volume(Value, isMuted);
    }

    /// <summary>
    /// 音量値を変更した新しいVolumeオブジェクトを返します
    /// </summary>
    /// <param name="newValue">新しい音量値</param>
    /// <returns>更新されたVolumeオブジェクト</returns>
    public Volume WithValue(float newValue)
    {
        return new Volume(newValue, IsMuted);
    }

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>音量情報の文字列</returns>
    public override string ToString()
    {
        return IsMuted ? $"Muted ({Percent}%)" : $"{Percent}%";
    }
}
