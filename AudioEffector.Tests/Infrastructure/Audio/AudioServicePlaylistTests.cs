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
    /// シャッフル再生中に解除した場合、現在曲より前の曲は除外され、再生済み曲は穴あきとなり、未再生曲がアルバム順で復元されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_再生中にシャッフル解除時_現在曲以降の未再生曲のみがアルバム順で復元される()
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
        // 1. 現在再生中（Song 2）が先頭
        // 2. Song 2 より前の曲（Song 1）は未再生のためキューから除外
        // 3. Song 2 より後の曲（Song 3, 4, 5, 6）のうち、既に再生済みの Song 4 は穴あき（除外）
        // 4. 残る未再生曲（Song 3, 5, 6）がアルバム順で配置
        // 期待キュー: [Song 2, Song 3, Song 5, Song 6]
        Assert.Equal(4, receivedPlaylist.Count);
        Assert.Equal("Song 2", receivedPlaylist[0].Title);
        Assert.Equal("Song 3", receivedPlaylist[1].Title);
        Assert.Equal("Song 5", receivedPlaylist[2].Title);
        Assert.Equal("Song 6", receivedPlaylist[3].Title);
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
}

