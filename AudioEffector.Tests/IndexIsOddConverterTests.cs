using AudioEffector.Presentation.Converters;
using System.Globalization;
using Xunit;

namespace AudioEffector.Tests
{
    public class IndexIsOddConverterTests
    {
        private readonly IndexIsOddConverter _converter = new IndexIsOddConverter();

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(10, false)]
        [InlineData(99, true)]
        public void Convert_ReturnsCorrectOddState(int index, bool expected)
        {
            var result = _converter.Convert(index, typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Convert_NonInt_ReturnsFalse()
        {
            var result = _converter.Convert("not-an-int", typeof(bool), null, CultureInfo.InvariantCulture);
            Assert.False((bool)result);
        }
    }
}
