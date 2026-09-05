using System.Globalization;
using System.Windows.Data;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="EnumToBoolConverter"/> の単体テストクラス。
/// </summary>
public class EnumToBoolConverterTests
{
    /// <summary>
    /// 一致するEnum値が渡された場合、trueを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_一致するEnum値_Trueを返す()
    {
        // Arrange
        var sut = new EnumToBoolConverter();

        // Act
        var result = sut.Convert(ViewType.Albums, typeof(bool), ViewType.Albums, CultureInfo.InvariantCulture);

        // Assert
        Assert.True((bool)result);
    }

    /// <summary>
    /// 一致しないEnum値が渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_一致しないEnum値_Falseを返す()
    {
        // Arrange
        var sut = new EnumToBoolConverter();

        // Act
        var result = sut.Convert(ViewType.AllSongs, typeof(bool), ViewType.Albums, CultureInfo.InvariantCulture);

        // Assert
        Assert.False((bool)result);
    }

    /// <summary>
    /// null値が渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_Null値_Falseを返す()
    {
        // Arrange
        var sut = new EnumToBoolConverter();

        // Act
        var result1 = sut.Convert(null!, typeof(bool), ViewType.Albums, CultureInfo.InvariantCulture);
        var result2 = sut.Convert(ViewType.Albums, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.False((bool)result1);
        Assert.False((bool)result2);
    }

    /// <summary>
    /// trueが渡された場合、パラメータのEnum値を返すことを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_Trueの場合_パラメータのEnum値を返す()
    {
        // Arrange
        var sut = new EnumToBoolConverter();

        // Act
        var result = sut.ConvertBack(true, typeof(ViewType), ViewType.DeviceSync, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(ViewType.DeviceSync, result);
    }

    /// <summary>
    /// falseが渡された場合、Binding.DoNothingを返すことを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_Falseの場合_DoNothingを返す()
    {
        // Arrange
        var sut = new EnumToBoolConverter();

        // Act
        var result = sut.ConvertBack(false, typeof(ViewType), ViewType.DeviceSync, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(Binding.DoNothing, result);
    }
}
