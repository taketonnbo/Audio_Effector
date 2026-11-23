using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Audio;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Audio;

/// <summary>
/// Issue #225 で導入された再生キューのセクション分離モデル（予約キュー／アルバムの残り曲）の
/// 順序制御、優先消費、統合、シャッフル適用範囲限定、クリア動作を網羅的に検証するテストクラス。
/// </summary>
public sealed class AudioServiceSectionQueueTests
{
    private static List<Track> CreateSampleTracks(string prefix, int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Track
            {
                FilePath = $@"C:\Music\{prefix}{i}.mp3",
                Title = $"{prefix}{i}",
                Artist = "Artist",
                Album = $"Album {prefix}",
                TrackNumber = (uint)i,
                Duration = TimeSpan.FromMinutes(3)
            })
            .ToList();
    }

    /// <summary>
    /// 予約キューに曲が存在する場合、アルバムの残り曲よりも優先して再生されることを検証します。
    /// </summary>
    [Fact]
    public void Next_予約キューが存在する場合_アルバムキューより優先して消費される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 3); // A1, A2, A3
        sut.SetPlaylist(albumTracks, albumTracks[0]); // A1再生中、AlbumQueue: [A2, A3]

        var userTracks = CreateSampleTracks("U", 2); // U1, U2
        sut.EnqueueTracks(userTracks, playNext: true); // UserQueue: [U1, U2]

        Track? currentTrack = null;
        sut.TrackChanged += t => currentTrack = t;

        // Act & Assert 1: 次へ進む -> U1が再生される
        sut.Next();
        Assert.NotNull(currentTrack);
        Assert.Equal("U1", currentTrack.Title);
        Assert.Single(sut.UserQueue);
        Assert.Equal("U2", sut.UserQueue[0].Title);
        Assert.Equal(2, sut.AlbumQueue.Count);

        // Act & Assert 2: 次へ進む -> U2が再生される
        sut.Next();
        Assert.NotNull(currentTrack);
        Assert.Equal("U2", currentTrack.Title);
        Assert.Empty(sut.UserQueue);
        Assert.Equal(2, sut.AlbumQueue.Count);

        // Act & Assert 3: 予約キュー消化後に次へ進む -> アルバムキュー先頭の A2 が再生される
        sut.Next();
        Assert.NotNull(currentTrack);
        Assert.Equal("A2", currentTrack.Title);
        Assert.Empty(sut.UserQueue);
        Assert.Single(sut.AlbumQueue);
        Assert.Equal("A3", sut.AlbumQueue[0].Title);
    }

    /// <summary>
    /// 「最後に再生」（playNext: false）を実行した場合、予約キューとアルバム残りが統合され、
    /// すべて予約キュー（UserQueue）として管理されることを検証します。
    /// </summary>
    [Fact]
    public void EnqueueTracks_最後に再生実行時_アルバム残りが予約キューに統合され末尾に追加される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumA = CreateSampleTracks("A", 3); // A1, A2, A3
        sut.SetPlaylist(albumA, albumA[0]); // A1再生、AlbumQueue: [A2, A3]

        var userTrack = CreateSampleTracks("U", 1); // U1
        sut.EnqueueTracks(userTrack, playNext: true); // UserQueue: [U1], AlbumQueue: [A2, A3]

        var lastTracks = CreateSampleTracks("L", 2); // L1, L2

        // Act - 「最後に再生」を実行
        sut.EnqueueTracks(lastTracks, playNext: false);

        // Assert
        // AlbumQueue はすべて UserQueue に統合されて空になること
        Assert.Empty(sut.AlbumQueue);
        // UserQueue は [U1, A2, A3, L1, L2] の 5曲になること
        Assert.Equal(5, sut.UserQueue.Count);
        Assert.Equal("U1", sut.UserQueue[0].Title);
        Assert.Equal("A2", sut.UserQueue[1].Title);
        Assert.Equal("A3", sut.UserQueue[2].Title);
        Assert.Equal("L1", sut.UserQueue[3].Title);
        Assert.Equal("L2", sut.UserQueue[4].Title);
    }

    /// <summary>
    /// シャッフルをON/OFFした際、シャッフルの影響範囲がAlbumQueueに限定され、
    /// UserQueue（予約キュー）の順序が完全に保護・維持されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_切り替え時_予約キューの順序は保護されアルバムキューのみが対象となる()
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 10);
        sut.SetPlaylist(albumTracks, albumTracks[0]); // A1再生、AlbumQueue: A2〜A10 (9曲)

        var userTracks = CreateSampleTracks("U", 3); // U1, U2, U3
        sut.EnqueueTracks(userTracks, playNext: true); // UserQueue: [U1, U2, U3]

        // Act 1: シャッフル有効化
        sut.IsShuffleEnabled = true;

        // Assert 1: UserQueue は不変
        Assert.Equal(3, sut.UserQueue.Count);
        Assert.Equal("U1", sut.UserQueue[0].Title);
        Assert.Equal("U2", sut.UserQueue[1].Title);
        Assert.Equal("U3", sut.UserQueue[2].Title);
        // AlbumQueue は 9曲存在すること
        Assert.Equal(9, sut.AlbumQueue.Count);

        // Act 2: シャッフル解除
        sut.IsShuffleEnabled = false;

        // Assert 2: UserQueue は不変、AlbumQueue はアルバム順 (A2〜A10) に復元
        Assert.Equal(3, sut.UserQueue.Count);
        Assert.Equal("U1", sut.UserQueue[0].Title);
        Assert.Equal("U2", sut.UserQueue[1].Title);
        Assert.Equal("U3", sut.UserQueue[2].Title);
        Assert.Equal(9, sut.AlbumQueue.Count);
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal($"A{i + 2}", sut.AlbumQueue[i].Title);
        }
    }

    /// <summary>
    /// シャッフルON時の「最後に再生」において、アルバム単位（複数曲）の追加トラックがシャッフルされて末尾に追加されることを検証します。
    /// </summary>
    [Fact]
    public void EnqueueTracks_シャッフルON時の最後に再生_アルバム収録曲がシャッフルされて末尾追加される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumA = CreateSampleTracks("A", 2); // A1, A2
        sut.SetPlaylist(albumA, albumA[0]);
        sut.IsShuffleEnabled = true;

        var albumB = CreateSampleTracks("B", 10); // B1〜B10

        // Act - シャッフルON時にアルバムBを「最後に再生」
        sut.EnqueueTracks(albumB, playNext: false);

        // Assert
        // AlbumQueue の A2 が統合され、albumB (10曲) が末尾追加されて計 11曲
        Assert.Empty(sut.AlbumQueue);
        Assert.Equal(11, sut.UserQueue.Count);
        Assert.Equal("A2", sut.UserQueue[0].Title);

        // 後続 10曲は B1〜B10 の集合と一致すること
        var tailTitles = sut.UserQueue.Skip(1).Select(t => t.Title).OrderBy(t => t).ToList();
        var expectedTitles = albumB.Select(t => t.Title).OrderBy(t => t).ToList();
        Assert.Equal(expectedTitles, tailTitles);
    }

    /// <summary>
    /// ClearUserQueue を呼び出した際、予約キューのみが全消去され、アルバムキューおよび現在再生中曲が影響を受けないことを検証します。
    /// </summary>
    [Fact]
    public void ClearUserQueue_実行時_予約キューのみがクリアされアルバムキューは維持される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 3);
        sut.SetPlaylist(albumTracks, albumTracks[0]); // A1再生、AlbumQueue: [A2, A3]

        var userTracks = CreateSampleTracks("U", 3);
        sut.EnqueueTracks(userTracks, playNext: true); // UserQueue: [U1, U2, U3]

        Assert.Equal(3, sut.UserQueue.Count);
        Assert.Equal(2, sut.AlbumQueue.Count);

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - 予約クリア実行
        sut.ClearUserQueue();

        // Assert
        Assert.Empty(sut.UserQueue);
        Assert.Equal(2, sut.AlbumQueue.Count);
        Assert.Equal("A2", sut.AlbumQueue[0].Title);
        Assert.Equal("A3", sut.AlbumQueue[1].Title);

        // PlaylistChanged で通知されたキューも AlbumQueue のみ（2曲）であること
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(2, receivedPlaylist.Count);
        Assert.Equal("A2", receivedPlaylist[0].Title);
        Assert.Equal("A3", receivedPlaylist[1].Title);
    }

    /// <summary>
    /// MoveUserQueueItem 実行時、予約キューの順序と PlaylistChanged イベントが正しく同期され、
    /// 次曲再生（Next）で変更後の順序通りに再生されることを検証します。
    /// </summary>
    [Fact]
    public void MoveUserQueueItem_正常なインデックス移動時_予約キュー順序と次曲再生順が同期される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 2); // A1再生、AlbumQueue: [A2]
        sut.SetPlaylist(albumTracks, albumTracks[0]);

        var userTracks = CreateSampleTracks("U", 3); // U1, U2, U3
        sut.EnqueueTracks(userTracks, playNext: true);

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        Track? currentTrack = null;
        sut.TrackChanged += t => currentTrack = t;

        // Act - 先頭の U1(0) を末尾(2)へ移動 -> [U2, U3, U1]
        sut.MoveUserQueueItem(0, 2);

        // Assert
        Assert.Equal(new[] { "U2", "U3", "U1" }, sut.UserQueue.Select(t => t.Title).ToArray());
        Assert.Single(sut.AlbumQueue);
        Assert.Equal("A2", sut.AlbumQueue[0].Title);
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(new[] { "U2", "U3", "U1", "A2" }, receivedPlaylist.Select(t => t.Title).ToArray());

        // Act & Assert - 後ろから前へ移動: U1(2) を先頭(0)へ -> [U1, U2, U3]
        sut.MoveUserQueueItem(2, 0);
        Assert.Equal(new[] { "U1", "U2", "U3" }, sut.UserQueue.Select(t => t.Title).ToArray());

        // 次曲再生で U1 が優先再生されること
        sut.Next();
        Assert.NotNull(currentTrack);
        Assert.Equal("U1", currentTrack.Title);
    }

    /// <summary>
    /// MoveUserQueueItem に範囲外または同一インデックスを指定した場合、状態が変更されずイベントも発火しないことを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 5)]
    [InlineData(1, 1)]
    public void MoveUserQueueItem_範囲外または同一インデックス時_状態不変かつイベント未発火である(int fromIndex, int toIndex)
    {
        // Arrange
        using var sut = new AudioService();
        var userTracks = CreateSampleTracks("U", 3);
        sut.EnqueueTracks(userTracks, playNext: true);

        int eventCount = 0;
        sut.PlaylistChanged += _ => eventCount++;

        // Act
        sut.MoveUserQueueItem(fromIndex, toIndex);

        // Assert
        Assert.Equal(0, eventCount);
        Assert.Equal(new[] { "U1", "U2", "U3" }, sut.UserQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// MoveAlbumQueueItem 実行時、アルバムキューの順序と PlaylistChanged イベントが同期され、
    /// 次曲再生（Next）で変更後の順序通りに再生されることを検証します。
    /// </summary>
    [Fact]
    public void MoveAlbumQueueItem_正常なインデックス移動時_アルバムキュー順序と次曲再生順が同期される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 4); // A1再生、AlbumQueue: [A2, A3, A4]
        sut.SetPlaylist(albumTracks, albumTracks[0]);

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        Track? currentTrack = null;
        sut.TrackChanged += t => currentTrack = t;

        // Act - A4(2) を先頭(0)へ移動 -> [A4, A2, A3]
        sut.MoveAlbumQueueItem(2, 0);

        // Assert
        Assert.Equal(new[] { "A4", "A2", "A3" }, sut.AlbumQueue.Select(t => t.Title).ToArray());
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(new[] { "A4", "A2", "A3" }, receivedPlaylist.Select(t => t.Title).ToArray());

        // 次曲再生で A4 が再生されること
        sut.Next();
        Assert.NotNull(currentTrack);
        Assert.Equal("A4", currentTrack.Title);
    }

    /// <summary>
    /// MoveAlbumQueueItem に範囲外または同一インデックスを指定した場合、状態が変更されずイベントも発火しないことを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 3)]
    [InlineData(0, 0)]
    public void MoveAlbumQueueItem_範囲外または同一インデックス時_状態不変かつイベント未発火である(int fromIndex, int toIndex)
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 4); // AlbumQueue: [A2, A3, A4]
        sut.SetPlaylist(albumTracks, albumTracks[0]);

        int eventCount = 0;
        sut.PlaylistChanged += _ => eventCount++;

        // Act
        sut.MoveAlbumQueueItem(fromIndex, toIndex);

        // Assert
        Assert.Equal(0, eventCount);
        Assert.Equal(new[] { "A2", "A3", "A4" }, sut.AlbumQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// MoveAlbumQueueItemToUserQueue 実行時、アルバムキューの楽曲が予約キューの指定位置へ昇格移動し、
    /// 次曲再生順にも即座に反映されることを検証します。
    /// </summary>
    [Fact]
    public void MoveAlbumQueueItemToUserQueue_正常な移動時_予約キューへ昇格し優先再生される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 4); // A1再生、AlbumQueue: [A2, A3, A4]
        sut.SetPlaylist(albumTracks, albumTracks[0]);

        var userTracks = CreateSampleTracks("U", 2); // UserQueue: [U1, U2]
        sut.EnqueueTracks(userTracks, playNext: true);

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        Track? currentTrack = null;
        sut.TrackChanged += t => currentTrack = t;

        // Act - AlbumQueue の A4(index=2) を UserQueue の先頭(index=0)へ昇格
        sut.MoveAlbumQueueItemToUserQueue(2, 0);

        // Assert
        Assert.Equal(new[] { "A4", "U1", "U2" }, sut.UserQueue.Select(t => t.Title).ToArray());
        Assert.Equal(new[] { "A2", "A3" }, sut.AlbumQueue.Select(t => t.Title).ToArray());
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(new[] { "A4", "U1", "U2", "A2", "A3" }, receivedPlaylist.Select(t => t.Title).ToArray());

        // Act 2 - AlbumQueue の A3(index=1) を UserQueue の末尾(index=3)へ昇格
        sut.MoveAlbumQueueItemToUserQueue(1, 3);
        Assert.Equal(new[] { "A4", "U1", "U2", "A3" }, sut.UserQueue.Select(t => t.Title).ToArray());
        Assert.Equal(new[] { "A2" }, sut.AlbumQueue.Select(t => t.Title).ToArray());

        // 次曲再生で昇格した A4 が最優先再生されること
        sut.Next();
        Assert.NotNull(currentTrack);
        Assert.Equal("A4", currentTrack.Title);
    }

    /// <summary>
    /// MoveAlbumQueueItemToUserQueue に範囲外の移動元インデックスを指定した場合、状態が変更されないことを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    public void MoveAlbumQueueItemToUserQueue_範囲外の移動元インデックス時_状態不変かつイベント未発火である(int fromAlbumIndex, int toUserIndex)
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 3); // AlbumQueue: [A2, A3]
        sut.SetPlaylist(albumTracks, albumTracks[0]);

        int eventCount = 0;
        sut.PlaylistChanged += _ => eventCount++;

        // Act
        sut.MoveAlbumQueueItemToUserQueue(fromAlbumIndex, toUserIndex);

        // Assert
        Assert.Equal(0, eventCount);
        Assert.Empty(sut.UserQueue);
        Assert.Equal(new[] { "A2", "A3" }, sut.AlbumQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// シャッフルOFF時にアルバム（9曲）の途中曲・先頭曲・最終曲を開始トラックに指定して SetPlaylist を実行した際、
    /// 選択曲より前のトラックおよび選択曲自身は含まれず、後続トラックのみが AlbumQueue に格納されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(6, new[] { "A8", "A9" })]
    [InlineData(8, new string[0])]
    [InlineData(0, new[] { "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9" })]
    public void SetPlaylist_シャッフルOFFで途中曲を指定時_後続トラックのみがAlbumQueueに格納される(
        int startIndex,
        string[] expectedTitles)
    {
        // Arrange
        using var sut = new AudioService();
        var albumTracks = CreateSampleTracks("A", 9);

        // Act
        sut.SetPlaylist(albumTracks, albumTracks[startIndex]);

        // Assert
        Assert.Empty(sut.UserQueue);
        Assert.Equal(expectedTitles, sut.AlbumQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// シャッフルON時にアルバム（9曲）の途中曲（7曲目）を開始トラックに指定して SetPlaylist を実行した際、
    /// 選択曲自身は含まれず残りの未再生曲のみがシャッフルされて AlbumQueue に格納されることを検証します。
    /// </summary>
    [Fact]
    public void SetPlaylist_シャッフルONで途中曲を指定時_選択曲以外の未再生曲のみがAlbumQueueに格納される()
    {
        // Arrange
        using var sut = new AudioService();
        sut.IsShuffleEnabled = true;
        var albumTracks = CreateSampleTracks("A", 9);
        var startTrack = albumTracks[6]; // A7

        // Act
        sut.SetPlaylist(albumTracks, startTrack);

        // Assert
        Assert.Empty(sut.UserQueue);
        Assert.Equal(8, sut.AlbumQueue.Count);
        Assert.DoesNotContain(sut.AlbumQueue, t => t.Title == "A7");

        var expected = albumTracks.Where(t => t.Title != "A7").Select(t => t.Title).OrderBy(t => t).ToList();
        var actual = sut.AlbumQueue.Select(t => t.Title).OrderBy(t => t).ToList();
        Assert.Equal(expected, actual);
    }
}
