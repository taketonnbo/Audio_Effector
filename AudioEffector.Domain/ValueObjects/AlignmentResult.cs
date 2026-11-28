using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 音声アライメント／文字起こしエンジンの生成結果を表す不変の値オブジェクトです
/// </summary>
public sealed record AlignmentResult
{
    /// <summary>
    /// 生成された同期歌詞ドキュメントを取得します
    /// </summary>
    public LyricsDocument Document { get; }

    /// <summary>
    /// LRC 形式に整形されたテキスト文字列を取得します
    /// </summary>
    public string LrcContent { get; }

    /// <summary>
    /// 解析中に抽出された認識セグメント一覧を取得します
    /// </summary>
    public IReadOnlyList<AlignmentSegment> RecognizedSegments { get; }

    /// <summary>
    /// 実行された LRC 生成モードを取得します
    /// </summary>
    public LrcGenerationMode Mode { get; }

    /// <summary>
    /// 処理を実行した解析エンジン名を取得します
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// <see cref="AlignmentResult"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="document">生成された歌詞ドキュメント</param>
    /// <param name="lrcContent">フォーマット済み LRC テキスト</param>
    /// <param name="recognizedSegments">認識セグメント一覧</param>
    /// <param name="mode">実行モード</param>
    /// <param name="engineName">解析エンジン名</param>
    public AlignmentResult(
        LyricsDocument document,
        string lrcContent,
        IEnumerable<AlignmentSegment>? recognizedSegments,
        LrcGenerationMode mode,
        string engineName)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        LrcContent = lrcContent ?? string.Empty;
        RecognizedSegments = recognizedSegments is null
            ? Array.Empty<AlignmentSegment>()
            : new ReadOnlyCollection<AlignmentSegment>(recognizedSegments.Where(s => s is not null).ToArray());
        Mode = mode;
        EngineName = string.IsNullOrWhiteSpace(engineName) ? "Unknown" : engineName.Trim();
    }
}
