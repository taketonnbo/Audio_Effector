using System;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

public class FrequencyBandTests
{
    /// <summary>
    /// 有効な中心周波数、ゲイン、帯域幅を指定した場合に正しくプロパティが初期化されるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_有効な中心周波数とゲイン_正しく初期化されること()
    {
        // Arrange
        var gain = Gain.FromDecibels(3.0f);

        // Act
        var sut = new FrequencyBand(1000.0f, gain, 1.4f);

        // Assert
        Assert.Equal(1000.0f, sut.CenterFrequency);
        Assert.Equal(gain, sut.Gain);
        Assert.Equal(1.4f, sut.Bandwidth);
    }

    /// <summary>
    /// 中心周波数に0以下の値を指定した場合、ArgumentOutOfRangeExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(-100.0f)]
    public void コンストラクタ_0以下の中心周波数_ArgumentOutOfRangeExceptionをスローすること(float invalidFrequency)
    {
        // Arrange
        var gain = Gain.Zero;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrequencyBand(invalidFrequency, gain));
    }

    /// <summary>
    /// 帯域幅（Q値）に0以下の値を指定した場合、デフォルトの1.0fにフォールバックされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f)]
    public void コンストラクタ_0以下の帯域幅_デフォルト1_0にフォールバックすること(float invalidBandwidth)
    {
        // Arrange
        var gain = Gain.Zero;

        // Act
        var sut = new FrequencyBand(500.0f, gain, invalidBandwidth);

        // Assert
        Assert.Equal(1.0f, sut.Bandwidth);
    }

    /// <summary>
    /// WithGainを呼び出した場合、中心周波数と帯域幅を維持したままゲインのみが更新された新しいインスタンスが返されるかを検証します。
    /// </summary>
    [Fact]
    public void WithGain_新しいゲインの指定_周波数を維持しゲインが更新されたインスタンスを返すこと()
    {
        // Arrange
        var initialGain = Gain.FromDecibels(2.0f);
        var sut = new FrequencyBand(1000.0f, initialGain, 1.2f);
        var newGain = Gain.FromDecibels(-4.0f);

        // Act
        var updated = sut.WithGain(newGain);

        // Assert
        Assert.Equal(1000.0f, updated.CenterFrequency);
        Assert.Equal(newGain, updated.Gain);
        Assert.Equal(1.2f, updated.Bandwidth);
    }

    /// <summary>
    /// 中心周波数が1000Hz以上の場合、"X.X kHz" 形式でラベルが生成されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(1000.0f, "1.0 kHz")]
    [InlineData(2000.0f, "2.0 kHz")]
    [InlineData(16000.0f, "16.0 kHz")]
    public void GetFrequencyLabel_1kHz以上_kHz単位でフォーマットされること(float freq, string expectedLabel)
    {
        // Arrange
        var sut = new FrequencyBand(freq, Gain.Zero);

        // Act
        var actualLabel = sut.GetFrequencyLabel();

        // Assert
        Assert.Equal(expectedLabel, actualLabel);
    }

    /// <summary>
    /// 中心周波数が1000Hz未満の場合、"X Hz" 形式でラベルが生成されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(31.25f, "31 Hz")]
    [InlineData(60.0f, "60 Hz")]
    [InlineData(500.0f, "500 Hz")]
    public void GetFrequencyLabel_1kHz未満_Hz単位でフォーマットされること(float freq, string expectedLabel)
    {
        // Arrange
        var sut = new FrequencyBand(freq, Gain.Zero);

        // Act
        var actualLabel = sut.GetFrequencyLabel();

        // Assert
        Assert.Equal(expectedLabel, actualLabel);
    }
}
