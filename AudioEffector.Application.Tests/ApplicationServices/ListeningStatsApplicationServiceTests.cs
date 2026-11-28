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

    private static Track CreateTrack(
        string title,
        int durationSeconds = 200,
        string artist = "Artist A",
        string album = "Album A",
        string? filePath = null)
    {
        return new Track
        {
            FilePath = filePath ?? $@"C:\Music\{album}\{artist}\{title}.flac",
            Title = title,
            Artist = artist,
            Album = album,
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
    /// GetTopPlayedTracksForAlbum がファイルパス集合指定時に、対象アルバム内の曲のみ（別アーティストのコンピレーション曲含む）を
    /// 再生回数降順で最大5件抽出し、他アルバムの最多再生曲や再生回数0の曲を除外することを検証します。
    /// </summary>
    [Fact]
    public void GetTopPlayedTracksForAlbum_アルバム内ファイルパス集合指定時_対象アルバム内の上位5件のみ抽出され他アルバム曲が除外されること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var now = new DateTime(2026, 11, 29, 13, 0, 0);
        using var sut = new ListeningStatsApplicationService(repository, audioMock.Object, () => now);

        var albumTrack1 = CreateTrack("Track 1", 180, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\01.flac");
        var albumTrack2 = CreateTrack("Track 2", 240, "Guest Artist", "Greatest Hits", @"C:\Music\AlbumA\02.flac");
        var albumTrack3 = CreateTrack("Track 3", 200, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\03.flac");
        var albumTrack4 = CreateTrack("Track 4", 190, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\04.flac");
        var albumTrack5 = CreateTrack("Track 5", 170, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\05.flac");
        var albumTrack6 = CreateTrack("Track 6", 160, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\06.flac");
        var zeroPlayAlbumTrack = CreateTrack("Track 0", 150, "Artist A", "Greatest Hits", @"C:\Music\AlbumA\00.flac");
        var otherSameNameAlbumTrack = CreateTrack("Other Top Hit", 300, "Artist B", "Greatest Hits", @"C:\Music\AlbumB\01.flac");

        repository.Update(data => data.TrackStats.Add(TrackListeningStat.FromTrack(zeroPlayAlbumTrack)));

        for (int i = 0; i < 10; i++) sut.RecordPlaybackCompleted(otherSameNameAlbumTrack);
        for (int i = 0; i < 4; i++) sut.RecordPlaybackCompleted(albumTrack2);
        for (int i = 0; i < 3; i++) sut.RecordPlaybackCompleted(albumTrack3);
        for (int i = 0; i < 2; i++) sut.RecordPlaybackCompleted(albumTrack1);
        for (int i = 0; i < 2; i++) sut.RecordPlaybackCompleted(albumTrack4);
        sut.RecordPlaybackCompleted(albumTrack5);
        sut.RecordPlaybackCompleted(albumTrack6);

        var targetAlbumPaths = new[]
        {
            albumTrack1.FilePath,
            albumTrack2.FilePath,
            albumTrack3.FilePath,
            albumTrack4.FilePath,
            albumTrack5.FilePath,
            albumTrack6.FilePath,
            zeroPlayAlbumTrack.FilePath,
        };

        // Act
        IReadOnlyList<TrackListeningStat> result = sut.GetTopPlayedTracksForAlbum(
            targetAlbumPaths,
            albumName: "Greatest Hits",
            artistName: "Artist A",
            count: 5);

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal("Track 2", result[0].Title); // コンピレーションの別アーティスト曲もパス集合指定により含まれる
        Assert.Equal("Track 3", result[1].Title);
        Assert.Equal("Track 4", result[2].Title); // 同率2回では累計再生時間(380s > 360s)が大きい順
        Assert.Equal("Track 1", result[3].Title);
        Assert.Equal("Track 5", result[4].Title); // 同率1回では累計再生時間(170s > 160s)が大きい順
        Assert.DoesNotContain(result, s => s.FilePath == otherSameNameAlbumTrack.FilePath);
        Assert.DoesNotContain(result, s => s.FilePath == zeroPlayAlbumTrack.FilePath);
    }

    /// <summary>
    /// GetTopPlayedTracksForAlbum がファイルパス集合未指定のフォールバック時に、
    /// アルバム名とアーティスト名の両一致で同名別アーティストアルバムの混入を防ぐことを検証します。
    /// </summary>
    [Fact]
    public void GetTopPlayedTracksForAlbum_パス未指定のフォールバック時_同名別アーティストの楽曲が混入しないこと()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var now = new DateTime(2026, 11, 29, 13, 30, 0);
        using var sut = new ListeningStatsApplicationService(repository, audioMock.Object, () => now);

        var artistATrack = CreateTrack("Song A", 200, "Artist A", "Greatest Hits", @"C:\Music\A\song.flac");
        var artistBTrack = CreateTrack("Song B", 250, "Artist B", "Greatest Hits", @"C:\Music\B\song.flac");

        sut.RecordPlaybackCompleted(artistATrack);
        for (int i = 0; i < 5; i++) sut.RecordPlaybackCompleted(artistBTrack);

        // Act
        IReadOnlyList<TrackListeningStat> result = sut.GetTopPlayedTracksForAlbum(
            albumTrackFilePaths: null,
            albumName: "Greatest Hits",
            artistName: "Artist A",
            count: 5);

        // Assert
        Assert.Single(result);
        Assert.Equal("Song A", result[0].Title);
        Assert.Equal("Artist A", result[0].Artist);
    }

    /// <summary>
    /// GetTopPlayedTracksForAlbum がファイルパス集合未指定かつアルバム名が空または既定不明値の場合に、
    /// 誤混入防止のため空一覧を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown Album")]
    public void GetTopPlayedTracksForAlbum_パス未指定かつアルバム名不明時_空一覧を返すこと(string? albumName)
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        using var sut = new ListeningStatsApplicationService(repository, audioMock.Object);
        sut.RecordPlaybackCompleted(CreateTrack("Unknown Song", 180, "Artist A", "Unknown Album", @"C:\Music\u.flac"));

        // Act
        IReadOnlyList<TrackListeningStat> result = sut.GetTopPlayedTracksForAlbum(
            albumTrackFilePaths: null,
            albumName: albumName,
            artistName: "Artist A",
            count: 5);

        // Assert
        Assert.Empty(result);
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
