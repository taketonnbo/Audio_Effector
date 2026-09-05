using System;
using System.IO;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Library;

/// <summary>
/// TagLibMetadataExtractorのメタデータ抽出およびカバーアート抽出を検証するテストクラス
/// </summary>
public sealed class TagLibMetadataExtractorTests : IDisposable
{
    private readonly string _tempDirectory;

    public TagLibMetadataExtractorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_TagLib", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // クリーンアップエラーは無視
        }
    }

    private string CreateDummyWavFile(string fileName, int sampleRate = 44100, short bitsPerSample = 16, short channels = 2)
    {
        string filePath = Path.Combine(_tempDirectory, fileName);
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("RIFF"u8.ToArray());
            bw.Write(36); // chunk size
            bw.Write("WAVE"u8.ToArray());
            bw.Write("fmt "u8.ToArray());
            bw.Write(16); // PCM subchunk size
            bw.Write((short)1); // AudioFormat PCM
            bw.Write(channels);
            bw.Write(sampleRate);
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            bw.Write(byteRate);
            short blockAlign = (short)(channels * (bitsPerSample / 8));
            bw.Write(blockAlign);
            bw.Write(bitsPerSample);
            bw.Write("data"u8.ToArray());
            bw.Write(0); // 0 bytes data
        }
        return filePath;
    }

    /// <summary>
    /// 存在しないファイルパスを指定した場合、nullを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractMetadataAsync_存在しないファイルパス指定_nullを返すこと()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        var nonExistentPath = AudioPath.Create(Path.Combine(_tempDirectory, "not_exist.mp3"));

        // Act
        var actual = await sut.ExtractMetadataAsync(nonExistentPath);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// 破損ファイルまたはタグなしのダミーファイルを指定した場合、例外をスローせずファイル名ベースの最小限Trackを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractMetadataAsync_破損ファイル指定_ファイル名ベースの最小限Trackを返すこと()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        string corruptedFilePath = Path.Combine(_tempDirectory, "corrupted.mp3");
        await File.WriteAllBytesAsync(corruptedFilePath, [0x00, 0xFF, 0xAA, 0x55]); // 不正なバイナリ

        // Act
        var actual = await sut.ExtractMetadataAsync(AudioPath.Create(corruptedFilePath));

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("corrupted.mp3", actual.Title);
        Assert.Equal("Unknown Artist", actual.Artist);
        Assert.Equal("Unknown Album", actual.Album);
        Assert.Equal(TimeSpan.Zero, actual.Duration);
    }

    /// <summary>
    /// 存在しないファイルパスを指定した場合、ExtractAlbumArtBytesAsyncがnullを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractAlbumArtBytesAsync_存在しないファイルパス指定_nullを返すこと()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        var nonExistentPath = AudioPath.Create(Path.Combine(_tempDirectory, "no_art.mp3"));

        // Act
        var actual = await sut.ExtractAlbumArtBytesAsync(nonExistentPath);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// カバーアートを含まない音声ファイルを指定した場合、nullを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractAlbumArtBytesAsync_画像を含まないファイル指定_nullを返すこと()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        string wavPath = CreateDummyWavFile("simple.wav");

        // Act
        var actual = await sut.ExtractAlbumArtBytesAsync(AudioPath.Create(wavPath));

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// 有効なWAVファイルからサンプリングレートやビット深度などのフォーマットメタデータが正しく抽出されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractMetadataAsync_有効なWAV音声ファイル_各種メタデータが正しく抽出されること()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        string wavPath = CreateDummyWavFile("track_44k.wav", sampleRate: 44100, bitsPerSample: 16);

        // Act
        var actual = await sut.ExtractMetadataAsync(AudioPath.Create(wavPath));

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(44100, actual.SampleRate);
        Assert.Equal(16, actual.BitsPerSample);
        Assert.Equal("WAV", actual.Format);
        Assert.True(actual.IsLossless);
        Assert.False(actual.IsHiRes);
    }

    /// <summary>
    /// 96kHzまたは24bitのハイレゾ音源を指定した際、IsHiResがtrueと判定されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(96000, 16, true)]
    [InlineData(44100, 24, true)]
    [InlineData(192000, 24, true)]
    [InlineData(44100, 16, false)]
    public async Task ExtractMetadataAsync_ハイレゾ音源判定_期待されるIsHiRes判定結果を返すこと(int sampleRate, short bitsPerSample, bool expectedHiRes)
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        string wavPath = CreateDummyWavFile($"hires_{sampleRate}_{bitsPerSample}.wav", sampleRate: sampleRate, bitsPerSample: bitsPerSample);

        // Act
        var actual = await sut.ExtractMetadataAsync(AudioPath.Create(wavPath));

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(expectedHiRes, actual.IsHiRes);
    }

    /// <summary>
    /// 可逆圧縮フォーマット（WAV）の場合、IsLosslessがtrueと判定されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractMetadataAsync_可逆圧縮音源_IsLosslessがtrueと判定されること()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        string wavPath = CreateDummyWavFile("lossless.wav");

        // Act
        var actual = await sut.ExtractMetadataAsync(AudioPath.Create(wavPath));

        // Assert
        Assert.NotNull(actual);
        Assert.True(actual.IsLossless);
    }

    /// <summary>
    /// カバーアートが埋め込まれたファイルから、画像のバイト配列が正しく抽出されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ExtractAlbumArtBytesAsync_カバーアートが埋め込まれたファイル_画像のバイト配列が抽出されること()
    {
        // Arrange
        var sut = new TagLibMetadataExtractor();
        string wavPath = CreateDummyWavFile("with_art.wav");
        byte[] expectedPictureBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

        // TagLibでID3v2タグおよびカバーアートを付与
        using (var tagFile = TagLib.File.Create(wavPath))
        {
            var picture = new TagLib.Picture(new TagLib.ByteVector(expectedPictureBytes))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "image/png",
                Description = "Cover Image"
            };
            tagFile.Tag.Pictures = new TagLib.IPicture[] { picture };
            tagFile.Save();
        }

        // Act
        var actual = await sut.ExtractAlbumArtBytesAsync(AudioPath.Create(wavPath));

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(expectedPictureBytes, actual);
    }
}
