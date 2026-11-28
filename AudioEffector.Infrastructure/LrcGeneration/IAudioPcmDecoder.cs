using System;
using System.Threading;
using System.Threading.Tasks;

namespace AudioEffector.Infrastructure.LrcGeneration;

/// <summary>
/// 音声ファイルを音声認識モデル向けの 16kHz モノラル PCM へ変換する内部デコーダ契約です
/// </summary>
internal interface IAudioPcmDecoder
{
    /// <summary>
    /// 指定された音声ファイルを 16kHz モノラル PCM 配列へ非同期デコードします
    /// </summary>
    /// <param name="audioFilePath">デコード対象の音声ファイルパス</param>
    /// <param name="progress">デコード進捗（0.0〜1.0、任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デコード結果 <see cref="DecodedAudioPcm"/></returns>
    Task<DecodedAudioPcm> DecodeToMono16kHzAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 音声デコード結果（16kHz モノラル PCM サンプル配列と総再生時間）を保持するレコードです
/// </summary>
/// <param name="Samples">16kHz モノラル正規化済み浮動小数点 PCM サンプル配列</param>
/// <param name="Duration">デコードされた音声の総再生時間</param>
internal sealed record DecodedAudioPcm(float[] Samples, TimeSpan Duration);
