using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

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

    /// <summary>
    /// 同一ファイルを共有する複数インスタンスから同一キーへ並行して要素追加を行った場合、ロストアップデートせずに全要素が保持されることを検証します。
    /// </summary>
    [Fact]
    public async Task UpdateValueAsync_複数インスタンスから同一キーへ並行追加_全要素がロストアップデートせず保持されること()
    {
        // Arrange
        const string key = "CustomPresets";
        const int workerCount = 6;
        string[] expectedItems = Enumerable.Range(0, workerCount)
            .Select(i => $"Preset-{i}")
            .ToArray();

        // Act
        Task<bool>[] tasks = expectedItems.Select(item => Task.Run(async () =>
        {
            using var sut = CreateSut();
            return await sut.UpdateValueAsync(key, previous =>
            {
                List<string> items = DeserializeList(previous);
                items.Add(item);
                return JsonSerializer.Serialize(items);
            });
        })).ToArray();

        bool[] results = await Task.WhenAll(tasks);

        using var verifierSut = CreateSut();
        string? rawAfter = await verifierSut.GetValueAsync(key);
        List<string> actualItems = DeserializeList(rawAfter);

        // Assert
        Assert.All(results, Assert.True);
        Assert.Equal(workerCount, actualItems.Count);
        foreach (string expectedItem in expectedItems)
        {
            Assert.Contains(expectedItem, actualItems);
        }
    }

    /// <summary>
    /// 同一ファイルを共有する複数インスタンスから並行して要素削除と別要素追加を行った場合、削除対象のみが除去され別要素の変更が維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task UpdateValueAsync_複数インスタンスから並行して要素追加と要素削除を実行_削除対象のみ除去され別要素の変更が維持されること()
    {
        // Arrange
        const string key = "CustomPresets";
        using (var initialSut = CreateSut())
        {
            await initialSut.SetValueAsync(key, JsonSerializer.Serialize(new[] { "PresetA", "PresetB" }));
        }

        // Act
        Task<bool> deleteTask = Task.Run(async () =>
        {
            using var sut = CreateSut();
            return await sut.UpdateValueAsync(key, previous =>
            {
                List<string> items = DeserializeList(previous);
                items.Remove("PresetA");
                return JsonSerializer.Serialize(items);
            });
        });

        Task<bool> addTask1 = Task.Run(async () =>
        {
            using var sut = CreateSut();
            return await sut.UpdateValueAsync(key, previous =>
            {
                List<string> items = DeserializeList(previous);
                items.Add("PresetC");
                return JsonSerializer.Serialize(items);
            });
        });

        Task<bool> addTask2 = Task.Run(async () =>
        {
            using var sut = CreateSut();
            return await sut.UpdateValueAsync(key, previous =>
            {
                List<string> items = DeserializeList(previous);
                items.Add("PresetD");
                return JsonSerializer.Serialize(items);
            });
        });

        bool[] results = await Task.WhenAll(deleteTask, addTask1, addTask2);

        using var verifierSut = CreateSut();
        List<string> actualItems = DeserializeList(await verifierSut.GetValueAsync(key));

        // Assert
        Assert.All(results, Assert.True);
        Assert.DoesNotContain("PresetA", actualItems);
        Assert.Equal(3, actualItems.Count);
        Assert.Contains("PresetB", actualItems);
        Assert.Contains("PresetC", actualItems);
        Assert.Contains("PresetD", actualItems);
    }

    /// <summary>
    /// UpdateValueAsyncのコールバックが現在値と同一の値を返した場合、falseを返しディスク上の設定値が変更されないことを検証します。
    /// </summary>
    [Fact]
    public async Task UpdateValueAsync_コールバックが変更なしを返す場合_falseを返しディスク上の設定値を維持すること()
    {
        // Arrange
        using var sut = CreateSut();
        await sut.SetValueAsync("Theme", "Dark");

        // Act
        bool unchangedExistingResult = await sut.UpdateValueAsync("Theme", previous => previous);
        bool unchangedMissingResult = await sut.UpdateValueAsync("MissingKey", previous => previous);

        using var verifierSut = CreateSut();
        string? themeAfter = await verifierSut.GetValueAsync("Theme");
        string? missingAfter = await verifierSut.GetValueAsync("MissingKey");

        // Assert
        Assert.False(unchangedExistingResult);
        Assert.False(unchangedMissingResult);
        Assert.Equal("Dark", themeAfter);
        Assert.Null(missingAfter);
    }

    /// <summary>
    /// UpdateValueAsyncのコールバックがnullを返した場合、trueを返し対象キーのみがディスクから削除されることを検証します。
    /// </summary>
    [Fact]
    public async Task UpdateValueAsync_コールバックがnullを返す場合_trueを返し対象キーのみディスクから削除されること()
    {
        // Arrange
        using var sut = CreateSut();
        await sut.SetValueAsync("TargetKey", "ValueToRemove");
        await sut.SetValueAsync("KeepKey", "ValueToKeep");

        // Act
        bool deletedResult = await sut.UpdateValueAsync("TargetKey", _ => null);

        using var verifierSut = CreateSut();
        string? targetAfter = await verifierSut.GetValueAsync("TargetKey");
        string? keepAfter = await verifierSut.GetValueAsync("KeepKey");

        // Assert
        Assert.True(deletedResult);
        Assert.Null(targetAfter);
        Assert.Equal("ValueToKeep", keepAfter);
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
}
