using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

/// <summary>
/// JsonFavoriteRepositoryのメタデータベース永続化、旧形式後方互換マイグレーション、および破損JSON耐性を検証するテストクラス
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

    private static Track CreateSampleTrack(
        string title = "Midnight City",
        string artist = "M83",
        string album = "Hurry Up, We're Dreaming",
        uint trackNumber = 2,
        double durationSeconds = 243.5,
        string filePath = @"D:\Music\M83\02_Midnight_City.flac")
    {
        return new Track
        {
            Title = title,
            Artist = artist,
            Album = album,
            TrackNumber = trackNumber,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            FilePath = filePath
        };
    }

    /// <summary>
    /// 新スキーマの全メタデータプロパティがJSONオブジェクト配列として保存され、別インスタンスから正しく復元されることを検証します。
    /// </summary>
    [Fact]
    public async Task AddOrUpdateAsync_新スキーマ保存と別インスタンス再読込_全メタデータプロパティが正しく永続化および復元されること()
    {
        // Arrange
        var addedAt = new DateTime(2026, 11, 22, 23, 50, 0, DateTimeKind.Local);
        var track = CreateSampleTrack(title: "夜に駆ける", artist: "YOASOBI", album: "THE BOOK");
        var entry = FavoriteTrackEntry.FromTrack(track, addedAt);

        using (var initialSut = CreateSut())
        {
            await initialSut.AddOrUpdateAsync(entry);
        }

        // Act
        string rawJson = await File.ReadAllTextAsync(_testFilePath);
        using var document = JsonDocument.Parse(rawJson);
        using var reloadedSut = CreateSut();
        var reloaded = await reloadedSut.GetAllAsync();

        // Assert
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var jsonObj = document.RootElement[0];
        Assert.Equal("夜に駆ける", jsonObj.GetProperty("Title").GetString());
        Assert.Equal("YOASOBI", jsonObj.GetProperty("Artist").GetString());
        Assert.Equal("THE BOOK", jsonObj.GetProperty("Album").GetString());
        Assert.Equal(2u, jsonObj.GetProperty("TrackNumber").GetUInt32());
        Assert.Equal(243.5, jsonObj.GetProperty("DurationSeconds").GetDouble());
        Assert.Equal(@"D:\Music\M83\02_Midnight_City.flac", jsonObj.GetProperty("LastKnownFilePath").GetString());
        Assert.True(jsonObj.TryGetProperty("AddedAt", out _));
        Assert.Contains("夜に駆ける", rawJson); // 日本語がUnicodeエスケープされず可読出力されること

        Assert.Single(reloaded);
        Assert.Equal("夜に駆ける", reloaded[0].Title);
        Assert.Equal("YOASOBI", reloaded[0].Artist);
        Assert.Equal("THE BOOK", reloaded[0].Album);
        Assert.Equal(2u, reloaded[0].TrackNumber);
        Assert.Equal(243.5, reloaded[0].DurationSeconds);
        Assert.Equal(@"D:\Music\M83\02_Midnight_City.flac", reloaded[0].LastKnownFilePath);
        Assert.Equal(addedAt, reloaded[0].AddedAt);
    }

    /// <summary>
    /// 同一メタデータの楽曲を別パスで重複登録した場合、二重登録されず1件のまま最新パスへ更新され登録日時が維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task AddOrUpdateAsync_同一メタデータの重複登録_二重登録されず最新パスへ更新されること()
    {
        // Arrange
        using var sut = CreateSut();
        var firstAddedAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Local);
        var first = FavoriteTrackEntry.FromTrack(CreateSampleTrack(filePath: @"C:\Music\02_Midnight_City.mp3"), firstAddedAt);
        var second = FavoriteTrackEntry.FromTrack(CreateSampleTrack(filePath: @"D:\HiRes\02_Midnight_City.flac"), firstAddedAt.AddDays(1));

        // Act
        await sut.AddOrUpdateAsync(first);
        await sut.AddOrUpdateAsync(second);
        var all = await sut.GetAllAsync();

        // Assert
        Assert.Single(all);
        Assert.Equal(@"D:\HiRes\02_Midnight_City.flac", all[0].LastKnownFilePath);
        Assert.Equal(firstAddedAt, all[0].AddedAt);
    }

    /// <summary>
    /// 登録済み楽曲を別パス（フォルダ移動後）のTrackでContainsAsyncおよびRemoveAsyncに渡しても、メタデータ一致で判定・削除できることを検証します。
    /// </summary>
    [Fact]
    public async Task ContainsAsyncおよびRemoveAsync_フォルダ移動後のトラック指定_メタデータ一致で判定および削除できること()
    {
        // Arrange
        using var sut = CreateSut();
        var originalTrack = CreateSampleTrack(filePath: @"C:\Old\02_Midnight_City.mp3");
        var movedTrack = CreateSampleTrack(filePath: @"D:\Moved\MidnightCity.flac");
        await sut.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(originalTrack));

        // Act
        bool containsBeforeRemove = await sut.ContainsAsync(movedTrack);
        await sut.RemoveAsync(movedTrack);
        bool containsAfterRemove = await sut.ContainsAsync(movedTrack);
        var remaining = await sut.GetAllAsync();

        // Assert
        Assert.True(containsBeforeRemove);
        Assert.False(containsAfterRemove);
        Assert.Empty(remaining);
    }

    /// <summary>
    /// 未登録のトラックを削除しようとした場合、例外をスローせず安全に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_未登録のトラック削除_例外をスローせず安全に完了すること()
    {
        // Arrange
        using var sut = CreateSut();
        var unregistered = CreateSampleTrack(title: "Not Registered");

        // Act
        var exception = await Record.ExceptionAsync(() => sut.RemoveAsync(unregistered));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// 従来のファイルパス配列形式（List of string）のfavorites.jsonを読み込んだ際、エラーにならず新スキーマへ自動マイグレーションされることを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_旧形式ファイルパス配列JSONの読込_後方互換で読み込まれ新スキーマへ移行されること()
    {
        // Arrange
        var legacyJson = JsonSerializer.Serialize(new[]
        {
            @"D:\Music\ArtistA\Song1.mp3",
            @"D:\Music\ArtistB\Song2.flac",
            @"D:\Music\ArtistA\Song1.mp3" // 重複パス
        });
        await File.WriteAllTextAsync(_testFilePath, legacyJson);
        using var sut = CreateSut();

        // Act
        var entries = await sut.GetAllAsync();
        string migratedJson = await File.ReadAllTextAsync(_testFilePath);
        using var migratedDoc = JsonDocument.Parse(migratedJson);

        // Assert
        Assert.Equal(2, entries.Count);
        Assert.Equal(@"D:\Music\ArtistA\Song1.mp3", entries[0].LastKnownFilePath);
        Assert.Equal(@"D:\Music\ArtistB\Song2.flac", entries[1].LastKnownFilePath);
        Assert.Equal(JsonValueKind.Array, migratedDoc.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Object, migratedDoc.RootElement[0].ValueKind);
        Assert.Equal(@"D:\Music\ArtistA\Song1.mp3", migratedDoc.RootElement[0].GetProperty("LastKnownFilePath").GetString());
    }

    /// <summary>
    /// 旧JsonFavoriteRepositoryが保存したGuid配列形式が存在してもクラッシュせず安全にスキップされることを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_旧Guid配列JSONの読込_クラッシュせず安全に空リストとして扱われること()
    {
        // Arrange
        var legacyGuidJson = JsonSerializer.Serialize(new[] { Guid.NewGuid(), Guid.NewGuid() });
        await File.WriteAllTextAsync(_testFilePath, legacyGuidJson);
        using var sut = CreateSut();

        // Act
        var entries = await sut.GetAllAsync();

        // Assert
        Assert.Empty(entries);
    }

    /// <summary>
    /// 保存先ファイルが存在しない初期状態で、GetAllAsyncが空のリストを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_初期状態_空のリストを返すこと()
    {
        // Arrange
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// 破損したJSONファイルや空ファイルが存在する場合でも、例外をスローせず空コレクションとして安全に起動することを検証します。
    /// </summary>
    [Theory]
    [InlineData("{ broken favorite json }")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"unexpected\": true}")]
    public async Task GetAllAsync_破損JSONや空ファイルの場合_エラーにならず空リストで起動すること(string invalidContent)
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, invalidContent);
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// 未ロード状態からSaveAllAsyncを呼び出した際、重複排除したうえで既存ファイルを上書き保存できることを検証します。
    /// </summary>
    [Fact]
    public async Task SaveAllAsync_未ロード状態からの一括保存_重複排除され全件上書き保存されること()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, JsonSerializer.Serialize(new[] { @"C:\Legacy\Old.mp3" }));
        using var sut = CreateSut();
        var t1 = FavoriteTrackEntry.FromTrack(CreateSampleTrack(title: "Song 1", filePath: @"D:\Music\1.mp3"));
        var t1Duplicate = FavoriteTrackEntry.FromTrack(CreateSampleTrack(title: "Song 1", filePath: @"D:\HiRes\1.flac"));
        var t2 = FavoriteTrackEntry.FromTrack(CreateSampleTrack(title: "Song 2", filePath: @"D:\Music\2.flac"));

        // Act
        await sut.SaveAllAsync([t1, t1Duplicate, t2]);
        using var reloadedSut = CreateSut();
        var reloaded = await reloadedSut.GetAllAsync();

        // Assert
        Assert.Equal(2, reloaded.Count);
        Assert.Equal("Song 1", reloaded[0].Title);
        Assert.Equal("Song 2", reloaded[1].Title);
    }

    /// <summary>
    /// キャンセル済みトークンで初回読み込みが中断された場合、OperationCanceledExceptionが再スローされ、次回読み込みで既存データが保持されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_初回読込キャンセル後_OperationCanceledExceptionが再スローされ既存お気に入りが保護されること()
    {
        // Arrange
        var existingEntry = FavoriteTrackEntry.FromTrack(CreateSampleTrack(title: "Protected Song"));
        using (var seedSut = CreateSut())
        {
            await seedSut.AddOrUpdateAsync(existingEntry);
        }

        using var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert 1: キャンセル時は例外を再スロー
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GetAllAsync(cts.Token));

        // Act & Assert 2: 次回通常読み込みで既存データが失われず取得できること
        var reloaded = await sut.GetAllAsync();
        Assert.Single(reloaded);
        Assert.Equal("Protected Song", reloaded[0].Title);
    }

    /// <summary>
    /// 一時的なファイルロック中にAddOrUpdateAsyncが呼ばれても既存ファイルが上書き破壊されず、ロック解除後に既存データが復元されることを検証します。
    /// </summary>
    [Fact]
    public async Task AddOrUpdateAsync_一時的ファイルロック中_既存お気に入りファイルが上書き消滅しないこと()
    {
        // Arrange
        var existingEntry = FavoriteTrackEntry.FromTrack(CreateSampleTrack(title: "Existing Favorite"));
        using (var seedSut = CreateSut())
        {
            await seedSut.AddOrUpdateAsync(existingEntry);
        }

        using var sut = CreateSut();
        using (new FileStream(_testFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Act: ロック中に読み込み・追加を試行
            var duringLock = await sut.GetAllAsync();
            await sut.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(CreateSampleTrack(title: "New Favorite", filePath: @"D:\Music\New.flac")));
            Assert.Empty(duringLock);
        }

        // Assert: ロック解除後に再試行すると既存データがそのまま残っていること
        var afterUnlock = await sut.GetAllAsync();
        Assert.Single(afterUnlock);
        Assert.Equal("Existing Favorite", afterUnlock[0].Title);
    }

    /// <summary>
    /// Dispose後に各メソッドを呼び出した場合、ObjectDisposedExceptionがスローされることを検証します。
    /// </summary>
    [Fact]
    public async Task Dispose後_メソッド呼び出し_ObjectDisposedExceptionがスローされること()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dispose();
        var track = CreateSampleTrack();
        var entry = FavoriteTrackEntry.FromTrack(track);

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.GetAllAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.AddOrUpdateAsync(entry));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.RemoveAsync(track));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.SaveAllAsync([entry]));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.ContainsAsync(track));
    }
}
