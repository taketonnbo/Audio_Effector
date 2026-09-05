using System;
using System.Globalization;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="IndexConverter"/> の単体テストクラス。
/// </summary>
public class IndexConverterTests
{
    /// <summary>
    /// 0始まりのインデックスに対して、1加算された表示用番号を返すことを検証します。
    /// </summary>
    /// <param name="index">入力インデックス</param>
    /// <param name="expected">期待される加算後の値</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(5, 6)]
    [InlineData(99, 100)]
    public void Convert_0始まりインデックス_1加算された値を返す(int index, int expected)
    {
        // Arrange
        var sut = new IndexConverter();

        // Act
        var result = sut.Convert(index, typeof(int), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// int型以外の値が渡された場合、元の値をそのまま返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非int値_元の値をそのまま返す()
    {
        // Arrange
        var sut = new IndexConverter();
        const string input = "not-an-int";

        // Act
        var result = sut.Convert(input, typeof(string), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Same(input, result);
    }

    /// <summary>
    /// ConvertBack呼び出し時、NotImplementedExceptionをスローすることを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_呼び出し時_NotImplementedExceptionをスローする()
    {
        // Arrange
        var sut = new IndexConverter();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            sut.ConvertBack(1, typeof(int), null!, CultureInfo.InvariantCulture));
    }
}
