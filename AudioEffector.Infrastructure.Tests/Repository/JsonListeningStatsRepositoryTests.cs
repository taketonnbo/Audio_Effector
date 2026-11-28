using System;
using System.IO;
using System.Linq;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

/// <summary>
/// <see cref="JsonListeningStatsRepository"/> の永続化・旧 RecentTracks.json マイグレーション・
/// 破損 JSON フォールバック・IRecentTrackRepository 互換・2インスタンス間リロード・動的ディレクトリ追従を検証するテストクラス
/// </summary>
public sealed class JsonListeningStatsRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public JsonListeningStatsRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"listening_stats_repo_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static Track CreateTrack(string title, double durationSeconds = 200)
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
    /// 曲別統計と日時毎再生履歴イベントを保存した後に読み込むと、同じ内容で正確に復元されることを検証します。
    /// </summary>
    [Fact]
    public void SaveAndLoad_統計と再生イベント保存時_同じ内容で復元されること()
    {
        // Arrange
        string statsPath = Path.Combine(_tempDir, "ListeningStats.json");
        var sut = new JsonListeningStatsRepository(statsPath);
        var track = CreateTrack("Midnight City", 243);
        var playedAt = new DateTime(2026, 11, 29, 2, 15, 0);
        var stat = TrackListeningStat.FromTrack(track);
        stat.RecordPlaybackCompleted(track, playedAt, track.Duration);
        var data = new ListeningStatsData([stat], [TrackPlayEvent.FromTrack(track, playedAt)]);

        // Act
        sut.Save(data);
        ListeningStatsData loaded = sut.Load();

        // Assert
        Assert.Single(loaded.TrackStats);
        Assert.Single(loaded.PlayEvents);
        Assert.Equal(track.FilePath, loaded.TrackStats[0].FilePath);
        Assert.Equal(1, loaded.TrackStats[0].PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(243), loaded.TrackStats[0].TotalListeningDuration);
        Assert.Equal(playedAt, loaded.TrackStats[0].LastPlayedAt);
        Assert.Equal(playedAt, loaded.PlayEvents[0].PlayedAt);
    }

    /// <summary>
    /// ListeningStats.json が未存在で旧 RecentTracks.json が存在する場合、初回読み込み時にデータ欠損なく自動マイグレーションされることを検証します。
    /// </summary>
    [Fact]
    public void Load_旧RecentTracksJsonが存在する場合_ListeningStatsJsonへ安全に自動マイグレーションされること()
    {
        // Arrange
        string statsPath = Path.Combine(_tempDir, "ListeningStats.json");
        string legacyPath = Path.Combine(_tempDir, "RecentTracks.json");
        string legacyJson = """
            [
              null,
              {
                "FilePath": "",
                "Title": "Invalid",
                "Artist": "Unknown",
                "Album": "Unknown",
                "DurationSeconds": 100,
                "PlayedAt": "2026-11-28T20:00:00"
              },
              {
                "FilePath": "C:\\Music\\Track1.flac",
                "Title": "Track 1",
                "Artist": "Artist 1",
                "Album": "Album 1",
                "DurationSeconds": 240,
                "PlayedAt": "2026-11-28T21:15:00"
              },
              {
                "FilePath": "C:\\Music\\Track2.flac",
                "Title": "Track 2",
                "Artist": "Artist 2",
                "Album": "Album 2",
                "DurationSeconds": 180,
                "PlayedAt": "2026-11-28T20:30:00"
              }
            ]
            """;
        File.WriteAllText(legacyPath, legacyJson);
        var sut = new JsonListeningStatsRepository(statsPath, legacyRecentTracksFilePath: legacyPath);

        // Act
        ListeningStatsData loaded = sut.Load();

        // Assert
        Assert.True(File.Exists(statsPath));
        Assert.Equal(2, loaded.TrackStats.Count);
        Assert.Equal(2, loaded.PlayEvents.Count);
        TrackListeningStat? track1 = loaded.FindTrackStat(@"C:\Music\Track1.flac");
        Assert.NotNull(track1);
        Assert.Equal(1, track1!.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(240), track1.TotalListeningDuration);
        Assert.Equal(new DateTime(2026, 11, 28, 21, 15, 0), track1.LastPlayedAt);
        Assert.Equal(new DateTime(2026, 11, 28, 21, 15, 0), track1.RecentPlayedAt);
    }

    /// <summary>
    /// 破損した ListeningStats.json を読み込んでも例外を送出せず空データへフォールバックすることを検証します。
    /// </summary>
    [Fact]
    public void Load_破損したJSONファイルの場合_空データを返すこと()
    {
        // Arrange
        string statsPath = Path.Combine(_tempDir, "ListeningStats.json");
        File.WriteAllText(statsPath, "{ broken json");
        var sut = new JsonListeningStatsRepository(statsPath);

        // Act
        ListeningStatsData loaded = sut.Load();

        // Assert
        Assert.Empty(loaded.TrackStats);
        Assert.Empty(loaded.PlayEvents);
    }

    /// <summary>
    /// IRecentTrackRepository 経由で履歴を削除・全消去しても、TrackStats の累計再生統計や PlayEvents が保持されることを検証します。
    /// </summary>
    [Fact]
    public void IRecentTrackRepository互換操作_履歴クリア実行時_累計統計と再生イベントが破壊されず保持されること()
    {
        // Arrange
        string statsPath = Path.Combine(_tempDir, "ListeningStats.json");
        var sut = new JsonListeningStatsRepository(statsPath);
        IRecentTrackRepository recentRepo = sut;
        var track = CreateTrack("PreservedSong", 195);
        var playedAt = new DateTime(2026, 11, 29, 3, 0, 0);

        sut.Update(data =>
        {
            var stat = TrackListeningStat.FromTrack(track);
            stat.RecordPlaybackCompleted(track, playedAt, track.Duration);
            data.TrackStats.Add(stat);
            data.PlayEvents.Add(TrackPlayEvent.FromTrack(track, playedAt));
        });

        // Act
        recentRepo.Update(entries => entries.Clear());

        // Assert
        Assert.Empty(recentRepo.Load());
        ListeningStatsData afterClear = sut.Load();
        TrackListeningStat? preserved = afterClear.FindTrackStat(track.FilePath);
        Assert.NotNull(preserved);
        Assert.Equal(1, preserved!.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(195), preserved.TotalListeningDuration);
        Assert.Null(preserved.RecentPlayedAt);
        Assert.Single(afterClear.PlayEvents);
    }

    /// <summary>
    /// 同一 ListeningStats.json を参照する 2 インスタンス間で、一方の更新後に他方が自動リロードで最新データを取得できることを検証します。
    /// </summary>
    [Fact]
    public void Load_2インスタンス間更新時_他方が自動リロードで最新データを取得できること()
    {
        // Arrange
        string statsPath = Path.Combine(_tempDir, "ListeningStats.json");
        var writer = new JsonListeningStatsRepository(statsPath);
        var sut = new JsonListeningStatsRepository(statsPath);
        Assert.Empty(sut.Load().TrackStats);

        // Act
        var track = CreateTrack("SyncedSong", 210);
        writer.Update(data =>
        {
            var stat = TrackListeningStat.FromTrack(track);
            stat.RecordPlaybackCompleted(track, new DateTime(2026, 11, 29, 4, 0, 0), track.Duration);
            data.TrackStats.Add(stat);
        });
        ListeningStatsData reloaded = sut.Load();

        // Assert
        Assert.Single(reloaded.TrackStats);
        Assert.Equal("SyncedSong", reloaded.TrackStats[0].Title);
    }

    /// <summary>
    /// DataDirectoryProvider の保存先ディレクトリが切り替わった場合、新ディレクトリの ListeningStats.json へ自動追従することを検証します。
    /// </summary>
    [Fact]
    public void Load_DataDirectoryProvider切替後_新ディレクトリのListeningStatsJsonへ追従すること()
    {
        // Arrange
        string dirA = Path.Combine(_tempDir, "DirA");
        string dirB = Path.Combine(_tempDir, "DirB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        var provider = new DataDirectoryProvider(dirA);
        var sut = new JsonListeningStatsRepository(provider);
        var trackA = CreateTrack("TrackA");
        var trackB = CreateTrack("TrackB");

        sut.Update(data => data.TrackStats.Add(TrackListeningStat.FromTrack(trackA)));

        // Act
        provider.UpdateDataDirectory(dirB);
        Assert.Empty(sut.Load().TrackStats);
        sut.Update(data => data.TrackStats.Add(TrackListeningStat.FromTrack(trackB)));

        // Assert
        Assert.True(File.Exists(Path.Combine(dirA, "ListeningStats.json")));
        Assert.True(File.Exists(Path.Combine(dirB, "ListeningStats.json")));
        Assert.Equal("TrackB", Assert.Single(sut.Load().TrackStats).Title);
    }
}
