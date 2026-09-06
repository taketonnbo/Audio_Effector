using System;
using System.Collections.ObjectModel;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="PlayerControlViewModel"/> の再生制御コマンド、音量・ミュート制御、再生モードおよびイベント購読を検証するテストクラス。
/// </summary>
public sealed class PlayerControlViewModelTests
{
    private readonly Mock<IAudioService> _audioServiceMock = new();
    private readonly Mock<IAudioEngine> _audioEngineMock = new();
    private readonly Mock<ISettingsService> _settingsServiceMock = new();
    private readonly InMemoryEventBus _eventBus = new();

    public PlayerControlViewModelTests()
    {
        _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings { Volume = 0.5f });
    }

    /// <summary>
    /// StopCommand実行時、AudioServiceのStopが呼び出されることを検証します。
    /// </summary>
    [Fact]
    public void StopCommand_実行時_AudioServiceのStopが呼び出される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.StopCommand.Execute(null);

        // Assert
        _audioServiceMock.Verify(a => a.Stop(It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Volumeプロパティ変更時、AudioServiceと設定に反映されVolumePercentが更新されることを検証します。
    /// </summary>
    [Fact]
    public void Volume_変更時_AudioServiceと設定に反映される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.Volume = 0.75f;

        // Assert
        Assert.Equal(0.75f, sut.Volume);
        Assert.Equal("75%", sut.VolumePercent);
        _audioServiceMock.VerifySet(a => a.Volume = 0.75f, Times.AtLeastOnce);
        _settingsServiceMock.Verify(s => s.SaveSettings(It.Is<AppSettings>(st => Math.Abs(st.Volume - 0.75f) < 0.01f)), Times.Once);
    }

    /// <summary>
    /// IsMutedをtrueに設定した際、音量が0になり、falseに戻した際に直前の音量が復元されることを検証します。
    /// </summary>
    [Fact]
    public void IsMuted_Trueに設定時_音量が0になり解除時に元の音量に戻る()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object)
        {
            Volume = 0.6f
        };

        // Act & Assert - Mute On
        sut.IsMuted = true;
        Assert.True(sut.IsMuted);
        Assert.Equal(0f, sut.Volume);

        // Act & Assert - Mute Off
        sut.IsMuted = false;
        Assert.False(sut.IsMuted);
        Assert.Equal(0.6f, sut.Volume);
    }

    /// <summary>
    /// IsShuffleEnabled変更時、AudioServiceに反映されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabled_変更時_AudioServiceに反映される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.IsShuffleEnabled = true;

        // Assert
        Assert.True(sut.IsShuffleEnabled);
        _audioServiceMock.VerifySet(a => a.IsShuffleEnabled = true, Times.Once);
    }

    /// <summary>
    /// RepeatMode変更時、IsAlbumRepeatプロパティと連動して更新されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, false)] // なし
    [InlineData(1, true)]  // 全曲
    public void RepeatMode_変更時_IsAlbumRepeatと連動して更新される(int mode, bool expectedIsAlbumRepeat)
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.RepeatMode = mode;

        // Assert
        Assert.Equal(mode, sut.RepeatMode);
        Assert.Equal(expectedIsAlbumRepeat, sut.IsAlbumRepeat);
    }

    /// <summary>
    /// CurrentTrack変更時、PlaybackListTracks内の該当トラックのみIsPlayingがtrueになり他はfalseに排他同期されることを検証します。
    /// </summary>
    [Fact]
    public void CurrentTrack_変更時_PlaybackListTracks内の該当トラックのみIsPlayingがTrueになり他はFalseになる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1", IsPlaying = true };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2", IsPlaying = true };
        var track3 = new Track { FilePath = @"C:\Music\song3.mp3", Title = "Song 3", IsPlaying = true };

        sut.PlaybackListTracks = new ObservableCollection<Track> { track1, track2, track3 };

        // Act - track2を再生中トラックに指定
        sut.CurrentTrack = track2;

        // Assert
        Assert.False(track1.IsPlaying);
        Assert.True(track2.IsPlaying);
        Assert.False(track3.IsPlaying);
    }

    /// <summary>
    /// 別インスタンスだが同一FilePathのトラックがCurrentTrackに設定された場合でも正しく排他同期されることを検証します。
    /// </summary>
    [Fact]
    public void CurrentTrack_別インスタンスだが同一FilePathの場合_正しく排他同期される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1", IsPlaying = false };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2", IsPlaying = false };
        var track1DifferentInstance = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1 Different Instance" };

        sut.PlaybackListTracks = new ObservableCollection<Track> { track1, track2 };

        // Act
        sut.CurrentTrack = track1DifferentInstance;

        // Assert
        Assert.True(track1.IsPlaying);
        Assert.False(track2.IsPlaying);
    }

    /// <summary>
    /// CurrentTrackがnullの場合、PlaybackListTracks内の全トラックのIsPlayingがfalseになることを検証します。
    /// </summary>
    [Fact]
    public void CurrentTrack_Null設定時_PlaybackListTracks内の全トラックのIsPlayingがFalseになる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1", IsPlaying = true };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2", IsPlaying = true };

        sut.PlaybackListTracks = new ObservableCollection<Track> { track1, track2 };

        // Act
        sut.CurrentTrack = null;

        // Assert
        Assert.False(track1.IsPlaying);
        Assert.False(track2.IsPlaying);
    }

    /// <summary>
    /// SetPlaybackList呼び出し時、現在再生中のトラックが正しく反映されて排他同期されることを検証します。
    /// </summary>
    [Fact]
    public void SetPlaybackList_呼び出し時_CurrentTrackの状態が排他同期される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1", IsPlaying = true };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2", IsPlaying = false };
        var track3 = new Track { FilePath = @"C:\Music\song3.mp3", Title = "Song 3", IsPlaying = true };

        sut.CurrentTrack = track2;

        // Act
        sut.SetPlaybackList(new[] { track1, track2, track3 }, "Test Album", "Test Artist");

        // Assert
        Assert.False(sut.PlaybackListTracks[0].IsPlaying);
        Assert.True(sut.PlaybackListTracks[1].IsPlaying);
        Assert.False(sut.PlaybackListTracks[2].IsPlaying);
    }

    /// <summary>
    /// RemoveFromQueue呼び出し時、指定されたトラックがキューから削除されAudioServiceに反映されることを検証します。
    /// </summary>
    [Fact]
    public void RemoveFromQueue_指定トラックが存在する場合_キューから削除されAudioServiceに反映される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };
        sut.PlayQueue = new ObservableCollection<Track> { track1, track2 };

        // Act
        sut.RemoveFromQueueCommand.Execute(track1);

        // Assert
        Assert.Single(sut.PlayQueue);
        Assert.Equal("Song 2", sut.PlayQueue[0].Title);
        _audioServiceMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(l => l.Count == 1 && l[0].Title == "Song 2"), It.IsAny<Track?>()), Times.Once);
    }

    /// <summary>
    /// ClearQueue呼び出し時、キューの全曲が削除されAudioServiceに空リストが渡されることを検証します。
    /// </summary>
    [Fact]
    public void ClearQueue_キューに曲が存在する場合_全曲削除されAudioServiceに空リストが設定される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };
        sut.PlayQueue = new ObservableCollection<Track> { track1, track2 };

        // Act
        sut.ClearQueueCommand.Execute(null);

        // Assert
        Assert.Empty(sut.PlayQueue);
        _audioServiceMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(l => l.Count == 0), It.IsAny<Track?>()), Times.Once);
    }

    /// <summary>
    /// AudioServiceのPlaylistChangedイベント発火時、PlayQueueの要素が更新されることを検証します。
    /// </summary>
    [Fact]
    public void PlaylistChanged_発火時_PlayQueueが更新される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };
        var newPlaylist = new List<Track> { track2, track1 };

        // Act - イベントを発火
        _audioServiceMock.Raise(a => a.PlaylistChanged += null, newPlaylist);

        // Assert
        Assert.Equal(2, sut.PlayQueue.Count);
        Assert.Equal("Song 2", sut.PlayQueue[0].Title);
        Assert.Equal("Song 1", sut.PlayQueue[1].Title);
    }

    /// <summary>
    /// CurrentTrackに実体のないファイルパスを持つトラックを設定した際、画像読み込みで例外が発生せず安全にフォールバックされることを検証します。
    /// </summary>
    [Fact]
    public void CurrentTrack_画像読み込み失敗時_例外が発生せず安全にフォールバックされる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track = new Track
        {
            FilePath = @"C:\NonExistent\non_existent_song.mp3",
            Title = "Non Existent Song",
            Duration = TimeSpan.FromMinutes(3),
            CoverImage = null
        };

        // Act & Assert - 例外が発生しないこと
        var ex = Record.Exception(() => sut.CurrentTrack = track);
        Assert.Null(ex);
        Assert.Equal("03:00", sut.TotalTimeDisplay);
    }

    /// <summary>
    /// PreviousCommand実行時、例外が発生せずAudioServiceのPreviousが呼び出されることを検証します。
    /// </summary>
    [Fact]
    public void PreviousCommand_実行時_例外が発生せずAudioServiceのPreviousが呼び出される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        var ex = Record.Exception(() => sut.PreviousCommand.Execute(null));

        // Assert
        Assert.Null(ex);
        _audioServiceMock.Verify(a => a.Previous(), Times.Once);
    }

    /// <summary>
    /// ClearQueue実行時、AudioServiceのStopが呼ばれ、CurrentTrackおよび再生中表示がクリアされることを検証します。
    /// </summary>
    [Fact]
    public void ClearQueue_実行時_再生が停止され再生中情報がクリアされる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track = new Track
        {
            FilePath = @"C:\Music\song1.mp3",
            Title = "Song 1",
            Duration = TimeSpan.FromMinutes(4)
        };
        sut.PlayQueue.Add(track);
        sut.CurrentTrack = track;
        sut.Progress = 50.0;

        // Act
        sut.ClearQueue();

        // Assert
        Assert.Empty(sut.PlayQueue);
        Assert.Null(sut.CurrentTrack);
        Assert.Equal("00:00", sut.TotalTimeDisplay);
        Assert.Equal("00:00", sut.CurrentTimeDisplay);
        Assert.Equal(0.0, sut.Progress);
        Assert.Null(sut.NowPlayingImage);
        _audioServiceMock.Verify(a => a.Stop(false), Times.Once);
        _audioServiceMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(l => l.Count == 0), null), Times.Once);
    }

    /// <summary>
    /// RemoveFromQueueで最後の1曲を削除した際、ClearQueueが実行され再生停止・情報クリアが行われることを検証します。
    /// </summary>
    [Fact]
    public void RemoveFromQueue_最後の1曲を削除時_ClearQueueが実行され再生中情報がクリアされる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track = new Track
        {
            FilePath = @"C:\Music\song1.mp3",
            Title = "Song 1",
            Duration = TimeSpan.FromMinutes(3)
        };
        sut.PlayQueue.Add(track);
        sut.CurrentTrack = track;

        // Act
        sut.RemoveFromQueue(track);

        // Assert
        Assert.Empty(sut.PlayQueue);
        Assert.Null(sut.CurrentTrack);
        Assert.Equal("00:00", sut.TotalTimeDisplay);
        Assert.Equal("00:00", sut.CurrentTimeDisplay);
        _audioServiceMock.Verify(a => a.Stop(false), Times.Once);
    }

    /// <summary>
    /// RemoveFromQueueで複数曲ある状態で現在再生中の曲を削除した際、後続の曲へ再生が遷移することを検証します。
    /// </summary>
    [Fact]
    public void RemoveFromQueue_再生中の曲を削除時_後続曲へ遷移する()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };
        sut.PlayQueue.Add(track1);
        sut.PlayQueue.Add(track2);
        sut.CurrentTrack = track1;

        // Act - 現在再生中のtrack1を削除
        sut.RemoveFromQueue(track1);

        // Assert
        Assert.Single(sut.PlayQueue);
        Assert.Equal("Song 2", sut.PlayQueue[0].Title);
        _audioServiceMock.Verify(a => a.PlayTrack(track2), Times.Once);
    }
}

