using System;
using System.Collections.Generic;
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
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
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
        mockPlaylistRepo.Verify(r => r.SaveAsync(playlist, It.IsAny<CancellationToken>()), Times.Once);
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
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
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
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
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
        mockPlaylistRepo.Verify(r => r.SaveAsync(playlist, It.IsAny<CancellationToken>()), Times.Once);
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
        mockPlaylistRepo.Setup(r => r.GetByIdAsync(playlistId, It.IsAny<CancellationToken>()))
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
        mockPlaylistRepo.Verify(r => r.SaveAsync(playlist, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaylistUpdatedEvent>(e => e.Playlist == playlist), It.IsAny<CancellationToken>()), Times.Once);
    }
}
