using System.IO;
using System.Text.Json;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Repository;
using AudioEffector.McpServer.Protocol;
using AudioEffector.McpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;

namespace AudioEffector.McpServer.Tests.Tools;

public sealed class PlaylistToolsTests
{
    /// <summary>
    /// search_playlists ツールでキーワード検索と includeTracks=true を指定した場合、一致プレイリストと収録トラック詳細が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_キーワード検索とincludeTracks有効_プレイリストと収録トラック一覧を返す()
    {
        // Arrange
        var track1 = CreateTrack("Blue Note", "Miles Trio", "Midnight Sessions", @"C:\Music\blue.flac");
        var track2 = CreateTrack("Soft Rain", "Miles Trio", "Midnight Sessions", @"C:\Music\rain.flac");
        var createdAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 11, 20, 15, 30, 0, DateTimeKind.Utc);

        var playlist1 = new UserPlaylist(PlaylistId.New(), "Night Jazz", [track2.Id, track1.Id], createdAt, updatedAt);
        var playlist2 = new UserPlaylist(PlaylistId.New(), "Morning Rock", [track1.Id], createdAt, updatedAt);

        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        playlistRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([playlist1, playlist2]);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, track2]);

        // Act
        CallToolResult result = await sut.SearchPlaylistsAsync(keyword: "  jazz ", includeTracks: true);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("count").GetInt32());
        Assert.Equal(0, data.GetProperty("offset").GetInt32());
        Assert.Equal(PlaylistSearchQuery.DEFAULT_LIMIT, data.GetProperty("limit").GetInt32());

        JsonElement playlistJson = Assert.Single(data.GetProperty("playlists").EnumerateArray());
        Assert.Equal(playlist1.Id.Value, playlistJson.GetProperty("id").GetGuid());
        Assert.Equal("Night Jazz", playlistJson.GetProperty("name").GetString());
        Assert.Equal(2, playlistJson.GetProperty("trackCount").GetInt32());
        Assert.Equal(createdAt, playlistJson.GetProperty("createdAt").GetDateTime().ToUniversalTime());
        Assert.Equal(updatedAt, playlistJson.GetProperty("updatedAt").GetDateTime().ToUniversalTime());

        List<JsonElement> tracks = playlistJson.GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal(2, tracks.Count);
        Assert.Equal(track2.Id.Value, tracks[0].GetProperty("id").GetGuid());
        Assert.Equal("Soft Rain", tracks[0].GetProperty("title").GetString());
        Assert.Equal(track1.Id.Value, tracks[1].GetProperty("id").GetGuid());
        Assert.Equal("Blue Note", tracks[1].GetProperty("title").GetString());
    }

    /// <summary>
    /// search_playlists ツールで includeTracks=false（既定値）の場合、tracks プロパティが省略され ITrackRepository へアクセスしないことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_includeTracks無効_tracksプロパティが省略されトラックリポジトリを参照しない()
    {
        // Arrange
        var playlist = new UserPlaylist(PlaylistId.New(), "Focus", [TrackId.New()]);
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        playlistRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([playlist]);

        // Act
        CallToolResult result = await sut.SearchPlaylistsAsync();

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        JsonElement playlistJson = Assert.Single(doc.RootElement.GetProperty("data").GetProperty("playlists").EnumerateArray());
        Assert.False(playlistJson.TryGetProperty("tracks", out _));
        trackRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// search_playlists ツールで検索結果が 0 件の場合、空配列と totalCount=0 の正常応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_検索結果0件_空配列とtotalCount0の正常応答を返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);
        playlistRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserPlaylist>());

        // Act
        CallToolResult result = await sut.SearchPlaylistsAsync(keyword: "存在しないプレイリスト", includeTracks: true);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("count").GetInt32());
        Assert.Empty(data.GetProperty("playlists").EnumerateArray());
    }

    /// <summary>
    /// search_playlists ツールで limit または offset に範囲外の値を指定した場合、INVALID_PARAMETER エラーを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0, "limit")]
    [InlineData(201, 0, "limit")]
    [InlineData(50, -1, "offset")]
    public async Task SearchPlaylistsAsync_範囲外パラメータ指定_INVALID_PARAMETERエラーを返す(
        int limit,
        int offset,
        string expectedParam)
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);

        // Act
        CallToolResult result = await sut.SearchPlaylistsAsync(limit: limit, offset: offset);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, error.GetProperty("code").GetString());
        Assert.Equal(expectedParam, error.GetProperty("details").GetProperty("parameter").GetString());

        playlistRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// search_playlists 実行中に未捕捉例外が発生した場合、INTERNAL_ERROR に変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_リポジトリ例外発生時_INTERNAL_ERRORを返し内部詳細を露出しない()
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out Mock<ILogger<PlaylistTools>> loggerMock);
        playlistRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET_PLAYLIST_ERROR"));

        // Act
        CallToolResult result = await sut.SearchPlaylistsAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SECRET_PLAYLIST_ERROR", text, StringComparison.Ordinal);
        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, doc.RootElement.GetProperty("error").GetProperty("code").GetString());

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// search_playlists 実行中にファイルロックタイムアウトが発生した場合、CONFLICT_LOCK_TIMEOUT を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_ファイルロックタイムアウト発生時_CONFLICT_LOCK_TIMEOUTを返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);
        playlistRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileLockTimeoutException(@"C:\SecretDir\playlists.json", TimeSpan.FromSeconds(5)));

        // Act
        CallToolResult result = await sut.SearchPlaylistsAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SecretDir", text, StringComparison.Ordinal);
        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.Equal(McpErrorCodes.CONFLICT_LOCK_TIMEOUT, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// 実 JsonPlaylistRepository / JsonTrackRepository を使用し、別インスタンス経由で追加されたプレイリストが再起動なしで検索に反映されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_実リポジトリで外部更新発生時_最新プレイリストを反映して検索できる()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistToolsFreshness_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");

            using var mcpPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            using var mcpTrackRepo = new JsonTrackRepository(tracksPath);
            var playlistService = new PlaylistApplicationService(
                mcpPlaylistRepo,
                mcpTrackRepo,
                new Mock<IEventBus>().Object);
            var sut = new PlaylistTools(playlistService, new Mock<ILogger<PlaylistTools>>().Object);

            // 初回検索（空）
            CallToolResult initialResult = await sut.SearchPlaylistsAsync(keyword: "Drive");
            using (JsonDocument initialDoc = JsonDocument.Parse(GetSingleText(initialResult)))
            {
                Assert.Equal(0, initialDoc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
            }

            // 別プロセス（WPF本体相当）による更新
            using var wpfPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            using var wpfTrackRepo = new JsonTrackRepository(tracksPath);
            var track = CreateTrack("Highway Star", "Deep Band", "Machine", Path.Combine(tempDir, "highway.flac"));
            await wpfTrackRepo.SaveAsync(track);
            var playlist = new UserPlaylist(PlaylistId.New(), "Night Drive", [track.Id]);
            await wpfPlaylistRepo.SaveAsync(playlist);

            // Act: MCP 側インスタンスを再生成せずに検索
            CallToolResult updatedResult = await sut.SearchPlaylistsAsync(keyword: "drive", includeTracks: true);

            // Assert
            Assert.False(updatedResult.IsError ?? false);
            using JsonDocument updatedDoc = JsonDocument.Parse(GetSingleText(updatedResult));
            JsonElement data = updatedDoc.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
            JsonElement playlistJson = Assert.Single(data.GetProperty("playlists").EnumerateArray());
            Assert.Equal(playlist.Id.Value, playlistJson.GetProperty("id").GetGuid());
            Assert.Equal("Night Drive", playlistJson.GetProperty("name").GetString());
            JsonElement trackJson = Assert.Single(playlistJson.GetProperty("tracks").EnumerateArray());
            Assert.Equal("Highway Star", trackJson.GetProperty("title").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// WPF 側の保存経路（PlaylistApplicationService.SavePlaylists）および旧 WPF 形式 JSON（Name / TrackPaths のみ）から、別インスタンスの search_playlists で収録順・ID・お気に入り状態を正しく取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_WPF保存経路と旧形式JSONおよび初回お気に入り照合_ID維持と最新isFavoriteで検索できる()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistWpfCompat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            string favoritesPath = Path.Combine(tempDir, "favorites.json");

            string path1 = Path.Combine(tempDir, "song1.flac");
            string path2 = Path.Combine(tempDir, "song2.flac");
            var track1 = CreateTrack("First Song", "Artist A", "Album A", path1);
            var track2 = CreateTrack("Second Song", "Artist A", "Album A", path2);

            // tracks.json 側では IsFavorite=false、favorites.json に track2 のみ登録
            using (var seedTrackRepo = new JsonTrackRepository(tracksPath))
            using (var seedFavRepo = new JsonFavoriteRepository(favoritesPath))
            {
                await seedTrackRepo.SaveRangeAsync([track1, track2]);
                await seedFavRepo.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(track2));
            }

            // 1) 旧 WPF 形式（Id 欠落・Name / TrackPaths のみ）の playlists.json を作成
            string legacyJson = JsonSerializer.Serialize(new[]
            {
                new { Name = "WPF Legacy List", TrackPaths = new[] { path2, path1 } },
            });
            await File.WriteAllTextAsync(playlistsPath, legacyJson);

            // Act 1: サーバ起動直後（search_albums 未実行）の初回 search_playlists(includeTracks: true)
            Guid legacyAssignedId;
            using (var mcpPlaylistRepo1 = new JsonPlaylistRepository(playlistsPath))
            using (var mcpTrackRepo1 = new JsonTrackRepository(tracksPath))
            using (var mcpFavRepo1 = new JsonFavoriteRepository(favoritesPath))
            {
                var service1 = new PlaylistApplicationService(mcpPlaylistRepo1, mcpTrackRepo1, new Mock<IEventBus>().Object, mcpFavRepo1);
                var sut1 = new PlaylistTools(service1, new Mock<ILogger<PlaylistTools>>().Object);

                CallToolResult res1 = await sut1.SearchPlaylistsAsync(keyword: "WPF Legacy", includeTracks: true);
                Assert.False(res1.IsError ?? false);
                using JsonDocument doc1 = JsonDocument.Parse(GetSingleText(res1));
                JsonElement playlistJson = Assert.Single(doc1.RootElement.GetProperty("data").GetProperty("playlists").EnumerateArray());
                legacyAssignedId = playlistJson.GetProperty("id").GetGuid();
                Assert.NotEqual(Guid.Empty, legacyAssignedId);
                Assert.Equal(2, playlistJson.GetProperty("trackCount").GetInt32());

                List<JsonElement> tracks = playlistJson.GetProperty("tracks").EnumerateArray().ToList();
                Assert.Equal(2, tracks.Count);
                Assert.Equal("Second Song", tracks[0].GetProperty("title").GetString());
                Assert.True(tracks[0].GetProperty("isFavorite").GetBoolean());
                Assert.Equal("First Song", tracks[1].GetProperty("title").GetString());
                Assert.False(tracks[1].GetProperty("isFavorite").GetBoolean());
            }

            // Act 2: WPF 側 LoadPlaylists -> SavePlaylists 経由で再保存後、別インスタンス MCP から同一 ID で検索できること
            using (var wpfPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            using (var wpfTrackRepo = new JsonTrackRepository(tracksPath))
            {
                var wpfService = new PlaylistApplicationService(wpfPlaylistRepo, wpfTrackRepo, new Mock<IEventBus>().Object);
                List<UserPlaylist> loaded = wpfService.LoadPlaylists();
                UserPlaylist single = Assert.Single(loaded);
                Assert.Equal(legacyAssignedId, single.Id.Value);
                wpfService.SavePlaylists(loaded);
            }

            using (var mcpPlaylistRepo2 = new JsonPlaylistRepository(playlistsPath))
            using (var mcpTrackRepo2 = new JsonTrackRepository(tracksPath))
            using (var mcpFavRepo2 = new JsonFavoriteRepository(favoritesPath))
            {
                var service2 = new PlaylistApplicationService(mcpPlaylistRepo2, mcpTrackRepo2, new Mock<IEventBus>().Object, mcpFavRepo2);
                var sut2 = new PlaylistTools(service2, new Mock<ILogger<PlaylistTools>>().Object);

                CallToolResult res2 = await sut2.SearchPlaylistsAsync(includeTracks: true);
                Assert.False(res2.IsError ?? false);
                using JsonDocument doc2 = JsonDocument.Parse(GetSingleText(res2));
                JsonElement playlistJson2 = Assert.Single(doc2.RootElement.GetProperty("data").GetProperty("playlists").EnumerateArray());

                // Assert
                Assert.Equal(legacyAssignedId, playlistJson2.GetProperty("id").GetGuid());
                List<JsonElement> tracks2 = playlistJson2.GetProperty("tracks").EnumerateArray().ToList();
                Assert.Equal(["Second Song", "First Song"], tracks2.Select(t => t.GetProperty("title").GetString()).ToArray());
                Assert.True(tracks2[0].GetProperty("isFavorite").GetBoolean());
                Assert.False(tracks2[1].GetProperty("isFavorite").GetBoolean());
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// create_playlist ツールでプレイリスト名のみを指定した場合、空のプレイリストが作成され統一応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_名前のみ指定_空のプレイリストを作成し正常応答を返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);

        // Act
        CallToolResult result = await sut.CreatePlaylistAsync(name: "  朝の集中BGM  ");

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("addedCount").GetInt32());
        Assert.Empty(data.GetProperty("skippedItems").EnumerateArray());

        JsonElement playlistJson = data.GetProperty("playlist");
        Assert.NotEqual(Guid.Empty, playlistJson.GetProperty("id").GetGuid());
        Assert.Equal("朝の集中BGM", playlistJson.GetProperty("name").GetString());
        Assert.Equal(0, playlistJson.GetProperty("trackCount").GetInt32());
        Assert.Empty(playlistJson.GetProperty("tracks").EnumerateArray());

        playlistRepoMock.Verify(
            r => r.SaveAsync(It.Is<UserPlaylist>(p => p.Name == "朝の集中BGM" && p.TrackCount == 0), It.IsAny<CancellationToken>()),
            Times.Once);
        trackRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// create_playlist ツールで trackIds を指定した場合、指定順に収録されたプレイリストが作成されることを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_trackIds指定_指定順で収録されたプレイリストを作成する()
    {
        // Arrange
        var track1 = CreateTrack("Blue Note", "Miles Trio", "Midnight Sessions", @"C:\Music\blue.flac");
        var track2 = CreateTrack("Soft Rain", "Miles Trio", "Midnight Sessions", @"C:\Music\rain.flac");
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, track2]);

        // Act
        CallToolResult result = await sut.CreatePlaylistAsync(
            name: "Night Jazz",
            trackIds: [track2.Id.ToString(), track1.Id.ToString()]);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("addedCount").GetInt32());
        Assert.Empty(data.GetProperty("skippedItems").EnumerateArray());

        JsonElement playlistJson = data.GetProperty("playlist");
        Assert.Equal("Night Jazz", playlistJson.GetProperty("name").GetString());
        Assert.Equal(2, playlistJson.GetProperty("trackCount").GetInt32());

        List<JsonElement> tracks = playlistJson.GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal(2, tracks.Count);
        Assert.Equal(track2.Id.Value, tracks[0].GetProperty("id").GetGuid());
        Assert.Equal("Soft Rain", tracks[0].GetProperty("title").GetString());
        Assert.Equal(track1.Id.Value, tracks[1].GetProperty("id").GetGuid());
        Assert.Equal("Blue Note", tracks[1].GetProperty("title").GetString());

        playlistRepoMock.Verify(
            r => r.SaveAsync(
                It.Is<UserPlaylist>(p =>
                    p.Name == "Night Jazz"
                    && p.TrackIds.Count == 2
                    && p.TrackIds[0] == track2.Id
                    && p.TrackIds[1] == track1.Id
                    && p.TrackPaths.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// create_playlist ツールで filePaths 指定および trackIds との重複指定を行った場合、初出順で登録し重複分を skippedItems へ返すことを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_filePaths指定と重複混在_初出順で登録し重複をskippedItemsへ返す()
    {
        // Arrange
        var track1 = CreateTrack("Song A", "Artist", "Album", @"C:\Music\songA.flac");
        var track2 = CreateTrack("Song B", "Artist", "Album", @"C:\Music\songB.flac");
        var sut = CreateSut(out _, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, track2]);

        // Act
        CallToolResult result = await sut.CreatePlaylistAsync(
            name: "Mixed Input",
            trackIds: [track1.Id.ToString()],
            filePaths: [@"c:\music\songa.flac", @"C:\Music\songB.flac"]);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("addedCount").GetInt32());

        JsonElement skipped = Assert.Single(data.GetProperty("skippedItems").EnumerateArray());
        Assert.Equal(@"c:\music\songa.flac", skipped.GetString());

        List<JsonElement> tracks = data.GetProperty("playlist").GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal([track1.Id.Value, track2.Id.Value], tracks.Select(t => t.GetProperty("id").GetGuid()).ToArray());
    }

    /// <summary>
    /// create_playlist ツールで空名・空白名・最大長超過を指定した場合、INVALID_PARAMETER エラーを返しリポジトリへアクセスしないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePlaylistAsync_不正な名前指定_INVALID_PARAMETERを返しリポジトリを呼ばない(string invalidName)
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);

        // Act
        CallToolResult emptyResult = await sut.CreatePlaylistAsync(name: invalidName);
        CallToolResult tooLongResult = await sut.CreatePlaylistAsync(
            name: new string('X', PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH + 1));

        // Assert
        Assert.True(emptyResult.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(emptyResult)))
        {
            JsonElement error = doc.RootElement.GetProperty("error");
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, error.GetProperty("code").GetString());
            Assert.Equal("name", error.GetProperty("details").GetProperty("parameter").GetString());
        }

        Assert.True(tooLongResult.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(tooLongResult)))
        {
            JsonElement error = doc.RootElement.GetProperty("error");
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, error.GetProperty("code").GetString());
            Assert.Equal("name", error.GetProperty("details").GetProperty("parameter").GetString());
            Assert.Equal(
                PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH,
                error.GetProperty("details").GetProperty("maxLength").GetInt32());
        }

        playlistRepoMock.Verify(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// create_playlist ツールで不正 GUID や相対パスを指定した場合、INVALID_PARAMETER エラーを返し保存しないことを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_不正GUIDまたは相対パス指定_INVALID_PARAMETERを返し保存しない()
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);

        // Act
        CallToolResult badGuidResult = await sut.CreatePlaylistAsync(name: "Test", trackIds: ["not-a-guid"]);
        CallToolResult emptyGuidResult = await sut.CreatePlaylistAsync(name: "Test", trackIds: [Guid.Empty.ToString()]);
        CallToolResult relativePathResult = await sut.CreatePlaylistAsync(name: "Test", filePaths: [@"relative\song.flac"]);
        CallToolResult emptyPathResult = await sut.CreatePlaylistAsync(name: "Test", filePaths: ["   "]);

        // Assert
        foreach (CallToolResult res in new[] { badGuidResult, emptyGuidResult })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("trackIds", doc.RootElement.GetProperty("error").GetProperty("details").GetProperty("parameter").GetString());
        }

        foreach (CallToolResult res in new[] { relativePathResult, emptyPathResult })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("filePaths", doc.RootElement.GetProperty("error").GetProperty("details").GetProperty("parameter").GetString());
        }

        playlistRepoMock.Verify(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// create_playlist ツールでライブラリに存在しないトラック ID またはファイルパスを指定した場合、NOT_FOUND エラーを返し不完全なプレイリストを保存しないことを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_存在しないトラック指定_NOT_FOUNDを返し不完全プレイリストを保存しない()
    {
        // Arrange
        var existingTrack = CreateTrack("Existing", "Artist", "Album", @"C:\Music\existing.flac");
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingTrack]);

        // Act
        CallToolResult missingIdResult = await sut.CreatePlaylistAsync(
            name: "IncompleteById",
            trackIds: [existingTrack.Id.ToString(), Guid.NewGuid().ToString()]);
        CallToolResult missingPathResult = await sut.CreatePlaylistAsync(
            name: "IncompleteByPath",
            filePaths: [@"C:\Music\not_in_library.flac"]);

        // Assert
        foreach (CallToolResult res in new[] { missingIdResult, missingPathResult })
        {
            Assert.True(res.IsError);
            string text = GetSingleText(res);
            Assert.DoesNotContain("not_in_library.flac", text, StringComparison.Ordinal);
            using JsonDocument doc = JsonDocument.Parse(text);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(McpErrorCodes.NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        playlistRepoMock.Verify(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// create_playlist 実行中にリポジトリ例外やファイルロックタイムアウトが発生した場合、適切なエラーコードへ変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_リポジトリ例外またはロックタイムアウト発生時_適切なエラーコードを返し内部詳細を露出しない()
    {
        // Arrange
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out Mock<ILogger<PlaylistTools>> loggerMock);
        playlistRepoMock
            .SetupSequence(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileLockTimeoutException(@"C:\SecretDir\playlists.json", TimeSpan.FromSeconds(5)))
            .ThrowsAsync(new InvalidOperationException("SECRET_CREATE_ERROR"));

        // Act
        CallToolResult timeoutResult = await sut.CreatePlaylistAsync(name: "TimeoutTest");
        CallToolResult internalResult = await sut.CreatePlaylistAsync(name: "InternalTest");

        // Assert
        Assert.True(timeoutResult.IsError);
        string timeoutText = GetSingleText(timeoutResult);
        Assert.DoesNotContain("SecretDir", timeoutText, StringComparison.Ordinal);
        using (JsonDocument doc = JsonDocument.Parse(timeoutText))
        {
            Assert.Equal(McpErrorCodes.CONFLICT_LOCK_TIMEOUT, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Assert.True(internalResult.IsError);
        string internalText = GetSingleText(internalResult);
        Assert.DoesNotContain("SECRET_CREATE_ERROR", internalText, StringComparison.Ordinal);
        using (JsonDocument doc = JsonDocument.Parse(internalText))
        {
            Assert.Equal(McpErrorCodes.INTERNAL_ERROR, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// 実 JsonPlaylistRepository を用いて create_playlist で作成したプレイリストが playlists.json へ永続化され、別インスタンス（WPF 相当）および search_playlists から取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_実リポジトリ使用時_永続化され別インスタンスとsearch_playlistsから取得できる()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorCreatePlaylist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            string favoritesPath = Path.Combine(tempDir, "favorites.json");

            var track1 = CreateTrack("Dawn Jazz", "Miles Trio", "Morning", Path.Combine(tempDir, "dawn.flac"));
            var track2 = CreateTrack("Sunset Groove", "Miles Trio", "Evening", Path.Combine(tempDir, "sunset.flac"));

            using (var seedTrackRepo = new JsonTrackRepository(tracksPath))
            using (var seedFavRepo = new JsonFavoriteRepository(favoritesPath))
            {
                await seedTrackRepo.SaveRangeAsync([track1, track2]);
                await seedFavRepo.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(track2));
            }

            using var mcpPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            using var mcpTrackRepo = new JsonTrackRepository(tracksPath);
            using var mcpFavRepo = new JsonFavoriteRepository(favoritesPath);
            var mcpService = new PlaylistApplicationService(mcpPlaylistRepo, mcpTrackRepo, new Mock<IEventBus>().Object, mcpFavRepo);
            var sut = new PlaylistTools(mcpService, new Mock<ILogger<PlaylistTools>>().Object);

            // Act
            CallToolResult createResult = await sut.CreatePlaylistAsync(
                name: "AI 自動作成ドライブ",
                trackIds: [track2.Id.ToString(), track1.Id.ToString()]);

            // Assert 1: create_playlist 応答検証
            Assert.False(createResult.IsError ?? false);
            Guid createdId;
            using (JsonDocument createDoc = JsonDocument.Parse(GetSingleText(createResult)))
            {
                JsonElement data = createDoc.RootElement.GetProperty("data");
                Assert.Equal(2, data.GetProperty("addedCount").GetInt32());
                JsonElement pl = data.GetProperty("playlist");
                createdId = pl.GetProperty("id").GetGuid();
                Assert.NotEqual(Guid.Empty, createdId);
                Assert.Equal("AI 自動作成ドライブ", pl.GetProperty("name").GetString());
                List<JsonElement> tracks = pl.GetProperty("tracks").EnumerateArray().ToList();
                Assert.Equal(["Sunset Groove", "Dawn Jazz"], tracks.Select(t => t.GetProperty("title").GetString()).ToArray());
                Assert.True(tracks[0].GetProperty("isFavorite").GetBoolean());
                Assert.False(tracks[1].GetProperty("isFavorite").GetBoolean());
            }

            // Assert 2: 同一 MCP インスタンスの search_playlists から取得できること
            CallToolResult searchResult = await sut.SearchPlaylistsAsync(keyword: "自動作成", includeTracks: true);
            Assert.False(searchResult.IsError ?? false);
            using (JsonDocument searchDoc = JsonDocument.Parse(GetSingleText(searchResult)))
            {
                JsonElement pl = Assert.Single(searchDoc.RootElement.GetProperty("data").GetProperty("playlists").EnumerateArray());
                Assert.Equal(createdId, pl.GetProperty("id").GetGuid());
                Assert.Equal(2, pl.GetProperty("trackCount").GetInt32());
            }

            // Assert 3: 別インスタンス（WPF 本体相当）から LoadPlaylists で読み込めること
            using var wpfPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            using var wpfTrackRepo = new JsonTrackRepository(tracksPath);
            var wpfService = new PlaylistApplicationService(wpfPlaylistRepo, wpfTrackRepo, new Mock<IEventBus>().Object);
            List<UserPlaylist> loadedByWpf = wpfService.LoadPlaylists();
            UserPlaylist wpfPlaylist = Assert.Single(loadedByWpf);
            Assert.Equal(createdId, wpfPlaylist.Id.Value);
            Assert.Equal("AI 自動作成ドライブ", wpfPlaylist.Name);
            Assert.Equal([track2.Id, track1.Id], wpfPlaylist.TrackIds.ToArray());
            Assert.Equal([track2.FilePath, track1.FilePath], wpfPlaylist.TrackPaths.ToArray());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// add_tracks_to_playlist ツールで trackIds と filePaths を併用した場合、既存曲の後に重複を除外して一括追加し統一応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksToPlaylistAsync_trackIdsとfilePaths指定_一括追加と重複スキップの統一応答を返す()
    {
        // Arrange
        var track1 = CreateTrack("Song 1", "Artist", "Album", @"C:\Music\s1.flac");
        var track2 = CreateTrack("Song 2", "Artist", "Album", @"C:\Music\s2.flac");
        var track3 = CreateTrack("Song 3", "Artist", "Album", @"C:\Music\s3.flac");
        var playlist = new UserPlaylist(PlaylistId.New(), "Night Mix", [track1.Id]) { TrackPaths = [track1.FilePath] };

        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([track1, track2, track3]);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        // Act
        CallToolResult result = await sut.AddTracksToPlaylistAsync(
            playlistId: playlist.Id.ToString(),
            trackIds: [track1.Id.ToString(), track2.Id.ToString()],
            filePaths: [track3.FilePath]);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("add_tracks", data.GetProperty("operation").GetString());
        Assert.Equal(2, data.GetProperty("changedCount").GetInt32());
        Assert.Equal(track1.Id.ToString(), Assert.Single(data.GetProperty("skippedItems").EnumerateArray()).GetString());

        JsonElement pl = data.GetProperty("playlist");
        Assert.Equal(3, pl.GetProperty("trackCount").GetInt32());
        List<Guid> ids = pl.GetProperty("tracks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([track1.Id.Value, track2.Id.Value, track3.Id.Value], ids);
    }

    /// <summary>
    /// add_tracks_to_playlist ツールで不正パラメータ・存在しないプレイリスト・存在しないトラックを指定した場合に適切なエラーコードを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksToPlaylistAsync_異常系パラメータおよび未存在対象_適切なエラーコードを返す()
    {
        // Arrange
        var track1 = CreateTrack("Song 1", "Artist", "Album", @"C:\Music\s1.flac");
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([track1]);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<PlaylistId>(), It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);

        // Act
        CallToolResult badPlaylistId = await sut.AddTracksToPlaylistAsync("not-a-guid", trackIds: [track1.Id.ToString()]);
        CallToolResult noTargets = await sut.AddTracksToPlaylistAsync(Guid.NewGuid().ToString());
        CallToolResult relativePath = await sut.AddTracksToPlaylistAsync(Guid.NewGuid().ToString(), filePaths: [@"rel\a.flac"]);
        CallToolResult missingTrack = await sut.AddTracksToPlaylistAsync(Guid.NewGuid().ToString(), trackIds: [Guid.NewGuid().ToString()]);
        CallToolResult missingPlaylist = await sut.AddTracksToPlaylistAsync(Guid.NewGuid().ToString(), trackIds: [track1.Id.ToString()]);

        // Assert
        foreach (CallToolResult res in new[] { badPlaylistId, noTargets, relativePath })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        foreach (CallToolResult res in new[] { missingTrack, missingPlaylist })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// remove_tracks_from_playlist ツールで複数インデックスを指定した際、ずれなく削除され統一応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveTracksFromPlaylistAsync_複数インデックス指定_ずれなく削除し統一応答を返す()
    {
        // Arrange
        var t0 = CreateTrack("S0", "Artist", "Album", @"C:\Music\s0.flac");
        var t1 = CreateTrack("S1", "Artist", "Album", @"C:\Music\s1.flac");
        var t2 = CreateTrack("S2", "Artist", "Album", @"C:\Music\s2.flac");
        var playlist = new UserPlaylist(PlaylistId.New(), "Edit Target", [t0.Id, t1.Id, t2.Id])
        {
            TrackPaths = [t0.FilePath, t1.FilePath, t2.FilePath],
        };

        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([t0, t1, t2]);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        // Act
        CallToolResult result = await sut.RemoveTracksFromPlaylistAsync(
            playlistId: playlist.Id.ToString(),
            indices: [0, 2]);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("remove_tracks", data.GetProperty("operation").GetString());
        Assert.Equal(2, data.GetProperty("changedCount").GetInt32());
        JsonElement remaining = Assert.Single(data.GetProperty("playlist").GetProperty("tracks").EnumerateArray());
        Assert.Equal(t1.Id.Value, remaining.GetProperty("id").GetGuid());
    }

    /// <summary>
    /// remove_tracks_from_playlist ツールで対象未指定・負数/範囲外インデックス・存在しないプレイリストを指定した場合に適切なエラーコードを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveTracksFromPlaylistAsync_異常系パラメータおよび未存在プレイリスト_適切なエラーコードを返す()
    {
        // Arrange
        var playlist = new UserPlaylist(PlaylistId.New(), "Small", [TrackId.New()]);
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(It.Is<PlaylistId>(id => id != playlist.Id), It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);

        // Act
        CallToolResult noTargets = await sut.RemoveTracksFromPlaylistAsync(playlist.Id.ToString());
        CallToolResult negativeIdx = await sut.RemoveTracksFromPlaylistAsync(playlist.Id.ToString(), indices: [-1]);
        CallToolResult outOfRangeIdx = await sut.RemoveTracksFromPlaylistAsync(playlist.Id.ToString(), indices: [3]);
        CallToolResult missingPlaylist = await sut.RemoveTracksFromPlaylistAsync(Guid.NewGuid().ToString(), indices: [0]);

        // Assert
        foreach (CallToolResult res in new[] { noTargets, negativeIdx, outOfRangeIdx })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Assert.True(missingPlaylist.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(missingPlaylist)))
        {
            Assert.Equal(McpErrorCodes.NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// reorder_playlist_track ツールで曲順を並び替えた際、変更後の順序で統一応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task ReorderPlaylistTrackAsync_有効インデックス指定_順序変更後のプレイリストを返す()
    {
        // Arrange
        var t0 = CreateTrack("S0", "Artist", "Album", @"C:\Music\s0.flac");
        var t1 = CreateTrack("S1", "Artist", "Album", @"C:\Music\s1.flac");
        var playlist = new UserPlaylist(PlaylistId.New(), "Reorder", [t0.Id, t1.Id]) { TrackPaths = [t0.FilePath, t1.FilePath] };

        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out Mock<ITrackRepository> trackRepoMock, out _);
        trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([t0, t1]);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        // Act
        CallToolResult result = await sut.ReorderPlaylistTrackAsync(playlist.Id.ToString(), fromIndex: 1, toIndex: 0);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("reorder_track", data.GetProperty("operation").GetString());
        Assert.Equal(1, data.GetProperty("changedCount").GetInt32());
        List<Guid> ids = data.GetProperty("playlist").GetProperty("tracks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([t1.Id.Value, t0.Id.Value], ids);
    }

    /// <summary>
    /// reorder_playlist_track ツールで負数・範囲外インデックス・未存在プレイリストを指定した場合に適切なエラーコードを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task ReorderPlaylistTrackAsync_異常系パラメータおよび未存在プレイリスト_適切なエラーコードを返す()
    {
        // Arrange
        var playlist = new UserPlaylist(PlaylistId.New(), "Reorder", [TrackId.New()]);
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(It.Is<PlaylistId>(id => id != playlist.Id), It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);

        // Act
        CallToolResult negFrom = await sut.ReorderPlaylistTrackAsync(playlist.Id.ToString(), -1, 0);
        CallToolResult negTo = await sut.ReorderPlaylistTrackAsync(playlist.Id.ToString(), 0, -1);
        CallToolResult outOfRange = await sut.ReorderPlaylistTrackAsync(playlist.Id.ToString(), 0, 5);
        CallToolResult missing = await sut.ReorderPlaylistTrackAsync(Guid.NewGuid().ToString(), 0, 0);

        // Assert
        foreach (CallToolResult res in new[] { negFrom, negTo, outOfRange })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Assert.True(missing.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(missing)))
        {
            Assert.Equal(McpErrorCodes.NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// rename_playlist ツールで正常系・空名/長すぎ・未存在プレイリスト・内部例外発生時の応答と内部詳細非露出を検証します。
    /// </summary>
    [Fact]
    public async Task RenamePlaylistAsync_正常系および異常系とサービス例外_期待通りの応答を返す()
    {
        // Arrange
        var playlist = new UserPlaylist(PlaylistId.New(), "Before");
        var sut = CreateSut(out Mock<IPlaylistRepository> playlistRepoMock, out _, out _);
        playlistRepoMock
            .Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        var missingId = PlaylistId.New();
        playlistRepoMock
            .Setup(r => r.UpdateAsync(missingId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);

        var errorId = PlaylistId.New();
        playlistRepoMock
            .Setup(r => r.UpdateAsync(errorId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET_RENAME_FAILURE"));

        // Act
        CallToolResult okResult = await sut.RenamePlaylistAsync(playlist.Id.ToString(), "  After Name  ");
        CallToolResult emptyNameResult = await sut.RenamePlaylistAsync(playlist.Id.ToString(), "   ");
        CallToolResult tooLongResult = await sut.RenamePlaylistAsync(playlist.Id.ToString(), new string('A', PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH + 1));
        CallToolResult notFoundResult = await sut.RenamePlaylistAsync(missingId.ToString(), "Valid");
        CallToolResult internalErrorResult = await sut.RenamePlaylistAsync(errorId.ToString(), "Valid");

        // Assert
        Assert.False(okResult.IsError ?? false);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(okResult)))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal("rename", data.GetProperty("operation").GetString());
            Assert.Equal(1, data.GetProperty("changedCount").GetInt32());
            Assert.Equal("After Name", data.GetProperty("playlist").GetProperty("name").GetString());
        }

        foreach (CallToolResult res in new[] { emptyNameResult, tooLongResult })
        {
            Assert.True(res.IsError);
            using JsonDocument doc = JsonDocument.Parse(GetSingleText(res));
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Assert.True(notFoundResult.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(notFoundResult)))
        {
            Assert.Equal(McpErrorCodes.NOT_FOUND, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Assert.True(internalErrorResult.IsError);
        string errText = GetSingleText(internalErrorResult);
        Assert.DoesNotContain("SECRET_RENAME_FAILURE", errText, StringComparison.Ordinal);
        using (JsonDocument doc = JsonDocument.Parse(errText))
        {
            Assert.Equal(McpErrorCodes.INTERNAL_ERROR, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// 実 JsonPlaylistRepository を用いて add_tracks_to_playlist → reorder_playlist_track → remove_tracks_from_playlist → rename_playlist の一連の編集が原子的に永続化され、別インスタンス（WPF 相当）から整合した状態で読み込めることを検証します。
    /// </summary>
    [Fact]
    public async Task PlaylistEditTools_実リポジトリ使用時_追加並び替え削除名称変更が永続化されWPF互換読込できる()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorEditPlaylist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            var t1 = CreateTrack("Track 1", "Artist", "Album", Path.Combine(tempDir, "t1.flac"));
            var t2 = CreateTrack("Track 2", "Artist", "Album", Path.Combine(tempDir, "t2.flac"));
            var t3 = CreateTrack("Track 3", "Artist", "Album", Path.Combine(tempDir, "t3.flac"));

            using (var seedTrackRepo = new JsonTrackRepository(tracksPath))
            {
                await seedTrackRepo.SaveRangeAsync([t1, t2, t3]);
            }

            using var mcpPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            using var mcpTrackRepo = new JsonTrackRepository(tracksPath);
            var mcpService = new PlaylistApplicationService(mcpPlaylistRepo, mcpTrackRepo, new Mock<IEventBus>().Object);
            var sut = new PlaylistTools(mcpService, new Mock<ILogger<PlaylistTools>>().Object);

            // Act: 作成 → 追加 → 並び替え → 削除 → 名称変更
            CallToolResult createRes = await sut.CreatePlaylistAsync("Original", [t1.Id.ToString()]);
            Assert.False(createRes.IsError ?? false);
            Guid playlistId;
            using (JsonDocument doc = JsonDocument.Parse(GetSingleText(createRes)))
            {
                playlistId = doc.RootElement.GetProperty("data").GetProperty("playlist").GetProperty("id").GetGuid();
            }

            CallToolResult addRes = await sut.AddTracksToPlaylistAsync(playlistId.ToString(), [t2.Id.ToString(), t3.Id.ToString()]);
            Assert.False(addRes.IsError ?? false);

            CallToolResult reorderRes = await sut.ReorderPlaylistTrackAsync(playlistId.ToString(), fromIndex: 2, toIndex: 0);
            Assert.False(reorderRes.IsError ?? false);

            CallToolResult removeRes = await sut.RemoveTracksFromPlaylistAsync(playlistId.ToString(), indices: [1]);
            Assert.False(removeRes.IsError ?? false);

            CallToolResult renameRes = await sut.RenamePlaylistAsync(playlistId.ToString(), "Edited Final");
            Assert.False(renameRes.IsError ?? false);

            // Assert: 別インスタンス（WPF 本体相当）から読み込んでも TrackIds / TrackPaths / Name が完全一致すること
            using var wpfPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            using var wpfTrackRepo = new JsonTrackRepository(tracksPath);
            var wpfService = new PlaylistApplicationService(wpfPlaylistRepo, wpfTrackRepo, new Mock<IEventBus>().Object);
            UserPlaylist loaded = Assert.Single(wpfService.LoadPlaylists());

            Assert.Equal(playlistId, loaded.Id.Value);
            Assert.Equal("Edited Final", loaded.Name);
            // [t1, t2, t3] -> reorder(2->0): [t3, t1, t2] -> remove(1): [t3, t2]
            Assert.Equal([t3.Id, t2.Id], loaded.TrackIds.ToArray());
            Assert.Equal([t3.FilePath, t2.FilePath], loaded.TrackPaths.ToArray());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static PlaylistTools CreateSut(
        out Mock<IPlaylistRepository> playlistRepoMock,
        out Mock<ITrackRepository> trackRepoMock,
        out Mock<ILogger<PlaylistTools>> loggerMock)
    {
        playlistRepoMock = new Mock<IPlaylistRepository>();
        trackRepoMock = new Mock<ITrackRepository>();
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Track>());

        var playlistService = new PlaylistApplicationService(
            playlistRepoMock.Object,
            trackRepoMock.Object,
            new Mock<IEventBus>().Object);

        loggerMock = new Mock<ILogger<PlaylistTools>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        return new PlaylistTools(playlistService, loggerMock.Object);
    }

    private static Track CreateTrack(string title, string artist, string album, string filePath)
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(filePath),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromSeconds(200),
            year: 2024,
            trackNumber: 1,
            bitrate: 1411,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "FLAC",
            genre: "Jazz");
    }

    private static string GetSingleText(CallToolResult result)
    {
        TextContentBlock block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }
}
