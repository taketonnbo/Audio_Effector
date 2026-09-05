using System;
using System.Globalization;
using System.Windows;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="HeightToBorderThicknessConverter"/> の単体テストクラス。
/// </summary>
public class HeightToBorderThicknessConverterTests
{
    /// <summary>
    /// 高さ600の場合、太さ3.0のThicknessを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_高さ600_太さ3のThicknessを返す()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act
        var result = sut.Convert(600.0, typeof(Thickness), null!, CultureInfo.InvariantCulture);

        // Assert
        var thickness = Assert.IsAssignableFrom<Thickness>(result);
        Assert.Equal(3.0, thickness.Left);
        Assert.Equal(3.0, thickness.Top);
        Assert.Equal(3.0, thickness.Right);
        Assert.Equal(3.0, thickness.Bottom);
    }

    /// <summary>
    /// 高さ300の場合、太さ1.5のThicknessを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_高さ300_太さ1点5のThicknessを返す()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act
        var result = sut.Convert(300.0, typeof(Thickness), null!, CultureInfo.InvariantCulture);

        // Assert
        var thickness = Assert.IsAssignableFrom<Thickness>(result);
        Assert.Equal(1.5, thickness.Left);
    }

    /// <summary>
    /// 高さ0の場合、太さ0のThicknessを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_高さ0_太さ0のThicknessを返す()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act
        var result = sut.Convert(0.0, typeof(Thickness), null!, CultureInfo.InvariantCulture);

        // Assert
        var thickness = Assert.IsAssignableFrom<Thickness>(result);
        Assert.Equal(0.0, thickness.Left);
    }

    /// <summary>
    /// 負の高さが渡された場合、太さ0にクランプされることを検証します。
    /// </summary>
    [Fact]
    public void Convert_負の高さ_太さ0にクランプされる()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act
        var result = sut.Convert(-50.0, typeof(Thickness), null!, CultureInfo.InvariantCulture);

        // Assert
        var thickness = Assert.IsAssignableFrom<Thickness>(result);
        Assert.Equal(0.0, thickness.Left);
    }

    /// <summary>
    /// 600を超える高さが渡された場合、太さ3.0にクランプされることを検証します。
    /// </summary>
    [Fact]
    public void Convert_600超の高さ_太さ3にクランプされる()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act
        var result = sut.Convert(1200.0, typeof(Thickness), null!, CultureInfo.InvariantCulture);

        // Assert
        var thickness = Assert.IsAssignableFrom<Thickness>(result);
        Assert.Equal(3.0, thickness.Left);
    }

    /// <summary>
    /// double型以外の値が渡された場合、太さ0のThicknessを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非double値_太さ0のThicknessを返す()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act
        var result = sut.Convert("invalid", typeof(Thickness), null!, CultureInfo.InvariantCulture);

        // Assert
        var thickness = Assert.IsAssignableFrom<Thickness>(result);
        Assert.Equal(0.0, thickness.Left);
    }

    /// <summary>
    /// ConvertBack呼び出し時、NotImplementedExceptionをスローすることを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_呼び出し時_NotImplementedExceptionをスローする()
    {
        // Arrange
        var sut = new HeightToBorderThicknessConverter();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            sut.ConvertBack(new Thickness(1.0), typeof(double), null!, CultureInfo.InvariantCulture));
    }
}
