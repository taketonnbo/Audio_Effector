using System;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="ListeningStatsViewModel"/> の対象曲統計表示・右クリック指定曲ピン留め・再生中曲復帰・アクティブ連動・TOP N 表示を検証するテストクラス
/// </summary>
public sealed class ListeningStatsViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
    }

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

    private static Track CreateTrack(string title, int durationSeconds = 210)
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
    /// 再生中の楽曲が切り替わり再生完了が記録された際、累計再生回数・累計再生時間・最終再生日時および TOP N が即時反映されることを検証します。
    /// </summary>
    [Fact]
    public void Refresh_再生完了統計記録後_対象曲のKPIとTOP5ランキングが正しく表示されること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var track = CreateTrack("Starlight", 185);
        audioMock.Setup(a => a.CurrentTrack).Returns(track);
        var playedAt = new DateTime(2026, 11, 29, 14, 30, 0);
        using var statsService = new ListeningStatsApplicationService(repository, audioMock.Object, () => playedAt);
        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = true,
        };

        // Act
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);

        // Assert
        Assert.Same(track, sut.TargetTrack);
        Assert.Equal(1, sut.TotalPlayCount);
        Assert.Equal("1 回", sut.TotalPlayCountDisplay);
        Assert.Equal(TimeSpan.FromSeconds(185), sut.TotalListeningTime);
        Assert.Equal("3分 5秒", sut.TotalListeningTimeDisplay);
        Assert.Equal("2026/11/29 14:30", sut.LastPlayedAtDisplay);
        Assert.True(sut.HasTargetTrackStats);
        Assert.Single(sut.TopPlayedTracks);
        Assert.Equal(1, sut.TopPlayedTracks[0].Rank);
        Assert.Equal(100.0, sut.TopPlayedTracks[0].PlayCountPercent);
    }

    /// <summary>
    /// 再生中以外の楽曲を SetTargetTrack で指定した場合にピン留め表示となり、ReturnToPlayingTrackCommand で再生中の曲へ戻せることを検証します。
    /// </summary>
    [Fact]
    public void SetTargetTrack_再生中以外の曲を指定し戻すコマンド実行_対象曲表示と再生中曲復帰が正しく切り替わること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var playingTrack = CreateTrack("PlayingTrack", 180);
        var selectedTrack = CreateTrack("SelectedTrack", 240);
        audioMock.Setup(a => a.CurrentTrack).Returns(playingTrack);

        using var statsService = new ListeningStatsApplicationService(
            repository,
            audioMock.Object,
            () => new DateTime(2026, 11, 29, 15, 0, 0));
        statsService.RecordPlaybackCompleted(playingTrack);
        statsService.RecordPlaybackCompleted(selectedTrack);
        statsService.RecordPlaybackCompleted(selectedTrack);

        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = true,
        };

        // Act 1: 再生中以外の曲を右クリック指定
        sut.SetTargetTrack(selectedTrack);

        // Assert 1
        Assert.Equal("SelectedTrack", sut.TargetTrackTitle);
        Assert.Equal(2, sut.TotalPlayCount);
        Assert.True(sut.IsShowingNonPlayingTrack);
        Assert.True(sut.CanReturnToPlayingTrack);
        Assert.True(sut.ReturnToPlayingTrackCommand.CanExecute(null));

        // Act 2: 「再生中の曲に戻す」を実行
        sut.ReturnToPlayingTrackCommand.Execute(null);

        // Assert 2
        Assert.Equal("PlayingTrack", sut.TargetTrackTitle);
        Assert.Equal(1, sut.TotalPlayCount);
        Assert.False(sut.IsShowingNonPlayingTrack);
        Assert.False(sut.CanReturnToPlayingTrack);
    }

    /// <summary>
    /// ダッシュボードが非表示（IsActiveView = false）の間の統計更新は遅延され、再表示時に最新統計へ反映されることを検証します。
    /// </summary>
    [Fact]
    public void IsActiveView_非表示中の統計更新後にアクティブ化した場合_最新統計が遅延反映されること()
    {
        // Arrange
        var repository = new InMemoryListeningStatsRepository();
        var audioMock = new Mock<IAudioService>();
        var track = CreateTrack("LazyTrack", 120);
        audioMock.Setup(a => a.CurrentTrack).Returns(track);
        using var statsService = new ListeningStatsApplicationService(repository, audioMock.Object);
        using var sut = new ListeningStatsViewModel(statsService, audioMock.Object, new ImmediateDispatcherStub())
        {
            IsActiveView = false,
        };

        // Act 1: 非表示中に再生完了
        audioMock.Raise(a => a.TrackPlaybackEnded += null, track);

        // Assert 1: 非表示中は即時再描画しない
        Assert.Equal(0, sut.TotalPlayCount);

        // Act 2: アクティブ化
        sut.IsActiveView = true;

        // Assert 2: 最新統計が反映される
        Assert.Equal(1, sut.TotalPlayCount);
    }

    /// <summary>
    /// FormatDuration が 0 秒・秒のみ・分秒・時間分の各レンジを期待通りの日本語文字列へ整形することを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, "0分")]
    [InlineData(45, "45秒")]
    [InlineData(185, "3分 5秒")]
    [InlineData(4500, "1時間 15分")]
    public void FormatDuration_各種秒数入力時_期待される日本語書式を返すこと(int totalSeconds, string expected)
    {
        // Arrange & Act
        string actual = ListeningStatsViewModel.FormatDuration(TimeSpan.FromSeconds(totalSeconds));

        // Assert
        Assert.Equal(expected, actual);
    }
}
