using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Repository;
using Moq;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

/// <summary>
/// <see cref="PlaylistApplicationService"/> と JSON リポジトリ実装（ロック・共有違反・再読み込み整合性）の結合動作を検証するテストクラス
/// </summary>
public sealed class PlaylistPersistenceIntegrationTests
{
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

    /// <summary>
    /// 旧 WPF 形式のプレイリストにライブラリ未登録のトラック X を含む状態で曲 B を追記しても X が消失せず、再起動・X 再登録後にも [A, X, B] の順序で復元できることを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksPreservingOrder_旧形式に未解決トラックを含む場合_未解決参照を保持し再登録後にA_X_B順で復元できること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistUnresolvedKeep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");

            string pathA = Path.Combine(tempDir, "a.flac");
            string pathX = Path.Combine(tempDir, "x.flac");
            string pathB = Path.Combine(tempDir, "b.flac");

            var trackA = new Track(TrackId.New(), AudioPath.Create(pathA), title: "Track A", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
            var trackB = new Track(TrackId.New(), AudioPath.Create(pathB), title: "Track B", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(190));
            var trackX = new Track(TrackId.New(), AudioPath.Create(pathX), title: "Track X", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(200));

            // ライブラリには A と B のみ登録（X は未登録）
            using (var seedTrackRepo = new JsonTrackRepository(tracksPath))
            {
                await seedTrackRepo.SaveRangeAsync([trackA, trackB]);
            }

            // 旧 WPF 形式で [A, X] を保存
            string legacyJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new { Name = "Legacy Partial", TrackPaths = new[] { pathA, pathX } },
            });
            await File.WriteAllTextAsync(playlistsPath, legacyJson);

            // Act 1: B を追記して保存
            using (var editTrackRepo = new JsonTrackRepository(tracksPath))
            using (var editPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                var editService = new PlaylistApplicationService(editPlaylistRepo, editTrackRepo, new Mock<IEventBus>().Object);
                UserPlaylist loaded = Assert.Single(editService.LoadPlaylists());
                Result<IReadOnlyList<Track>> addResult = editService.TryAddTracksPreservingOrder(loaded, [trackB]);
                Assert.True(addResult.IsSuccess);
                Assert.Single(addResult.Value);
                Assert.True(editService.TrySavePlaylist(loaded).IsSuccess);
            }

            // Assert 1: 再起動相当（別インスタンス）で読み直しても TrackPaths は [A, X, B] を保持している
            using (var checkPlaylistRepo = new JsonPlaylistRepository(playlistsPath))
            {
                UserPlaylist reloaded = Assert.Single(await checkPlaylistRepo.GetAllAsync());
                Assert.Equal([pathA, pathX, pathB], reloaded.TrackPaths);
                Assert.Empty(reloaded.TrackIds);
            }

            // Act 2: 後から X をライブラリへ再登録し、別インスタンスで収録曲を解決
            using (var reregisterTrackRepo = new JsonTrackRepository(tracksPath))
            {
                await reregisterTrackRepo.SaveAsync(trackX);
            }

            using var finalTrackRepo = new JsonTrackRepository(tracksPath);
            using var finalPlaylistRepo = new JsonPlaylistRepository(playlistsPath);
            var finalService = new PlaylistApplicationService(finalPlaylistRepo, finalTrackRepo, new Mock<IEventBus>().Object);
            UserPlaylist finalPlaylist = Assert.Single(await finalService.GetAllPlaylistsAsync());
            IReadOnlyList<Track> resolved = await finalService.ResolvePlaylistTracksAsync(finalPlaylist);

            // Assert 2: [A, X, B] の収録順が完全に復元される
            Assert.Equal([trackA.Id, trackX.Id, trackB.Id], resolved.Select(t => t.Id).ToArray());
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
    /// playlists.json 本体が共有違反（FileShare.None）で読み込めない場合、TryLoadPlaylists が失敗を返し、ハンドル解放後の TrySavePlaylists 全件置換が拒否されて既存プレイリストが保護されることを検証します。
    /// </summary>
    [Fact]
    public async Task TryLoadPlaylistsおよびTrySavePlaylists_JSON本体共有違反時_読み込み失敗となり全件置換が拒否されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistShareViolation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            var existing = new UserPlaylist(PlaylistId.New(), "既存プレイリスト");
            using (var seedRepo = new JsonPlaylistRepository(playlistsPath))
            {
                await seedRepo.SaveAsync(existing);
            }

            using var wpfRepo = new JsonPlaylistRepository(playlistsPath);
            var sut = new PlaylistApplicationService(wpfRepo, new Mock<ITrackRepository>().Object, new Mock<IEventBus>().Object);

            // Act 1: playlists.json 本体を排他オープンした状態で読み込む
            Result<List<UserPlaylist>> loadResult;
            using (var exclusiveHold = new FileStream(playlistsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                loadResult = sut.TryLoadPlaylists();
            }

            // Act 2: ハンドル解放後に新規 1 件だけで全件保存を試みる
            var added = new UserPlaylist(PlaylistId.New(), "新規プレイリスト");
            Result saveResult = sut.TrySavePlaylists([added]);

            // Assert: 読み込み失敗と全件置換拒否が成立し、ディスク上の既存 1 件が保護されている
            Assert.True(loadResult.IsFailure);
            Assert.True(saveResult.IsFailure);
            using var verifyRepo = new JsonPlaylistRepository(playlistsPath);
            var persisted = await verifyRepo.GetAllAsync();
            Assert.Equal(existing.Id, Assert.Single(persisted).Id);
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
    /// favorites.json.lock が保持されている状態でも TryAddTracksPreservingOrder がロック待ちせず成功し、トラックリポジトリ読み取り失敗時は例外を送出せず Result.Failure を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task TryAddTracksPreservingOrder_お気に入りロック保持中およびトラック読み取り失敗時_ロック非依存で成功し読み取り失敗時はFailureを返すこと()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorPlaylistFavLockIndependent_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string playlistsPath = Path.Combine(tempDir, "playlists.json");
            string tracksPath = Path.Combine(tempDir, "tracks.json");
            string favoritesPath = Path.Combine(tempDir, "favorites.json");
            string favoritesLockPath = $"{favoritesPath}.lock";
            var shortTimeoutHelper = new JsonFileLockHelper(TimeSpan.FromMilliseconds(100));

            var track1 = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "1.flac")), title: "T1", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(180));
            var track2 = new Track(TrackId.New(), AudioPath.Create(Path.Combine(tempDir, "2.flac")), title: "T2", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(180));

            using var playlistRepo = new JsonPlaylistRepository(playlistsPath, shortTimeoutHelper);
            using var trackRepo = new JsonTrackRepository(tracksPath, shortTimeoutHelper);
            using var favRepo = new JsonFavoriteRepository(favoritesPath, shortTimeoutHelper);
            await trackRepo.SaveRangeAsync([track1, track2]);

            var sut = new PlaylistApplicationService(playlistRepo, trackRepo, new Mock<IEventBus>().Object, favRepo);
            var emptyPlaylist = new UserPlaylist(PlaylistId.New(), "空プレイリスト");
            var existingPlaylist = new UserPlaylist(PlaylistId.New(), "既存あり", [track1.Id]);

            // Act 1: favorites.json.lock を排他保持した状態で曲追加を実行
            Result<IReadOnlyList<Track>> emptyAddResult;
            Result<IReadOnlyList<Track>> existingAddResult;
            using (var favLockHold = new FileStream(favoritesLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                emptyAddResult = sut.TryAddTracksPreservingOrder(emptyPlaylist, [track1]);
                existingAddResult = sut.TryAddTracksPreservingOrder(existingPlaylist, [track2]);
            }

            // Assert 1: お気に入りロックに影響されず両方成功
            Assert.True(emptyAddResult.IsSuccess);
            Assert.Equal([track1.Id], emptyPlaylist.TrackIds);
            Assert.True(existingAddResult.IsSuccess);
            Assert.Equal([track1.Id, track2.Id], existingPlaylist.TrackIds);

            // Act 2: トラック読み取り自体がロックタイムアウトした場合は未処理例外にならず Failure を返す
            var failingTrackRepo = new Mock<ITrackRepository>();
            failingTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FileLockTimeoutException(tracksPath, TimeSpan.FromMilliseconds(100)));
            var failingSut = new PlaylistApplicationService(playlistRepo, failingTrackRepo.Object, new Mock<IEventBus>().Object, favRepo);

            Result<IReadOnlyList<Track>> failedResult = failingSut.TryAddTracksPreservingOrder(existingPlaylist, [track2]);

            // Assert 2
            Assert.True(failedResult.IsFailure);
            Assert.False(string.IsNullOrWhiteSpace(failedResult.ErrorMessage));
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
