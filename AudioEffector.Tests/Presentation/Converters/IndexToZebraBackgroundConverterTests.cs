using System.Globalization;
using System.Windows.Media;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="IndexToZebraBackgroundConverter"/> の単体テストクラス。
/// </summary>
public class IndexToZebraBackgroundConverterTests
{
    /// <summary>
    /// インデックスの偶数・奇数に応じた適切な背景ブラシを返すことを検証します。
    /// </summary>
    /// <param name="index">行インデックス</param>
    /// <param name="isTransparent">透明であるかどうかの期待値</param>
    [Theory]
    [InlineData(0, true)]  // 偶数行: 透明
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(1, false)] // 奇数行: 薄い白
    [InlineData(3, false)]
    [InlineData(5, false)]
    public void Convert_偶数奇数インデックス_適切な背景ブラシを返す(int index, bool isTransparent)
    {
        // Arrange
        var sut = new IndexToZebraBackgroundConverter();

        // Act
        var result = sut.Convert(index, typeof(Brush), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.NotNull(result);
        var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
        if (isTransparent)
        {
            Assert.Equal(Colors.Transparent, brush.Color);
        }
        else
        {
            Assert.NotEqual(Colors.Transparent, brush.Color);
            Assert.Equal((byte)16, brush.Color.A);
            Assert.Equal((byte)255, brush.Color.R);
            Assert.Equal((byte)255, brush.Color.G);
            Assert.Equal((byte)255, brush.Color.B);
        }
    }

    /// <summary>
    /// 無効な値（非整数）が渡された場合、透明ブラシを返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_無効な値_Transparentブラシを返す()
    {
        // Arrange
        var sut = new IndexToZebraBackgroundConverter();

        // Act
        var result = sut.Convert("invalid", typeof(Brush), null!, CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, brush.Color);
    }
}
