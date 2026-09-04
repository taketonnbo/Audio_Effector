using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 楽曲の現在再生位置および総再生時間を表す値オブジェクト
/// </summary>
public readonly record struct TimePosition : IEquatable<TimePosition>
{
    /// <summary>
    /// 現在の再生位置
    /// </summary>
    public TimeSpan Current { get; }

    /// <summary>
    /// 総再生時間（トラック長）
    /// </summary>
    public TimeSpan Total { get; }

    /// <summary>
    /// 再生進捗率（0.0〜1.0）
    /// </summary>
    public double ProgressRatio => Total.TotalSeconds > 0
        ? Math.Clamp(Current.TotalSeconds / Total.TotalSeconds, 0.0, 1.0)
        : 0.0;

    /// <summary>
    /// 現在再生位置のフォーマット文字列（例: "03:45" または "1:23:45"）
    /// </summary>
    public string CurrentString => FormatTimeSpan(Current);

    /// <summary>
    /// 総再生時間のフォーマット文字列（例: "03:45" または "1:23:45"）
    /// </summary>
    public string TotalString => FormatTimeSpan(Total);

    /// <summary>
    /// 残り再生時間
    /// </summary>
    public TimeSpan Remaining => Total > Current ? Total - Current : TimeSpan.Zero;

    /// <summary>
    /// 残り再生時間のフォーマット文字列（例: "-01:23"）
    /// </summary>
    public string RemainingString => $"-{FormatTimeSpan(Remaining)}";

    /// <summary>
    /// 初期位置ゼロのTimePositionインスタンス
    /// </summary>
    public static TimePosition Zero => new(TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>
    /// 現在位置と総時間でTimePositionを初期化します
    /// </summary>
    /// <param name="current">現在再生位置</param>
    /// <param name="total">総再生時間</param>
    public TimePosition(TimeSpan current, TimeSpan total)
    {
        Current = current < TimeSpan.Zero ? TimeSpan.Zero : current;
        Total = total < TimeSpan.Zero ? TimeSpan.Zero : total;
    }

    /// <summary>
    /// 現在位置のみを更新した新しいTimePositionオブジェクトを返します
    /// </summary>
    /// <param name="newCurrent">新しい現在位置</param>
    /// <returns>更新されたTimePosition</returns>
    public TimePosition WithCurrent(TimeSpan newCurrent)
    {
        return new TimePosition(newCurrent, Total);
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return timeSpan.TotalHours >= 1
            ? timeSpan.ToString(@"h\:mm\:ss")
            : timeSpan.ToString(@"mm\:ss");
    }

    /// <summary>
    /// 文字列形式（例: "01:23 / 03:45"）に変換します
    /// </summary>
    /// <returns>再生時間情報の文字列</returns>
    public override string ToString()
    {
        return $"{CurrentString} / {TotalString}";
    }
}
