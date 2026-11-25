using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
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
        Assert.Equal(track.AlbumId, actual.AlbumId);
        Assert.Equal(track.FilePath, actual.FilePath);
        Assert.Equal(track.Title, actual.Title);
        Assert.Equal(track.Artist, actual.Artist);
        Assert.Equal(track.Album, actual.Album);
        Assert.Equal(track.Duration, actual.Duration);
    }

    /// <summary>
    /// AlbumIdを含むトラックを保存した際、JSONファイルへAlbumIdが書き出され別インスタンスから同一AlbumIdで復元されることを検証します。
    /// </summary>
    [Fact]
    public async Task 永続化検証_AlbumIdの保存と再読込_JSONへAlbumIdが永続化され同一IDで復元されること()
    {
        // Arrange
        var track = CreateSampleTrack("Around The World", "Daft Punk", "Homework");
        var expectedAlbumId = track.AlbumId;
        using (var initialSut = CreateSut())
        {
            await initialSut.SaveAsync(track);
        }

        // Act
        string rawJson = await File.ReadAllTextAsync(_testFilePath);
        using var reloadedSut = CreateSut();
        var actual = await reloadedSut.GetByIdAsync(track.Id);

        // Assert
        Assert.Contains(expectedAlbumId.ToString(), rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(actual);
        Assert.Equal(expectedAlbumId, actual.AlbumId);
    }

    /// <summary>
    /// AlbumIdフィールドが未記載・空文字・不正文字列・空GUIDの旧形式JSONを読み込んだ場合でも例外とならず、作品ごとに一意なAlbumIdが自動補完されることを検証します。
    /// </summary>
    [Fact]
    public async Task 後方互換読込_AlbumId未記載や不正値の旧形式JSON_例外にならず作品ごとにAlbumIdが自動補完されること()
    {
        // Arrange
        var track1Id = Guid.NewGuid();
        var track2Id = Guid.NewGuid();
        var track3Id = Guid.NewGuid();
        string legacyJson = $$"""
            [
              {
                "Id": "{{track1Id}}",
                "FilePath": "C:\\Music\\ArtistA\\legacy1.mp3",
                "Title": "Legacy Song 1",
                "Artist": "Artist A",
                "Album": "Shared Album",
                "DurationTicks": 1800000000
              },
              {
                "Id": "{{track2Id}}",
                "AlbumId": "not-a-valid-guid",
                "FilePath": "C:\\Music\\ArtistB\\legacy2.mp3",
                "Title": "Legacy Song 2",
                "Artist": "Artist B",
                "Album": "Shared Album",
                "DurationTicks": 2000000000
              },
              {
                "Id": "{{track3Id}}",
                "AlbumId": "00000000-0000-0000-0000-000000000000",
                "FilePath": "C:\\Music\\ArtistC\\legacy3.mp3",
                "Title": "Legacy Song 3",
                "Artist": "Artist C",
                "Album": "Another Album",
                "DurationTicks": 2100000000
              }
            ]
            """;
        await File.WriteAllTextAsync(_testFilePath, legacyJson);
        using var sut = CreateSut();

        // Act
        var tracks = await sut.GetAllAsync();

        // Assert
        Assert.Equal(3, tracks.Count);
        var loaded1 = Assert.Single(tracks, t => t.Id == TrackId.From(track1Id));
        var loaded2 = Assert.Single(tracks, t => t.Id == TrackId.From(track2Id));
        var loaded3 = Assert.Single(tracks, t => t.Id == TrackId.From(track3Id));
        Assert.NotEqual(Guid.Empty, loaded1.AlbumId.Value);
        Assert.NotEqual(Guid.Empty, loaded2.AlbumId.Value);
        Assert.NotEqual(Guid.Empty, loaded3.AlbumId.Value);
        Assert.NotEqual(loaded1.AlbumId, loaded2.AlbumId);
        Assert.NotEqual(loaded1.AlbumId, loaded3.AlbumId);
    }

    /// <summary>
    /// 複数曲を含むJSONのうち一部に数値・オブジェクトなど文字列以外の型でAlbumIdが記載されていても、全体が0曲にならずその曲のみ新規AlbumIdで自動補完され正常な他曲も復元されることを検証します。
    /// </summary>
    [Fact]
    public async Task 後方互換読込_AlbumIdが数値やオブジェクトなど不正型を含む複数曲JSON_不正曲のみ自動補完され全曲が復元されること()
    {
        // Arrange
        var malformedTrackId = Guid.NewGuid();
        var validTrackId = Guid.NewGuid();
        var objectAlbumIdTrackId = Guid.NewGuid();
        var validAlbumId = AlbumId.New();
        string mixedJson = $$"""
            [
              {
                "Id": "{{malformedTrackId}}",
                "AlbumId": 123,
                "FilePath": "C:\\Music\\malformed.mp3",
                "Title": "Malformed Song",
                "Artist": "Artist Malformed",
                "Album": "Recovered Album",
                "DurationTicks": 1800000000
              },
              {
                "Id": "{{validTrackId}}",
                "AlbumId": "{{validAlbumId}}",
                "FilePath": "C:\\Music\\valid.mp3",
                "Title": "Valid Song",
                "Artist": "Valid Artist",
                "Album": "Valid Album",
                "DurationTicks": 2000000000
              },
              {
                "Id": "{{objectAlbumIdTrackId}}",
                "AlbumId": { "unexpected": true },
                "FilePath": "C:\\Music\\object_id.mp3",
                "Title": "Object Id Song",
                "Artist": "Artist Object",
                "Album": "Object Album",
                "DurationTicks": 2100000000
              }
            ]
            """;
        await File.WriteAllTextAsync(_testFilePath, mixedJson);
        using var sut = CreateSut();

        // Act
        var tracks = await sut.GetAllAsync();

        // Assert
        Assert.Equal(3, tracks.Count);
        var malformed = Assert.Single(tracks, t => t.Id == TrackId.From(malformedTrackId));
        var valid = Assert.Single(tracks, t => t.Id == TrackId.From(validTrackId));
        var objectId = Assert.Single(tracks, t => t.Id == TrackId.From(objectAlbumIdTrackId));
        Assert.NotEqual(Guid.Empty, malformed.AlbumId.Value);
        Assert.Equal(validAlbumId, valid.AlbumId);
        Assert.NotEqual(Guid.Empty, objectId.AlbumId.Value);
        Assert.NotEqual(malformed.AlbumId, valid.AlbumId);
    }

    /// <summary>
    /// 演奏者ごとに別々のAlbumIdが保存されたコンピレーションJSONを読み込んだ際、作品単位の共通AlbumIdへ統一されることを検証します。
    /// </summary>
    [Fact]
    public async Task 後方互換読込_旧AlbumIdで保存されたコンピレーションJSON_読込時に作品単位の共通AlbumIdへ統一されること()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var legacyAlbumId1 = AlbumId.New();
        var legacyAlbumId2 = AlbumId.New();
        string legacyCompilationJson = $$"""
            [
              {
                "Id": "{{id1}}",
                "AlbumId": "{{legacyAlbumId1}}",
                "FilePath": "C:\\Music\\BlueNote\\01.flac",
                "Title": "Track 1",
                "Artist": "Clifford Brown",
                "Album": "Blue Note X Blue Giant",
                "TrackNumber": 1,
                "DurationTicks": 1800000000
              },
              {
                "Id": "{{id2}}",
                "AlbumId": "{{legacyAlbumId2}}",
                "FilePath": "C:\\Music\\BlueNote\\02.flac",
                "Title": "Track 2",
                "Artist": "Herbie Hancock",
                "Album": "Blue Note X Blue Giant",
                "TrackNumber": 2,
                "DurationTicks": 2000000000
              }
            ]
            """;
        await File.WriteAllTextAsync(_testFilePath, legacyCompilationJson);
        using var sut = CreateSut();

        // Act
        var loaded = await sut.GetAllAsync();

        // Assert
        Assert.Equal(2, loaded.Count);
        Assert.Equal(loaded[0].AlbumId, loaded[1].AlbumId);
        Assert.Contains(loaded, t => t.Artist == "Clifford Brown");
        Assert.Contains(loaded, t => t.Artist == "Herbie Hancock");
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

    /// <summary>
    /// ReplaceAllAsync のファイル書き込みが IOException または OperationCanceledException で失敗した場合、
    /// 同一インスタンスのキャッシュと別インスタンスの双方で以前のトラック一覧が維持されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplaceAllAsync_書き込みがIOExceptionまたはキャンセルで失敗した場合_同一インスタンスと別インスタンスの両方で以前の一覧が読めること(bool failWithCancellation)
    {
        // Arrange
        var failingLockHelper = new FailOnWriteFileLockHelper(JsonFileLockHelper.Default);
        using var sut = new JsonTrackRepository(_testFilePath, failingLockHelper);
        Track originalTrack = CreateSampleTrack("Original Track", "Original Artist", "Original Album");
        Track replacementTrack = CreateSampleTrack("Replacement Track", "New Artist", "New Album");

        await sut.ReplaceAllAsync([originalTrack]);
        // 事前に GetAllAsync を呼んで読み込み済み（_isLoaded == true, _lastLoadedMetadata 同期済み）にする
        Assert.Equal(originalTrack.Id, Assert.Single(await sut.GetAllAsync()).Id);

        failingLockHelper.WriteFailureFactory = failWithCancellation
            ? () => new OperationCanceledException("書き込みがキャンセルされました。")
            : () => new IOException("ディスクへの書き込みに失敗しました。");

        // Act
        if (failWithCancellation)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ReplaceAllAsync([replacementTrack]));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => sut.ReplaceAllAsync([replacementTrack]));
        }

        // Assert: 同一インスタンスでも保存されなかった新一覧へ切り替わらず以前の一覧を返すこと
        var sameInstanceTracks = await sut.GetAllAsync();
        Track sameInstanceTrack = Assert.Single(sameInstanceTracks);
        Assert.Equal(originalTrack.Id, sameInstanceTrack.Id);
        Assert.Equal("Original Track", sameInstanceTrack.Title);

        // Assert: 別インスタンスでもディスク上の以前の一覧が読めること
        using var otherInstance = new JsonTrackRepository(_testFilePath);
        var otherInstanceTracks = await otherInstance.GetAllAsync();
        Track otherInstanceTrack = Assert.Single(otherInstanceTracks);
        Assert.Equal(originalTrack.Id, otherInstanceTrack.Id);
        Assert.Equal("Original Track", otherInstanceTrack.Title);
    }

    /// <summary>
    /// 同一 ID・同一 Track オブジェクトのタグを直接更新した後にファイル置換が失敗した場合でも、
    /// キャッシュが呼び出し元オブジェクトから独立しており、同一インスタンスと別インスタンスの両方で更新前タイトルが読めることを検証します。
    /// </summary>
    [Fact]
    public async Task ReplaceAllAsync_同一オブジェクトのタグ更新後に書き込み失敗した場合_同一インスタンスと別インスタンスの両方で更新前タイトルが読めること()
    {
        // Arrange
        using var sut = CreateSut();
        Track track = CreateSampleTrack("Before", "Artist A", "Album A");
        await sut.ReplaceAllAsync([track]);
        Assert.Equal("Before", Assert.Single(await sut.GetAllAsync()).Title);

        // 呼び出し元が保持する同一オブジェクトのタグを直接更新（WPF の CopyTrackMetadata 相当）
        track.Title = "After";

        // Act: 対象 JSON ファイルを FileShare.Read で保持して原子的ファイル置換を失敗させる
        using (var blockingReadStream = new FileStream(_testFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Exception? ex = await Record.ExceptionAsync(() => sut.ReplaceAllAsync([track]));
            Assert.NotNull(ex);
            Assert.True(ex is IOException or UnauthorizedAccessException, $"予期しない例外型です: {ex.GetType().FullName}");
        }

        // Assert: 同一インスタンスでも未保存の "After" ではなく "Before" が返ること
        Track sameInstanceTrack = Assert.Single(await sut.GetAllAsync());
        Assert.Equal(track.Id, sameInstanceTrack.Id);
        Assert.Equal("Before", sameInstanceTrack.Title);

        // Assert: 別インスタンスでも "Before" が返ること
        using var otherInstance = CreateSut();
        Track otherInstanceTrack = Assert.Single(await otherInstance.GetAllAsync());
        Assert.Equal(track.Id, otherInstanceTrack.Id);
        Assert.Equal("Before", otherInstanceTrack.Title);
    }

    private sealed class FailOnWriteFileLockHelper : IJsonFileLockHelper
    {
        private readonly IJsonFileLockHelper _inner;

        public FailOnWriteFileLockHelper(IJsonFileLockHelper inner)
        {
            _inner = inner;
        }

        public Func<Exception>? WriteFailureFactory { get; set; }

        public Task<IDisposable> AcquireLockAsync(
            string filePath,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => _inner.AcquireLockAsync(filePath, timeout, cancellationToken);

        public IDisposable AcquireLock(string filePath, TimeSpan? timeout = null)
            => _inner.AcquireLock(filePath, timeout);

        public Task WriteAllTextAtomicAsync(
            string filePath,
            string content,
            CancellationToken cancellationToken = default)
        {
            if (WriteFailureFactory is not null)
            {
                throw WriteFailureFactory();
            }

            return _inner.WriteAllTextAtomicAsync(filePath, content, cancellationToken);
        }

        public void WriteAllTextAtomic(string filePath, string content)
        {
            if (WriteFailureFactory is not null)
            {
                throw WriteFailureFactory();
            }

            _inner.WriteAllTextAtomic(filePath, content);
        }

        public FileMetadataSnapshot GetFileMetadata(string filePath)
            => _inner.GetFileMetadata(filePath);
    }
}
