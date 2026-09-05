using System;
using System.Globalization;
using System.Windows;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="BoolToVisConverter"/> の単体テストクラス。
/// </summary>
public class BoolToVisConverterTests
{
    /// <summary>
    /// trueでパラメータなしの場合、Visibleを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_Trueでパラメータなし_Visibleを返す()
    {
        // Arrange
        var sut = new BoolToVisConverter();

        // Act
        var result = sut.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    /// <summary>
    /// falseでパラメータなしの場合、Collapsedを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_Falseでパラメータなし_Collapsedを返す()
    {
        // Arrange
        var sut = new BoolToVisConverter();

        // Act
        var result = sut.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    /// <summary>
    /// trueでパラメータ"Invert"の場合、Collapsedを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_TrueでパラメータInvert_Collapsedを返す()
    {
        // Arrange
        var sut = new BoolToVisConverter();

        // Act
        var result = sut.Convert(true, typeof(Visibility), "Invert", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    /// <summary>
    /// falseでパラメータ"Invert"の場合、Visibleを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_FalseでパラメータInvert_Visibleを返す()
    {
        // Arrange
        var sut = new BoolToVisConverter();

        // Act
        var result = sut.Convert(false, typeof(Visibility), "Invert", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    /// <summary>
    /// 非bool値が渡された場合、Collapsedを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非bool値_Collapsedを返す()
    {
        // Arrange
        var sut = new BoolToVisConverter();

        // Act
        var result = sut.Convert("not-a-bool", typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    /// <summary>
    /// ConvertBack呼び出し時、NotImplementedExceptionをスローすることを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_呼び出し時_NotImplementedExceptionをスローする()
    {
        // Arrange
        var sut = new BoolToVisConverter();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            sut.ConvertBack(Visibility.Visible, typeof(bool), null!, CultureInfo.InvariantCulture));
    }
}
