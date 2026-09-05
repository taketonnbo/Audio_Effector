using System;
using System.Globalization;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="ZeroToTransparentConverter"/> の単体テストクラス。
/// </summary>
public class ZeroToTransparentConverterTests
{
    /// <summary>
    /// 0.01以下の数値が渡された場合、透明度0.0（Transparent）を返すことを検証します。
    /// </summary>
    /// <param name="value">入力数値</param>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.005)]
    [InlineData(0.01)]
    [InlineData(-1.0)]
    public void Convert_0点01以下の値_透明度0を返す(double value)
    {
        // Arrange
        var sut = new ZeroToTransparentConverter();

        // Act
        var result = sut.Convert(value, typeof(double), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(0.0, result);
    }

    /// <summary>
    /// 0.01を超える数値が渡された場合、不透明度1.0（Opaque）を返すことを検証します。
    /// </summary>
    /// <param name="value">入力数値</param>
    [Theory]
    [InlineData(0.011)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(100.0)]
    public void Convert_0点01超の値_不透明度1を返す(double value)
    {
        // Arrange
        var sut = new ZeroToTransparentConverter();

        // Act
        var result = sut.Convert(value, typeof(double), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(1.0, result);
    }

    /// <summary>
    /// double型以外の値が渡された場合、不透明度1.0を返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非double値_不透明度1を返す()
    {
        // Arrange
        var sut = new ZeroToTransparentConverter();

        // Act
        var result = sut.Convert("not-a-double", typeof(double), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(1.0, result);
    }

    /// <summary>
    /// ConvertBack呼び出し時、NotImplementedExceptionをスローすることを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_呼び出し時_NotImplementedExceptionをスローする()
    {
        // Arrange
        var sut = new ZeroToTransparentConverter();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            sut.ConvertBack(1.0, typeof(double), null!, CultureInfo.InvariantCulture));
    }
}
