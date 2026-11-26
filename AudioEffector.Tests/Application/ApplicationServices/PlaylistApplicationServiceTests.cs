using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Repository;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Application.ApplicationServices;

/// <summary>
/// PlaylistApplicationServiceのCRUD操作、トラック追加・削除・並び替え、およびPlaylistUpdatedEvent発行を検証するテストクラス
/// </summary>
public sealed class PlaylistApplicationServiceTests
{
    /// <summary>
    /// コンストラクタ引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PlaylistApplicationService(null!, mockTrackRepo.Object, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new PlaylistApplicationService(mockPlaylistRepo.Object, null!, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, null!));
    }

    /// <summary>
    /// GetAllPlaylistsAsync呼び出し時、IPlaylistRepositoryのGetAllAsyncが呼ばれ結果を返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllPlaylistsAsync_呼び出し時_IPlaylistRepositoryのGetAllAsyncを呼び出すこと()
    {
        // Arrange
        var playlists = new List<UserPlaylist> { new(PlaylistId.New(), "P1"), new(PlaylistId.New(), "P2") };
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(playlists);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var actual = await sut.GetAllPlaylistsAsync();

        // Assert
        Assert.Equal(2, actual.Count);
        mockPlaylistRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// CreatePlaylistAsync呼び出し時、新規プレイリストが保存されPlaylistUpdatedEventが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_名前指定_新規プレイリストが保存されPlaylistUpdatedEventが発行されること()
    {
        // Arrange
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var actual = await sut.CreatePlaylistAsync("ワークアウトBGM");

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("ワークアウトBGM", actual.Name);
        mockPlaylistRepo.Verify(r => r.SaveAsync(It.Is<UserPlaylist>(p => p.Name == "ワークアウトBGM"), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == actual), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 登録済みのプレイリストIDを指定して削除した場合、リポジトリのDeleteAsyncが実行されイベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task DeletePlaylistAsync_登録済みプレイリストID_削除が実行されPlaylistUpdatedEventが発行されること()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var playlist = new UserPlaylist(playlistId, "削除対象");

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playlist);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.DeletePlaylistAsync(playlistId);

        // Assert
        mockPlaylistRepo.Verify(r => r.DeleteAsync(playlistId, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 存在しないプレイリストIDを指定して削除した場合、イベントが発行されないことを検証します。
    /// </summary>
    [Fact]
    public async Task DeletePlaylistAsync_存在しないプレイリストID_イベントが発行されないこと()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.DeletePlaylistAsync(playlistId);

        // Assert
        mockPlaylistRepo.Verify(r => r.DeleteAsync(playlistId, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<PlaylistUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 存在するプレイリストにトラックを追加した際、トラックが追加されて保存されイベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task AddTrackAsync_存在するプレイリスト_トラックが追加され保存とイベント発行が行われること()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var playlist = new UserPlaylist(playlistId, "Chill");
        var trackId = TrackId.New();

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var actual = await sut.AddTrackAsync(playlistId, trackId);

        // Assert
        Assert.NotNull(actual);
        Assert.Single(actual.TrackIds);
        Assert.Equal(trackId, actual.TrackIds[0]);
        mockPlaylistRepo.Verify(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 存在しないプレイリストIDを指定してトラックを追加しようとした場合、nullを返し保存やイベント発行が行われないことを検証します。
    /// </summary>
    [Fact]
    public async Task AddTrackAsync_存在しないプレイリスト_nullを返し保存やイベント発行が行われないこと()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var actual = await sut.AddTrackAsync(playlistId, TrackId.New());

        // Assert
        Assert.Null(actual);
        mockPlaylistRepo.Verify(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<PlaylistUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 有効なインデックスを指定してトラックを削除した際、トラックが削除され保存とイベント発行が行われるかを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveTrackAtAsync_有効インデックス指定_トラックが削除され保存とイベント発行が行われること()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var t1 = TrackId.New();
        var t2 = TrackId.New();
        var playlist = new UserPlaylist(playlistId, "Pop", [t1, t2]);

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var actual = await sut.RemoveTrackAtAsync(playlistId, 0);

        // Assert
        Assert.NotNull(actual);
        Assert.Single(actual.TrackIds);
        Assert.Equal(t2, actual.TrackIds[0]);
        mockPlaylistRepo.Verify(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// トラックの並び替えを実行した際、順序が変更されて保存されイベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ReorderTrackAsync_順序並び替え_トラック順序が変更され保存とイベント発行が行われること()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var t1 = TrackId.New();
        var t2 = TrackId.New();
        var playlist = new UserPlaylist(playlistId, "ReorderTest", [t1, t2]);

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act: 0番目と1番目を入れ替え
        var actual = await sut.ReorderTrackAsync(playlistId, 0, 1);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(t2, actual.TrackIds[0]);
        Assert.Equal(t1, actual.TrackIds[1]);
        mockPlaylistRepo.Verify(r => r.UpdateAsync(playlistId, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SearchPlaylistsAsync でキーワード絞り込み・昇順ソート・ページネーションが正しく適用されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchPlaylistsAsync_キーワードとページネーション指定_絞り込みと安定ソート結果を返すこと()
    {
        // Arrange
        var p1 = new UserPlaylist(PlaylistId.New(), "Jazz Night");
        var p2 = new UserPlaylist(PlaylistId.New(), "Ambient Jazz");
        var p3 = new UserPlaylist(PlaylistId.New(), "Rock Hits");
        var p4 = new UserPlaylist(PlaylistId.New(), "Smooth jazz");

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([p1, p2, p3, p4]);

        var sut = new PlaylistApplicationService(
            mockPlaylistRepo.Object,
            new Mock<ITrackRepository>().Object,
            new Mock<IEventBus>().Object);

        // Act
        PlaylistSearchResult result = await sut.SearchPlaylistsAsync(
            new PlaylistSearchQuery(Keyword: "  JAZZ ", Limit: 1, Offset: 1));

        // Assert
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Offset);
        Assert.Equal(1, result.Limit);
        UserPlaylist single = Assert.Single(result.Playlists);
        Assert.Equal("Jazz Night", single.Name);
    }

    /// <summary>
    /// SearchPlaylistsAsync で不正な limit または offset を指定した場合、ArgumentOutOfRangeException がスローされることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(201, 0)]
    [InlineData(50, -1)]
    public async Task SearchPlaylistsAsync_範囲外パラメータ指定_ArgumentOutOfRangeExceptionをスローすること(int limit, int offset)
    {
        // Arrange
        var sut = new PlaylistApplicationService(
            new Mock<IPlaylistRepository>().Object,
            new Mock<ITrackRepository>().Object,
            new Mock<IEventBus>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.SearchPlaylistsAsync(new PlaylistSearchQuery(Limit: limit, Offset: offset)));
    }

    /// <summary>
    /// ResolvePlaylistTracksAsync が TrackIds による順序解決および旧互換 TrackPaths フォールバック解決を正しく行うことを検証します。
    /// </summary>
    [Fact]
    public async Task ResolvePlaylistTracksAsync_TrackIdsと旧互換TrackPaths指定_順序通りにトラック詳細を解決すること()
    {
        // Arrange
        var track1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\first.flac"), title: "First", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
        var track2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\second.flac"), title: "Second", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
        var missingId = TrackId.New();

        var idBasedPlaylist = new UserPlaylist(PlaylistId.New(), "ByIds", [track2.Id, missingId, track1.Id]);
        var pathBasedPlaylist = new UserPlaylist(PlaylistId.New(), "ByPaths")
        {
            TrackPaths = [@"c:\music\second.flac", @"C:\Music\missing.flac", @"C:\Music\first.flac"],
        };

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, track2]);

        var sut = new PlaylistApplicationService(
            new Mock<IPlaylistRepository>().Object,
            mockTrackRepo.Object,
            new Mock<IEventBus>().Object);

        // Act
        IReadOnlyList<Track> resolvedByIds = await sut.ResolvePlaylistTracksAsync(idBasedPlaylist);
        IReadOnlyList<Track> resolvedByPaths = await sut.ResolvePlaylistTracksAsync(pathBasedPlaylist);

        // Assert
        Assert.Equal([track2.Id, track1.Id], resolvedByIds.Select(t => t.Id).ToArray());
        Assert.Equal([track2.Id, track1.Id], resolvedByPaths.Select(t => t.Id).ToArray());
    }

    /// <summary>
    /// ロックタイムアウトで読み込みが失敗した直後に SavePlaylists（全件置換）を呼んでも既存プレイリストが消失せず、単一保存（TrySavePlaylist）では既存分を保持したまま追加できることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadPlaylistsおよびSavePlaylists_ロックタイムアウト後の全件保存_既存プレイリストが消失しないこと()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistLockGuard_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string playlistsLockPath = $"{playlistsPath}.lock";
            var shortTimeoutLockHelper = new JsonFileLockHelper(TimeSpan.FromMilliseconds(100));

            var existing = new UserPlaylist(PlaylistId.New(), "既存プレイリスト");
            using (var seedRepo = new JsonPlaylistRepository(playlistsPath, shortTimeoutLockHelper))
            {
                await seedRepo.SaveAsync(existing);
            }

            using var wpfRepo = new JsonPlaylistRepository(playlistsPath, shortTimeoutLockHelper);
            var sut = new PlaylistApplicationService(wpfRepo, new Mock<ITrackRepository>().Object, new Mock<IEventBus>().Object);

            // ロック保持中に読み込みを実行してタイムアウトさせる
            Result<List<UserPlaylist>> failedLoad;
            using (var lockHold = new FileStream(playlistsLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                failedLoad = sut.TryLoadPlaylists();
            }

            // Act 1: ロック解放後に空一覧＋新規 1 件で全件保存を試みる（ガードにより拒否されるべき）
            var added = new UserPlaylist(PlaylistId.New(), "新規プレイリスト");
            Result blockedSave = sut.TrySavePlaylists([added]);

            // Assert 1: 既存プレイリストがディスク上に残っている
            Assert.True(failedLoad.IsFailure);
            Assert.True(blockedSave.IsFailure);
            using (var verifyRepo1 = new JsonPlaylistRepository(playlistsPath, shortTimeoutLockHelper))
            {
                var persisted1 = await verifyRepo1.GetAllAsync();
                Assert.Equal(existing.Id, Assert.Single(persisted1).Id);
            }

            // Act 2: 単一保存（TrySavePlaylist）なら既存分を消さずに新規追加できる
            Result singleSave = sut.TrySavePlaylist(added);

            // Assert 2: 既存＋新規の 2 件が保存されている
            Assert.True(singleSave.IsSuccess);
            using var verifyRepo2 = new JsonPlaylistRepository(playlistsPath, shortTimeoutLockHelper);
            var persisted2 = await verifyRepo2.GetAllAsync();
            Assert.Equal(2, persisted2.Count);
            Assert.Contains(persisted2, p => p.Id == existing.Id);
            Assert.Contains(persisted2, p => p.Id == added.Id);
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
    /// ID 形式（TrackIds のみ）の既存プレイリストに対して曲を追加・削除して保存した場合、再読み込み後の WPF（TrackPaths）と MCP（ResolvePlaylistTracksAsync）で同じ収録曲・同じ順序が解決されることを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksPreservingOrderおよびSyncPlaylistTracks_ID形式プレイリストの追加削除_再読み込み後もWPFとMCPで順序が一致すること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistIdEdit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");

            var trackA = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "a.flac")), title: "Track A", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
            var trackB = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "b.flac")), title: "Track B", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(200));

            using (var seedTrackRepo = new JsonTrackRepository(tracksPath))
            using (var seedPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                await seedTrackRepo.SaveRangeAsync([trackA, trackB]);
                // ID 形式のみ（TrackPaths は空）で保存
                await seedPlaylistRepo.SaveAsync(new UserPlaylist(PlaylistId.New(), "ID形式リスト", [trackA.Id]));
            }

            // Act 1: 読み込み → Track B を追加 → 保存
            using (var editTrackRepo = new JsonTrackRepository(tracksPath))
            using (var editPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                var editService = new PlaylistApplicationService(editPlaylistRepo, editTrackRepo, new Mock<IEventBus>().Object);
                UserPlaylist loaded = Assert.Single(editService.LoadPlaylists());
                IReadOnlyList<Track> added = editService.AddTracksPreservingOrder(loaded, [trackB]);
                Assert.Single(added);
                Assert.True(editService.TrySavePlaylist(loaded).IsSuccess);
            }

            // Assert 1: 再読み込み後、WPF（TrackPaths）と MCP（ResolvePlaylistTracksAsync）がともに [A, B] 順で一致
            using (var verifyTrackRepo = new JsonTrackRepository(tracksPath))
            using (var verifyPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                var verifyService = new PlaylistApplicationService(verifyPlaylistRepo, verifyTrackRepo, new Mock<IEventBus>().Object);
                UserPlaylist reloaded = Assert.Single(await verifyService.GetAllPlaylistsAsync());
                IReadOnlyList<Track> mcpResolved = await verifyService.ResolvePlaylistTracksAsync(reloaded);

                Assert.Equal([trackA.FilePath, trackB.FilePath], reloaded.TrackPaths);
                Assert.Equal([trackA.Id, trackB.Id], reloaded.TrackIds);
                Assert.Equal([trackA.Id, trackB.Id], mcpResolved.Select(t => t.Id).ToArray());

                // Act 2: Track A を削除して同期保存
                PlaylistApplicationService.SyncPlaylistTracks(reloaded, [trackB]);
                Assert.True(verifyService.TrySavePlaylist(reloaded).IsSuccess);
            }

            // Assert 2: 削除後の再読み込みで WPF・MCP ともに [B] のみになる
            using var finalTrackRepo = new JsonTrackRepository(tracksPath);
            using var finalPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            var finalService = new PlaylistApplicationService(finalPlaylistRepo, finalTrackRepo, new Mock<IEventBus>().Object);
            UserPlaylist afterDelete = Assert.Single(await finalService.GetAllPlaylistsAsync());
            IReadOnlyList<Track> finalResolved = await finalService.ResolvePlaylistTracksAsync(afterDelete);

            Assert.Equal([trackB.FilePath], afterDelete.TrackPaths);
            Assert.Equal([trackB.Id], afterDelete.TrackIds);
            Assert.Equal([trackB.Id], finalResolved.Select(t => t.Id).ToArray());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
