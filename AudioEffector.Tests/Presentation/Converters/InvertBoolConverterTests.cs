using System.Globalization;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="InvertBoolConverter"/> の単体テストクラス。
/// </summary>
public class InvertBoolConverterTests
{
    /// <summary>
    /// trueが渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_Trueの場合_Falseを返す()
    {
        // Arrange
        var sut = new InvertBoolConverter();

        // Act
        var result = sut.Convert(true, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    /// <summary>
    /// falseが渡された場合、trueを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_Falseの場合_Trueを返す()
    {
        // Arrange
        var sut = new InvertBoolConverter();

        // Act
        var result = sut.Convert(false, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    /// <summary>
    /// 非bool値が渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非bool値の場合_Falseを返す()
    {
        // Arrange
        var sut = new InvertBoolConverter();

        // Act
        var result = sut.Convert("not-bool", typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    /// <summary>
    /// ConvertBackでtrueが渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_Trueの場合_Falseを返す()
    {
        // Arrange
        var sut = new InvertBoolConverter();

        // Act
        var result = sut.ConvertBack(true, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }

    /// <summary>
    /// ConvertBackでfalseが渡された場合、trueを返すことを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_Falseの場合_Trueを返す()
    {
        // Arrange
        var sut = new InvertBoolConverter();

        // Act
        var result = sut.ConvertBack(false, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(true, result);
    }

    /// <summary>
    /// ConvertBackで非bool値が渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_非bool値の場合_Falseを返す()
    {
        // Arrange
        var sut = new InvertBoolConverter();

        // Act
        var result = sut.ConvertBack(12345, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(false, result);
    }
}
