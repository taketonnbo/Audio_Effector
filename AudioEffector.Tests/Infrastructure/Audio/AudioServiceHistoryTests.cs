using System;
using System.Collections.Generic;
using System.Reflection;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Audio;
using Xunit;

namespace AudioEffector.Tests.Infrastructure.Audio;

/// <summary>
/// <see cref="AudioService"/> の再生終了検知、履歴イベント発火、および再生キューからの削除挙動を検証するテストクラス。
/// </summary>
public sealed class AudioServiceHistoryTests
{
    private static Track CreateTrack(string id = "1", string title = "Test Song")
    {
        return new Track
        {
            FilePath = $@"C:\Music\{id}.mp3",
            Title = title,
            Artist = "Artist",
            Album = "Album",
            Duration = TimeSpan.FromMinutes(3)
        };
    }

    [Fact]
    public void OnTrackEnded_トラック完奏時_TrackPlaybackEndedイベントが発火されキューから削除される()
    {
        // Arrange
        using var sut = new AudioService();
        var track1 = CreateTrack("1", "Track 1");
        var track2 = CreateTrack("2", "Track 2");
        sut.SetPlaylist(new List<Track> { track1, track2 });

        // リフレクションで _lastPlayingTrack を設定
        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        lastPlayingTrackField.SetValue(sut, track1);

        Track? endedTrack = null;
        int eventCount = 0;
        sut.TrackPlaybackEnded += t =>
        {
            endedTrack = t;
            eventCount++;
        };

        List<Track>? updatedPlaylist = null;
        sut.PlaylistChanged += p => updatedPlaylist = p;

        // Act - private メソッド OnTrackEnded を呼び出し
        var onTrackEndedMethod = typeof(AudioService).GetMethod("OnTrackEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onTrackEndedMethod);
        onTrackEndedMethod.Invoke(sut, null);

        // Assert
        Assert.Equal(1, eventCount);
        Assert.NotNull(endedTrack);
        Assert.Equal(track1.FilePath, endedTrack.FilePath);

        // 再生キューから終了曲が削除され、track2のみ残ること
        Assert.NotNull(updatedPlaylist);
        Assert.Single(updatedPlaylist);
        Assert.Equal(track2.FilePath, updatedPlaylist[0].FilePath);
    }

    [Fact]
    public void Next_次へスキップ時_再生キューから直前曲が削除され履歴イベントが発火される()
    {
        // Arrange
        using var sut = new AudioService();
        var track1 = CreateTrack("1", "Track 1");
        var track2 = CreateTrack("2", "Track 2");
        sut.SetPlaylist(new List<Track> { track1, track2 });

        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        lastPlayingTrackField.SetValue(sut, track1);

        Track? endedTrack = null;
        sut.TrackPlaybackEnded += t => endedTrack = t;

        List<Track>? updatedPlaylist = null;
        sut.PlaylistChanged += p => updatedPlaylist = p;

        // Act
        sut.Next();

        // Assert
        Assert.NotNull(endedTrack);
        Assert.Equal(track1.FilePath, endedTrack.FilePath);

        // 再生キューから track1 が削除され track2 のみが残る
        Assert.NotNull(updatedPlaylist);
        Assert.Single(updatedPlaylist);
        Assert.Equal(track2.FilePath, updatedPlaylist[0].FilePath);
    }

    [Fact]
    public void OnTrackEnded_同一トラックで複数回呼ばれても二重発火しない()
    {
        // Arrange
        using var sut = new AudioService();
        var track = CreateTrack("1", "Track 1");

        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        lastPlayingTrackField.SetValue(sut, track);

        int eventCount = 0;
        sut.TrackPlaybackEnded += _ => eventCount++;

        var onTrackEndedMethod = typeof(AudioService).GetMethod("OnTrackEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onTrackEndedMethod);

        // Act
        onTrackEndedMethod.Invoke(sut, null);
        onTrackEndedMethod.Invoke(sut, null); // 2回目の呼び出し

        // Assert - 二重発火防止フラグにより1回のみ発火
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void CheckAndPreparePlaybackEnded_再生時間が5秒未満でも報告される()
    {
        // Arrange
        using var sut = new AudioService();
        var track = CreateTrack("1", "Short Track");

        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        lastPlayingTrackField.SetValue(sut, track);

        var checkMethod = typeof(AudioService).GetMethod("CheckAndPreparePlaybackEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(checkMethod);

        // Act - forceEnded: false (5秒未満相当)
        var result = checkMethod.Invoke(sut, new object[] { false }) as Track;

        // Assert - 5秒未満でも履歴追加対象として返却されること
        Assert.NotNull(result);
        Assert.Equal(track.FilePath, result.FilePath);
    }

    [Fact]
    public void SetPlaylist_キュー変更時に報告済みフラグがリセットされる()
    {
        // Arrange
        using var sut = new AudioService();
        var track1 = CreateTrack("1", "Track 1");
        var track2 = CreateTrack("2", "Track 2");

        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        var reportedField = typeof(AudioService).GetField("_currentTrackReportedAsEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        Assert.NotNull(reportedField);

        reportedField.SetValue(sut, true);

        // Act
        sut.SetPlaylist(new List<Track> { track1, track2 });

        // Assert
        var isReported = (bool)(reportedField.GetValue(sut) ?? true);
        Assert.False(isReported);
    }

    [Fact]
    public void OnTrackEnded_キューに未再生曲が残っていてもキュー最終曲の再生終了時_次アルバムへ移行するためPlaylistEndedが発火する()
    {
        // Arrange
        using var sut = new AudioService();
        var track1 = CreateTrack("1", "Track 1");
        var track2 = CreateTrack("2", "Track 2");
        sut.SetPlaylist(new List<Track> { track1, track2 }); // 最終曲は track2

        // track2 を直前曲として設定
        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        lastPlayingTrackField.SetValue(sut, track2);

        bool playlistEndedFired = false;
        sut.PlaylistEnded += (s, e) => playlistEndedFired = true;

        var onTrackEndedMethod = typeof(AudioService).GetMethod("OnTrackEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onTrackEndedMethod);

        // Act - 最終曲 track2 の終了
        onTrackEndedMethod.Invoke(sut, null);

        // Assert - キューに曲が残っていても最終曲が終了したので PlaylistEnded が発火すること
        Assert.True(playlistEndedFired);
    }

    [Fact]
    public void OnTrackEnded_PlaylistEndedハンドラで次のアルバム再生が開始された場合_停止イベントやTrackChanged_nullが発火せず新アルバムの再生状態が維持される()
    {
        // Arrange
        using var sut = new AudioService();
        var track1 = CreateTrack("1", "Track 1");
        var nextAlbumTrack = CreateTrack("2", "Next Album Track");
        sut.SetPlaylist(new List<Track> { track1 });

        // track1 を直前曲として設定
        var lastPlayingTrackField = typeof(AudioService).GetField("_lastPlayingTrack", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastPlayingTrackField);
        lastPlayingTrackField.SetValue(sut, track1);

        Track? lastTrackChanged = null;
        sut.TrackChanged += t => lastTrackChanged = t;

        bool stoppedFired = false;
        sut.PlaybackStopped += () => stoppedFired = true;

        // PlaylistEnded のハンドラ内で次のアルバム再生を開始する（MainViewModel.OnPlaylistEnded と同様の動作）
        sut.PlaylistEnded += (s, e) =>
        {
            sut.SetPlaylist(new List<Track> { nextAlbumTrack });
            sut.PlayTrack(nextAlbumTrack);
        };

        var onTrackEndedMethod = typeof(AudioService).GetMethod("OnTrackEnded", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onTrackEndedMethod);

        // Act - 最終曲 track1 の終了
        onTrackEndedMethod.Invoke(sut, null);

        // Assert - PlaylistEnded 内で開始された nextAlbumTrack の再生が null で上書きされず維持され、PlaybackStopped も発火しないこと
        Assert.NotNull(lastTrackChanged);
        Assert.Equal(nextAlbumTrack.FilePath, lastTrackChanged.FilePath);
        Assert.False(stoppedFired);
    }
}
