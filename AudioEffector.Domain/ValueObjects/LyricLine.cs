using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 歌詞の 1 行（タイムスタンプ・本文・行インデックス）を表す不変の値オブジェクトです。
/// </summary>
public sealed record LyricLine
{
    /// <summary>
    /// 歌詞ドキュメント内における 0 始まりの行インデックスを取得します。
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// 歌詞行に紐づくタイムスタンプを取得します（非同期・プレーン歌詞の場合は <see langword="null"/>）。
    /// </summary>
    public TimeSpan? Timestamp { get; }

    /// <summary>
    /// 歌詞テキストを取得します（間奏・歌唱休止行の場合は空文字列）。
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// タイムスタンプを持つ同期行かどうかを取得します。
    /// </summary>
    public bool HasTimestamp => Timestamp.HasValue;

    /// <summary>
    /// テキストを含まない間奏・歌唱休止行かどうかを取得します。
    /// </summary>
    public bool IsInstrumental => string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// 行クリックによる再生位置シークの対象となるかどうかを取得します。
    /// タイムスタンプを持ち、かつテキストを含む行のみシーク対象です。
    /// </summary>
    public bool IsSeekable => Timestamp.HasValue && !IsInstrumental;

    /// <summary>
    /// <see cref="LyricLine"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="index">0 始まりの行インデックス。</param>
    /// <param name="timestamp">タイムスタンプ（非同期歌詞の場合は <see langword="null"/>。負の値は <see cref="TimeSpan.Zero"/> にクランプされます）。</param>
    /// <param name="text">歌詞テキスト（<see langword="null"/> の場合は空文字列に正規化されます）。</param>
    public LyricLine(int index, TimeSpan? timestamp, string? text)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "行インデックスは 0 以上である必要があります。");
        }

        Index = index;
        Timestamp = timestamp.HasValue
            ? (timestamp.Value < TimeSpan.Zero ? TimeSpan.Zero : timestamp.Value)
            : null;
        Text = text ?? string.Empty;
    }

    /// <summary>
    /// 行インデックスのみを差し替えた新しい <see cref="LyricLine"/> インスタンスを返します。
    /// </summary>
    /// <param name="newIndex">新しい 0 始まりの行インデックス。</param>
    /// <returns>インデックスが更新された <see cref="LyricLine"/>。</returns>
    public LyricLine WithIndex(int newIndex)
    {
        return new LyricLine(newIndex, Timestamp, Text);
    }
}
