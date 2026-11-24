using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Infrastructure.Repository;
using AudioEffector.McpServer.Protocol;
using AudioEffector.McpServer.Tools;
using AudioEffector.Presentation.ViewModels;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;

namespace AudioEffector.Tests.McpServer.Tools;

public sealed class LibraryToolsTests
{
    /// <summary>
    /// search_tracks ツールでキーワード検索を実行した場合、タイトル・アーティスト・アルバムに大文字小文字を区別せず一致する楽曲の全メタデータ JSON を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_キーワード検索_一致した楽曲の全メタデータJSONを返す()
    {
        // Arrange
        var trackId = TrackId.New();
        var track = new Track(
            id: trackId,
            filePath: AudioPath.Create(@"C:\Music\Jazz\BlueNote.flac"),
            title: "Blue Note Morning",
            artist: "Miles Trio",
            album: "Midnight Sessions",
            duration: TimeSpan.FromSeconds(225.5),
            year: 2024,
            trackNumber: 3,
            bitrate: 1411,
            sampleRate: 96000,
            bitsPerSample: 24,
            format: "FLAC",
            genre: "Jazz",
            isFavorite: false,
            isLossless: true,
            isHiRes: true);

        var favoriteEntry = FavoriteTrackEntry.FromTrack(track);
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out Mock<IFavoriteRepository> favRepoMock, out _);

        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track]);
        favRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([favoriteEntry]);

        // Act
        CallToolResult result = await sut.SearchTracksAsync(keyword: "  blue  ");
        string text = GetSingleText(result);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("count").GetInt32());
        Assert.Equal(0, data.GetProperty("offset").GetInt32());
        Assert.Equal(TrackSearchQuery.DEFAULT_LIMIT, data.GetProperty("limit").GetInt32());

        JsonElement trackJson = Assert.Single(data.GetProperty("tracks").EnumerateArray());
        Assert.Equal(trackId.Value, trackJson.GetProperty("id").GetGuid());
        Assert.Equal("Blue Note Morning", trackJson.GetProperty("title").GetString());
        Assert.Equal("Miles Trio", trackJson.GetProperty("artist").GetString());
        Assert.Equal("Midnight Sessions", trackJson.GetProperty("album").GetString());
        Assert.Equal(225.5, trackJson.GetProperty("durationSeconds").GetDouble());
        Assert.Equal("03:45", trackJson.GetProperty("durationString").GetString());
        Assert.Equal(3u, trackJson.GetProperty("trackNumber").GetUInt32());
        Assert.Equal(2024u, trackJson.GetProperty("year").GetUInt32());
        Assert.Equal("Jazz", trackJson.GetProperty("genre").GetString());
        Assert.Equal("FLAC", trackJson.GetProperty("format").GetString());
        Assert.Equal(1411, trackJson.GetProperty("bitrate").GetInt32());
        Assert.Equal(96000, trackJson.GetProperty("sampleRate").GetInt32());
        Assert.Equal(24, trackJson.GetProperty("bitsPerSample").GetInt32());
        Assert.True(trackJson.GetProperty("isFavorite").GetBoolean());
        Assert.Equal(@"C:\Music\Jazz\BlueNote.flac", trackJson.GetProperty("filePath").GetString());
    }

    /// <summary>
    /// search_tracks ツールで artist・album・genre・isFavoriteOnly の複合フィルタおよび limit・offset ページネーションが正しく適用されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_複合条件フィルタとページネーション_絞り込みとページ分割結果が正しく返る()
    {
        // Arrange
        var track1 = CreateTrack("Song A", "Alpha Band", "Best Hits", "Rock", trackNumber: 1, filePath: @"C:\Music\a.flac");
        var track2 = CreateTrack("Song B", "Alpha Band", "Best Hits", "Rock", trackNumber: 2, filePath: @"C:\Music\b.flac");
        var track3 = CreateTrack("Song C", "Alpha Band", "Best Hits", "Rock", trackNumber: 3, filePath: @"C:\Music\c.flac");
        var track4 = CreateTrack("Song D", "Alpha Band", "Best Hits", "Pop", trackNumber: 4, filePath: @"C:\Music\d.flac");
        var track5 = CreateTrack("Song E", "Beta Artist", "Best Hits", "Rock", trackNumber: 5, filePath: @"C:\Music\e.flac");

        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out Mock<IFavoriteRepository> favRepoMock, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track3, track1, track4, track5, track2]);
        favRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                FavoriteTrackEntry.FromTrack(track1),
                FavoriteTrackEntry.FromTrack(track2),
                FavoriteTrackEntry.FromTrack(track3),
                FavoriteTrackEntry.FromTrack(track4),
            ]);

        // Act
        CallToolResult result = await sut.SearchTracksAsync(
            artist: "alpha",
            album: "BEST",
            genre: "rock",
            isFavoriteOnly: true,
            limit: 1,
            offset: 1);

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("count").GetInt32());
        Assert.Equal(1, data.GetProperty("offset").GetInt32());
        Assert.Equal(1, data.GetProperty("limit").GetInt32());

        JsonElement trackJson = Assert.Single(data.GetProperty("tracks").EnumerateArray());
        Assert.Equal("Song B", trackJson.GetProperty("title").GetString());
        Assert.True(trackJson.GetProperty("isFavorite").GetBoolean());
    }

    /// <summary>
    /// search_tracks ツールで検索結果が 0 件の場合、totalCount=0・count=0・空配列 tracks を含む正常応答を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_検索結果0件_空配列とtotalCount0の正常応答を返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Track>());

        // Act
        CallToolResult result = await sut.SearchTracksAsync(keyword: "存在しない曲");

        // Assert
        Assert.False(result.IsError ?? false);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("count").GetInt32());
        Assert.Equal(0, data.GetProperty("offset").GetInt32());
        Assert.Equal(TrackSearchQuery.DEFAULT_LIMIT, data.GetProperty("limit").GetInt32());
        Assert.Empty(data.GetProperty("tracks").EnumerateArray());
    }

    /// <summary>
    /// search_tracks ツールで limit に許容範囲外の値を指定した場合、INVALID_PARAMETER エラーと範囲詳細を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    public async Task SearchTracksAsync_limitが範囲外_INVALID_PARAMETERエラーを返す(int invalidLimit)
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, out _);

        // Act
        CallToolResult result = await sut.SearchTracksAsync(limit: invalidLimit);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, error.GetProperty("code").GetString());
        JsonElement details = error.GetProperty("details");
        Assert.Equal("limit", details.GetProperty("parameter").GetString());
        Assert.Equal(TrackSearchQuery.MIN_LIMIT, details.GetProperty("min").GetInt32());
        Assert.Equal(TrackSearchQuery.MAX_LIMIT, details.GetProperty("max").GetInt32());

        trackRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        trackRepoMock.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// search_tracks ツールで offset に負数を指定した場合、INVALID_PARAMETER エラーを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task SearchTracksAsync_offsetが負数_INVALID_PARAMETERエラーを返す(int invalidOffset)
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, out _);

        // Act
        CallToolResult result = await sut.SearchTracksAsync(offset: invalidOffset);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());

        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, error.GetProperty("code").GetString());
        JsonElement details = error.GetProperty("details");
        Assert.Equal("offset", details.GetProperty("parameter").GetString());
        Assert.Equal(TrackSearchQuery.DEFAULT_OFFSET, details.GetProperty("min").GetInt32());

        trackRepoMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// search_tracks 実行中にリポジトリが未捕捉例外を送出した場合、INTERNAL_ERROR へ変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_リポジトリ例外発生時_INTERNAL_ERRORを返し内部詳細を露出しない()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, out Mock<ILogger<LibraryTools>> loggerMock);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SECRET_SEARCH_EXCEPTION"));

        // Act
        CallToolResult result = await sut.SearchTracksAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SECRET_SEARCH_EXCEPTION", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
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
    /// search_tracks 実行中にファイルロックタイムアウトが発生した場合、CONFLICT_LOCK_TIMEOUT へ変換されパスが露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_ファイルロックタイムアウト発生時_CONFLICT_LOCK_TIMEOUTを返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, out _);
        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileLockTimeoutException(@"C:\SecretDir\tracks.json", TimeSpan.FromSeconds(5)));

        // Act
        CallToolResult result = await sut.SearchTracksAsync();
        string text = GetSingleText(result);

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain("SecretDir", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.Equal(McpErrorCodes.CONFLICT_LOCK_TIMEOUT, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// search_tracks が呼び出し元の CancellationToken をリポジトリへ伝播し、キャンセル時は OPERATION_CANCELLED を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_キャンセル要求時_CancellationTokenを伝播しOPERATION_CANCELLEDを返す()
    {
        // Arrange
        var sut = CreateSut(out Mock<ITrackRepository> trackRepoMock, out _, out _);
        using var cts = new CancellationTokenSource();
        CancellationToken passedToken = default;

        trackRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => passedToken = ct)
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act
        CallToolResult result = await sut.SearchTracksAsync(keyword: "jazz", cancellationToken: cts.Token);

        // Assert
        Assert.Equal(cts.Token, passedToken);
        Assert.True(result.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(result));
        Assert.Equal(McpErrorCodes.OPERATION_CANCELLED, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// 実 JsonTrackRepository / JsonFavoriteRepository を使用し、別インスタンス経由で更新された最新トラックおよびお気に入り状態が再起動なしで検索に反映されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_実リポジトリで外部更新発生時_最新トラックとお気に入り状態を反映して検索できる()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorLibraryToolsFreshness_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            string favoritesPath = Path.Combine(tempDir, "favorites.json");

            using var mcpTrackRepo = new JsonTrackRepository(tracksPath);
            using var mcpFavRepo = new JsonFavoriteRepository(favoritesPath);
            var metadataExtractorMock = new Mock<IMetadataExtractor>();
            var eventBusMock = new Mock<IEventBus>();
            var libraryService = new LibraryApplicationService(
                mcpTrackRepo,
                mcpFavRepo,
                metadataExtractorMock.Object,
                eventBusMock.Object);
            var loggerMock = new Mock<ILogger<LibraryTools>>();
            var sut = new LibraryTools(libraryService, loggerMock.Object);

            // 初回検索（空ライブラリ）
            CallToolResult initialResult = await sut.SearchTracksAsync(keyword: "Nocturne");
            using (JsonDocument initialDoc = JsonDocument.Parse(GetSingleText(initialResult)))
            {
                Assert.Equal(0, initialDoc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
            }

            // 別プロセス（WPF本体相当）による tracks.json / favorites.json 更新
            using var wpfTrackRepo = new JsonTrackRepository(tracksPath);
            using var wpfFavRepo = new JsonFavoriteRepository(favoritesPath);
            var newTrack = CreateTrack("Nocturne Op.9", "Chopin", "Classic Selection", "Classical", filePath: @"C:\Music\nocturne.flac");
            await wpfTrackRepo.SaveAsync(newTrack);
            await wpfFavRepo.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(newTrack));

            // Act: MCP 側インスタンスを再生成せずに検索
            CallToolResult updatedResult = await sut.SearchTracksAsync(keyword: "nocturne", isFavoriteOnly: true);

            // Assert
            Assert.False(updatedResult.IsError ?? false);
            using JsonDocument updatedDoc = JsonDocument.Parse(GetSingleText(updatedResult));
            JsonElement data = updatedDoc.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
            JsonElement trackJson = Assert.Single(data.GetProperty("tracks").EnumerateArray());
            Assert.Equal("Nocturne Op.9", trackJson.GetProperty("title").GetString());
            Assert.True(trackJson.GetProperty("isFavorite").GetBoolean());
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
    /// 実リポジトリ上で、お気に入り保存パスの曲が改題され別パスに旧メタデータ一致曲が存在する状態において、改題曲のお気に入り限定検索が 0 件を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_実リポジトリで元パス改題曲と旧メタデータ一致曲が共存_改題曲のお気に入り限定検索が0件になる()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorLibraryToolsFavReconcile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            string favoritesPath = Path.Combine(tempDir, "favorites.json");

            using var trackRepo = new JsonTrackRepository(tracksPath);
            using var favRepo = new JsonFavoriteRepository(favoritesPath);

            var renamedTrack = CreateTrack(
                title: "Renamed",
                artist: "Artist A",
                album: "Album A",
                genre: "Rock",
                trackNumber: 1,
                filePath: Path.Combine(tempDir, "original_path.flac"));
            var originalTrack = CreateTrack(
                title: "Original",
                artist: "Artist A",
                album: "Album A",
                genre: "Rock",
                trackNumber: 1,
                filePath: Path.Combine(tempDir, "another_path.flac"));

            await trackRepo.SaveRangeAsync([renamedTrack, originalTrack]);
            await favRepo.AddOrUpdateAsync(new FavoriteTrackEntry(
                title: "Original",
                artist: "Artist A",
                album: "Album A",
                trackNumber: 1,
                durationSeconds: 180.0,
                lastKnownFilePath: renamedTrack.FilePath,
                addedAt: DateTime.UtcNow));

            var libraryService = new LibraryApplicationService(
                trackRepo,
                favRepo,
                new Mock<IMetadataExtractor>().Object,
                new Mock<IEventBus>().Object);
            var sut = new LibraryTools(libraryService, new Mock<ILogger<LibraryTools>>().Object);

            // Act
            CallToolResult favoritesOnlyResult = await sut.SearchTracksAsync(isFavoriteOnly: true);
            CallToolResult renamedFavoriteResult = await sut.SearchTracksAsync(keyword: "Renamed", isFavoriteOnly: true);

            // Assert
            Assert.False(favoritesOnlyResult.IsError ?? false);
            using (JsonDocument favDoc = JsonDocument.Parse(GetSingleText(favoritesOnlyResult)))
            {
                JsonElement favData = favDoc.RootElement.GetProperty("data");
                Assert.Equal(1, favData.GetProperty("totalCount").GetInt32());
                JsonElement favTrack = Assert.Single(favData.GetProperty("tracks").EnumerateArray());
                Assert.Equal("Original", favTrack.GetProperty("title").GetString());
                Assert.True(favTrack.GetProperty("isFavorite").GetBoolean());
            }

            Assert.False(renamedFavoriteResult.IsError ?? false);
            using (JsonDocument renamedDoc = JsonDocument.Parse(GetSingleText(renamedFavoriteResult)))
            {
                JsonElement renamedData = renamedDoc.RootElement.GetProperty("data");
                Assert.Equal(0, renamedData.GetProperty("totalCount").GetInt32());
                Assert.Equal(0, renamedData.GetProperty("count").GetInt32());
                Assert.Empty(renamedData.GetProperty("tracks").EnumerateArray());
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
    /// 空の一時データディレクトリとタグ付き MP4 を用い、WPF の通常読み込み経路（LibraryViewModel.LoadLibraryAsync）から別インスタンス MCP の search_tracks まで、追加・タグ更新（ID 維持）・削除・フォルダ切替が一貫して同期されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchTracksAsync_WPF通常読み込み経路でタグ付きMP4をスキャン後_別インスタンスMCPからアーティスト名と曲名で検索でき更新削除フォルダ切替とID維持も同期される()
    {
        // Arrange
        string rootDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorWpfToMcpMp4_{Guid.NewGuid():N}");
        string dataDir = Path.Combine(rootDir, "data");
        string musicDirA = Path.Combine(rootDir, "musicA");
        string musicDirB = Path.Combine(rootDir, "musicB");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(musicDirA);
        Directory.CreateDirectory(musicDirB);

        try
        {
            string tracksPath = Path.Combine(dataDir, "tracks.json");
            string favoritesPath = Path.Combine(dataDir, "favorites.json");
            string mp4PathA = Path.Combine(musicDirA, "starlight.mp4");
            string mp4PathB = Path.Combine(musicDirB, "horizon.mp4");

            CreateTaggedMp4File(mp4PathA, title: "Starlight MP4", artist: "Luna Quartet", album: "Night Sky");
            CreateTaggedMp4File(mp4PathB, title: "Horizon Line", artist: "Solar Ensemble", album: "Dawn");

            // WPF 側の通常読み込み経路
            using var wpfTrackRepo = new JsonTrackRepository(tracksPath);
            using var wpfFavRepo = new JsonFavoriteRepository(favoritesPath);
            var wpfLibraryService = new LibraryApplicationService(
                wpfTrackRepo,
                wpfFavRepo,
                new TagLibMetadataExtractor(),
                new InMemoryEventBus());
            var settingsMock = new Mock<ISettingsService>();
            settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings { DetectMetadataChangesOnRescan = true });
            using var wpfViewModel = new LibraryViewModel(
                wpfLibraryService,
                new Mock<IAudioService>().Object,
                settingsMock.Object);

            // 別インスタンスの MCP 側
            using var mcpTrackRepo = new JsonTrackRepository(tracksPath);
            using var mcpFavRepo = new JsonFavoriteRepository(favoritesPath);
            var mcpLibraryService = new LibraryApplicationService(
                mcpTrackRepo,
                mcpFavRepo,
                new TagLibMetadataExtractor(),
                new InMemoryEventBus());
            var sut = new LibraryTools(mcpLibraryService, new Mock<ILogger<LibraryTools>>().Object);

            // Act 1: WPF の通常読み込みで MP4 をスキャン
            await wpfViewModel.LoadLibraryAsync(musicDirA);
            Track wpfTrack = Assert.Single(wpfViewModel.Tracks);

            // Assert 1: 別インスタンス MCP からアーティスト名・曲名それぞれで同一トラックが取得できる
            CallToolResult byArtist = await sut.SearchTracksAsync(keyword: "Luna Quartet");
            CallToolResult byTitle = await sut.SearchTracksAsync(keyword: "Starlight MP4");

            Guid initialId;
            using (JsonDocument artistDoc = JsonDocument.Parse(GetSingleText(byArtist)))
            {
                JsonElement trackJson = Assert.Single(artistDoc.RootElement.GetProperty("data").GetProperty("tracks").EnumerateArray());
                initialId = trackJson.GetProperty("id").GetGuid();
                Assert.Equal(wpfTrack.Id.Value, initialId);
                Assert.Equal("Starlight MP4", trackJson.GetProperty("title").GetString());
                Assert.Equal("Luna Quartet", trackJson.GetProperty("artist").GetString());
                Assert.Equal("MP4", trackJson.GetProperty("format").GetString());
            }

            using (JsonDocument titleDoc = JsonDocument.Parse(GetSingleText(byTitle)))
            {
                JsonElement trackJson = Assert.Single(titleDoc.RootElement.GetProperty("data").GetProperty("tracks").EnumerateArray());
                Assert.Equal(initialId, trackJson.GetProperty("id").GetGuid());
            }

            // Act 2: タグ更新後に再スキャン（既存 TrackId を維持して更新されること）
            CreateTaggedMp4File(mp4PathA, title: "Starlight Remastered", artist: "Luna Quartet", album: "Night Sky");
            File.SetLastWriteTimeUtc(mp4PathA, DateTime.UtcNow.AddSeconds(10));
            await wpfViewModel.LoadLibraryAsync(musicDirA);

            CallToolResult afterUpdate = await sut.SearchTracksAsync(keyword: "Starlight Remastered");
            using (JsonDocument updatedDoc = JsonDocument.Parse(GetSingleText(afterUpdate)))
            {
                JsonElement trackJson = Assert.Single(updatedDoc.RootElement.GetProperty("data").GetProperty("tracks").EnumerateArray());
                Assert.Equal(initialId, trackJson.GetProperty("id").GetGuid());
                Assert.Equal("Starlight Remastered", trackJson.GetProperty("title").GetString());
            }

            // Act 3: フォルダ切替（musicDirB へ切替後、旧フォルダの曲が消え新フォルダの曲だけ検索できること）
            await wpfViewModel.LoadLibraryAsync(musicDirB);

            CallToolResult oldFolderSearch = await sut.SearchTracksAsync(keyword: "Luna Quartet");
            CallToolResult newFolderSearch = await sut.SearchTracksAsync(keyword: "Solar Ensemble");
            using (JsonDocument oldDoc = JsonDocument.Parse(GetSingleText(oldFolderSearch)))
            {
                Assert.Equal(0, oldDoc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
            }

            Guid folderBTrackId;
            using (JsonDocument newDoc = JsonDocument.Parse(GetSingleText(newFolderSearch)))
            {
                JsonElement trackJson = Assert.Single(newDoc.RootElement.GetProperty("data").GetProperty("tracks").EnumerateArray());
                folderBTrackId = trackJson.GetProperty("id").GetGuid();
                Assert.Equal("Horizon Line", trackJson.GetProperty("title").GetString());
            }

            // Act 4: アプリ再起動相当（新しい LibraryViewModel で同フォルダ再読み込み）でも既存 TrackId を維持すること
            using var restartedWpfViewModel = new LibraryViewModel(
                wpfLibraryService,
                new Mock<IAudioService>().Object,
                settingsMock.Object);
            await restartedWpfViewModel.LoadLibraryAsync(musicDirB);
            Track restartedTrack = Assert.Single(restartedWpfViewModel.Tracks);
            Assert.Equal(folderBTrackId, restartedTrack.Id.Value);

            // Act 5: ファイル削除後に再スキャン（MCP 検索結果が 0 件になること）
            File.Delete(mp4PathB);
            await restartedWpfViewModel.LoadLibraryAsync(musicDirB);

            CallToolResult afterDelete = await sut.SearchTracksAsync();
            using (JsonDocument deleteDoc = JsonDocument.Parse(GetSingleText(afterDelete)))
            {
                Assert.Equal(0, deleteDoc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
            }
        }
        finally
        {
            if (Directory.Exists(rootDir))
            {
                Directory.Delete(rootDir, recursive: true);
            }
        }
    }

    private static void CreateTaggedMp4File(string filePath, string title, string artist, string album)
    {
        byte[] ftyp = CreateIsoBox("ftyp", [
            (byte)'M', (byte)'4', (byte)'A', (byte)' ',
            0, 0, 0, 0,
            (byte)'M', (byte)'4', (byte)'A', (byte)' ',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
        ]);

        // mvhd (version 0, 100 bytes payload)
        byte[] mvhdPayload = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(12, 4), 1000); // timescale
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(16, 4), 180000); // duration (180s)
        mvhdPayload[20] = 0x00;
        mvhdPayload[21] = 0x01; // rate 1.0
        mvhdPayload[24] = 0x01; // volume 1.0
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(36, 4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(52, 4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(68, 4), 0x40000000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhdPayload.AsSpan(96, 4), 2); // next_track_id
        byte[] mvhd = CreateIsoBox("mvhd", mvhdPayload);

        byte[] hdlrPayload = new byte[25];
        Encoding.ASCII.GetBytes("mdir").CopyTo(hdlrPayload, 8);
        Encoding.ASCII.GetBytes("appl").CopyTo(hdlrPayload, 12);
        byte[] hdlr = CreateIsoBox("hdlr", hdlrPayload);

        byte[] ilst = CreateIsoBox("ilst", ConcatBytes(
            CreateMp4TextAtom("\u00A9nam", title),
            CreateMp4TextAtom("\u00A9ART", artist),
            CreateMp4TextAtom("\u00A9alb", album)));

        byte[] metaPayload = ConcatBytes([0, 0, 0, 0], hdlr, ilst);
        byte[] meta = CreateIsoBox("meta", metaPayload);
        byte[] udta = CreateIsoBox("udta", meta);
        byte[] moov = CreateIsoBox("moov", ConcatBytes(mvhd, udta));

        File.WriteAllBytes(filePath, ConcatBytes(ftyp, moov));
    }

    private static byte[] CreateMp4TextAtom(string atomType, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        byte[] dataPayload = new byte[8 + utf8.Length];
        dataPayload[3] = 1; // UTF-8 text type
        utf8.CopyTo(dataPayload, 8);
        byte[] dataBox = CreateIsoBox("data", dataPayload);
        return CreateIsoBox(atomType, dataBox);
    }

    private static byte[] CreateIsoBox(string type, byte[] payload)
    {
        byte[] result = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), (uint)result.Length);
        Encoding.Latin1.GetBytes(type).CopyTo(result, 4);
        payload.CopyTo(result, 8);
        return result;
    }

    private static byte[] ConcatBytes(params byte[][] arrays)
    {
        int totalLength = arrays.Sum(a => a.Length);
        byte[] result = new byte[totalLength];
        int offset = 0;
        foreach (byte[] arr in arrays)
        {
            arr.CopyTo(result, offset);
            offset += arr.Length;
        }

        return result;
    }

    private static LibraryTools CreateSut(
        out Mock<ITrackRepository> trackRepoMock,
        out Mock<IFavoriteRepository> favRepoMock,
        out Mock<ILogger<LibraryTools>> loggerMock)
    {
        trackRepoMock = new Mock<ITrackRepository>();
        favRepoMock = new Mock<IFavoriteRepository>();
        favRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        var metadataExtractorMock = new Mock<IMetadataExtractor>();
        var eventBusMock = new Mock<IEventBus>();
        var libraryService = new LibraryApplicationService(
            trackRepoMock.Object,
            favRepoMock.Object,
            metadataExtractorMock.Object,
            eventBusMock.Object);

        loggerMock = new Mock<ILogger<LibraryTools>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        return new LibraryTools(libraryService, loggerMock.Object);
    }

    private static Track CreateTrack(
        string title,
        string artist,
        string album,
        string genre,
        uint trackNumber = 1,
        string? filePath = null)
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(filePath ?? $@"C:\Music\{title}.mp3"),
            title: title,
            artist: artist,
            album: album,
            duration: TimeSpan.FromSeconds(180),
            year: 2023,
            trackNumber: trackNumber,
            bitrate: 320,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "MP3",
            genre: genre);
    }

    private static string GetSingleText(CallToolResult result)
    {
        TextContentBlock block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }
}
