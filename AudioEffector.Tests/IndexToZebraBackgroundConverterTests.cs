using System.Globalization;
using System.Windows.Media;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests
{
    public class IndexToZebraBackgroundConverterTests
    {
        private readonly IndexToZebraBackgroundConverter _converter = new IndexToZebraBackgroundConverter();

        [Theory]
        [InlineData(0, true)]  // 偶数行: 透明
        [InlineData(2, true)]
        [InlineData(4, true)]
        [InlineData(1, false)] // 奇数行: 薄い白
        [InlineData(3, false)]
        [InlineData(5, false)]
        public void Convert_インデックスに応じたブラシを返す(int index, bool isTransparent)
        {
            var result = _converter.Convert(index, typeof(Brush), null!, CultureInfo.InvariantCulture);

            Assert.NotNull(result);
            Assert.IsAssignableFrom<SolidColorBrush>(result);

            var brush = (SolidColorBrush)result;
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

        [Fact]
        public void Convert_無効な値_Transparentを返す()
        {
            var result = _converter.Convert("invalid", typeof(Brush), null!, CultureInfo.InvariantCulture);
            var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
            Assert.Equal(Colors.Transparent, brush.Color);
        }
    }
}
