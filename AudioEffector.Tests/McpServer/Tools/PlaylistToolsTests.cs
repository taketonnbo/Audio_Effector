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

namespace AudioEffector.Tests.McpServer.Tools;

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
