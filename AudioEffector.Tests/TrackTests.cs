using AudioEffector.Models;
using Xunit;

namespace AudioEffector.Tests
{
    public class TrackTests
    {
        [Theory]
        [InlineData(44100, 16, false)]
        [InlineData(48000, 16, false)]
        [InlineData(96000, 16, true)]
        [InlineData(44100, 24, true)]
        [InlineData(192000, 24, true)]
        public void QualityLabel_IsHiRes_ReturnsCorrectLabel(int sampleRate, int bitsPerSample, bool expectedIsHiRes)
        {
            // Arrange
            var track = new Track
            {
                SampleRate = sampleRate,
                BitsPerSample = bitsPerSample
            };

            // Act
            track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;
            var label = track.QualityLabel;

            // Assert
            Assert.Equal(expectedIsHiRes, track.IsHiRes);
            if (expectedIsHiRes)
            {
                Assert.Equal("Hi-Res", label);
            }
        }

        [Theory]
        [InlineData(".flac", true)]
        [InlineData(".wav", true)]
        [InlineData(".aiff", true)]
        [InlineData(".alac", true)]
        [InlineData(".mp3", false)]
        [InlineData(".m4a", false)]
        [InlineData(".aac", false)]
        public void QualityLabel_IsLossless_ReturnsCorrectLabel(string extension, bool expectedIsLossless)
        {
            // Arrange
            var track = new Track
            {
                SampleRate = 44100,
                BitsPerSample = 16,
                Format = extension.TrimStart('.').ToUpper()
            };

            // Act
            track.IsLossless = new[] { ".flac", ".wav", ".aiff", ".alac" }.Contains(extension);
            var label = track.QualityLabel;

            // Assert
            Assert.Equal(expectedIsLossless, track.IsLossless);
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
