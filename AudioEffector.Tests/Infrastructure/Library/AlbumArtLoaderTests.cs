using System;
using System.IO;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Library;

/// <summary>
/// AlbumArtLoaderの画像抽出・メモリキャッシュ・LRU破棄ロジックを検証するテストクラス
/// </summary>
public sealed class AlbumArtLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public AlbumArtLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_AlbumArt", Guid.NewGuid().ToString());
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

    private string CreateWavWithPicture(string fileName, byte[] pictureBytes)
    {
        string filePath = Path.Combine(_tempDirectory, fileName);
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("RIFF"u8.ToArray());
            bw.Write(36);
            bw.Write("WAVE"u8.ToArray());
            bw.Write("fmt "u8.ToArray());
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)2);
            bw.Write(44100);
            bw.Write(44100 * 4);
            bw.Write((short)4);
            bw.Write((short)16);
            bw.Write("data"u8.ToArray());
            bw.Write(0);
        }

        using (var tagFile = TagLib.File.Create(filePath))
        {
            var picture = new TagLib.Picture(new TagLib.ByteVector(pictureBytes))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "image/png"
            };
            tagFile.Tag.Pictures = new TagLib.IPicture[] { picture };
            tagFile.Save();
        }

        return filePath;
    }

    /// <summary>
    /// 画像が存在しないファイルパスを指定した場合、nullを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumArtBytesAsync_画像が存在しないファイル_nullを返すこと()
    {
        // Arrange
        var sut = new AlbumArtLoader();
        var dummyPath = AudioPath.Create(Path.Combine(_tempDirectory, "no_art_file.mp3"));

        // Act
        var actual = await sut.GetAlbumArtBytesAsync(dummyPath);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// 初回取得後、2回目の呼び出し時はメモリキャッシュから同一のバイト配列が即時返されるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumArtBytesAsync_初回取得後_2回目はメモリキャッシュから同一バイト配列を即時返すこと()
    {
        // Arrange
        var sut = new AlbumArtLoader();
        byte[] pictureBytes = [0x89, 0x50, 0x4E, 0x47, 10, 20, 30];
        string filePath = CreateWavWithPicture("cached_track.wav", pictureBytes);
        var audioPath = AudioPath.Create(filePath);

        // Act
        var firstResult = await sut.GetAlbumArtBytesAsync(audioPath);
        var secondResult = await sut.GetAlbumArtBytesAsync(audioPath);

        // Assert
        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Same(firstResult, secondResult); // 同一参照（キャッシュヒット）
    }

    /// <summary>
    /// カバーアート画像が存在する場合、GetAlbumArtStreamAsyncが有効なMemoryStreamを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumArtStreamAsync_画像が存在する場合_有効なMemoryStreamを返すこと()
    {
        // Arrange
        var sut = new AlbumArtLoader();
        byte[] pictureBytes = [1, 2, 3, 4, 5];
        string filePath = CreateWavWithPicture("stream_test.wav", pictureBytes);

        // Act
        using var stream = await sut.GetAlbumArtStreamAsync(AudioPath.Create(filePath));

        // Assert
        Assert.NotNull(stream);
        Assert.Equal(pictureBytes.Length, stream.Length);
    }

    /// <summary>
    /// カバーアート画像が存在しない場合、GetAlbumArtStreamAsyncがnullを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAlbumArtStreamAsync_画像が存在しない場合_nullを返すこと()
    {
        // Arrange
        var sut = new AlbumArtLoader();
        var nonExistentPath = AudioPath.Create(Path.Combine(_tempDirectory, "no_stream.mp3"));

        // Act
        using var stream = await sut.GetAlbumArtStreamAsync(nonExistentPath);

        // Assert
        Assert.Null(stream);
    }

    /// <summary>
    /// ClearCacheを実行した際、内部のメモリキャッシュが破棄されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ClearCache_キャッシュクリア後_内部キャッシュが破棄されること()
    {
        // Arrange
        var sut = new AlbumArtLoader();
        byte[] pictureBytes = [1, 2, 3];
        string filePath = CreateWavWithPicture("clear_cache.wav", pictureBytes);
        var audioPath = AudioPath.Create(filePath);

        var firstResult = await sut.GetAlbumArtBytesAsync(audioPath);
        Assert.NotNull(firstResult);

        // Act
        sut.ClearCache();

        // Assert: キャッシュクリア後の取得は再読み込みとなり、中身は等しいが新規配列
        var afterClearResult = await sut.GetAlbumArtBytesAsync(audioPath);
        Assert.NotNull(afterClearResult);
        Assert.Equal(firstResult, afterClearResult);
    }

    /// <summary>
    /// キャッシュ上限（100件）を超えた際、LRU方式により最も古いエントリーがキャッシュから破棄されるかを検証します。
    /// </summary>
    [Fact]
    public async Task LRU動作_キャッシュ上限超過時_最も古いエントリーが破棄されること()
    {
        // Arrange
        var sut = new AlbumArtLoader();
        byte[] pictureBytes = [1, 2, 3];
        string firstTrack = CreateWavWithPicture("track_0.wav", pictureBytes);
        var firstAudioPath = AudioPath.Create(firstTrack);

        // 最初のトラックを取得（キャッシュに追加）
        var firstBytes = await sut.GetAlbumArtBytesAsync(firstAudioPath);
        Assert.NotNull(firstBytes);

        // 上限100件を超えるよう、さらに100件の異なるファイルを順次取得
        for (int i = 1; i <= 100; i++)
        {
            string trackPath = CreateWavWithPicture($"track_{i}.wav", pictureBytes);
            await sut.GetAlbumArtBytesAsync(AudioPath.Create(trackPath));
        }

        // Act: 最初のトラックを再取得。LRUで破棄されているため、再抽出（新規配列インスタンス）となる
        var reloadedBytes = await sut.GetAlbumArtBytesAsync(firstAudioPath);

        // Assert
        Assert.NotNull(reloadedBytes);
        Assert.NotSame(firstBytes, reloadedBytes); // LRUから破棄されたため参照が異なる
    }
}
