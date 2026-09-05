using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Repository;

/// <summary>
/// JsonTrackRepositoryのトラックメタデータ永続化およびCRUD操作・検索を検証するテストクラス
/// </summary>
public sealed class JsonTrackRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testFilePath;

    public JsonTrackRepositoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Track", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _testFilePath = Path.Combine(_tempDirectory, "tracks.json");
    }

    private JsonTrackRepository CreateSut() => new(_testFilePath);

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

    private static Track CreateSampleTrack(string title = "Song A", string artist = "Artist A", string album = "Album A", string extension = ".mp3")
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create($@"C:\Music\{title}{extension}"),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromMinutes(3.5),
            year: 2024,
            trackNumber: 1,
            bitrate: 320,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "MP3",
            genre: "Rock",
            isFavorite: true,
            isLossless: false,
            isHiRes: false);
    }

    /// <summary>
    /// 単一トラックを保存した後、IDで正しく取得でき、全メタデータが保持されているかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveAsyncおよびGetByIdAsync_単一トラック保存_全メタデータが正しく取得できること()
    {
        // Arrange
        using var sut = CreateSut();
        var track = CreateSampleTrack();

        // Act
        await sut.SaveAsync(track);
        var actual = await sut.GetByIdAsync(track.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(track.Id, actual.Id);
        Assert.Equal(track.FilePath, actual.FilePath);
        Assert.Equal(track.Title, actual.Title);
        Assert.Equal(track.Artist, actual.Artist);
        Assert.Equal(track.Album, actual.Album);
        Assert.Equal(track.Duration, actual.Duration);
        Assert.Equal(track.Year, actual.Year);
        Assert.Equal(track.TrackNumber, actual.TrackNumber);
        Assert.Equal(track.Bitrate, actual.Bitrate);
        Assert.Equal(track.SampleRate, actual.SampleRate);
        Assert.Equal(track.BitsPerSample, actual.BitsPerSample);
        Assert.Equal(track.Format, actual.Format);
        Assert.Equal(track.Genre, actual.Genre);
        Assert.Equal(track.IsFavorite, actual.IsFavorite);
        Assert.Equal(track.IsLossless, actual.IsLossless);
        Assert.Equal(track.IsHiRes, actual.IsHiRes);
    }

    /// <summary>
    /// 登録済みの音声ファイルパスを指定して、該当トラックが正しく取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetByPathAsync_登録済みFilePath指定_該当トラックを取得できること()
    {
        // Arrange
        using var sut = CreateSut();
        var track = CreateSampleTrack("SpecialTrack");
        await sut.SaveAsync(track);

        // Act
        var actual = await sut.GetByPathAsync(AudioPath.Create(track.FilePath));

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(track.Id, actual.Id);
        Assert.Equal(track.FilePath, actual.FilePath);
    }

    /// <summary>
    /// 大文字小文字の異なるファイルパスを指定した場合でも、一致するトラックを取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetByPathAsync_大文字小文字の異なるファイルパス指定_一致するトラックを取得できること()
    {
        // Arrange
        using var sut = CreateSut();
        var track = CreateSampleTrack("CaseTest");
        await sut.SaveAsync(track);

        // Act
        var lowerPath = AudioPath.Create(track.FilePath.ToLowerInvariant());
        var actual = await sut.GetByPathAsync(lowerPath);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(track.Id, actual.Id);
    }

    /// <summary>
    /// 未登録のファイルパスを指定した場合、nullが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task GetByPathAsync_未登録ファイルパス指定_nullを返すこと()
    {
        // Arrange
        using var sut = CreateSut();
        var unrecordedPath = AudioPath.Create(@"C:\Music\non_existent.flac");

        // Act
        var actual = await sut.GetByPathAsync(unrecordedPath);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// タイトル、アーティスト、アルバムに部分一致するキーワードを指定した際、該当するトラック一覧が取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task SearchAsync_タイトルやアーティストに部分一致するキーワード指定_一致したトラック一覧を返すこと()
    {
        // Arrange
        using var sut = CreateSut();
        var t1 = CreateSampleTrack("Spring Breeze", "Sakura Band", "Seasons");
        var t2 = CreateSampleTrack("Summer Heat", "Breeze Ensemble", "Vacation");
        var t3 = CreateSampleTrack("Winter Cold", "Snow", "Frozen");
        await sut.SaveRangeAsync([t1, t2, t3]);

        // Act
        var breezeResults = await sut.SearchAsync("breeze");
        var snowResults = await sut.SearchAsync("SNOW");

        // Assert
        Assert.Equal(2, breezeResults.Count);
        Assert.Contains(breezeResults, t => t.Id == t1.Id);
        Assert.Contains(breezeResults, t => t.Id == t2.Id);

        Assert.Single(snowResults);
        Assert.Equal(t3.Id, snowResults[0].Id);
    }

    /// <summary>
    /// 検索キーワードに空文字または空白を指定した場合、すべてのトラックが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task SearchAsync_空文字または空白指定_全トラックを返すこと()
    {
        // Arrange
        using var sut = CreateSut();
        var t1 = CreateSampleTrack("Track1");
        var t2 = CreateSampleTrack("Track2");
        await sut.SaveRangeAsync([t1, t2]);

        // Act
        var actualEmpty = await sut.SearchAsync("");
        var actualWhitespace = await sut.SearchAsync("   ");

        // Assert
        Assert.Equal(2, actualEmpty.Count);
        Assert.Equal(2, actualWhitespace.Count);
    }

    /// <summary>
    /// 複数のトラックを一括保存（SaveRangeAsync）した後、全件が正しく取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveRangeAsync_複数トラック一括保存_全件が正しく保存および取得できること()
    {
        // Arrange
        using var sut = CreateSut();
        var tracks = new[]
        {
            CreateSampleTrack("Batch1"),
            CreateSampleTrack("Batch2"),
            CreateSampleTrack("Batch3")
        };

        // Act
        await sut.SaveRangeAsync(tracks);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Equal(3, actual.Count);
    }

    /// <summary>
    /// 登録済みのトラックIDを指定して削除した場合、正常に削除され取得できなくなるかを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_存在するトラックID指定_正常に削除されGetByIdでnullとなること()
    {
        // Arrange
        using var sut = CreateSut();
        var track = CreateSampleTrack("DeleteTarget");
        await sut.SaveAsync(track);

        // Act
        await sut.DeleteAsync(track.Id);
        var actual = await sut.GetByIdAsync(track.Id);
        var all = await sut.GetAllAsync();

        // Assert
        Assert.Null(actual);
        Assert.Empty(all);
    }

    /// <summary>
    /// 存在しないトラックIDを指定して削除した場合、例外なく安全に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_存在しないトラックID指定_例外なく安全に完了すること()
    {
        // Arrange
        using var sut = CreateSut();
        var nonExistentId = TrackId.New();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.DeleteAsync(nonExistentId));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// インスタンス破棄後に別インスタンスで同一ファイルを再読み込みした際、すべてのDTOプロパティが復元されるかを検証します。
    /// </summary>
    [Fact]
    public async Task 永続化検証_別インスタンスで再読込_全フィールドが正確にシリアライズおよびデシリアライズされること()
    {
        // Arrange
        var track = CreateSampleTrack("PersistenceTrack", "Super Star", "Greatest Hits");
        using (var initialSut = CreateSut())
        {
            await initialSut.SaveAsync(track);
        }

        // Act
        using var newSut = CreateSut();
        var actual = await newSut.GetByIdAsync(track.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(track.Id, actual.Id);
        Assert.Equal(track.FilePath, actual.FilePath);
        Assert.Equal(track.Title, actual.Title);
        Assert.Equal(track.Artist, actual.Artist);
        Assert.Equal(track.Album, actual.Album);
        Assert.Equal(track.Duration, actual.Duration);
    }

    /// <summary>
    /// track引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public async Task 引数null検証_trackがnullの場合_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        using var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SaveAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SaveRangeAsync(null!));
    }

    /// <summary>
    /// 破損したJSONファイルが存在する場合でも、例外をスローせず空状態で安全に起動することを検証します。
    /// </summary>
    [Fact]
    public async Task 初期化_破損JSONファイル存在時_エラーにならず空状態で起動すること()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "corrupted raw text without json");
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }
}
