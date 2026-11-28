using System;
using System.Collections.Generic;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using Moq;
using Xunit;

namespace AudioEffector.Application.Tests.ApplicationServices;

/// <summary>
/// <see cref="ListeningStatsApplicationService"/> の再生完了カウント記録・日時毎再生履歴永続化・TOP N 集計・購読解除を検証するテストクラス
/// </summary>
public sealed class ListeningStatsApplicationServiceTests
{
    private sealed class InMemoryListeningStatsRepository : IListeningStatsRepository
    {
        private ListeningStatsData _data = new();

        public ListeningStatsData Load() => _data.Clone();

        public void Save(ListeningStatsData data) => _data = data.Clone();

        public ListeningStatsData Update(Action<ListeningStatsData> update)
        {
            ListeningStatsData working = _data.Clone();
            update(working);
            _data = working.Clone();
            return _data.Clone();
        }
    }

    private static Track CreateTrack(string title, int durationSeconds = 200)
    {
        return new Track
        {
            FilePath = $@"C:\Music\{title}.flac",
            Title = title,
            Artist = "Artist A",
            Album = "Album A",
            Duration = TimeSpan.FromSeconds(durationSeconds),
        };
    }

    /// <summary>
    /// IAudioService.TrackPlaybackEnded 発火時に累計再生回数・累計再生時間・LastPlayedAt および日時毎再生イベントが記録・永続化されることを検証します。
    /// </summary>
    [Fact]
    public void TrackPlaybackEnded_再生完了イベント発火時_累計統計と日時毎再生履歴が記録され通知されること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var timestamps = new Queue<DateTime>([
            new DateTime(2026, 11, 29, 10, 0, 0),
            new DateTime(2026, 11, 29, 10, 10, 0),
        ]);
        using var sut = new ListeningStatsApplicationService(repository, audioMock.Object, () => timestamps.Dequeue());
        var track = CreateTrack("Hero Song", 180);
        int updatedNotifications = 0;
        sut.StatsUpdated += _ => updatedNotifications++;

        // Act
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);

        // Assert
        TrackListeningStat? stat = sut.GetTrackStat(track.FilePath);
        Assert.NotNull(stat);
        Assert.Equal(2, stat!.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(360), stat.TotalListeningDuration);
        Assert.Equal(new DateTime(2026, 11, 29, 10, 10, 0), stat.LastPlayedAt);
        Assert.Equal(2, updatedNotifications);

        ListeningStatsData snapshot = sut.GetAllStatsSnapshot();
        Assert.Equal(2, snapshot.PlayEvents.Count);
        Assert.Equal(new DateTime(2026, 11, 29, 10, 0, 0), snapshot.PlayEvents[0].PlayedAt);
        Assert.Equal(new DateTime(2026, 11, 29, 10, 10, 0), snapshot.PlayEvents[1].PlayedAt);
    }

    /// <summary>
    /// GetTopPlayedTracks が再生回数降順・同率時は累計再生時間降順で指定件数 TOP N を返し、再生回数 0 曲を含めないことを検証します。
    /// </summary>
    [Fact]
    public void GetTopPlayedTracks_複数曲記録済みの場合_再生回数降順で上位N件が安定ソートされること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var now = new DateTime(2026, 11, 29, 12, 0, 0);
        using var sut = new ListeningStatsApplicationService(repository, audioMock.Object, () => now);

        var trackA = CreateTrack("Alpha", 180);
        var trackB = CreateTrack("Beta", 240);
        var trackC = CreateTrack("Gamma", 300);
        var zeroPlayTrack = CreateTrack("Zero", 200);

        repository.Update(data => data.TrackStats.Add(TrackListeningStat.FromTrack(zeroPlayTrack)));
        sut.RecordPlaybackCompleted(trackA);
        sut.RecordPlaybackCompleted(trackB);
        sut.RecordPlaybackCompleted(trackB);
        sut.RecordPlaybackCompleted(trackC);
        sut.RecordPlaybackCompleted(trackC);
        sut.RecordPlaybackCompleted(trackC);

        // Act
        IReadOnlyList<TrackListeningStat> top2 = sut.GetTopPlayedTracks(2);
        IReadOnlyList<TrackListeningStat> top5 = sut.GetTopPlayedTracks(5);

        // Assert
        Assert.Equal(2, top2.Count);
        Assert.Equal("Gamma", top2[0].Title);
        Assert.Equal("Beta", top2[1].Title);
        Assert.Equal(3, top5.Count);
        Assert.DoesNotContain(top5, s => s.Title == "Zero");
    }

    /// <summary>
    /// Dispose 呼び出し後は TrackPlaybackEnded イベントの購読が解除され、統計が加算されないことを検証します。
    /// </summary>
    [Fact]
    public void Dispose_破棄後にTrackPlaybackEnded発火時_イベント購読解除により統計が更新されないこと()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var sut = new ListeningStatsApplicationService(repository, audioMock.Object);
        var track = CreateTrack("AfterDispose");

        // Act
        sut.Dispose();
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);

        // Assert
        Assert.Empty(repository.Load().TrackStats);
        Assert.Empty(repository.Load().PlayEvents);
    }
}
