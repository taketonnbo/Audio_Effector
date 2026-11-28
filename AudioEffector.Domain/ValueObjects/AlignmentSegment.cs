using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 音声認識エンジンから抽出された 1 つの発話セグメント（開始時刻・終了時刻・認識テキスト）を表す不変の値オブジェクトです
/// </summary>
public sealed record AlignmentSegment
{
    /// <summary>
    /// セグメントの開始時刻を取得します
    /// </summary>
    public TimeSpan Start { get; }

    /// <summary>
    /// セグメントの終了時刻を取得します
    /// </summary>
    public TimeSpan End { get; }

    /// <summary>
    /// 認識されたテキストを取得します
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// <see cref="AlignmentSegment"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="start">開始時刻（負の値は <see cref="TimeSpan.Zero"/> にクランプされます）</param>
    /// <param name="end">終了時刻（<paramref name="start"/> 未満の場合は <paramref name="start"/> にクランプされます）</param>
    /// <param name="text">認識テキスト</param>
    public AlignmentSegment(TimeSpan start, TimeSpan end, string? text)
    {
        Start = start < TimeSpan.Zero ? TimeSpan.Zero : start;
        End = end < Start ? Start : end;
        Text = (text ?? string.Empty).Trim();
    }
}
