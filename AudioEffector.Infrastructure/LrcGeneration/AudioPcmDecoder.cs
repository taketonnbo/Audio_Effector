using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace AudioEffector.Infrastructure.LrcGeneration;

/// <summary>
/// NAudio を用いて各種音声ファイルをローカル音声認識モデル向けの 16kHz モノラル PCM（float[]）へ変換するデコーダです
/// </summary>
internal sealed class AudioPcmDecoder : IAudioPcmDecoder
{
    private const int TARGET_SAMPLE_RATE = 16000;
    private const int READ_BUFFER_SAMPLES = 16384;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 指定された音声ファイルを 16kHz モノラル PCM 配列へ非同期デコードします
    /// </summary>
    /// <param name="audioFilePath">デコード対象の音声ファイルパス</param>
    /// <param name="progress">デコード進捗（0.0〜1.0、任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デコード結果 <see cref="DecodedAudioPcm"/></returns>
    public Task<DecodedAudioPcm> DecodeToMono16kHzAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
        {
            throw new ArgumentException("音声ファイルパスが指定されていません。", nameof(audioFilePath));
        }

        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("対象の音声ファイルが見つかりません。", audioFilePath);
        }

        return Task.Run(() => DecodeSync(audioFilePath, progress, cancellationToken), cancellationToken);
    }

    private static DecodedAudioPcm DecodeSync(
        string audioFilePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var reader = new AudioFileReader(audioFilePath);
            TimeSpan estimatedDuration = reader.TotalTime;
            long expectedMonoSamples = estimatedDuration > TimeSpan.Zero
                ? Math.Max(TARGET_SAMPLE_RATE, (long)(estimatedDuration.TotalSeconds * TARGET_SAMPLE_RATE))
                : TARGET_SAMPLE_RATE * 60;

            ISampleProvider provider = reader;

            // 多チャンネル音声をモノラルへダウンミックス
            int channels = provider.WaveFormat.Channels;
            if (channels == 2)
            {
                provider = new StereoToMonoSampleProvider(provider)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f,
                };
            }
            else if (channels > 2)
            {
                provider = new MultiChannelToMonoSampleProvider(provider);
            }

            // 16kHz へリサンプリング
            if (provider.WaveFormat.SampleRate != TARGET_SAMPLE_RATE)
            {
                provider = new WdlResamplingSampleProvider(provider, TARGET_SAMPLE_RATE);
            }

            int initialCapacity = (int)Math.Min(expectedMonoSamples, 16_000 * 600);
            var samples = new List<float>(Math.Max(READ_BUFFER_SAMPLES, initialCapacity));
            float[] buffer = new float[READ_BUFFER_SAMPLES];

            int read;
            double lastReported = -1.0;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (int i = 0; i < read; i++)
                {
                    samples.Add(buffer[i]);
                }

                if (progress is not null && expectedMonoSamples > 0)
                {
                    double ratio = Math.Clamp((double)samples.Count / expectedMonoSamples, 0.0, 0.99);
                    if (ratio - lastReported >= 0.05)
                    {
                        progress.Report(ratio);
                        lastReported = ratio;
                    }
                }
            }

            progress?.Report(1.0);

            TimeSpan actualDuration = samples.Count > 0
                ? TimeSpan.FromSeconds((double)samples.Count / TARGET_SAMPLE_RATE)
                : estimatedDuration;

            return new DecodedAudioPcm(samples.ToArray(), actualDuration);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (LrcGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(
                ex,
                "音声ファイルの 16kHz PCM デコードに失敗しました: AudioFilePath={AudioFilePath}, ExceptionType={ExceptionType}",
                audioFilePath,
                ex.GetType().FullName);
            throw LrcInfrastructureExceptionTranslator.TranslateDecodeException(ex, isLogged: true);
        }
    }

    private sealed class MultiChannelToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _sourceBuffer = Array.Empty<float>();

        public WaveFormat WaveFormat { get; }

        public MultiChannelToMonoSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int requiredSourceSamples = count * _channels;
            if (_sourceBuffer.Length < requiredSourceSamples)
            {
                _sourceBuffer = new float[requiredSourceSamples];
            }

            int sourceRead = _source.Read(_sourceBuffer, 0, requiredSourceSamples);
            int framesRead = sourceRead / _channels;

            for (int frame = 0; frame < framesRead; frame++)
            {
                float sum = 0f;
                int baseIndex = frame * _channels;
                for (int ch = 0; ch < _channels; ch++)
                {
                    sum += _sourceBuffer[baseIndex + ch];
                }

                buffer[offset + frame] = sum / _channels;
            }

            return framesRead;
        }
    }
}
