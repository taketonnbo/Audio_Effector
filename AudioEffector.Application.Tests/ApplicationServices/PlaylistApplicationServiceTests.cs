using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AudioEffector.Application.Tests.ApplicationServices;

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
    /// CreatePlaylistAsync に初期トラック ID 一覧を指定した場合、TrackIds と TrackPaths の整合性を保って保存されイベントが発行されることを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistAsync_初期トラックID一覧指定_順序とTrackPaths整合性を保って保存されること()
    {
        // Arrange
        var track1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\track1.flac"), title: "Song 1", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
        var track2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\track2.flac"), title: "Song 2", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(200));

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, track2]);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        UserPlaylist actual = await sut.CreatePlaylistAsync("  ドライブBGM  ", [track2.Id, track1.Id]);

        // Assert
        Assert.Equal("ドライブBGM", actual.Name);
        Assert.Equal([track2.Id, track1.Id], actual.TrackIds.ToArray());
        Assert.Equal([track2.FilePath, track1.FilePath], actual.TrackPaths.ToArray());
        Assert.Equal(2, actual.TrackCount);
        mockPlaylistRepo.Verify(r => r.SaveAsync(actual, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == actual), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// CreatePlaylistWithTracksAsync に trackIds と filePaths を併用し重複が含まれる場合、初出順で登録され重複項目が SkippedItems に記録されることを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistWithTracksAsync_trackIdsとfilePaths併用かつ重複あり_初出順で登録し重複をスキップすること()
    {
        // Arrange
        var track1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\first.flac"), title: "First", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
        var track2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\second.flac"), title: "Second", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(210));

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([track1, track2]);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        PlaylistCreationResult actual = await sut.CreatePlaylistWithTracksAsync(
            "Night Mix",
            initialTrackIds: [track1.Id, track1.Id],
            initialFilePaths: [@"c:\music\first.flac", @"C:\Music\second.flac"]);

        // Assert
        Assert.Equal("Night Mix", actual.Playlist.Name);
        Assert.Equal([track1.Id, track2.Id], actual.Playlist.TrackIds.ToArray());
        Assert.Equal([track1.FilePath, track2.FilePath], actual.Playlist.TrackPaths.ToArray());
        Assert.Equal([track1.Id, track2.Id], actual.Tracks.Select(t => t.Id).ToArray());
        Assert.Equal(2, actual.SkippedItems.Count);
        Assert.Equal(track1.Id.ToString(), actual.SkippedItems[0]);
        Assert.Equal(@"c:\music\first.flac", actual.SkippedItems[1]);
        mockPlaylistRepo.Verify(r => r.SaveAsync(actual.Playlist, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// CreatePlaylistAsync に空名・空白名または最大長超過の名前を指定した場合、ArgumentException がスローされ保存されないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePlaylistAsync_不正なプレイリスト名指定_ArgumentExceptionをスローし保存しないこと(string invalidName)
    {
        // Arrange
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreatePlaylistAsync(invalidName));
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CreatePlaylistAsync(new string('A', PlaylistApplicationService.MAX_PLAYLIST_NAME_LENGTH + 1)));

        mockPlaylistRepo.Verify(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<PlaylistUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// CreatePlaylistWithTracksAsync にライブラリ未登録の trackId または filePath を指定した場合、KeyNotFoundException をスローし不完全なプレイリストを保存しないことを検証します。
    /// </summary>
    [Fact]
    public async Task CreatePlaylistWithTracksAsync_存在しないトラック指定_KeyNotFoundExceptionをスローし保存しないこと()
    {
        // Arrange
        var existingTrack = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\exists.flac"), title: "Exists", artist: "Artist", album: "Album", duration: TimeSpan.FromSeconds(180));
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingTrack]);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.CreatePlaylistWithTracksAsync("MissingById", initialTrackIds: [existingTrack.Id, TrackId.New()]));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.CreatePlaylistWithTracksAsync("MissingByPath", initialFilePaths: [@"C:\Music\missing.flac"]));

        mockPlaylistRepo.Verify(r => r.SaveAsync(It.IsAny<UserPlaylist>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<PlaylistUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
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
    /// DeletePlaylistWithResultAsync で登録済みのプレイリスト ID を指定した場合、削除が実行されイベント発行後に削除前エンティティを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task DeletePlaylistWithResultAsync_登録済みプレイリストID_削除されたプレイリストを返しイベント発行すること()
    {
        // Arrange
        var playlistId = PlaylistId.New();
        var playlist = new UserPlaylist(playlistId, "削除対象", [TrackId.New(), TrackId.New()]);

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playlist);

        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        UserPlaylist? actual = await sut.DeletePlaylistWithResultAsync(playlistId);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(playlistId, actual.Id);
        Assert.Equal("削除対象", actual.Name);
        Assert.Equal(2, actual.TrackCount);
        mockPlaylistRepo.Verify(r => r.DeleteAsync(playlistId, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// DeletePlaylistWithResultAsync で存在しないプレイリスト ID を指定した場合、null を返し削除処理とイベント発行を行わないことを検証します。
    /// </summary>
    [Fact]
    public async Task DeletePlaylistWithResultAsync_存在しないプレイリストID_nullを返し削除とイベント発行を行わないこと()
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
        UserPlaylist? actual = await sut.DeletePlaylistWithResultAsync(playlistId);

        // Assert
        Assert.Null(actual);
        mockPlaylistRepo.Verify(r => r.DeleteAsync(It.IsAny<PlaylistId>(), It.IsAny<CancellationToken>()), Times.Never);
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
    /// AddTracksAsync で複数トラック ID とファイルパスを一括追加した際、1 回の更新で重複を除外しつつ TrackIds と TrackPaths が整合して永続化されることを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksAsync_複数トラックIDとファイルパス一括追加_重複スキップとTrackPaths整合性を維持すること()
    {
        // Arrange
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\track1.flac"), title: "T1", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(180));
        var t2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\track2.flac"), title: "T2", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(180));
        var t3 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\track3.flac"), title: "T3", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(180));
        var playlist = new UserPlaylist(PlaylistId.New(), "Drive", [t1.Id]) { TrackPaths = [t1.FilePath] };

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);

        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([t1, t2, t3]);

        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        PlaylistUpdateResult? actual = await sut.AddTracksAsync(
            playlist.Id,
            [t1.Id, t2.Id],
            [@"c:\music\track2.flac", @"C:\Music\track3.flac"]);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(2, actual.ChangedCount);
        Assert.Equal([t1.Id.ToString(), @"c:\music\track2.flac"], actual.SkippedItems.ToArray());
        Assert.Equal([t1.Id, t2.Id, t3.Id], actual.Playlist.TrackIds.ToArray());
        Assert.Equal([t1.FilePath, t2.FilePath, t3.FilePath], actual.Playlist.TrackPaths.ToArray());
        Assert.Equal([t1.Id, t2.Id, t3.Id], actual.Tracks.Select(t => t.Id).ToArray());
        mockPlaylistRepo.Verify(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// AddTracksAsync で存在しないトラック ID を指定した場合、KeyNotFoundException をスローしプレイリストを更新しないことを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksAsync_存在しないトラックID指定_KeyNotFoundExceptionをスローし更新しないこと()
    {
        // Arrange
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Track>());
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, new Mock<IEventBus>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.AddTracksAsync(PlaylistId.New(), [TrackId.New()]));
        mockPlaylistRepo.Verify(r => r.UpdateAsync(It.IsAny<PlaylistId>(), It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// AddTracksAsync で存在しないプレイリスト ID を指定した場合、null を返しイベントを発行しないことを検証します。
    /// </summary>
    [Fact]
    public async Task AddTracksAsync_存在しないプレイリストID指定_nullを返すこと()
    {
        // Arrange
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\track1.flac"), title: "T1", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(180));
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(It.IsAny<PlaylistId>(), It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([t1]);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        PlaylistUpdateResult? actual = await sut.AddTracksAsync(PlaylistId.New(), [t1.Id]);

        // Assert
        Assert.Null(actual);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<PlaylistUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// RemoveTracksAsync で複数インデックスを昇順・重複混在で指定した場合でも、降順削除によりインデックスずれなく意図通りのトラックが削除されることを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveTracksAsync_複数インデックス同時指定_降順削除によりずれなく削除されること()
    {
        // Arrange
        var t0 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t0.flac"), title: "T0", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t1.flac"), title: "T1", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var t2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t2.flac"), title: "T2", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var t3 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t3.flac"), title: "T3", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var playlist = new UserPlaylist(PlaylistId.New(), "Mix", [t0.Id, t1.Id, t2.Id, t3.Id])
        {
            TrackPaths = [t0.FilePath, t1.FilePath, t2.FilePath, t3.FilePath],
        };

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([t0, t1, t2, t3]);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act: 0 番目と 2 番目を同時削除（重複指定 0 を含む）
        PlaylistUpdateResult? actual = await sut.RemoveTracksAsync(playlist.Id, [0, 2, 0], trackIds: null);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(2, actual.ChangedCount);
        Assert.Equal([t1.Id, t3.Id], actual.Playlist.TrackIds.ToArray());
        Assert.Equal([t1.FilePath, t3.FilePath], actual.Playlist.TrackPaths.ToArray());
        Assert.Equal([t1.Id, t3.Id], actual.Tracks.Select(t => t.Id).ToArray());
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// RemoveTracksAsync で trackIds を指定した際、一致したトラックを削除し、プレイリストに含まれない ID を SkippedItems へ記録することを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveTracksAsync_トラックID指定と未一致混在_一致分を削除し未一致をSkippedItemsへ返すこと()
    {
        // Arrange
        var t0 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t0.flac"), title: "T0", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t1.flac"), title: "T1", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var absentId = TrackId.New();
        var playlist = new UserPlaylist(PlaylistId.New(), "Mix", [t0.Id, t1.Id]) { TrackPaths = [t0.FilePath, t1.FilePath] };

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([t0, t1]);
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, new Mock<IEventBus>().Object);

        // Act
        PlaylistUpdateResult? actual = await sut.RemoveTracksAsync(playlist.Id, indices: null, trackIds: [t0.Id, absentId]);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(1, actual.ChangedCount);
        Assert.Equal([absentId.ToString()], actual.SkippedItems.ToArray());
        Assert.Equal([t1.Id], actual.Playlist.TrackIds.ToArray());
        Assert.Equal([t1.FilePath], actual.Playlist.TrackPaths.ToArray());
    }

    /// <summary>
    /// RemoveTracksAsync で範囲外インデックスを指定した場合、ArgumentOutOfRangeException をスローしプレイリストを変更しないことを検証します。
    /// </summary>
    [Fact]
    public async Task RemoveTracksAsync_範囲外インデックス指定_ArgumentOutOfRangeExceptionをスローし変更しないこと()
    {
        // Arrange
        var t0 = TrackId.New();
        var playlist = new UserPlaylist(PlaylistId.New(), "Mix", [t0]);
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Track>());
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, new Mock<IEventBus>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.RemoveTracksAsync(playlist.Id, [0, 5], trackIds: null));
        Assert.Single(playlist.TrackIds);
    }

    /// <summary>
    /// ReorderPlaylistTrackAsync で有効なインデックスを指定した際、TrackIds と TrackPaths の双方が整合して並び替わることを検証します。
    /// </summary>
    [Fact]
    public async Task ReorderPlaylistTrackAsync_有効インデックス指定_TrackIdsとTrackPathsが整合して並び替わること()
    {
        // Arrange
        var t0 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t0.flac"), title: "T0", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var t1 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t1.flac"), title: "T1", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var t2 = new Track(TrackId.New(), AudioPath.Create(@"C:\Music\t2.flac"), title: "T2", artist: "A", album: "Al", duration: TimeSpan.FromSeconds(100));
        var playlist = new UserPlaylist(PlaylistId.New(), "Order", [t0.Id, t1.Id, t2.Id])
        {
            TrackPaths = [t0.FilePath, t1.FilePath, t2.FilePath],
        };

        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([t0, t1, t2]);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act: 2 番目を先頭（0 番目）へ移動
        PlaylistUpdateResult? actual = await sut.ReorderPlaylistTrackAsync(playlist.Id, 2, 0);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(1, actual.ChangedCount);
        Assert.Equal([t2.Id, t0.Id, t1.Id], actual.Playlist.TrackIds.ToArray());
        Assert.Equal([t2.FilePath, t0.FilePath, t1.FilePath], actual.Playlist.TrackPaths.ToArray());
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ReorderPlaylistTrackAsync で範囲外インデックスを指定した場合、ArgumentOutOfRangeException をスローすることを検証します。
    /// </summary>
    [Fact]
    public async Task ReorderPlaylistTrackAsync_範囲外インデックス指定_ArgumentOutOfRangeExceptionをスローすること()
    {
        // Arrange
        var playlist = new UserPlaylist(PlaylistId.New(), "Order", [TrackId.New()]);
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, new Mock<ITrackRepository>().Object, new Mock<IEventBus>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.ReorderPlaylistTrackAsync(playlist.Id, 0, 2));
    }

    /// <summary>
    /// RenamePlaylistAsync で有効な新名称を指定した際、前後空白をトリムして名称変更し PlaylistUpdatedEvent を発行することを検証します。
    /// </summary>
    [Fact]
    public async Task RenamePlaylistAsync_有効な新名称指定_トリムして名称変更しイベント発行すること()
    {
        // Arrange
        var playlist = new UserPlaylist(PlaylistId.New(), "Old Name");
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(playlist.Id, It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .Callback<PlaylistId, Func<UserPlaylist, bool>, CancellationToken>((_, update, _) => update(playlist))
            .ReturnsAsync(playlist);
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Track>());
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        PlaylistUpdateResult? actual = await sut.RenamePlaylistAsync(playlist.Id, "  New Jazz Name  ");

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(1, actual.ChangedCount);
        Assert.Equal("New Jazz Name", actual.Playlist.Name);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// RenamePlaylistAsync で存在しないプレイリスト ID を指定した場合、null を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task RenamePlaylistAsync_存在しないプレイリストID指定_nullを返すこと()
    {
        // Arrange
        var mockPlaylistRepo = new Mock<IPlaylistRepository>();
        mockPlaylistRepo.Setup(r => r.UpdateAsync(It.IsAny<PlaylistId>(), It.IsAny<Func<UserPlaylist, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPlaylist?)null);
        var mockEventBus = new Mock<IEventBus>();
        var sut = new PlaylistApplicationService(mockPlaylistRepo.Object, new Mock<ITrackRepository>().Object, mockEventBus.Object);

        // Act
        PlaylistUpdateResult? actual = await sut.RenamePlaylistAsync(PlaylistId.New(), "Renamed");

        // Assert
        Assert.Null(actual);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<PlaylistUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
