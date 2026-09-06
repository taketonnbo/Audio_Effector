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
        for (int i = 0; i < tracks.Count; i++)
        {
            Assert.Equal(tracks[i].FilePath, receivedPlaylist[i].FilePath);
        }
    }
}
