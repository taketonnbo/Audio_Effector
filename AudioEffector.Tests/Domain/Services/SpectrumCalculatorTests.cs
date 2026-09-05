using System;
using AudioEffector.Domain.Services;
using Xunit;

namespace AudioEffector.Tests.Domain.Services;

public class SpectrumCalculatorTests
{
    /// <summary>
    /// 正常なFFT振幅配列とサンプリングレートを指定した場合、指定本数のバー振幅配列が生成されるかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_正常なFFT入力_指定本数のバー振幅配列を返すこと()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        var fftMagnitudes = new double[512];
        Array.Fill(fftMagnitudes, 0.05);

        // Act
        var bars = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 64);

        // Assert
        Assert.Equal(64, bars.Length);
        Assert.All(bars, val => Assert.True(val >= 0.0));
    }

    /// <summary>
    /// 入力FFT振幅がすべて0（無音）かつチルト補正なしの場合、すべてのバー振幅が0.0となるかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_無音入力_全要素0のバー振幅を返すこと()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        var fftMagnitudes = new double[512]; // 全要素0.0

        // Act
        var bars = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 32, trebleTiltDb: 0.0);

        // Assert
        Assert.Equal(32, bars.Length);
        Assert.All(bars, val => Assert.Equal(0.0, val));
    }

    /// <summary>
    /// 特定周波数（例: 1000Hz）に強い振幅ピークが存在する場合、対応する周波数帯域のバー振幅が突出して高くなるかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_特定周波数ピーク入力_対応する帯域バーの振幅が最大となること()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        int sampleRate = 44100;
        int fftLength = 1024;
        var fftMagnitudes = new double[fftLength / 2]; // 512 bins, 各ビンは約43.06Hz
        double targetFreq = 1000.0;
        int targetBin = (int)Math.Round(targetFreq / (sampleRate / (double)fftLength));
        fftMagnitudes[targetBin] = 10.0; // 1kHzに突出した振幅

        // Act
        var bars = sut.CalculateBars(fftMagnitudes, sampleRate, barCount: 64);

        // Assert
        double maxBarVal = 0.0;
        int maxBarIndex = -1;
        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] > maxBarVal)
            {
                maxBarVal = bars[i];
                maxBarIndex = i;
            }
        }

        Assert.True(maxBarVal > 0.0);
        Assert.InRange(maxBarIndex, 25, 45); // 1kHzは64本中中音域（約35番前後）にマッピングされる
    }

    /// <summary>
    /// 低音域スケーリング係数（bassScale）を変更した際、低音域バーの出力値に比例して反映されるかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_低音中音高音スケーリング係数_指定された重み付けが適用されること()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        var fftMagnitudes = new double[512];
        Array.Fill(fftMagnitudes, 0.1);

        // Act
        var barsLowScale = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 64, bassScale: 0.5);
        var barsHighScale = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 64, bassScale: 1.0);

        // Assert
        // 低音域（先頭の数本）はbassScaleが大きい方が高くなる
        Assert.True(barsHighScale[1] > barsLowScale[1]);
    }

    /// <summary>
    /// 高音域チルト補正（trebleTiltDb）を高く設定した場合、高音域のバー振幅が増加するかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_チルト補正_高音域でブースト補正が適用されること()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        var fftMagnitudes = new double[512];
        Array.Fill(fftMagnitudes, 0.05);

        // Act
        var barsNormalTilt = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 64, trebleTiltDb: 0.0);
        var barsBoostTilt = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 64, trebleTiltDb: 12.0);

        // Assert
        // 高音域（末尾付近のバー）はチルト補正が大きい方が高くなる
        Assert.True(barsBoostTilt[60] > barsNormalTilt[60]);
    }

    /// <summary>
    /// 空のFFT振幅配列が渡された場合、指定されたbarCountの0初期化配列が安全に返されるかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_空配列入力_空配列または0初期化配列を返すこと()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        ReadOnlySpan<double> empty = ReadOnlySpan<double>.Empty;

        // Act
        var bars = sut.CalculateBars(empty, sampleRate: 44100, barCount: 32);

        // Assert
        Assert.Equal(32, bars.Length);
        Assert.All(bars, val => Assert.Equal(0.0, val));
    }

    /// <summary>
    /// サンプリングレートに0以下の無効値が指定された場合、0要素配列が返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-44100)]
    public void CalculateBars_サンプリングレート不正_0要素配列を返すこと(int invalidSampleRate)
    {
        // Arrange
        var sut = new SpectrumCalculator();
        var fftMagnitudes = new double[512];

        // Act
        var bars = sut.CalculateBars(fftMagnitudes, invalidSampleRate, barCount: 64);

        // Assert
        Assert.Equal(64, bars.Length);
        Assert.All(bars, val => Assert.Equal(0.0, val));
    }

    /// <summary>
    /// 入力にNaNやInfinityが含まれている場合でも、例外が発生せず0.0に丸められるかを検証します。
    /// </summary>
    [Fact]
    public void CalculateBars_エッジケース値_NaNやInfinityが0に丸められること()
    {
        // Arrange
        var sut = new SpectrumCalculator();
        var fftMagnitudes = new double[512];
        fftMagnitudes[10] = double.NaN;
        fftMagnitudes[20] = double.PositiveInfinity;

        // Act
        var bars = sut.CalculateBars(fftMagnitudes, sampleRate: 44100, barCount: 64);

        // Assert
        Assert.Equal(64, bars.Length);
        Assert.All(bars, val =>
        {
            Assert.False(double.IsNaN(val));
            Assert.False(double.IsInfinity(val));
            Assert.True(val >= 0.0);
        });
    }
}
