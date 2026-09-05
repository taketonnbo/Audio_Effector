using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Repository;

/// <summary>
/// JsonFavoriteRepositoryのお気に入りトラックID永続化およびCRUD操作を検証するテストクラス
/// </summary>
public sealed class JsonFavoriteRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testFilePath;

    public JsonFavoriteRepositoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Favorite", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _testFilePath = Path.Combine(_tempDirectory, "favorites.json");
    }

    private JsonFavoriteRepository CreateSut() => new(_testFilePath);

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

    /// <summary>
    /// 新規トラックIDをお気に入りに追加後、ContainsAsyncでtrueが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task AddAsyncおよびContainsAsync_新規トラックID追加_お気に入り登録状態となること()
    {
        // Arrange
        using var sut = CreateSut();
        var trackId = TrackId.New();

        // Act
        await sut.AddAsync(trackId);
        var actual = await sut.ContainsAsync(trackId);

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// 同一のトラックIDを重複して追加した場合でも、二重登録されず安全に1件として保持されるかを検証します。
    /// </summary>
    [Fact]
    public async Task AddAsync_重複したトラックID追加_二重登録されず正常に完了すること()
    {
        // Arrange
        using var sut = CreateSut();
        var trackId = TrackId.New();

        // Act
        await sut.AddAsync(trackId);
        await sut.AddAsync(trackId);
        var favorites = await sut.GetFavoriteIdsAsync();

        // Assert
        Assert.Single(favorites);
        Assert.Contains(trackId, favorites);
    }

    /// <summary>
    /// 登録済みのトラックIDをお気に入りから削除後、ContainsAsyncでfalseが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_登録済みトラックID削除_お気に入り登録解除状態となること()
    {
        // Arrange
        using var sut = CreateSut();
        var trackId = TrackId.New();
        await sut.AddAsync(trackId);

        // Act
        await sut.RemoveAsync(trackId);
        var actual = await sut.ContainsAsync(trackId);
        var all = await sut.GetFavoriteIdsAsync();

        // Assert
        Assert.False(actual);
        Assert.Empty(all);
    }

    /// <summary>
    /// 未登録のトラックIDを削除しようとした場合、例外をスローせず安全に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_未登録のトラックID削除_例外をスローせず安全に完了すること()
    {
        // Arrange
        using var sut = CreateSut();
        var unregisteredId = TrackId.New();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.RemoveAsync(unregisteredId));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// 複数のトラックIDを追加後、GetFavoriteIdsAsyncですべてのお気に入りIDセットが取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetFavoriteIdsAsync_複数ID追加後_すべてのお気に入りIDセットを返すこと()
    {
        // Arrange
        using var sut = CreateSut();
        var id1 = TrackId.New();
        var id2 = TrackId.New();
        var id3 = TrackId.New();
        await sut.AddAsync(id1);
        await sut.AddAsync(id2);
        await sut.AddAsync(id3);

        // Act
        var actual = await sut.GetFavoriteIdsAsync();

        // Assert
        Assert.Equal(3, actual.Count);
        Assert.Contains(id1, actual);
        Assert.Contains(id2, actual);
        Assert.Contains(id3, actual);
    }

    /// <summary>
    /// 保存先ファイルが存在しない初期状態で、GetFavoriteIdsAsyncが空のセットを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetFavoriteIdsAsync_初期状態_空のセットを返すこと()
    {
        // Arrange
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetFavoriteIdsAsync();

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// インスタンス破棄後に別インスタンスで同一ファイルを再読み込みした際、お気に入りIDが正しく復元されるかを検証します。
    /// </summary>
    [Fact]
    public async Task 永続化検証_別インスタンスで再読込_ファイルからお気に入りIDが復元されること()
    {
        // Arrange
        var id1 = TrackId.New();
        var id2 = TrackId.New();

        using (var initialSut = CreateSut())
        {
            await initialSut.AddAsync(id1);
            await initialSut.AddAsync(id2);
        }

        // Act
        using var newSut = CreateSut();
        var actual = await newSut.GetFavoriteIdsAsync();

        // Assert
        Assert.Equal(2, actual.Count);
        Assert.Contains(id1, actual);
        Assert.Contains(id2, actual);
    }

    /// <summary>
    /// 破損したJSONファイルが存在する場合でも、例外をスローせず空セットとして安全に起動することを検証します。
    /// </summary>
    [Fact]
    public async Task 初期化_破損したJSONファイルが存在する場合_エラーにならず空セットで起動すること()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "{ broken favorite json }");
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetFavoriteIdsAsync();

        // Assert
        Assert.Empty(actual);
    }
}
