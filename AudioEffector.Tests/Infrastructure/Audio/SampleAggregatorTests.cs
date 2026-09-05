using System;
using AudioEffector.Application.Common;
using AudioEffector.Infrastructure.Audio;
using NAudio.Wave;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Audio;

/// <summary>
/// SampleAggregatorのサンプル集計、バッファリング、FFTイベント発火を検証するテストクラス
/// </summary>
public sealed class SampleAggregatorTests
{
    private sealed class TestSampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public WaveFormat WaveFormat { get; }

        public TestSampleProvider(int sampleRate = 44100, int channels = 2, float[]? samples = null)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _samples = samples ?? new float[44100 * channels];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = Math.Min(count, _samples.Length - _position);
            if (available <= 0) return 0;
            Array.Copy(_samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }

    /// <summary>
    /// source引数にnullを指定して初期化した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_sourceがnullの場合_ArgumentNullExceptionをスローすること()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SampleAggregator(null!));
    }

    /// <summary>
    /// WaveFormatプロパティがラップ元のISampleProviderのWaveFormatと一致しているかを検証します。
    /// </summary>
    [Fact]
    public void WaveFormat_プロパティ参照_ラップ元ISampleProviderのWaveFormatと一致すること()
    {
        // Arrange
        var source = new TestSampleProvider(sampleRate: 48000, channels: 2);

        // Act
        var sut = new SampleAggregator(source);

        // Assert
        Assert.Equal(48000, sut.WaveFormat.SampleRate);
        Assert.Equal(2, sut.WaveFormat.Channels);
    }

    /// <summary>
    /// Read呼び出し時に、ラップ元のソースから要求されたサンプル数を正しく読み込んで返すかを検証します。
    /// </summary>
    [Fact]
    public void Read_指定サンプル数読み込み_ラップ元から正しいサンプル数を読み込んで返すこと()
    {
        // Arrange
        var source = new TestSampleProvider(channels: 1, samples: new float[1000]);
        var sut = new SampleAggregator(source, fftLength: 1024);
        var buffer = new float[500];

        // Act
        int readCount = sut.Read(buffer, 0, 500);

        // Assert
        Assert.Equal(500, readCount);
    }

    /// <summary>
    /// 蓄積サンプル数がfftLengthに達した際、FftCalculatedイベントが発火し、正しい配列長とサンプリング周波数が通知されるかを検証します。
    /// </summary>
    [Fact]
    public void FftCalculated_蓄積サンプル数がfftLengthに達した時_FftCalculatedイベントが発火すること()
    {
        // Arrange
        const int fftLength = 1024;
        var source = new TestSampleProvider(sampleRate: 44100, channels: 1, samples: new float[2048]);
        var sut = new SampleAggregator(source, fftLength);
        var buffer = new float[fftLength];

        FftCalculatedEventArgs? receivedArgs = null;
        sut.FftCalculated += (_, e) => receivedArgs = e;

        // Act
        sut.Read(buffer, 0, fftLength);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(fftLength / 2, receivedArgs.Magnitudes.Length);
        Assert.Equal(44100, receivedArgs.SampleRate);
    }

    /// <summary>
    /// 蓄積サンプル数がfftLengthに達した際、ComplexFftCalculatedイベントが発火し、正しい複素数バッファ長が通知されるかを検証します。
    /// </summary>
    [Fact]
    public void ComplexFftCalculated_蓄積サンプル数がfftLengthに達した時_ComplexFftCalculatedイベントが発火すること()
    {
        // Arrange
        const int fftLength = 512;
        var source = new TestSampleProvider(channels: 1, samples: new float[1024]);
        var sut = new SampleAggregator(source, fftLength);
        var buffer = new float[fftLength];

        FftEventArgs? receivedArgs = null;
        sut.ComplexFftCalculated += (_, e) => receivedArgs = e;

        // Act
        sut.Read(buffer, 0, fftLength);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(fftLength, receivedArgs.Result.Length);
    }

    /// <summary>
    /// 蓄積サンプル数がfftLength未満の場合、FftCalculatedイベントが発火しないことを検証します。
    /// </summary>
    [Fact]
    public void Read_蓄積サンプル数がfftLength未満の場合_イベントが発火しないこと()
    {
        // Arrange
        const int fftLength = 1024;
        var source = new TestSampleProvider(channels: 1, samples: new float[1024]);
        var sut = new SampleAggregator(source, fftLength);
        var buffer = new float[512];

        bool eventFired = false;
        sut.FftCalculated += (_, _) => eventFired = true;

        // Act
        sut.Read(buffer, 0, 512);

        // Assert
        Assert.False(eventFired);
    }

    /// <summary>
    /// 複数回のRead呼び出しで累計サンプル数がfftLengthに達した瞬間にイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void Read_複数回の読み込みで累計サンプル数がfftLengthに達した時_イベントが発火すること()
    {
        // Arrange
        const int fftLength = 1024;
        var source = new TestSampleProvider(channels: 1, samples: new float[2048]);
        var sut = new SampleAggregator(source, fftLength);
        var buffer = new float[512];

        int eventCount = 0;
        sut.FftCalculated += (_, _) => eventCount++;

        // Act
        sut.Read(buffer, 0, 512); // 累計512
        Assert.Equal(0, eventCount);

        sut.Read(buffer, 0, 512); // 累計1024 -> 発火

        // Assert
        Assert.Equal(1, eventCount);
    }

    /// <summary>
    /// ステレオ音源の場合、チャンネルステップ分スキップして左チャンネルのサンプルが集計されることを検証します。
    /// </summary>
    [Fact]
    public void Read_ステレオ音源_左チャンネルのみが集計されること()
    {
        // Arrange
        const int fftLength = 256;
        int totalStereoSamples = fftLength * 2; // 2チャンネル
        var stereoSamples = new float[totalStereoSamples];
        // 左チャンネル（偶数インデックス）に0.5f、右チャンネル（奇数インデックス）に0.0f
        for (int i = 0; i < totalStereoSamples; i += 2)
        {
            stereoSamples[i] = 0.5f;
            stereoSamples[i + 1] = 0.0f;
        }

        var source = new TestSampleProvider(channels: 2, samples: stereoSamples);
        var sut = new SampleAggregator(source, fftLength);
        var buffer = new float[totalStereoSamples];

        FftCalculatedEventArgs? args = null;
        sut.FftCalculated += (_, e) => args = e;

        // Act
        sut.Read(buffer, 0, totalStereoSamples);

        // Assert: 256サンプルの左チャンネル（0.5f）が集計され、FFTが計算されていること
        Assert.NotNull(args);
        Assert.Equal(fftLength / 2, args.Magnitudes.Length);
    }
}
