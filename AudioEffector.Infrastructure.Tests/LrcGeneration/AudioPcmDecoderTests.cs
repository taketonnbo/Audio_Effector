using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Infrastructure.LrcGeneration;
using NAudio.Wave;

namespace AudioEffector.Infrastructure.Tests.LrcGeneration;

public sealed class AudioPcmDecoderTests : IDisposable
{
    private readonly string _tempDir;

    public AudioPcmDecoderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffector_PcmDecoderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 44.1kHz ステレオ WAV ファイルを 16kHz モノラル PCM 配列へ正しくダウンミックス・リサンプルできることを検証します。
    /// </summary>
    [Fact]
    public async Task DecodeToMono16kHzAsync_44100HzステレオWAV_16kHzモノラルサンプル配列へ変換する()
    {
        // Arrange
        var sut = new AudioPcmDecoder();
        string wavPath = Path.Combine(_tempDir, "stereo_44100.wav");
        CreateSineWaveFile(wavPath, sampleRate: 44100, channels: 2, durationSeconds: 1.0);

        // Act
        DecodedAudioPcm decoded = await sut.DecodeToMono16kHzAsync(wavPath);

        // Assert
        Assert.InRange(decoded.Samples.Length, 15500, 16500);
        Assert.InRange(decoded.Duration.TotalSeconds, 0.95, 1.05);
    }

    /// <summary>
    /// 事前にキャンセルされた CancellationToken を渡した場合に OperationCanceledException が送出されることを検証します。
    /// </summary>
    [Fact]
    public async Task DecodeToMono16kHzAsync_事前キャンセル済みトークン_OperationCanceledExceptionを送出する()
    {
        // Arrange
        var sut = new AudioPcmDecoder();
        string wavPath = Path.Combine(_tempDir, "cancel_test.wav");
        CreateSineWaveFile(wavPath, sampleRate: 16000, channels: 1, durationSeconds: 0.5);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.DecodeToMono16kHzAsync(wavPath, cancellationToken: cts.Token));
    }

    /// <summary>
    /// 存在しない音声ファイルパスを指定した場合に FileNotFoundException が送出されることを検証します。
    /// </summary>
    [Fact]
    public async Task DecodeToMono16kHzAsync_存在しないファイル_FileNotFoundExceptionを送出する()
    {
        // Arrange
        var sut = new AudioPcmDecoder();
        string missingPath = Path.Combine(_tempDir, "missing.wav");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.DecodeToMono16kHzAsync(missingPath));
    }

    private static void CreateSineWaveFile(string path, int sampleRate, int channels, double durationSeconds)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        using var writer = new WaveFileWriter(path, format);
        int totalFrames = (int)(sampleRate * durationSeconds);
        float[] buffer = new float[totalFrames * channels];
        for (int frame = 0; frame < totalFrames; frame++)
        {
            float sample = (float)(0.25 * Math.Sin(2.0 * Math.PI * 440.0 * frame / sampleRate));
            for (int ch = 0; ch < channels; ch++)
            {
                buffer[(frame * channels) + ch] = sample;
            }
        }

        writer.WriteSamples(buffer, 0, buffer.Length);
    }
}
