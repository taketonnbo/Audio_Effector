using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

/// <summary>
/// 各 JSON リポジトリが <see cref="IDataDirectoryProvider"/> の動的ディレクトリ切替へ即座に追従し、旧ディレクトリのキャッシュを返却しないことを検証するテストクラス
/// </summary>
public sealed class JsonRepositoryDynamicPathTests : IDisposable
{
    private readonly string _tempRoot;

    public JsonRepositoryDynamicPathTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"AudioEffector_RepoDynamic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// JsonTrackRepository が DataDirectoryProvider 切替後に新ディレクトリの tracks.json を読み書きすることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonTrackRepository_データディレクトリ切替後_キャッシュ混在なく新ディレクトリのデータを読み書きすること()
    {
        // Arrange
        string dirA = Path.Combine(_tempRoot, "TrackA");
        string dirB = Path.Combine(_tempRoot, "TrackB");
        var provider = new DataDirectoryProvider(dirA);
        using var sut = new JsonTrackRepository(provider);

        var trackA = new Track(TrackId.From(Guid.NewGuid()), AudioPath.Create(@"C:\Music\a.mp3"), "Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3));
        var trackB = new Track(TrackId.From(Guid.NewGuid()), AudioPath.Create(@"C:\Music\b.mp3"), "Song B", "Artist B", "Album B", TimeSpan.FromMinutes(4));

        // Act
        await sut.SaveAsync(trackA);
        var loadedA = await sut.GetAllAsync();

        provider.UpdateDataDirectory(dirB);
        var loadedBEmpty = await sut.GetAllAsync();
        await sut.SaveAsync(trackB);
        var loadedB = await sut.GetAllAsync();

        // Assert
        Assert.Single(loadedA);
        Assert.Equal("Song A", loadedA[0].Title);
        Assert.Empty(loadedBEmpty);
        Assert.Single(loadedB);
        Assert.Equal("Song B", loadedB[0].Title);
        Assert.True(File.Exists(Path.Combine(dirA, "tracks.json")));
        Assert.True(File.Exists(Path.Combine(dirB, "tracks.json")));
    }

    /// <summary>
    /// JsonPlaylistRepository が DataDirectoryProvider 切替後に新ディレクトリの playlists.json を読み書きすることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonPlaylistRepository_データディレクトリ切替後_新ディレクトリのプレイリストを読み書きすること()
    {
        // Arrange
        string dirA = Path.Combine(_tempRoot, "PlaylistA");
        string dirB = Path.Combine(_tempRoot, "PlaylistB");
        var provider = new DataDirectoryProvider(dirA);
        using var sut = new JsonPlaylistRepository(provider);

        var playlistA = new UserPlaylist(PlaylistId.From(Guid.NewGuid()), "List A");
        var playlistB = new UserPlaylist(PlaylistId.From(Guid.NewGuid()), "List B");

        // Act
        await sut.SaveAsync(playlistA);
        var loadedA = await sut.GetAllAsync();

        provider.UpdateDataDirectory(dirB);
        var loadedBEmpty = await sut.GetAllAsync();
        await sut.SaveAsync(playlistB);
        var loadedB = await sut.GetAllAsync();

        // Assert
        Assert.Single(loadedA);
        Assert.Equal("List A", loadedA[0].Name);
        Assert.Empty(loadedBEmpty);
        Assert.Single(loadedB);
        Assert.Equal("List B", loadedB[0].Name);
    }

    /// <summary>
    /// JsonFavoriteRepository が DataDirectoryProvider 切替後に新ディレクトリの favorites.json を読み書きすることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonFavoriteRepository_データディレクトリ切替後_新ディレクトリのお気に入りを読み書きすること()
    {
        // Arrange
        string dirA = Path.Combine(_tempRoot, "FavA");
        string dirB = Path.Combine(_tempRoot, "FavB");
        var provider = new DataDirectoryProvider(dirA);
        using var sut = new JsonFavoriteRepository(provider);

        var favA = new FavoriteTrackEntry("Fav Song A", "Artist A", "Album A", 1, 180, @"C:\Music\fa.mp3", DateTime.UtcNow);
        var favB = new FavoriteTrackEntry("Fav Song B", "Artist B", "Album B", 2, 200, @"C:\Music\fb.mp3", DateTime.UtcNow);

        // Act
        await sut.AddOrUpdateAsync(favA);
        var loadedA = await sut.GetAllAsync();

        provider.UpdateDataDirectory(dirB);
        var loadedBEmpty = await sut.GetAllAsync();
        await sut.AddOrUpdateAsync(favB);
        var loadedB = await sut.GetAllAsync();

        // Assert
        Assert.Single(loadedA);
        Assert.Equal("Fav Song A", loadedA[0].Title);
        Assert.Empty(loadedBEmpty);
        Assert.Single(loadedB);
        Assert.Equal("Fav Song B", loadedB[0].Title);
    }

    /// <summary>
    /// JsonRecentTrackRepository が DataDirectoryProvider 切替後に新ディレクトリの RecentTracks.json を読み書きすることを検証します。
    /// </summary>
    [Fact]
    public void JsonRecentTrackRepository_データディレクトリ切替後_新ディレクトリの再生履歴を読み書きすること()
    {
        // Arrange
        string dirA = Path.Combine(_tempRoot, "RecentA");
        string dirB = Path.Combine(_tempRoot, "RecentB");
        var provider = new DataDirectoryProvider(dirA);
        var sut = new JsonRecentTrackRepository(provider);

        var entryA = new RecentTrackEntry(new Track { FilePath = @"C:\Music\ra.mp3", Title = "Recent A" }, DateTime.UtcNow);
        var entryB = new RecentTrackEntry(new Track { FilePath = @"C:\Music\rb.mp3", Title = "Recent B" }, DateTime.UtcNow);

        // Act
        sut.Save(new List<RecentTrackEntry> { entryA });
        var loadedA = sut.Load();

        provider.UpdateDataDirectory(dirB);
        var loadedBEmpty = sut.Load();
        sut.Save(new List<RecentTrackEntry> { entryB });
        var loadedB = sut.Load();

        // Assert
        Assert.Single(loadedA);
        Assert.Equal("Recent A", loadedA[0].Title);
        Assert.Empty(loadedBEmpty);
        Assert.Single(loadedB);
        Assert.Equal("Recent B", loadedB[0].Title);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // 一時ディレクトリ削除失敗はテスト結果へ影響させない
            }
        }
    }
}
