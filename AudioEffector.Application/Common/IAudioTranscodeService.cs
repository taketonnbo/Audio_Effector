using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AudioEffector.Application.Common;

/// <summary>
/// 機器転送時の音声フォーマット変換および一時ファイル管理を担うサービスインターフェース
/// </summary>
public interface IAudioTranscodeService
{
    /// <summary>
    /// 指定されたファイルがオプションに基づき形式変換の対象であるかを判定します
    /// </summary>
    /// <param name="sourceFilePath">変換元ファイルパス</param>
    /// <param name="options">変換オプション</param>
    /// <returns>変換対象の場合はtrue</returns>
    bool NeedsTranscode(string sourceFilePath, AudioTranscodeOptions options);

    /// <summary>
    /// 変換オプションを適用した場合の転送先ファイル名を解決します
    /// </summary>
    /// <param name="sourceFilePath">変換元ファイルパス</param>
    /// <param name="options">変換オプション</param>
    /// <returns>転送先ファイル名</returns>
    string ResolveTargetFileName(string sourceFilePath, AudioTranscodeOptions options);

    /// <summary>
    /// 指定ファイルを転送用に準備します（変換対象の場合は一時ディレクトリへ変換して返却）
    /// </summary>
    /// <param name="sourceFilePath">変換元ファイルパス</param>
    /// <param name="options">変換オプション</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>転送準備結果（使用後に必ずDisposeすること）</returns>
    Task<TranscodedAudioResult> PrepareForTransferAsync(
        string sourceFilePath,
        AudioTranscodeOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 機器転送時の音声フォーマット変換オプション
/// </summary>
public sealed class AudioTranscodeOptions
{
    /// <summary>
    /// MP4形式の音声ファイルをM4A形式へ変換するかどうかを取得または設定します
    /// </summary>
    public bool ConvertMp4ToM4a { get; init; }
}

/// <summary>
/// 転送用に準備された音声ファイル（変換時は一時ファイル・一時ディレクトリの自動後始末を提供）
/// </summary>
public sealed class TranscodedAudioResult : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// 変換元のオリジナルファイルパスを取得します
    /// </summary>
    public string SourceFilePath { get; }

    /// <summary>
    /// 実際に転送すべきローカルファイルパス（変換なし時は元パス、変換あり時は一時ファイルパス）を取得します
    /// </summary>
    public string OutputFilePath { get; }

    /// <summary>
    /// 転送先デバイスへ書き込むファイル名（変換時は変更後の拡張子）を取得します
    /// </summary>
    public string TargetFileName { get; }

    /// <summary>
    /// 形式変換が実施され一時ファイルが生成されたかどうかを取得します
    /// </summary>
    public bool WasTranscoded { get; }

    /// <summary>
    /// 一時ファイルを格納した一時ディレクトリパス（変換なし時はnull）を取得します
    /// </summary>
    public string? TemporaryDirectoryPath { get; }

    /// <summary>
    /// TranscodedAudioResultを初期化します
    /// </summary>
    /// <param name="sourceFilePath">変換元ファイルパス</param>
    /// <param name="outputFilePath">転送対象ファイルパス</param>
    /// <param name="targetFileName">転送先ファイル名</param>
    /// <param name="wasTranscoded">形式変換が行われたかどうか</param>
    /// <param name="temporaryDirectoryPath">一時ディレクトリパス（変換なし時はnull）</param>
    public TranscodedAudioResult(
        string sourceFilePath,
        string outputFilePath,
        string targetFileName,
        bool wasTranscoded,
        string? temporaryDirectoryPath = null)
    {
        SourceFilePath = sourceFilePath ?? throw new ArgumentNullException(nameof(sourceFilePath));
        OutputFilePath = outputFilePath ?? throw new ArgumentNullException(nameof(outputFilePath));
        TargetFileName = targetFileName ?? throw new ArgumentNullException(nameof(targetFileName));
        WasTranscoded = wasTranscoded;
        TemporaryDirectoryPath = temporaryDirectoryPath;
    }

    /// <summary>
    /// 変換なし（オリジナルファイルをそのまま転送）の結果を生成します
    /// </summary>
    /// <param name="sourceFilePath">元ファイルパス</param>
    /// <returns>変換なしの転送準備結果</returns>
    public static TranscodedAudioResult Passthrough(string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        return new TranscodedAudioResult(
            sourceFilePath,
            sourceFilePath,
            Path.GetFileName(sourceFilePath),
            wasTranscoded: false,
            temporaryDirectoryPath: null);
    }

    /// <summary>
    /// 生成された一時ファイルおよび一時ディレクトリを安全にクリーンアップします
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!WasTranscoded)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(OutputFilePath) && File.Exists(OutputFilePath))
            {
                File.Delete(OutputFilePath);
            }
        }
        catch
        {
            // 一時ファイル削除失敗時はアプリを落とさない
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(TemporaryDirectoryPath) && Directory.Exists(TemporaryDirectoryPath))
            {
                Directory.Delete(TemporaryDirectoryPath, recursive: true);
            }
        }
        catch
        {
            // 一時ディレクトリ削除失敗時はアプリを落とさない
        }
    }
}
