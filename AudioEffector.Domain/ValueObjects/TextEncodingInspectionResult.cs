using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 入力歌詞テキストファイルの文字コードおよび行数検査結果を表す不変の値オブジェクトです
/// </summary>
public sealed record TextEncodingInspectionResult
{
    /// <summary>
    /// 検査対象のファイルパスを取得します
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 入力ファイルが有効な UTF-8 であるかどうかを取得します
    /// </summary>
    public bool IsUtf8 { get; }

    /// <summary>
    /// UTF-8 BOM（0xEF 0xBB 0xBF）が付与されているかどうかを取得します
    /// </summary>
    public bool HasUtf8Bom { get; }

    /// <summary>
    /// 検出された文字コードの表示名（例: <c>UTF-8</c>, <c>Shift-JIS (CP932)</c>, <c>EUC-JP</c>, <c>UTF-16 LE</c>）を取得します
    /// </summary>
    public string DetectedEncodingName { get; }

    /// <summary>
    /// 続行前にユーザー承諾を伴う UTF-8 変換が必要かどうかを取得します
    /// </summary>
    public bool RequiresConversion => !IsUtf8;

    /// <summary>
    /// ファイル内の総行数を取得します
    /// </summary>
    public int TotalLineCount { get; }

    /// <summary>
    /// 空白・改行のみの行を除いた有効な歌詞行数を取得します
    /// </summary>
    public int NonEmptyLineCount { get; }

    /// <summary>
    /// ファイルが空または有効な歌詞行が 1 行も含まれていないかどうかを取得します
    /// </summary>
    public bool IsEmpty => NonEmptyLineCount <= 0;

    /// <summary>
    /// <see cref="TextEncodingInspectionResult"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="filePath">対象ファイルパス</param>
    /// <param name="isUtf8">UTF-8 かどうか</param>
    /// <param name="hasUtf8Bom">UTF-8 BOM を持つかどうか</param>
    /// <param name="detectedEncodingName">検出された文字コード名</param>
    /// <param name="totalLineCount">総行数</param>
    /// <param name="nonEmptyLineCount">非空行数</param>
    public TextEncodingInspectionResult(
        string filePath,
        bool isUtf8,
        bool hasUtf8Bom,
        string detectedEncodingName,
        int totalLineCount,
        int nonEmptyLineCount)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        IsUtf8 = isUtf8;
        HasUtf8Bom = hasUtf8Bom;
        DetectedEncodingName = string.IsNullOrWhiteSpace(detectedEncodingName) ? "Unknown" : detectedEncodingName.Trim();
        TotalLineCount = Math.Max(0, totalLineCount);
        NonEmptyLineCount = Math.Max(0, nonEmptyLineCount);
    }
}
