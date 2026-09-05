using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

public class GainTests
{
    /// <summary>
    /// 許容範囲（-12.0dB〜+12.0dB）内のデシベル値を指定した場合、そのままの値が保持されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(-12.0f)]
    [InlineData(-6.5f)]
    [InlineData(0.0f)]
    [InlineData(3.0f)]
    [InlineData(12.0f)]
    public void FromDecibels_許容範囲内のdB値_指定値が設定されること(float db)
    {
        // Arrange & Act
        var sut = Gain.FromDecibels(db);

        // Assert
        Assert.Equal(db, sut.Value);
    }

    /// <summary>
    /// 最小許容ゲイン値（-12.0dB）未満の値を指定した場合、MIN_GAIN_DBにクランプされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(-12.1f)]
    [InlineData(-20.0f)]
    [InlineData(-100.0f)]
    public void FromDecibels_最小値未満の値_MIN_GAIN_DBにクランプされること(float db)
    {
        // Arrange & Act
        var sut = Gain.FromDecibels(db);

        // Assert
        Assert.Equal(Gain.MIN_GAIN_DB, sut.Value);
    }

    /// <summary>
    /// 最大許容ゲイン値（+12.0dB）を超える値を指定した場合、MAX_GAIN_DBにクランプされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(12.1f)]
    [InlineData(24.0f)]
    [InlineData(100.0f)]
    public void FromDecibels_最大値超の値_MAX_GAIN_DBにクランプされること(float db)
    {
        // Arrange & Act
        var sut = Gain.FromDecibels(db);

        // Assert
        Assert.Equal(Gain.MAX_GAIN_DB, sut.Value);
    }

    /// <summary>
    /// Gain.Zeroプロパティから取得したインスタンスが0.0dBを持つかを検証します。
    /// </summary>
    [Fact]
    public void Zero_プロパティ取得_0dBのGainインスタンスを返すこと()
    {
        // Arrange & Act
        var sut = Gain.Zero;

        // Assert
        Assert.Equal(0.0f, sut.Value);
    }

    /// <summary>
    /// デシベル値から線形増幅率（リニア倍率: 10^(dB/20)）が正しく計算されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0.0f, 1.0f)]
    [InlineData(6.0206f, 2.0f)]
    [InlineData(-6.0206f, 0.5f)]
    public void ToLinearGain_各デシベル値_期待されるリニア倍率を返すこと(float db, float expectedLinear)
    {
        // Arrange
        var sut = Gain.FromDecibels(db);

        // Act
        var actualLinear = sut.ToLinearGain();

        // Assert
        Assert.Equal(expectedLinear, actualLinear, precision: 2);
    }

    /// <summary>
    /// デシベル値に応じた符号付きフォーマット文字列（例: "+3.0 dB", "-2.5 dB", "+0.0 dB"）が生成されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(3.0f, "+3.0 dB")]
    [InlineData(-2.5f, "-2.5 dB")]
    [InlineData(0.0f, "+0.0 dB")]
    public void ToString_正負および0のゲイン値_符号付きdB文字列を返すこと(float db, string expectedString)
    {
        // Arrange
        var sut = Gain.FromDecibels(db);

        // Act
        var actualString = sut.ToString();

        // Assert
        Assert.Equal(expectedString, actualString);
    }

    /// <summary>
    /// 同一のゲイン値を持つGainインスタンス同士が等価と判定されるかを検証します。
    /// </summary>
    [Fact]
    public void Equals_同一デシベル値のGain_等価と判定されること()
    {
        // Arrange
        var sut1 = Gain.FromDecibels(4.5f);
        var sut2 = Gain.FromDecibels(4.5f);
        var sut3 = Gain.FromDecibels(4.6f);

        // Act & Assert
        Assert.True(sut1.Equals(sut2));
        Assert.True(sut1 == sut2);
        Assert.False(sut1 == sut3);
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
    }
}
