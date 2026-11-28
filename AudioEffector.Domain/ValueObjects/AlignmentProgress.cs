using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// LRC 自動生成・文字起こし処理の進捗情報を表す不変の値オブジェクトです
/// </summary>
public sealed record AlignmentProgress
{
    /// <summary>
    /// 現在の処理フェーズを取得します
    /// </summary>
    public AlignmentPhase Phase { get; }

    /// <summary>
    /// 全体の進捗率（0.0〜1.0）を取得します
    /// </summary>
    public double Percentage { get; }

    /// <summary>
    /// ユーザー向けの現在状態メッセージを取得します
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// <see cref="AlignmentProgress"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="phase">現在の処理フェーズ</param>
    /// <param name="percentage">進捗率（0.0〜1.0 にクランプされます）</param>
    /// <param name="message">ステータスメッセージ</param>
    public AlignmentProgress(AlignmentPhase phase, double percentage, string? message = null)
    {
        Phase = phase;
        Percentage = double.IsNaN(percentage)
            ? 0.0
            : Math.Clamp(percentage, 0.0, 1.0);
        Message = message ?? string.Empty;
    }
}
