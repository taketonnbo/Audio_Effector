using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Repository;

/// <summary>
/// JsonSettingsRepositoryの設定値永続化およびCRUD操作を検証するテストクラス
/// </summary>
public sealed class JsonSettingsRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testFilePath;

    public JsonSettingsRepositoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Settings", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _testFilePath = Path.Combine(_tempDirectory, "settings.json");
    }

    private JsonSettingsRepository CreateSut() => new(_testFilePath);

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
    /// キーと設定値を保存した後、GetValueAsyncで正しく取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task SetValueAsyncおよびGetValueAsync_キーと値の保存_設定値が取得できること()
    {
        // Arrange
        using var sut = CreateSut();

        // Act
        await sut.SetValueAsync("Theme", "Dark");
        var actual = await sut.GetValueAsync("Theme");

        // Assert
        Assert.Equal("Dark", actual);
    }

    /// <summary>
    /// 大文字小文字の異なるキーで取得した場合でも、区別なく同一の設定値を取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetValueAsync_大文字小文字の異なるキー指定_大文字小文字を区別せず同一設定値を取得できること()
    {
        // Arrange
        using var sut = CreateSut();
        await sut.SetValueAsync("VolumeLevel", "85");

        // Act
        var lowerResult = await sut.GetValueAsync("volumelevel");
        var upperResult = await sut.GetValueAsync("VOLUMELEVEL");

        // Assert
        Assert.Equal("85", lowerResult);
        Assert.Equal("85", upperResult);
    }

    /// <summary>
    /// 存在しないキーを指定した場合、第2引数で指定したデフォルト値が返るかを検証します。
    /// </summary>
    [Fact]
    public async Task GetValueAsync_存在しないキー指定_デフォルト値が返ること()
    {
        // Arrange
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetValueAsync("NonExistentKey", "DefaultFallback");

        // Assert
        Assert.Equal("DefaultFallback", actual);
    }

    /// <summary>
    /// SetValueAsyncで値にnullを指定した場合、該当キーの設定が削除されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SetValueAsync_null値指定_該当キーの設定が削除されること()
    {
        // Arrange
        using var sut = CreateSut();
        await sut.SetValueAsync("TempKey", "TempValue");

        // Act
        await sut.SetValueAsync("TempKey", null);
        var actual = await sut.GetValueAsync("TempKey");

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// RemoveAsyncで登録済みキーを削除した場合、設定が削除されnullが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_登録済みキー削除_設定が削除されること()
    {
        // Arrange
        using var sut = CreateSut();
        await sut.SetValueAsync("ShortcutPlay", "Space");

        // Act
        await sut.RemoveAsync("ShortcutPlay");
        var actual = await sut.GetValueAsync("ShortcutPlay");

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// 複数の設定を保存した後、GetAllAsyncですべてのキーと値のディクショナリを取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_複数キー保存後_全設定のディクショナリを返すこと()
    {
        // Arrange
        using var sut = CreateSut();
        await sut.SetValueAsync("Key1", "Value1");
        await sut.SetValueAsync("Key2", "Value2");
        await sut.SetValueAsync("Key3", "Value3");

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Equal(3, actual.Count);
        Assert.Equal("Value1", actual["Key1"]);
        Assert.Equal("Value2", actual["Key2"]);
        Assert.Equal("Value3", actual["Key3"]);
    }

    /// <summary>
    /// 保存先ファイルが存在しない初期状態で、GetAllAsyncが空のディクショナリを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_初期状態_空のディクショナリを返すこと()
    {
        // Arrange
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// インスタンス破棄後に別インスタンスで同一ファイルを再読み込みした際、設定値が正しく復元されるかを検証します。
    /// </summary>
    [Fact]
    public async Task 永続化検証_別インスタンスで再読込_ファイルから設定値が復元されること()
    {
        // Arrange
        using (var initialSut = CreateSut())
        {
            await initialSut.SetValueAsync("SavedTheme", "Light");
            await initialSut.SetValueAsync("BufferSize", "2048");
        }

        // Act
        using var newSut = CreateSut();
        var theme = await newSut.GetValueAsync("SavedTheme");
        var buffer = await newSut.GetValueAsync("BufferSize");

        // Assert
        Assert.Equal("Light", theme);
        Assert.Equal("2048", buffer);
    }

    /// <summary>
    /// keyにnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public async Task 引数null検証_keyがnullの場合_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        using var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetValueAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetValueAsync(null!, "val"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RemoveAsync(null!));
    }

    /// <summary>
    /// 破損したJSONファイルが存在する場合でも、例外をスローせず空状態で安全に起動することを検証します。
    /// </summary>
    [Fact]
    public async Task 初期化_破損したJSONファイルが存在する場合_エラーにならず空状態で起動すること()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "{ corrupted json without close brace");
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }
}
