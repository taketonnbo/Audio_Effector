using System;
using System.Globalization;
using System.Windows;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="NullToVisConverter"/> の単体テストクラス。
/// </summary>
public class NullToVisConverterTests
{
    /// <summary>
    /// null値が渡された場合、Collapsedを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_Nullの場合_Collapsedを返す()
    {
        // Arrange
        var sut = new NullToVisConverter();

        // Act
        var result = sut.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    /// <summary>
    /// 空文字列が渡された場合、Collapsedを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_空文字列の場合_Collapsedを返す()
    {
        // Arrange
        var sut = new NullToVisConverter();

        // Act
        var result = sut.Convert(string.Empty, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    /// <summary>
    /// 非null非空のオブジェクトが渡された場合、Visibleを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非Null非空オブジェクトの場合_Visibleを返す()
    {
        // Arrange
        var sut = new NullToVisConverter();

        // Act
        var resultString = sut.Convert("Valid text", typeof(Visibility), null!, CultureInfo.InvariantCulture);
        var resultObject = sut.Convert(12345, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Visibility.Visible, resultString);
        Assert.Equal(Visibility.Visible, resultObject);
    }

    /// <summary>
    /// ConvertBack呼び出し時、NotImplementedExceptionをスローすることを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_呼び出し時_NotImplementedExceptionをスローする()
    {
        // Arrange
        var sut = new NullToVisConverter();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            sut.ConvertBack(Visibility.Visible, typeof(object), null!, CultureInfo.InvariantCulture));
    }
}
