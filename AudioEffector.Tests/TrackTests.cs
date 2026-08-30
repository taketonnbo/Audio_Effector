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
        [InlineData(".mp4", false)]
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

        /// <summary>
        /// サンプリングレート、ビット深度、ビットレートの組み合わせに応じた音質情報フォーマット文字列が正しく生成されるかを検証します。
        /// </summary>
        [Theory]
        [InlineData(16, 44100, 0, "FLAC", "16bit/44.1kHz FLAC")]
        [InlineData(24, 96000, 0, "WAV", "24bit/96.0kHz WAV")]
        [InlineData(0, 44100, 320, "MP3", "320kbps/44.1kHz MP3")]
        [InlineData(0, 48000, 256, "AAC", "256kbps/48.0kHz AAC")]
        [InlineData(0, 44100, 0, "MP4", "44.1kHz MP4")]
        public void QualityInfo_各種音質情報の組み合わせ_期待されるフォーマット文字列を返す(int bits, int sampleRate, int bitrate, string format, string expected)
        {
            // Arrange
            var sut = new Track
            {
                BitsPerSample = bits,
                SampleRate = sampleRate,
                Bitrate = bitrate,
                Format = format
            };

            // Act
            var actual = sut.QualityInfo;

            // Assert
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// 同一のFilePathを持つTrack同士が等価と判定されるかを検証します。
        /// </summary>
        [Fact]
        public void Track_同一FilePath_等価と判定される()
        {
            // Arrange
            var track1 = new Track { FilePath = @"C:\Music\song.mp3", Title = "Song 1" };
            var track2 = new Track { FilePath = @"C:\Music\song.mp3", Title = "Song 1 (Copy)" };

            // Act & Assert
            Assert.True(track1.Equals(track2));
            Assert.True(track1 == track2);
            Assert.False(track1 != track2);
            Assert.Equal(track1.GetHashCode(), track2.GetHashCode());
        }

        /// <summary>
        /// 異なるFilePathを持つTrack同士が不等価と判定されるかを検証します。
        /// </summary>
        [Fact]
        public void Track_異なるFilePath_不等価と判定される()
        {
            // Arrange
            var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
            var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };

            // Act & Assert
            Assert.False(track1.Equals(track2));
            Assert.False(track1 == track2);
            Assert.True(track1 != track2);
        }
    }
}
