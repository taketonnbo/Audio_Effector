using System;
using System.IO;
using System.Linq;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 音声ファイルのパスを表す値オブジェクト
/// </summary>
public readonly record struct AudioPath : IEquatable<AudioPath>
{
    private static readonly string[] SUPPORTED_EXTENSIONS = [".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".alac", ".aiff"];

    /// <summary>
    /// ファイルパスの文字列
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// ファイル拡張子（小文字、例: ".mp3"）
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// ファイル名（拡張子含む）
    /// </summary>
    public string FileName { get; }

    private AudioPath(string value, string extension, string fileName)
    {
        Value = value;
        Extension = extension;
        FileName = fileName;
    }

    /// <summary>
    /// 指定されたパスからAudioPathを生成します
    /// </summary>
    /// <param name="path">ファイルパス文字列</param>
    /// <returns>生成されたAudioPath</returns>
    public static AudioPath Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("ファイルパスを空にすることはできません", nameof(path));
        }

        var trimmed = path.Trim();
        var extension = Path.GetExtension(trimmed).ToLowerInvariant();
        var fileName = Path.GetFileName(trimmed);

        return new AudioPath(trimmed, extension, fileName);
    }

    /// <summary>
    /// サポート対象の音声フォーマットかどうかを判定します
    /// </summary>
    /// <returns>サポート対象の場合はtrue、それ以外はfalse</returns>
    public bool IsSupportedAudioFormat()
    {
        return SUPPORTED_EXTENSIONS.Contains(Extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 文字列形式（フルパス）に変換します
    /// </summary>
    /// <returns>フルパス文字列</returns>
    public override string ToString() => Value;
}
