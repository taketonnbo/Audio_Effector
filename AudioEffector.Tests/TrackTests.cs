using AudioEffector.Models;
using Xunit;

namespace AudioEffector.Tests
{
    public class TrackTests
    {
        /// <summary>
        /// サンプリングレートとビット深度の組み合わせにより、ハイレゾ音源として正しく判定されるかを検証します。
        /// </summary>
        [Theory]
        [InlineData(44100, 16, false)]
        [InlineData(48000, 16, false)]
        [InlineData(96000, 16, true)]
        [InlineData(44100, 24, true)]
        [InlineData(192000, 24, true)]
        public void IsHiRes判定_サンプリングレートとビット深度の組み合わせ_期待されるHiRes判定結果を返す(int sampleRate, int bitsPerSample, bool expectedIsHiRes)
        {
            // Arrange
            var sut = new Track
            {
                SampleRate = sampleRate,
                BitsPerSample = bitsPerSample
            };

            // Act
            sut.IsHiRes = sut.SampleRate > 48000 || sut.BitsPerSample > 16;
            var label = sut.QualityLabel;

            // Assert
            Assert.Equal(expectedIsHiRes, sut.IsHiRes);
            if (expectedIsHiRes)
            {
                Assert.Equal("Hi-Res", label);
            }
        }

        /// <summary>
        /// 拡張子の指定により、ロスレス音源として正しく判定され、適切な品質ラベルが設定されるかを検証します。
        /// </summary>
        [Theory]
        [InlineData(".flac", true)]
        [InlineData(".wav", true)]
        [InlineData(".aiff", true)]
        [InlineData(".alac", true)]
        [InlineData(".mp3", false)]
        [InlineData(".m4a", false)]
        [InlineData(".aac", false)]
        public void IsLossless判定_各種拡張子の指定_期待されるLossless判定結果とラベルを返す(string extension, bool expectedIsLossless)
        {
            // Arrange
            var sut = new Track
            {
                SampleRate = 44100,
                BitsPerSample = 16,
                Format = extension.TrimStart('.').ToUpper()
            };

            // Act
            sut.IsLossless = new[] { ".flac", ".wav", ".aiff", ".alac" }.Contains(extension);
            var label = sut.QualityLabel;

            // Assert
            Assert.Equal(expectedIsLossless, sut.IsLossless);
            if (expectedIsLossless)
            {
                Assert.Equal("Lossless", label);
            }
            else
            {
                Assert.Equal("", label);
            }
        }
    }
}
