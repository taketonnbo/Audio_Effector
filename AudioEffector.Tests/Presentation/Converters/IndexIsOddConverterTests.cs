using System.Globalization;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="IndexIsOddConverter"/> の単体テストクラス。
/// </summary>
public class IndexIsOddConverterTests
{
    /// <summary>
    /// 整数インデックスに対して奇数の場合にtrue、偶数の場合にfalseを返すことを検証します。
    /// </summary>
    /// <param name="index">入力インデックス</param>
    /// <param name="expected">期待される真偽値</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(10, false)]
    [InlineData(99, true)]
    public void Convert_整数インデックス_奇数ならTrue偶数ならFalseを返す(int index, bool expected)
    {
        // Arrange
        var sut = new IndexIsOddConverter();

        // Act
        var result = sut.Convert(index, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// int型以外の値が渡された場合、falseを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_非int値_Falseを返す()
    {
        // Arrange
        var sut = new IndexIsOddConverter();

        // Act
        var result = sut.Convert("not-an-int", typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.False((bool)result!);
    }
}
