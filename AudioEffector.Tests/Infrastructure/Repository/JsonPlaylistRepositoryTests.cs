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
/// JsonPlaylistRepositoryの永続化およびCRUD操作を検証するテストクラス
/// </summary>
public sealed class JsonPlaylistRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testFilePath;

    public JsonPlaylistRepositoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Playlist", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _testFilePath = Path.Combine(_tempDirectory, "playlists.json");
    }

    private JsonPlaylistRepository CreateSut() => new(_testFilePath);

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
    /// 新規プレイリストを保存した後、指定IDで正しく取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveAsyncおよびGetByIdAsync_新規プレイリスト保存_IDで正しく取得できること()
    {
        // Arrange
        using var sut = CreateSut();
        var playlist = new UserPlaylist(PlaylistId.New(), "お気に入りジャズ");
        playlist.AddTrack(TrackId.New());
        playlist.AddTrack(TrackId.New());

        // Act
        await sut.SaveAsync(playlist);
        var actual = await sut.GetByIdAsync(playlist.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(playlist.Id, actual.Id);
        Assert.Equal("お気に入りジャズ", actual.Name);
        Assert.Equal(2, actual.TrackIds.Count);
    }

    /// <summary>
    /// 既存のプレイリストを変更して再度保存した際、更新内容が正しく反映されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveAsync_既存プレイリストの変更_更新内容が正しく反映されること()
    {
        // Arrange
        using var sut = CreateSut();
        var playlist = new UserPlaylist(PlaylistId.New(), "作業用BGM");
        await sut.SaveAsync(playlist);

        // Act
        playlist.Rename("深夜の作業用BGM");
        var trackId = TrackId.New();
        playlist.AddTrack(trackId);
        await sut.SaveAsync(playlist);

        var actual = await sut.GetByIdAsync(playlist.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("深夜の作業用BGM", actual.Name);
        Assert.Single(actual.TrackIds);
        Assert.Equal(trackId, actual.TrackIds[0]);
    }

    /// <summary>
    /// SaveAsyncにnull引数を渡した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveAsync_null引数指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        using var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SaveAsync(null!));
    }

    /// <summary>
    /// 複数のプレイリストを保存した後、GetAllAsyncですべてのプレイリストを取得できるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_複数プレイリスト保存後_全件のリストを返すこと()
    {
        // Arrange
        using var sut = CreateSut();
        var p1 = new UserPlaylist(PlaylistId.New(), "プレイリスト1");
        var p2 = new UserPlaylist(PlaylistId.New(), "プレイリスト2");
        var p3 = new UserPlaylist(PlaylistId.New(), "プレイリスト3");
        await sut.SaveAsync(p1);
        await sut.SaveAsync(p2);
        await sut.SaveAsync(p3);

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Equal(3, actual.Count);
        Assert.Contains(actual, p => p.Id == p1.Id && p.Name == "プレイリスト1");
        Assert.Contains(actual, p => p.Id == p2.Id && p.Name == "プレイリスト2");
        Assert.Contains(actual, p => p.Id == p3.Id && p.Name == "プレイリスト3");
    }

    /// <summary>
    /// 保存先ファイルが存在しない初期状態で、GetAllAsyncが空のリストを返すかを検証します。
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
    /// 登録済みのプレイリストを削除した際、GetByIdAsyncでnullとなり削除が反映されるかを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_存在するプレイリストID_正常に削除されGetByIdでnullとなること()
    {
        // Arrange
        using var sut = CreateSut();
        var playlist = new UserPlaylist(PlaylistId.New(), "削除予定プレイリスト");
        await sut.SaveAsync(playlist);

        // Act
        await sut.DeleteAsync(playlist.Id);
        var actual = await sut.GetByIdAsync(playlist.Id);
        var all = await sut.GetAllAsync();

        // Assert
        Assert.Null(actual);
        Assert.Empty(all);
    }

    /// <summary>
    /// 存在しないプレイリストIDを指定して削除した場合、例外をスローせず安全に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_存在しないプレイリストID_例外をスローせず安全に完了すること()
    {
        // Arrange
        using var sut = CreateSut();
        var nonExistentId = PlaylistId.New();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.DeleteAsync(nonExistentId));

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// インスタンスを破棄して別インスタンスで同一ファイルを再読み込みした際、永続化されたデータが正しく復元されるかを検証します。
    /// </summary>
    [Fact]
    public async Task 永続化検証_別インスタンスで再読込_ファイルからデータが正しく復元されること()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var trackId1 = TrackId.New();
        var trackId2 = TrackId.New();
        var playlist = new UserPlaylist(playlistId, "永続化テストプレイリスト", [trackId1, trackId2], DateTime.UtcNow, DateTime.UtcNow);

        using (var initialSut = CreateSut())
        {
            await initialSut.SaveAsync(playlist);
        }

        // Act
        using var newSut = CreateSut();
        var actual = await newSut.GetByIdAsync(playlistId);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(playlistId, actual.Id);
        Assert.Equal("永続化テストプレイリスト", actual.Name);
        Assert.Equal(2, actual.TrackIds.Count);
        Assert.Equal(trackId1, actual.TrackIds[0]);
        Assert.Equal(trackId2, actual.TrackIds[1]);
    }

    /// <summary>
    /// 破損したJSONファイルが存在する場合でも、例外をスローせず空の状態で安全に起動することを検証します。
    /// </summary>
    [Fact]
    public async Task 初期化_破損したJSONファイルが存在する場合_エラーにならず空状態で起動すること()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "{ broken json content: invalid format }");
        using var sut = CreateSut();

        // Act
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// キャンセル済みトークンを指定して呼び出した場合、OperationCanceledExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public async Task CancellationToken_キャンセル済みトークン指定_OperationCanceledExceptionをスローすること()
    {
        // Arrange
        using var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GetAllAsync(cts.Token));
    }
}
