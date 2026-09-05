using System;
using AudioEffector.Infrastructure.Audio;
using NAudio.Wave;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Audio;

/// <summary>
/// EqualizerDspの10バンドBiQuadイコライザー処理およびゲイン制御を検証するテストクラス
/// </summary>
public sealed class EqualizerDspTests
{
    private static readonly float[] DefaultFrequencies = [31.25f, 62.5f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f];

    private sealed class TestSampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public WaveFormat WaveFormat { get; }

        public TestSampleProvider(int sampleRate = 44100, int channels = 1, float[]? samples = null)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _samples = samples ?? new float[44100];
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
    /// sourceまたはfrequenciesにnullを指定して初期化した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_sourceまたはfrequenciesがnullの場合_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var source = new TestSampleProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EqualizerDsp(null!, DefaultFrequencies));
        Assert.Throws<ArgumentNullException>(() => new EqualizerDsp(source, null!));
    }

    /// <summary>
    /// WaveFormatプロパティがラップ元のWaveFormatと一致しているかを検証します。
    /// </summary>
    [Fact]
    public void WaveFormat_プロパティ参照_ラップ元ISampleProviderのWaveFormatと一致すること()
    {
        // Arrange
        var source = new TestSampleProvider(sampleRate: 48000, channels: 2);

        // Act
        var sut = new EqualizerDsp(source, DefaultFrequencies);

        // Assert
        Assert.Equal(48000, sut.WaveFormat.SampleRate);
        Assert.Equal(2, sut.WaveFormat.Channels);
    }

    /// <summary>
    /// SetBandGainおよびUpdateGainで特定バンドのゲインを変更した際、例外なく設定され処理が継続できるかを検証します。
    /// </summary>
    [Fact]
    public void SetBandGainおよびUpdateGain_特定バンドのゲイン設定_フィルタが更新されること()
    {
        // Arrange
        var source = new TestSampleProvider();
        var sut = new EqualizerDsp(source, DefaultFrequencies);

        // Act
        sut.SetBandGain(0, 6.0f);
        sut.UpdateGain(1, -3.0f);

        var buffer = new float[100];
        int read = sut.Read(buffer, 0, 100);

        // Assert
        Assert.Equal(100, read);
    }

    /// <summary>
    /// 範囲外（負値またはバンド数以上）のインデックスを指定した場合、例外をスローせず安全に無視されるかを検証します。
    /// </summary>
    [Fact]
    public void SetBandGain_範囲外インデックス指定_例外をスローせず無視されること()
    {
        // Arrange
        var source = new TestSampleProvider();
        var sut = new EqualizerDsp(source, DefaultFrequencies);

        // Act
        var exNegative = Record.Exception(() => sut.SetBandGain(-1, 5.0f));
        var exOverflow = Record.Exception(() => sut.SetBandGain(100, 5.0f));

        // Assert
        Assert.Null(exNegative);
        Assert.Null(exOverflow);
    }

    /// <summary>
    /// SetAllGainsで全バンドのゲインを一括設定した際、例外なく反映されるかを検証します。
    /// </summary>
    [Fact]
    public void SetAllGains_全バンドゲイン一括設定_指定配列の値が反映されること()
    {
        // Arrange
        var source = new TestSampleProvider();
        var sut = new EqualizerDsp(source, DefaultFrequencies);
        float[] gains = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f];

        // Act
        sut.SetAllGains(gains);
        var buffer = new float[100];
        int read = sut.Read(buffer, 0, 100);

        // Assert
        Assert.Equal(100, read);
    }

    /// <summary>
    /// 全バンドが0dB（フラット）の場合、入力サンプルの振幅がほぼ変化せず出力されるかを検証します。
    /// </summary>
    [Fact]
    public void Read_フラット設定_入力サンプルがほぼ変化せず出力されること()
    {
        // Arrange: 1kHzのサイン波入力
        const int sampleRate = 44100;
        const int length = 1000;
        var inputSamples = new float[length];
        for (int i = 0; i < length; i++)
        {
            inputSamples[i] = (float)Math.Sin(2.0 * Math.PI * 1000.0 * i / sampleRate);
        }

        var source = new TestSampleProvider(sampleRate: sampleRate, channels: 1, samples: inputSamples);
        var sut = new EqualizerDsp(source, DefaultFrequencies);
        var buffer = new float[length];

        // Act
        sut.Read(buffer, 0, length);

        // Assert: BiQuadフィルターの過渡応答後の後半サンプルで差分がごく微小（0.05以内）であること
        for (int i = 500; i < length; i++)
        {
            Assert.InRange(Math.Abs(buffer[i] - inputSamples[i]), 0.0f, 0.05f);
        }
    }

    /// <summary>
    /// 特定バンドをブーストした場合、該当周波数の振幅が増加して出力されるかを検証します。
    /// </summary>
    [Fact]
    public void Read_特定バンドブースト_該当周波数の振幅が増加すること()
    {
        // Arrange: 1kHzのサイン波入力（振幅0.2f）
        const int sampleRate = 44100;
        const int length = 2000;
        var inputSamples = new float[length];
        for (int i = 0; i < length; i++)
        {
            inputSamples[i] = 0.2f * (float)Math.Sin(2.0 * Math.PI * 1000.0 * i / sampleRate);
        }

        var source = new TestSampleProvider(sampleRate: sampleRate, channels: 1, samples: inputSamples);
        var sut = new EqualizerDsp(source, DefaultFrequencies);

        // 1000Hz（インデックス5）を+12dBブースト
        sut.SetBandGain(5, 12.0f);
        var buffer = new float[length];

        // Act
        sut.Read(buffer, 0, length);

        // Assert: 安定後のピーク振幅が元の0.2fより明確に大きくなっていること
        float maxOutput = 0f;
        for (int i = 1000; i < length; i++)
        {
            maxOutput = Math.Max(maxOutput, Math.Abs(buffer[i]));
        }

        Assert.True(maxOutput > 0.35f, $"Max output amplitude {maxOutput} should be greater than 0.35f for +12dB boost.");
    }
}
