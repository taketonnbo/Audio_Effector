using System;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// ローカル音声認識モデルの識別情報・配置パス・利用可否を表す不変の値オブジェクトです
/// </summary>
public sealed record LocalSpeechModelInfo
{
    /// <summary>
    /// モデル識別子（例: <c>tiny</c>, <c>base</c>, <c>small</c>, <c>custom</c>）を取得します
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// ユーザー向けの表示名（例: <c>Whisper (base)</c>）を取得します
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// モデルファイル名（例: <c>ggml-base.bin</c>）を取得します
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// モデルファイルのフルパスを取得します
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// モデルファイルがディスク上に存在し利用可能かどうかを取得します
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// モデルファイルの推定サイズ（バイト）を取得します
    /// </summary>
    public long EstimatedSizeBytes { get; }

    /// <summary>
    /// <see cref="LocalSpeechModelInfo"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="modelId">モデル識別子</param>
    /// <param name="displayName">表示名</param>
    /// <param name="fileName">ファイル名</param>
    /// <param name="filePath">ファイルフルパス</param>
    /// <param name="isAvailable">利用可能かどうか</param>
    /// <param name="estimatedSizeBytes">推定サイズ（バイト）</param>
    public LocalSpeechModelInfo(
        string modelId,
        string displayName,
        string fileName,
        string filePath,
        bool isAvailable,
        long estimatedSizeBytes)
    {
        ModelId = string.IsNullOrWhiteSpace(modelId) ? throw new ArgumentException("モデル識別子は必須です。", nameof(modelId)) : modelId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ModelId : displayName.Trim();
        FileName = fileName ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        IsAvailable = isAvailable;
        EstimatedSizeBytes = Math.Max(0L, estimatedSizeBytes);
    }
}
