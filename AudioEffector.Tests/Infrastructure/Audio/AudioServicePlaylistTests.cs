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
    /// IsShuffleEnabledをtrueにした際、PlaylistChangedが発火し、未再生キュー（AlbumQueue）が更新されることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
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
        // 現在再生中(tracks[0])を除く残り9曲が含まれていること
        Assert.Equal(tracks.Count - 1, receivedPlaylist.Count);
        for (int i = 1; i < tracks.Count; i++)
        {
            Assert.Contains(receivedPlaylist, r => r.FilePath == tracks[i].FilePath);
        }
    }

    /// <summary>
    /// IsShuffleEnabledをfalseに戻した際、PlaylistChangedが発火し、元のアルバム順に復元されることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
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
        // 現在再生中(tracks[0])を除く残り9曲が元の順序と完全に一致すること
        Assert.Equal(tracks.Count - 1, receivedPlaylist.Count);
        for (int i = 1; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].FilePath, receivedPlaylist[i - 1].FilePath);
        }
    }

    /// <summary>
    /// シャッフルON時にstartTrackを指定してSetPlaylistを呼んだ際、startTrackが即時再生され、キューには残り曲がシャッフルされて入ることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
    /// </summary>
    [Fact]
    public void SetPlaylist_シャッフルON時にstartTrack指定時_startTrackが先頭に固定される()
    {
        // Arrange
        using var sut = new AudioService();
        sut.IsShuffleEnabled = true;
        var tracks = CreateSampleTracks(10);
        var targetStartTrack = tracks[4]; // 5曲目 (インデックス 4)

        Track? currentPlayingTrack = null;
        sut.TrackChanged += t => currentPlayingTrack = t;

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act
        sut.SetPlaylist(tracks, targetStartTrack);

        // Assert
        Assert.NotNull(currentPlayingTrack);
        Assert.Equal(targetStartTrack.FilePath, currentPlayingTrack.FilePath);
        Assert.NotNull(receivedPlaylist);
        // targetStartTrack を除く残り9曲がキューに入っていること
        Assert.Equal(tracks.Count - 1, receivedPlaylist.Count);
        Assert.DoesNotContain(receivedPlaylist, r => r.FilePath == targetStartTrack.FilePath);
        var remainingExpected = tracks.Where(t => t.FilePath != targetStartTrack.FilePath).ToList();
        Assert.All(remainingExpected, t => Assert.Contains(receivedPlaylist, r => r.FilePath == t.FilePath));
    }

    /// <summary>
    /// シャッフルOFF時にSetPlaylistを呼んだ際、1曲目が即時再生され、後続曲が順序通りキューに通知されることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
    /// </summary>
    [Fact]
    public void SetPlaylist_シャッフルOFF時_元の順序で通知される()
    {
        // Arrange
        using var sut = new AudioService();
        sut.IsShuffleEnabled = false;
        var tracks = CreateSampleTracks(5);

        Track? currentPlayingTrack = null;
        sut.TrackChanged += t => currentPlayingTrack = t;

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act
        sut.SetPlaylist(tracks);

        // Assert
        Assert.NotNull(currentPlayingTrack);
        Assert.Equal(tracks[0].FilePath, currentPlayingTrack.FilePath);
        Assert.NotNull(receivedPlaylist);
        // 1曲目を除く4曲が順序通りキューに入っていること
        Assert.Equal(4, receivedPlaylist.Count);
        for (int i = 1; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].FilePath, receivedPlaylist[i - 1].FilePath);
        }
    }

    /// <summary>
    /// シャッフル再生中に解除した場合、未再生の曲のみがキューに残り、再生済み曲は除外されアルバム順で復元されることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_再生中にシャッフル解除時_未再生曲のみがキューに残りアルバム順で復元される()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(6); // Song 1, Song 2, Song 3, Song 4, Song 5, Song 6
        sut.SetPlaylist(tracks);
        sut.IsShuffleEnabled = true;

        // シャッフル再生の模擬:
        // Song 4 を再生（履歴に入る、キューから除外）
        sut.PlayTrack(tracks[3]); // Song 4
        // 次に Song 2 を再生（現在再生中曲となりキューから除外）
        sut.PlayTrack(tracks[1]); // Song 2

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - シャッフル解除
        sut.IsShuffleEnabled = false;

        // Assert
        Assert.NotNull(receivedPlaylist);
        // 仕様ルール:
        // 1. 現在再生中の曲（Song 2）はキューに含まれない
        // 2. 既に再生済みの Song 1, Song 4 はキューから除外
        // 3. 残る未再生曲（Song 3, 5, 6）がアルバム順で配置
        // 期待キュー: [Song 3, Song 5, Song 6]
        Assert.Equal(3, receivedPlaylist.Count);
        Assert.Equal("Song 3", receivedPlaylist[0].Title);
        Assert.Equal("Song 5", receivedPlaylist[1].Title);
        Assert.Equal("Song 6", receivedPlaylist[2].Title);
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
    /// シャッフル再生中にアルバム単位で「次に再生」を実行した場合、アルバム内の曲順がランダムな状態で予約キュー（キュー先頭）にまとめて追加されることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
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
        // 現在再生中の A1 を除く 7曲（予約キュー5曲 + アルバムキュー2曲）
        Assert.Equal(7, receivedPlaylist.Count);

        // 先頭 0〜4 はアルバムBの曲群（B1〜B5）がシャッフルされて挿入されていること
        var insertedChunk = receivedPlaylist.Take(5).Select(t => t.Title).ToList();
        var expectedTitles = albumB.Select(t => t.Title).OrderBy(t => t).ToList();
        Assert.Equal(expectedTitles, insertedChunk.OrderBy(t => t).ToList());

        // インデックス 5, 6 は元の後続曲（A2, A3）であること
        var tailChunk = receivedPlaylist.Skip(5).Take(2).Select(t => t.Title).OrderBy(t => t).ToList();
        Assert.Equal(new[] { "A2", "A3" }, tailChunk);
    }

    /// <summary>
    /// 複数アルバム混在時（「次に再生」「最後に再生」後）にシャッフルを解除した場合、
    /// 予約キューに統合された未再生曲が保持され、再生中・再生済み曲が除外されていることを検証します。
    /// （新仕様: 「最後に再生」で予約キューへ統合、現在再生中曲はキューに含まれない）
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_複数アルバム混在時にシャッフル解除_統合された未再生曲が保持され再生中曲は除外される()
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
        // 「最後に再生」を行うと予約キューとアルバム残りが統合され、albumCが末尾追加される
        sut.EnqueueTracks(albumC, playNext: false);

        // シャッフル再生の模擬:
        // 1. C2 を再生（キューから除外）
        sut.PlayTrack(albumC[1]); // C2
        // 2. B2 を現在再生中とする（キューから除外）
        sut.PlayTrack(albumB[1]); // B2

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - シャッフル解除
        sut.IsShuffleEnabled = false;

        // Assert
        Assert.NotNull(receivedPlaylist);

        // 新仕様ルール:
        // 1. 再生中の B2 および 再生済みの A1, C2 はキューに含まれない
        // 2. 未再生曲（B1, B3, A2, A3, C1, C3）の計6曲がキューに残る
        Assert.Equal(6, receivedPlaylist.Count);
        Assert.DoesNotContain(receivedPlaylist, t => t.Title == "A1");
        Assert.DoesNotContain(receivedPlaylist, t => t.Title == "B2");
        Assert.DoesNotContain(receivedPlaylist, t => t.Title == "C2");
        Assert.Contains(receivedPlaylist, t => t.Title == "B1");
        Assert.Contains(receivedPlaylist, t => t.Title == "B3");
        Assert.Contains(receivedPlaylist, t => t.Title == "A2");
        Assert.Contains(receivedPlaylist, t => t.Title == "A3");
        Assert.Contains(receivedPlaylist, t => t.Title == "C1");
        Assert.Contains(receivedPlaylist, t => t.Title == "C3");
    }

    /// <summary>
    /// SetPlaylistに空リストを設定した際、再生が停止され、TrackChangedイベントにnullが通知されることを検証します。
    /// </summary>
    [Fact]
    public void SetPlaylist_空リスト設定時_再生が停止しTrackChangedにnullが通知される()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(3);
        sut.SetPlaylist(tracks);

        Track? receivedTrack = tracks[0];
        bool trackChangedFired = false;
        sut.TrackChanged += t =>
        {
            receivedTrack = t;
            trackChangedFired = true;
        };

        bool playbackStoppedFired = false;
        sut.PlaybackStopped += () => playbackStoppedFired = true;

        // Act - 空リストを設定（キュークリア相当）
        sut.SetPlaylist(new List<Track>());

        // Assert
        Assert.True(trackChangedFired);
        Assert.Null(receivedTrack);
        Assert.True(playbackStoppedFired);
        Assert.False(sut.IsPlaying);
    }

    /// <summary>
    /// キューが空の状態でStopを実行した際、TrackChangedイベントにnullが通知されることを検証します。
    /// </summary>
    [Fact]
    public void Stop_キューが空の状態で呼び出し時_TrackChangedにnullが通知される()
    {
        // Arrange
        using var sut = new AudioService();
        Track? receivedTrack = new Track { Title = "Dummy" };
        bool trackChangedFired = false;
        sut.TrackChanged += t =>
        {
            receivedTrack = t;
            trackChangedFired = true;
        };

        // Act
        sut.Stop();

        // Assert
        Assert.True(trackChangedFired);
        Assert.Null(receivedTrack);
    }

    /// <summary>
    /// アルバムの楽曲がシャッフル再生された後、シャッフルを解除した際に残りの未再生トラックがトラック番号昇順に復元されることを検証します。
    /// （新仕様: 現在再生中曲はキューに含まれない）
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_シャッフル解除時_アルバムキューの未再生曲がトラック昇順に復元される()
    {
        // Arrange
        using var sut = new AudioService();
        var album = new List<Track>
        {
            new Track { FilePath = @"C:\Music\A1.mp3", Title = "A1", Album = "Album First", TrackNumber = 1 },
            new Track { FilePath = @"C:\Music\A2.mp3", Title = "A2", Album = "Album First", TrackNumber = 2 },
            new Track { FilePath = @"C:\Music\A3.mp3", Title = "A3", Album = "Album First", TrackNumber = 3 },
            new Track { FilePath = @"C:\Music\A4.mp3", Title = "A4", Album = "Album First", TrackNumber = 4 },
            new Track { FilePath = @"C:\Music\A5.mp3", Title = "A5", Album = "Album First", TrackNumber = 5 },
            new Track { FilePath = @"C:\Music\A6.mp3", Title = "A6", Album = "Album First", TrackNumber = 6 }
        };
        sut.SetPlaylist(album, album[0]); // A1 再生開始、AlbumQueue は A2〜A6 (5曲)
        sut.IsShuffleEnabled = true;

        Track? activeTrack = null;
        sut.TrackChanged += t => activeTrack = t;

        // シャッフル再生の模擬:
        // A4 を再生（現在再生中曲となり、キューから除外）
        sut.PlayTrack(album[3]); // A4

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act - シャッフル解除
        sut.IsShuffleEnabled = false;

        // Assert
        Assert.NotNull(receivedPlaylist);
        // 残り未再生曲は 4曲 (A2, A3, A5, A6)
        Assert.Equal(4, receivedPlaylist.Count);

        // トラック番号昇順（2, 3, 5, 6）に並ぶこと
        Assert.Equal("A2", receivedPlaylist[0].Title);
        Assert.Equal(2u, receivedPlaylist[0].TrackNumber);

        Assert.Equal("A3", receivedPlaylist[1].Title);
        Assert.Equal(3u, receivedPlaylist[1].TrackNumber);

        Assert.Equal("A5", receivedPlaylist[2].Title);
        Assert.Equal(5u, receivedPlaylist[2].TrackNumber);

        Assert.Equal("A6", receivedPlaylist[3].Title);
        Assert.Equal(6u, receivedPlaylist[3].TrackNumber);

        // 現在再生中の A4 が維持されていること
        Assert.Equal("A4", activeTrack?.Title);
    }

    /// <summary>
    /// RemoveTrackを呼び出した際、キューおよび元プレイリストから対象トラックが削除されることを検証します。
    /// （新仕様: 現在再生中曲を除く残りキューから削除）
    /// </summary>
    [Fact]
    public void RemoveTrack_指定トラックが正常に削除されPlaylistChangedが発火する()
    {
        // Arrange
        using var sut = new AudioService();
        var tracks = CreateSampleTracks(3); // tracks[0]再生、tracks[1], tracks[2]がキュー
        sut.SetPlaylist(tracks);

        List<Track>? receivedPlaylist = null;
        sut.PlaylistChanged += p => receivedPlaylist = p;

        // Act
        sut.RemoveTrack(tracks[1]);

        // Assert
        Assert.NotNull(receivedPlaylist);
        // tracks[1] 削除後は tracks[2] の 1曲のみ
        Assert.Single(receivedPlaylist);
        Assert.DoesNotContain(receivedPlaylist, t => t.FilePath == tracks[1].FilePath);
    }
}

