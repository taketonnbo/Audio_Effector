using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Audio;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Audio;

/// <summary>
/// <see cref="AudioService"/> のプレイリスト管理、シャッフル有効化/無効化時のイベント発火および順序連動を検証するテストクラス。
/// </summary>
public sealed class AudioServicePlaylistTests
{
    private static List<Track> CreateSampleTracks(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Track
            {
                FilePath = $@"C:\Music\song{i}.mp3",
                Title = $"Song {i}",
                Artist = "Artist",
                Album = "Album",
                TrackNumber = (uint)i,
                Duration = TimeSpan.FromMinutes(3)
            })
            .ToList();
    }

    /// <summary>
    /// IsShuffleEnabledをtrueにした際、PlaylistChangedが発火し、リストが更新されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_True設定時_PlaylistChangedが発火されシャッフルリストが通知される()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(10);
        sut.SetPlaylist(tracks);

        List<Track>? receivedPlaylist = null;
        int eventCallCount = 0;
        sut.PlaylistChanged += p =>
        {
            receivedPlaylist = p;
            eventCallCount++;
        };

        // Act
        sut.IsShuffleEnabled = true;

        // Assert
        Assert.True(sut.IsShuffleEnabled);
        Assert.Equal(1, eventCallCount);
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(tracks.Count, receivedPlaylist.Count);
        // 全楽曲が含まれていること
        Assert.All(tracks, t => Assert.Contains(receivedPlaylist, r => r.FilePath == t.FilePath));
    }

    /// <summary>
    /// IsShuffleEnabledをfalseに戻した際、PlaylistChangedが発火し、元の追加順序に復元されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_False復帰時_PlaylistChangedが発火され元の追加順に復元される()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(10);
        sut.SetPlaylist(tracks);
        sut.IsShuffleEnabled = true;

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act
        sut.IsShuffleEnabled = false;

        // Assert
        Assert.False(sut.IsShuffleEnabled);
        Assert.NotNull(receivedPlaylist);
        // 元の順序と完全に一致すること
        for (int i = 0; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].FilePath, receivedPlaylist[i].FilePath);
        }
    }

    /// <summary>
    /// シャッフルON時にstartTrackを指定してSetPlaylistを呼んだ際、startTrackが先頭に固定され残りがシャッフルされることを検証します。
    /// </summary>
    [Fact]
    public void SetPlaylist_シャッフルON時にstartTrack指定時_startTrackが先頭に固定される()
    {
        // Arrange
        using var sut = new AudioService();
        sut.IsShuffleEnabled = true;
        var tracks = CreateSampleTracks(10);
        var targetStartTrack = tracks[4]; // 5曲目 (インデックス 4)

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act
        sut.SetPlaylist(tracks, targetStartTrack);

        // Assert
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(tracks.Count, receivedPlaylist.Count);
        Assert.Equal(targetStartTrack.FilePath, receivedPlaylist[0].FilePath);
        // 他の9曲も全て含まれていること
        Assert.All(tracks, t => Assert.Contains(receivedPlaylist, r => r.FilePath == t.FilePath));
    }

    /// <summary>
    /// シャッフルOFF時にSetPlaylistを呼んだ際、元の順序でプレイリストが通知されることを検証します。
    /// </summary>
    [Fact]
    public void SetPlaylist_シャッフルOFF時_元の順序で通知される()
    {
        // Arrange
        using var sut = new AudioService();
        sut.IsShuffleEnabled = false;
        var tracks = CreateSampleTracks(5);

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act
        sut.SetPlaylist(tracks);

        // Assert
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(5, receivedPlaylist.Count);
        for (int i = 0; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].FilePath, receivedPlaylist[i].FilePath);
        }
    }

    /// <summary>
    /// シャッフル再生中に解除した場合、未再生の曲は現在曲の前（上）も含めて除外されず、再生済み曲のみが穴あきとなりアルバム順で復元されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_再生中にシャッフル解除時_未再生曲は除外されず再生中の曲の上に残り再生済みのみ穴あき復元される()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(6); // Song 1, Song 2, Song 3, Song 4, Song 5, Song 6
        sut.SetPlaylist(tracks);
        sut.IsShuffleEnabled = true;

        // シャッフル再生の模擬:
        // Song 4 を再生（履歴に入る）
        sut.PlayTrack(tracks[3]); // Song 4
        // 次に Song 2 を再生（現在再生中曲とする）
        sut.PlayTrack(tracks[1]); // Song 2

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - シャッフル解除
        sut.IsShuffleEnabled = false;

        // Assert
        Assert.NotNull(receivedPlaylist);
        // 仕様ルール:
        // 1. 未再生の曲（Song 1）は現在再生中の曲（Song 2）より前であっても除外されず、再生中の曲の上に残る
        // 2. 現在再生中の曲（Song 2）はそのまま維持される
        // 3. 既に再生済みの Song 4 は穴あき（除外）
        // 4. 残る未再生曲（Song 3, 5, 6）がアルバム順で配置
        // 期待キュー: [Song 1, Song 2, Song 3, Song 5, Song 6]
        Assert.Equal(5, receivedPlaylist.Count);
        Assert.Equal("Song 1", receivedPlaylist[0].Title);
        Assert.Equal("Song 2", receivedPlaylist[1].Title);
        Assert.Equal("Song 3", receivedPlaylist[2].Title);
        Assert.Equal("Song 5", receivedPlaylist[3].Title);
        Assert.Equal("Song 6", receivedPlaylist[4].Title);
    }

    /// <summary>
    /// Previous呼び出し時、例外が発生せず安全に動作することを検証します。
    /// </summary>
    [Fact]
    public void Previous_実行時_例外が発生せず安全に動作する()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(3);
        sut.SetPlaylist(tracks);
        sut.IsShuffleEnabled = true;

        // Act & Assert - 例外が発生しないこと
        var ex = Record.Exception(() => sut.Previous());
        Assert.Null(ex);
    }

    /// <summary>
    /// リピート無効時に先頭曲でPreviousを連続実行した場合、負のインデックスとならず先頭に留まり例外が発生しないことを検証します。
    /// </summary>
    [Fact]
    public void Previous_リピート無効かつ先頭曲再生時_インデックスが負にならず先頭に留まり例外が発生しない()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(3);
        sut.SetPlaylist(tracks);
        sut.IsRepeatEnabled = false;

        Track? lastChangedTrack = null;
        sut.TrackChanged += t => lastChangedTrack = t;

        // Act - 先頭でPreviousを複数回実行
        var ex1 = Record.Exception(() => sut.Previous());
        var ex2 = Record.Exception(() => sut.Previous());

        // Assert
        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.NotNull(lastChangedTrack);
        Assert.Equal(tracks[0].FilePath, lastChangedTrack.FilePath);
    }

    /// <summary>
    /// リピート有効時に先頭曲でPreviousを実行した場合、末尾の曲へ循環し例外が発生しないことを検証します。
    /// </summary>
    [Fact]
    public void Previous_リピート有効かつ先頭曲再生時_末尾の曲へ循環し例外が発生しない()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(3);
        sut.SetPlaylist(tracks);
        sut.IsRepeatEnabled = true;

        Track? lastChangedTrack = null;
        sut.TrackChanged += t => lastChangedTrack = t;

        // Act - 先頭でPreviousを実行
        var ex = Record.Exception(() => sut.Previous());

        // Assert
        Assert.Null(ex);
        Assert.NotNull(lastChangedTrack);
        Assert.Equal(tracks[2].FilePath, lastChangedTrack.FilePath);
    }

    /// <summary>
    /// PreviousとNextが複数スレッドから高頻度で並行実行された際、レースコンディションによる例外が発生しないことを検証します。
    /// </summary>
    [Fact]
    public async Task PreviousとNext_複数スレッドから並行実行時_レースコンディションによる例外が発生しない()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(10);
        sut.SetPlaylist(tracks);
        sut.IsShuffleEnabled = true;

        // Act - 複数スレッドから並行してPrevious/Nextを連打
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            try
            {
                for (int j = 0; j < 20; j++)
                {
                    if (j % 2 == 0)
                    {
                        sut.Previous();
                    }
                    else
                    {
                        sut.Next();
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(tasks);

        // Assert - スレッド競合による未処理例外が一切発生していないこと
        Assert.Empty(exceptions);
    }

    /// <summary>
    /// StopおよびStopInternal実行時、例外が発生せず安全に停止処理が行われることを検証します。
    /// </summary>
    [Fact]
    public void Stop_連続実行時_例外が発生せず安全に停止する()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(3);
        sut.SetPlaylist(tracks);

        // Act & Assert
        var ex1 = Record.Exception(() => sut.Stop());
        var ex2 = Record.Exception(() => sut.Stop());
        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    /// <summary>
    /// シャッフル再生中にアルバム単位で「次に再生」を実行した場合、アルバム内の曲順がランダムな状態で現在曲直後にまとめて追加されることを検証します。
    /// </summary>
    [Fact]
    public void EnqueueTracks_シャッフルON時にアルバム単位で次に再生_アルバムの曲がランダムな順序で現在曲直後にまとめて追加される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumA = new List<Track>
        {
            new Track { FilePath = @"C:\Music\A1.mp3", Title = "A1" },
            new Track { FilePath = @"C:\Music\A2.mp3", Title = "A2" },
            new Track { FilePath = @"C:\Music\A3.mp3", Title = "A3" }
        };
        sut.SetPlaylist(albumA, albumA[0]);
        sut.IsShuffleEnabled = true;

        var albumB = new List<Track>
        {
            new Track { FilePath = @"C:\Music\B1.mp3", Title = "B1" },
            new Track { FilePath = @"C:\Music\B2.mp3", Title = "B2" },
            new Track { FilePath = @"C:\Music\B3.mp3", Title = "B3" },
            new Track { FilePath = @"C:\Music\B4.mp3", Title = "B4" },
            new Track { FilePath = @"C:\Music\B5.mp3", Title = "B5" }
        };

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - アルバムBを「次に再生」
        sut.EnqueueTracks(albumB, playNext: true);

        // Assert
        Assert.NotNull(receivedPlaylist);
        Assert.Equal(8, receivedPlaylist.Count);

        // 先頭は現在再生中の A1 であること
        Assert.Equal("A1", receivedPlaylist[0].Title);

        // インデックス 1〜5 はアルバムBの曲群（B1〜B5）がまとめて挿入されていること
        var insertedChunk = receivedPlaylist.Skip(1).Take(5).Select(t => t.Title).ToList();
        var expectedTitles = albumB.Select(t => t.Title).OrderBy(t => t).ToList();
        Assert.Equal(expectedTitles, insertedChunk.OrderBy(t => t).ToList());

        // インデックス 6, 7 は元の後続曲（A2, A3）であること
        var tailChunk = receivedPlaylist.Skip(6).Take(2).Select(t => t.Title).OrderBy(t => t).ToList();
        Assert.Equal(new[] { "A2", "A3" }, tailChunk);
    }

    /// <summary>
    /// 複数アルバム混在時にシャッフルを解除した場合、追加したアルバム順かつ各アルバム内のトラック順に穴あき復元されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_複数アルバム混在時にシャッフル解除_追加したアルバム順かつ各アルバムのトラック順に穴あき復元される()
    {
        // Arrange
        using var sut = new AudioService();
        var albumA = new List<Track>
        {
            new Track { FilePath = @"C:\Music\A1.mp3", Title = "A1" },
            new Track { FilePath = @"C:\Music\A2.mp3", Title = "A2" },
            new Track { FilePath = @"C:\Music\A3.mp3", Title = "A3" }
        };
        sut.SetPlaylist(albumA, albumA[0]);
        sut.IsShuffleEnabled = true;

        var albumB = new List<Track>
        {
            new Track { FilePath = @"C:\Music\B1.mp3", Title = "B1" },
            new Track { FilePath = @"C:\Music\B2.mp3", Title = "B2" },
            new Track { FilePath = @"C:\Music\B3.mp3", Title = "B3" }
        };
        sut.EnqueueTracks(albumB, playNext: true);

        var albumC = new List<Track>
        {
            new Track { FilePath = @"C:\Music\C1.mp3", Title = "C1" },
            new Track { FilePath = @"C:\Music\C2.mp3", Title = "C2" },
            new Track { FilePath = @"C:\Music\C3.mp3", Title = "C3" }
        };
        sut.EnqueueTracks(albumC, playNext: false);

        // シャッフル再生の模擬:
        // 1. C2 を過去に再生（履歴に入る）
        sut.PlayTrack(albumC[1]); // C2
        // 2. B2 を現在再生中とする
        sut.PlayTrack(albumB[1]); // B2

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - シャッフル解除
        sut.IsShuffleEnabled = false;

        // Assert
        Assert.NotNull(receivedPlaylist);

        // 仕様ルール:
        // 1. B2 より前のアルバムA（A1〜A3）およびアルバムBのB1は未再生のため除外されず、B2の上にアルバム順・トラック順で残る
        // 2. 現在再生中（B2）の位置はインデックス 4
        // 3. B2 より後の曲（アルバムBのB3、アルバムCのC1〜C3）
        //    - B3: 未再生なのでB2の直後に配置
        //    - アルバムC: C2は再生済みなので穴あき（除外）。未再生のC1, C3がアルバムCのトラック順で並ぶ
        // 期待キュー: [ A1, A2, A3, B1, B2, B3, C1, C3 ]
        Assert.Equal(8, receivedPlaylist.Count);
        Assert.Equal("A1", receivedPlaylist[0].Title);
        Assert.Equal("A2", receivedPlaylist[1].Title);
        Assert.Equal("A3", receivedPlaylist[2].Title);
        Assert.Equal("B1", receivedPlaylist[3].Title);
        Assert.Equal("B2", receivedPlaylist[4].Title);
        Assert.Equal("B3", receivedPlaylist[5].Title);
        Assert.Equal("C1", receivedPlaylist[6].Title);
        Assert.Equal("C3", receivedPlaylist[7].Title);
    }
}

