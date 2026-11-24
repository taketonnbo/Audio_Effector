using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AudioEffector.Application;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure;
using AudioEffector.Infrastructure.Repository;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Repository;

/// <summary>
/// 全JSONリポジトリのプロセス間鮮度検知、並行書き込み整合性、ロックタイムアウト、およびデータディレクトリDI注入を検証するテストクラス
/// </summary>
public sealed class JsonRepositoryConcurrencyAndDiTests : IDisposable
{
    private readonly string _tempDirectory;

    public JsonRepositoryConcurrencyAndDiTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_RepoSync", Guid.NewGuid().ToString("N"));
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

    private static Track CreateSampleTrack(string title, string artist = "Artist", string album = "Album")
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create($@"C:\Music\{title}.flac"),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromSeconds(180),
            year: 2026,
            trackNumber: 1,
            bitrate: 1000,
            sampleRate: 48000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Electronic",
            isFavorite: false,
            isLossless: true,
            isHiRes: true);
    }

    /// <summary>
    /// 同一tracks.jsonを参照する2つのJsonTrackRepositoryインスタンス間で、一方の更新後に他方が自動リロードで最新データを取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonTrackRepository_2インスタンス間更新_他方が自動リロードで最新データを取得できること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "tracks.json");
        using var writer = new JsonTrackRepository(filePath);
        using var sut = new JsonTrackRepository(filePath);

        // 先にsutをロード済み状態にする
        Assert.Empty(await sut.GetAllAsync());

        // Act
        Track track = CreateSampleTrack("SyncedTrack");
        await writer.SaveAsync(track);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Single(actual);
        Assert.Equal(track.Id, actual[0].Id);
        Assert.Equal("SyncedTrack", actual[0].Title);
    }

    /// <summary>
    /// 同一playlists.jsonを参照する2つのJsonPlaylistRepositoryインスタンス間で、一方の更新後に他方が自動リロードで最新データを取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonPlaylistRepository_2インスタンス間更新_他方が自動リロードで最新データを取得できること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "playlists.json");
        using var writer = new JsonPlaylistRepository(filePath);
        using var sut = new JsonPlaylistRepository(filePath);

        Assert.Empty(await sut.GetAllAsync());

        // Act
        var playlist = new UserPlaylist(PlaylistId.New(), "CrossProcessPlaylist", [TrackId.New()], DateTime.UtcNow, DateTime.UtcNow);
        await writer.SaveAsync(playlist);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Single(actual);
        Assert.Equal("CrossProcessPlaylist", actual[0].Name);
    }

    /// <summary>
    /// 同一favorites.jsonを参照する2つのJsonFavoriteRepositoryインスタンス間で、一方の更新後に他方が自動リロードで最新データを取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonFavoriteRepository_2インスタンス間更新_他方が自動リロードで最新データを取得できること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "favorites.json");
        using var writer = new JsonFavoriteRepository(filePath);
        using var sut = new JsonFavoriteRepository(filePath);

        Assert.Empty(await sut.GetAllAsync());

        // Act
        var entry = FavoriteTrackEntry.FromTrack(CreateSampleTrack("FavTrack"));
        await writer.AddOrUpdateAsync(entry);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Single(actual);
        Assert.Equal("FavTrack", actual[0].Title);
    }

    /// <summary>
    /// 同一RecentTracks.jsonを参照する2つのJsonRecentTrackRepositoryインスタンス間で、一方の更新後に他方が自動リロードで最新データを取得できることを検証します。
    /// </summary>
    [Fact]
    public void JsonRecentTrackRepository_2インスタンス間更新_他方が自動リロードで最新データを取得できること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "RecentTracks.json");
        var writer = new JsonRecentTrackRepository(filePath);
        var sut = new JsonRecentTrackRepository(filePath);

        Assert.Empty(sut.Load());

        // Act
        var recent = new RecentTrackEntry(CreateSampleTrack("RecentTrack"), new DateTime(2026, 11, 24, 22, 0, 0));
        writer.Save([recent]);
        var actual = sut.Load();

        // Assert
        Assert.Single(actual);
        Assert.Equal("RecentTrack", actual[0].Title);
    }

    /// <summary>
    /// 同一settings.jsonを参照する2つのJsonSettingsRepositoryインスタンス間で、一方の更新後に他方が自動リロードで最新データを取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonSettingsRepository_2インスタンス間更新_他方が自動リロードで最新データを取得できること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "settings.json");
        using var writer = new JsonSettingsRepository(filePath);
        using var sut = new JsonSettingsRepository(filePath);

        Assert.Null(await sut.GetValueAsync("Theme"));

        // Act
        await writer.SetValueAsync("Theme", "Light");
        string? actual = await sut.GetValueAsync("Theme");

        // Assert
        Assert.Equal("Light", actual);
    }

    /// <summary>
    /// 外部からJSONファイルが削除された場合、読み込み時にキャッシュが空状態へ自動リロードされることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonTrackRepository_外部でファイル削除時_自動リロードで空状態が反映されること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "tracks_delete.json");
        using var sut = new JsonTrackRepository(filePath);
        await sut.SaveAsync(CreateSampleTrack("TempTrack"));
        Assert.Single(await sut.GetAllAsync());

        // Act
        File.Delete(filePath);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Empty(actual);
    }

    /// <summary>
    /// 複数インスタンスから同一tracks.jsonへ並行書き込みを行っても、ファイル破損やロストアップデートが発生しないことを検証します。
    /// </summary>
    [Fact]
    public async Task JsonTrackRepository_複数インスタンスからの並行書き込み_ファイル破損やロストアップデートが発生しないこと()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "concurrent_tracks.json");
        const int totalWrites = 16;
        Track[] tracks = Enumerable.Range(0, totalWrites)
            .Select(i => CreateSampleTrack($"ConcurrentTrack_{i:D2}"))
            .ToArray();

        // Act
        Task[] tasks = tracks.Select(async track =>
        {
            using var repo = new JsonTrackRepository(filePath);
            await repo.SaveAsync(track);
        }).ToArray();

        await Task.WhenAll(tasks);

        using var sut = new JsonTrackRepository(filePath);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Equal(totalWrites, actual.Count);
        foreach (var expected in tracks)
        {
            Assert.Contains(actual, t => t.Id == expected.Id && t.Title == expected.Title);
        }
    }

    /// <summary>
    /// 複数インスタンスから同一settings.jsonへ並行書き込みを行っても、全キーがロストアップデートなく保存されることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonSettingsRepository_複数インスタンスからの並行書き込み_全キーが欠損なく保存されること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "concurrent_settings.json");
        const int totalWrites = 16;

        // Act
        Task[] tasks = Enumerable.Range(0, totalWrites).Select(async i =>
        {
            using var repo = new JsonSettingsRepository(filePath);
            await repo.SetValueAsync($"Key_{i:D2}", $"Value_{i:D2}");
        }).ToArray();

        await Task.WhenAll(tasks);

        using var sut = new JsonSettingsRepository(filePath);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Equal(totalWrites, actual.Count);
        for (int i = 0; i < totalWrites; i++)
        {
            Assert.Equal($"Value_{i:D2}", actual[$"Key_{i:D2}"]);
        }
    }

    /// <summary>
    /// 複数インスタンスから同一favorites.jsonへ並行書き込みを行っても、全エントリが欠損なく保存されることを検証します。
    /// </summary>
    [Fact]
    public async Task JsonFavoriteRepository_複数インスタンスからの並行書き込み_全エントリが欠損なく保存されること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "concurrent_favorites.json");
        const int totalWrites = 12;
        FavoriteTrackEntry[] entries = Enumerable.Range(0, totalWrites)
            .Select(i => FavoriteTrackEntry.FromTrack(CreateSampleTrack($"FavConcurrent_{i:D2}")))
            .ToArray();

        // Act
        Task[] tasks = entries.Select(async entry =>
        {
            using var repo = new JsonFavoriteRepository(filePath);
            await repo.AddOrUpdateAsync(entry);
        }).ToArray();

        await Task.WhenAll(tasks);

        using var sut = new JsonFavoriteRepository(filePath);
        var actual = await sut.GetAllAsync();

        // Assert
        Assert.Equal(totalWrites, actual.Count);
    }

    /// <summary>
    /// ロックファイルが保持され続けた場合、リポジトリの保存処理がFileLockTimeoutExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public async Task JsonTrackRepository_ロックファイルが保持され続けた場合_FileLockTimeoutExceptionが送出されること()
    {
        // Arrange
        string filePath = Path.Combine(_tempDirectory, "locked_tracks.json");
        var shortTimeoutHelper = new JsonFileLockHelper(TimeSpan.FromMilliseconds(150));
        using IDisposable heldLock = await shortTimeoutHelper.AcquireLockAsync(filePath);
        using var sut = new JsonTrackRepository(filePath, shortTimeoutHelper);

        // Act & Assert
        await Assert.ThrowsAsync<FileLockTimeoutException>(() => sut.SaveAsync(CreateSampleTrack("BlockedTrack")));
    }

    /// <summary>
    /// ロックファイルが保持され続けた場合、SettingsApplicationServiceの非同期保存処理がFileLockTimeoutExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public async Task SettingsApplicationService_ロックファイルが保持され続けた場合_FileLockTimeoutExceptionが送出されること()
    {
        // Arrange
        var dataDirectoryProvider = new DataDirectoryProvider(_tempDirectory);
        var shortTimeoutHelper = new JsonFileLockHelper(TimeSpan.FromMilliseconds(150));
        string settingsPath = dataDirectoryProvider.GetFilePath("settings.json");
        using var settingsRepo = new JsonSettingsRepository(settingsPath, shortTimeoutHelper);
        using var sut = new SettingsApplicationService(settingsRepo, dataDirectoryProvider, shortTimeoutHelper);
        using IDisposable heldLock = await shortTimeoutHelper.AcquireLockAsync(settingsPath);

        // Act & Assert
        await Assert.ThrowsAsync<FileLockTimeoutException>(() => sut.SaveSettingsAsync(new AppSettings()));
    }

    /// <summary>旧形式の設定を読み込み、両APIの保存後にも設定とプリセットを復元できることを検証します</summary>
    [Theory]
    [InlineData("""{"Volume":0.35,"StartMinimized":true}""", false)]
    [InlineData("""{"CustomPreset":"legacy"}""", true)]
    public async Task 設定の交互更新_旧形式からの移行後も両方の値を保持する(string legacyJson, bool hasPreset)
    {
        // Arrange
        var directory = new DataDirectoryProvider(_tempDirectory);
        var path = directory.GetFilePath("settings.json");
        await File.WriteAllTextAsync(path, legacyJson);
        using var repository = new JsonSettingsRepository(path);
        using var sut = new SettingsApplicationService(repository, directory, JsonFileLockHelper.Default);

        // Act
        if (hasPreset)
        {
            sut.SaveSettings(new AppSettings { Volume = 0.35f });
            Assert.Equal("legacy", await sut.GetSettingAsync<string>("CustomPreset"));
        }
        await sut.SaveSettingAsync("CustomPreset", "updated");
        using var secondRepository = new JsonSettingsRepository(path);
        using var second = new SettingsApplicationService(secondRepository, directory, JsonFileLockHelper.Default);
        await Task.WhenAll(
            Task.Run(() => sut.UpdateSettings(settings => settings.AutoStart = true)),
            second.UpdateSettingsAsync(settings => settings.AudioBufferSizeMs = 240));

        // Assert
        var saved = await second.LoadSettingsAsync();
        Assert.Equal(0.35f, saved.Volume);
        Assert.Equal(!hasPreset, saved.StartMinimized);
        Assert.True(saved.AutoStart);
        Assert.Equal(240, saved.AudioBufferSizeMs);
        Assert.Equal("updated", await second.GetSettingAsync<string>("CustomPreset"));
    }

    /// <summary>同一プレイリストへの並行追加で両方の曲を保持することを検証します</summary>
    [Fact]
    public async Task プレイリスト_別インスタンスから同時に追加した両方の曲を保持する()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "playlists.json");
        using var first = new JsonPlaylistRepository(path);
        using var second = new JsonPlaylistRepository(path);
        var playlist = new UserPlaylist(PlaylistId.New(), "Favorites");
        await first.SaveAsync(playlist);
        var firstTrack = TrackId.New();
        var secondTrack = TrackId.New();
        var firstService = new PlaylistApplicationService(first, Moq.Mock.Of<ITrackRepository>(), new InMemoryEventBus());
        var secondService = new PlaylistApplicationService(second, Moq.Mock.Of<ITrackRepository>(), new InMemoryEventBus());

        // Act
        await Task.WhenAll(firstService.AddTrackAsync(playlist.Id, firstTrack), secondService.AddTrackAsync(playlist.Id, secondTrack));

        // Assert
        using var reader = new JsonPlaylistRepository(path);
        var saved = await reader.GetByIdAsync(playlist.Id);
        Assert.Equal(2, saved!.TrackIds.Count);
        Assert.Contains(firstTrack, saved.TrackIds);
        Assert.Contains(secondTrack, saved.TrackIds);
    }

    /// <summary>既存ライブラリの再読込に失敗した場合に元ファイルが保持されることを検証します</summary>
    [Theory]
    [InlineData("[")]
    [InlineData("null")]
    [InlineData("""[{"FilePath":""}]""")]
    public async Task ライブラリ_解析または復元に失敗した場合に元ファイルを保持する(string invalid)
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "tracks.json");
        using var sut = new JsonTrackRepository(path);
        await sut.SaveAsync(CreateSampleTrack("First"));
        await File.WriteAllTextAsync(path, invalid);

        // Act
        var error = await Record.ExceptionAsync(() => sut.SaveAsync(CreateSampleTrack("Second")));

        // Assert
        Assert.NotNull(error);
        Assert.Equal(invalid, await File.ReadAllTextAsync(path));
    }

    /// <summary>
    /// AddInfrastructureServicesにカスタムdataDirectoryを指定した場合、全リポジトリおよびSettingsApplicationServiceが指定ディレクトリ配下へ読み書きすることを検証します。
    /// </summary>
    [Fact]
    public async Task AddInfrastructureServices_カスタムdataDirectory指定時_全リポジトリと設定サービスが指定ディレクトリ配下へ読み書きすること()
    {
        // Arrange
        string customDir = Path.Combine(_tempDirectory, "custom_data");
        var services = new ServiceCollection();
        services.AddInfrastructureServices(customDir);
        services.AddApplicationServices();

        using ServiceProvider provider = services.BuildServiceProvider();
        var directoryProvider = provider.GetRequiredService<IDataDirectoryProvider>();
        var trackRepo = provider.GetRequiredService<ITrackRepository>();
        var playlistRepo = provider.GetRequiredService<IPlaylistRepository>();
        var favoriteRepo = provider.GetRequiredService<IFavoriteRepository>();
        var recentRepo = provider.GetRequiredService<IRecentTrackRepository>();
        var settingsService = provider.GetRequiredService<SettingsApplicationService>();

        // Act
        Track sampleTrack = CreateSampleTrack("DiTrack");
        await trackRepo.SaveAsync(sampleTrack);
        await playlistRepo.SaveAsync(new UserPlaylist(PlaylistId.New(), "DiPlaylist", [sampleTrack.Id], DateTime.UtcNow, DateTime.UtcNow));
        await favoriteRepo.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(sampleTrack));
        recentRepo.Save([new RecentTrackEntry(sampleTrack, DateTime.UtcNow)]);

        var appSettings = new AppSettings { Volume = 0.77f };
        await settingsService.SaveSettingsAsync(appSettings);
        AppSettings reloadedSettings = await settingsService.LoadSettingsAsync();

        // Assert
        Assert.True(directoryProvider.IsCustomDirectory);
        Assert.Equal(Path.GetFullPath(customDir), directoryProvider.DataDirectory);
        Assert.True(File.Exists(Path.Combine(customDir, "tracks.json")));
        Assert.True(File.Exists(Path.Combine(customDir, "playlists.json")));
        Assert.True(File.Exists(Path.Combine(customDir, "favorites.json")));
        Assert.True(File.Exists(Path.Combine(customDir, "RecentTracks.json")));
        Assert.True(File.Exists(Path.Combine(customDir, "settings.json")));
        Assert.Equal(0.77f, reloadedSettings.Volume, precision: 3);
    }

    /// <summary>
    /// AddInfrastructureServicesでdataDirectoryを未指定とした場合、既定のAppData配下ディレクトリが解決されることを検証します。
    /// </summary>
    [Fact]
    public void AddInfrastructureServices_dataDirectory未指定時_既定のAppDataディレクトリが解決されること()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructureServices();

        // Act
        using ServiceProvider provider = services.BuildServiceProvider();
        var sut = provider.GetRequiredService<IDataDirectoryProvider>();

        // Assert
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioEffector");
        Assert.False(sut.IsCustomDirectory);
        Assert.Equal(expected, sut.DataDirectory);
    }
}
