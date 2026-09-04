using System.Globalization;
using System.Windows.Data;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests
{
    public class EnumToBoolConverterTests
    {
        private readonly EnumToBoolConverter _converter = new EnumToBoolConverter();

        [Fact]
        public void Convert_一致するEnum値_Trueを返す()
        {
            var result = _converter.Convert(ViewType.Albums, typeof(bool), ViewType.Albums, CultureInfo.InvariantCulture);
            Assert.True((bool)result);
        }

        [Fact]
        public void Convert_一致しないEnum値_Falseを返す()
        {
            var result = _converter.Convert(ViewType.AllSongs, typeof(bool), ViewType.Albums, CultureInfo.InvariantCulture);
            Assert.False((bool)result);
        }

        [Fact]
        public void Convert_Null値_Falseを返す()
        {
            var result1 = _converter.Convert(null!, typeof(bool), ViewType.Albums, CultureInfo.InvariantCulture);
            var result2 = _converter.Convert(ViewType.Albums, typeof(bool), null!, CultureInfo.InvariantCulture);
            Assert.False((bool)result1);
            Assert.False((bool)result2);
        }

        [Fact]
        public void ConvertBack_Trueの場合_パラメータのEnum値を返す()
        {
            var result = _converter.ConvertBack(true, typeof(ViewType), ViewType.DeviceSync, CultureInfo.InvariantCulture);
            Assert.Equal(ViewType.DeviceSync, result);
        }

        [Fact]
        public void ConvertBack_Falseの場合_DoNothingを返す()
        {
            var result = _converter.ConvertBack(false, typeof(ViewType), ViewType.DeviceSync, CultureInfo.InvariantCulture);
            Assert.Equal(Binding.DoNothing, result);
        }
    }
}
