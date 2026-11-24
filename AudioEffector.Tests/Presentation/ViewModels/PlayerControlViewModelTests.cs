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
        _audioServiceMock.Verify(a => a.RemoveTrack(track1), Times.Once);
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
            Duration = TimeSpan.FromMinutes(3)
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
        sut.SetPlaybackList(new[] { track }, "Test Album", "Test Artist");
        sut.CurrentTrack = track;
        sut.Progress = 50.0;

        // Act
        sut.ClearQueue();

        // Assert
        Assert.Empty(sut.PlayQueue);
        Assert.Empty(sut.PlaybackListTracks);
        Assert.Equal("No Album Selected", sut.PlaybackListName);
        Assert.Equal(string.Empty, sut.PlaybackListSubtitle);
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
        sut.SetPlaybackList(new[] { track }, "Test Album", "Test Artist");
        _audioServiceMock.Raise(a => a.PlaylistChanged += null, new List<Track> { track });
        sut.CurrentTrack = track;

        // Act
        sut.RemoveFromQueue(track);

        // Assert
        Assert.Empty(sut.PlayQueue);
        Assert.Empty(sut.PlaybackListTracks);
        Assert.Equal("No Album Selected", sut.PlaybackListName);
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

    /// <summary>
    /// TrackPlaybackEndedイベント発火時、PlayHistoryの先頭にトラックが追加されることを検証します。
    /// </summary>
    [Fact]
    public void TrackPlaybackEnded_発火時_PlayHistoryに先頭追加される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };

        // Act
        _audioServiceMock.Raise(a => a.TrackPlaybackEnded += null, track1);
        _audioServiceMock.Raise(a => a.TrackPlaybackEnded += null, track2);

        // Assert - 最新のtrack2が先頭(index 0)にあること
        Assert.Equal(2, sut.PlayHistory.Count);
        Assert.Equal("Song 2", sut.PlayHistory[0].Title);
        Assert.Equal("Song 1", sut.PlayHistory[1].Title);
    }

    /// <summary>
    /// 既に履歴に存在する曲が再度再生終了した際、重複せず先頭に移動することを検証します。
    /// </summary>
    [Fact]
    public void AddTrackToHistory_既存曲が再再生された場合_先頭に移動する()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };
        var track1Duplicate = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1 Duplicate" };

        sut.AddTrackToHistory(track1);
        sut.AddTrackToHistory(track2);

        // Act - track1の重複を再度追加
        sut.AddTrackToHistory(track1Duplicate);

        // Assert - 2件のままで、track1が先頭に移動していること
        Assert.Equal(2, sut.PlayHistory.Count);
        Assert.Equal("C:\\Music\\song1.mp3", sut.PlayHistory[0].FilePath);
        Assert.Equal("C:\\Music\\song2.mp3", sut.PlayHistory[1].FilePath);
    }

    /// <summary>
    /// 履歴が上限（100件）を超えた場合、古い末尾アイテムが自動破棄されることを検証します。
    /// </summary>
    [Fact]
    public void AddTrackToHistory_100件超過時_古い末尾アイテムが自動破棄される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act - 105曲追加
        for (int i = 0; i < 105; i++)
        {
            sut.AddTrackToHistory(new Track { FilePath = $@"C:\Music\song{i}.mp3", Title = $"Song {i}" });
        }

        // Assert - 最大100件に制限され、最新のSong 104が先頭、最も古い5〜104が保持されていること
        Assert.Equal(100, sut.PlayHistory.Count);
        Assert.Equal("Song 104", sut.PlayHistory[0].Title);
        Assert.Equal("Song 5", sut.PlayHistory[99].Title);
    }

    /// <summary>
    /// ClearHistoryCommand実行時、PlayHistoryが全クリアされることを検証します。
    /// </summary>
    [Fact]
    public void ClearHistoryCommand_実行時_PlayHistoryが全クリアされる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        sut.PlayHistory.Add(new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" });
        sut.PlayHistory.Add(new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" });

        // Act
        sut.ClearHistoryCommand.Execute(null);

        // Assert
        Assert.Empty(sut.PlayHistory);
    }

    /// <summary>
    /// RemoveFromHistoryCommand実行時、指定トラックがPlayHistoryから削除されることを検証します。
    /// </summary>
    [Fact]
    public void RemoveFromHistoryCommand_実行時_指定トラックがPlayHistoryから削除される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" };
        sut.PlayHistory.Add(track1);
        sut.PlayHistory.Add(track2);

        // Act
        sut.RemoveFromHistoryCommand.Execute(track1);

        // Assert
        Assert.Single(sut.PlayHistory);
        Assert.Equal("Song 2", sut.PlayHistory[0].Title);
    }

    /// <summary>
    /// PlayFromHistoryCommand実行時、再生リストタブ（インデックス0）に切り替わり、キュー先頭挿入（insertAtBeginning: true）でPlayTrackが呼ばれることを検証します。
    /// </summary>
    [Fact]
    public void PlayFromHistoryCommand_実行時_再生リストタブへ自動切替されキュー先頭追加で再生が開始される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        sut.SelectedQueueTabIndex = 1; // 履歴タブを開いている状態
        var track = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };

        // Act
        sut.PlayFromHistoryCommand.Execute(track);

        // Assert - タブが 0（再生リスト）に自動切り替えされ、先頭追加で PlayTrack が呼ばれること
        Assert.Equal(0, sut.SelectedQueueTabIndex);
        _audioServiceMock.Verify(a => a.PlayTrack(track, true), Times.Once);
    }

    /// <summary>
    /// SyncTrackPlayingStates実行時、PlayQueue内の該当トラックのIsPlayingが更新されることを検証します。
    /// </summary>
    [Fact]
    public void SyncTrackPlayingStates_実行時_PlayQueue内のトラックのIsPlayingが同期更新される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1", IsPlaying = false };
        var track2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2", IsPlaying = false };
        sut.PlayQueue = new System.Collections.ObjectModel.ObservableCollection<Track> { track1, track2 };

        // Act
        sut.SyncTrackPlayingStates(track2);

        // Assert
        Assert.False(track1.IsPlaying);
        Assert.True(track2.IsPlaying);
    }

    /// <summary>
    /// SelectQueueTabCommand実行時、SelectedQueueTabIndexが更新されることを検証します。
    /// </summary>
    [Fact]
    public void SelectQueueTabCommand_実行時_SelectedQueueTabIndexが更新される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        Assert.Equal(0, sut.SelectedQueueTabIndex);

        // Act & Assert
        sut.SelectQueueTabCommand.Execute("1");
        Assert.Equal(1, sut.SelectedQueueTabIndex);

        sut.SelectQueueTabCommand.Execute(0);
        Assert.Equal(0, sut.SelectedQueueTabIndex);
    }

    /// <summary>
    /// ClearCurrentTabCommand実行時、タブ0ならキューがクリアされ、タブ1なら履歴がクリアされることを検証します。
    /// </summary>
    [Fact]
    public void ClearCurrentTabCommand_選択中タブに応じて適切なクリアが実行される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        sut.PlayQueue.Add(new Track { FilePath = @"C:\Music\queue.mp3", Title = "Queue Track" });
        sut.PlayHistory.Add(new Track { FilePath = @"C:\Music\history.mp3", Title = "History Track" });

        // Act 1 - タブ0（キュータブ）の状態で実行
        sut.SelectedQueueTabIndex = 0;
        sut.ClearCurrentTabCommand.Execute(null);

        // Assert 1 - キューのみクリアされ、履歴は残る
        Assert.Empty(sut.PlayQueue);
        Assert.Single(sut.PlayHistory);

        // Act 2 - タブ1（履歴タブ）の状態で実行
        sut.PlayQueue.Add(new Track { FilePath = @"C:\Music\queue2.mp3", Title = "Queue Track 2" });
        sut.SelectedQueueTabIndex = 1;
        sut.ClearCurrentTabCommand.Execute(null);

        // Assert 2 - 履歴がクリアされ、キューは残る
        Assert.Empty(sut.PlayHistory);
        Assert.Single(sut.PlayQueue);
    }

    /// <summary>
    /// PlayQueueプロパティに新しいコレクションが代入された際、CurrentTrackと一致する曲のIsPlayingが自動的に同期されることを検証します。
    /// </summary>
    [Fact]
    public void PlayQueue_新しいコレクション代入時_CurrentTrackと一致する曲のIsPlayingが自動的にtrueに同期される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var current = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" };
        sut.CurrentTrack = current;

        var newQueueTrack1 = new Track { FilePath = @"C:\Music\song1.mp3", Title = "Song 1", IsPlaying = false };
        var newQueueTrack2 = new Track { FilePath = @"C:\Music\song2.mp3", Title = "Song 2", IsPlaying = false };

        // Act - PlayQueue に新しいコレクションを設定
        sut.PlayQueue = new System.Collections.ObjectModel.ObservableCollection<Track> { newQueueTrack1, newQueueTrack2 };

        // Assert - setter 内で自動的に SyncTrackPlayingStates が実行され、newQueueTrack1.IsPlaying が true になること
        Assert.True(newQueueTrack1.IsPlaying);
        Assert.False(newQueueTrack2.IsPlaying);
    }

    /// <summary>
    /// MoveUserQueueItem 呼び出し時、AudioService の MoveUserQueueItem に委譲されることを検証します。
    /// </summary>
    [Fact]
    public void MoveUserQueueItem_呼び出し時_AudioServiceへ委譲される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.MoveUserQueueItem(0, 2);

        // Assert
        _audioServiceMock.Verify(a => a.MoveUserQueueItem(0, 2), Times.Once);
    }

    /// <summary>
    /// MoveAlbumQueueItem 呼び出し時、AudioService の MoveAlbumQueueItem に委譲されることを検証します。
    /// </summary>
    [Fact]
    public void MoveAlbumQueueItem_呼び出し時_AudioServiceへ委譲される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.MoveAlbumQueueItem(2, 0);

        // Assert
        _audioServiceMock.Verify(a => a.MoveAlbumQueueItem(2, 0), Times.Once);
    }

    /// <summary>
    /// MoveAlbumQueueItemToUserQueue 呼び出し時、AudioService の MoveAlbumQueueItemToUserQueue に委譲されることを検証します。
    /// </summary>
    [Fact]
    public void MoveAlbumQueueItemToUserQueue_呼び出し時_AudioServiceへ委譲される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        // Act
        sut.MoveAlbumQueueItemToUserQueue(1, 0);

        // Assert
        _audioServiceMock.Verify(a => a.MoveAlbumQueueItemToUserQueue(1, 0), Times.Once);
    }

    /// <summary>
    /// SetPlaybackList に startTrack を渡した場合、IAudioService.SetPlaylist へ開始トラックが正しく伝播されることを検証します。
    /// </summary>
    [Fact]
    public void SetPlaybackList_startTrack指定時_AudioServiceのSetPlaylistに開始トラックが渡される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var tracks = Enumerable.Range(1, 9)
            .Select(i => new Track { FilePath = $@"C:\Music\track{i}.mp3", Title = $"Track {i}", TrackNumber = (uint)i })
            .ToList();
        var startTrack = tracks[6]; // 7曲目

        // Act
        sut.SetPlaybackList(tracks, "Album A", "Artist A", updateAudioService: true, startTrack: startTrack);

        // Assert
        _audioServiceMock.Verify(
            a => a.SetPlaylist(
                It.Is<List<Track>>(l => l.Count == 9),
                It.Is<Track?>(t => ReferenceEquals(t, startTrack))),
            Times.Once);
        Assert.Equal(9, sut.PlaybackListTracks.Count);
        Assert.Empty(sut.PlayQueue);
    }

    /// <summary>
    /// SetPlaybackList で updateAudioService に false を指定した際、既存の PlayQueue（未再生キュー）が全曲リストで上書きされないことを検証します。
    /// </summary>
    [Fact]
    public void SetPlaybackList_updateAudioServiceがFalseの場合_PlayQueueが全曲で上書きされない()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var tracks = Enumerable.Range(1, 5)
            .Select(i => new Track { FilePath = $@"C:\Music\track{i}.mp3", Title = $"Track {i}", TrackNumber = (uint)i })
            .ToList();
        sut.PlayQueue = new ObservableCollection<Track> { tracks[3], tracks[4] }; // 4〜5曲目のみ未再生

        // Act
        sut.SetPlaybackList(tracks, "Album A", "Artist A", updateAudioService: false);

        // Assert
        _audioServiceMock.Verify(a => a.SetPlaylist(It.IsAny<List<Track>>(), It.IsAny<Track?>()), Times.Never);
        Assert.Equal(5, sut.PlaybackListTracks.Count);
        Assert.Equal(new[] { "Track 4", "Track 5" }, sut.PlayQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// アルバム（9曲）の途中曲・先頭曲・最終曲を PlayTrackCommand および TrackPlayHandler 経由で再生した際、
    /// AlbumQueue および PlayQueue に選択曲の後続トラックのみが格納され、選択曲より前と選択曲自身が含まれないことを検証します。
    /// </summary>
    [Theory]
    [InlineData(6, new[] { "Track 8", "Track 9" })]
    [InlineData(8, new string[0])]
    [InlineData(0, new[] { "Track 2", "Track 3", "Track 4", "Track 5", "Track 6", "Track 7", "Track 8", "Track 9" })]
    public void PlayTrackCommand_TrackPlayHandler経由でアルバム途中曲を再生時_後続トラックのみがキューに格納される(
        int startIndex,
        string[] expectedQueueTitles)
    {
        // Arrange
        using var audioService = new AudioEffector.Infrastructure.Audio.AudioService();
        using var sut = new PlayerControlViewModel(
            audioService,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var albumTracks = Enumerable.Range(1, 9)
            .Select(i => new Track { FilePath = $@"C:\Music\album_{i}.mp3", Title = $"Track {i}", TrackNumber = (uint)i })
            .ToList();

        sut.TrackPlayHandler = track =>
        {
            sut.SetPlaybackList(albumTracks, "Test Album", "Test Artist", startTrack: track);
            return false;
        };

        var targetTrack = albumTracks[startIndex];

        // Act
        sut.PlayTrackCommand.Execute(targetTrack);

        // Assert
        Assert.Equal(targetTrack.Title, sut.CurrentTrack?.Title);
        Assert.Equal(expectedQueueTitles, sut.AlbumQueue.Select(t => t.Title).ToArray());
        Assert.Equal(expectedQueueTitles, sut.PlayQueue.Select(t => t.Title).ToArray());
        Assert.DoesNotContain(sut.AlbumQueue, t => t.Title == targetTrack.Title);
    }

    /// <summary>
    /// LibraryViewModel 経由でアルバムの途中曲を再生し、PlaybackRequested ハンドラで SetPlaybackList(updateAudioService: false) を呼んだ際、
    /// AudioService.SetPlaylist が二重呼び出しされず後続曲のみがキューに残ることを検証します。
    /// </summary>
    [Fact]
    public void LibraryViewModel経由の途中曲再生_updateAudioServiceFalse連携時_SetPlaylistが二重呼び出しされず後続曲のみが残る()
    {
        // Arrange
        var trackRepoMock = new Mock<AudioEffector.Domain.Repositories.ITrackRepository>();
        var favoriteRepoMock = new Mock<AudioEffector.Domain.Repositories.IFavoriteRepository>();
        var libraryService = new LibraryApplicationService(
            trackRepoMock.Object,
            favoriteRepoMock.Object,
            new AudioEffector.Infrastructure.Library.TagLibMetadataExtractor(),
            _eventBus);

        using var audioService = new AudioEffector.Infrastructure.Audio.AudioService();
        int setPlaylistCallCount = 0;
        var countingAudioServiceMock = new Mock<IAudioService>();
        countingAudioServiceMock
            .Setup(a => a.SetPlaylist(It.IsAny<List<Track>>(), It.IsAny<Track?>()))
            .Callback<List<Track>, Track?>((tracks, start) =>
            {
                setPlaylistCallCount++;
                audioService.SetPlaylist(tracks, start);
            });
        countingAudioServiceMock
            .Setup(a => a.PlayTrack(It.IsAny<Track>()))
            .Callback<Track>(t => audioService.PlayTrack(t));
        countingAudioServiceMock.SetupGet(a => a.UserQueue).Returns(() => audioService.UserQueue);
        countingAudioServiceMock.SetupGet(a => a.AlbumQueue).Returns(() => audioService.AlbumQueue);
        audioService.PlaylistChanged += list => countingAudioServiceMock.Raise(a => a.PlaylistChanged += null, list);
        audioService.TrackChanged += t =>
        {
            if (t != null)
            {
                countingAudioServiceMock.Raise(a => a.TrackChanged += null, t);
            }
        };

        using var playerControl = new PlayerControlViewModel(
            countingAudioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        using var sut = new LibraryViewModel(libraryService, countingAudioServiceMock.Object, _settingsServiceMock.Object);

        var albumTracks = Enumerable.Range(1, 9)
            .Select(i => new Track { FilePath = $@"C:\Music\lib_{i}.mp3", Title = $"Track {i}", Album = "Album X", Artist = "Artist X", TrackNumber = (uint)i })
            .ToList();
        var album = new Album { Title = "Album X", Artist = "Artist X" };
        foreach (var t in albumTracks)
        {
            album.Tracks.Add(t);
        }
        sut.Albums.Add(album);

        sut.PlaybackRequested += (tracks, _, name, subtitle) =>
        {
            playerControl.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
        };

        // Act - 7曲目から再生開始
        sut.PlayTrackCommand.Execute(albumTracks[6]);

        // Assert - SetPlaylist は 1 回のみ呼ばれ、右側パネルには全9曲が表示されつつ、8〜9曲目のみがキューに残ること
        Assert.Equal(1, setPlaylistCallCount);
        Assert.Equal(9, playerControl.PlaybackListTracks.Count);
        Assert.Equal(albumTracks.Select(t => t.Title).ToArray(), playerControl.PlaybackListTracks.Select(t => t.Title).ToArray());
        Assert.True(albumTracks[6].IsPlaying);
        Assert.Equal(1, playerControl.PlaybackListTracks.Count(t => t.IsPlaying));
        Assert.Equal(new[] { "Track 8", "Track 9" }, playerControl.AlbumQueue.Select(t => t.Title).ToArray());
        Assert.Equal(new[] { "Track 8", "Track 9" }, playerControl.PlayQueue.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 既に再生中の同一トラックを PlayTrackCommand で指定した際、TrackPlayHandler でキューを再構築せず TogglePlayPause のみを呼ぶことを検証します。
    /// </summary>
    [Fact]
    public void PlayTrackCommand_現在再生中と同一トラック指定時_キューを再構築せずTogglePlayPauseが呼ばれる()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track = new Track { FilePath = @"C:\Music\same.mp3", Title = "Same Track" };
        sut.CurrentTrack = track;
        bool handlerCalled = false;
        sut.TrackPlayHandler = _ =>
        {
            handlerCalled = true;
            return false;
        };

        // Act
        sut.PlayTrackCommand.Execute(track);

        // Assert
        Assert.False(handlerCalled);
        _audioServiceMock.Verify(a => a.TogglePlayPause(), Times.Once);
        _audioServiceMock.Verify(a => a.PlayTrack(It.IsAny<Track>()), Times.Never);
    }

    /// <summary>
    /// 予約キュー再生に伴う右ペイン自動追従で PlaybackListName が別アルバムへ変化した状態で、
    /// アルバム残り曲から予約キューへ D&amp;D 移動（PlaylistChanged 発火）しても AlbumQueueTitle が元のアルバム名のまま維持されることを検証します。
    /// </summary>
    [Fact]
    public void AlbumQueueTitle_予約キュー再生でPlaybackListName変更後に移動しても_元のアルバム名が維持される()
    {
        // Arrange
        var albumATracks = new List<Track>
        {
            new() { FilePath = @"C:\Music\albumA_1.mp3", Title = "Album A Track 1", Album = "Album A" },
            new() { FilePath = @"C:\Music\albumA_2.mp3", Title = "Album A Track 2", Album = "Album A" },
            new() { FilePath = @"C:\Music\albumA_3.mp3", Title = "Album A Track 3", Album = "Album A" }
        };
        var userQueueTracks = new List<Track> { albumATracks[1] };
        var remainingAlbumATracks = new List<Track> { albumATracks[2] };

        _audioServiceMock.SetupGet(a => a.UserQueue).Returns(userQueueTracks);
        _audioServiceMock.SetupGet(a => a.AlbumQueue).Returns(remainingAlbumATracks);
        _audioServiceMock
            .Setup(a => a.MoveAlbumQueueItemToUserQueue(0, 0))
            .Callback(() => _audioServiceMock.Raise(a => a.PlaylistChanged += null, new List<Track> { albumATracks[1], albumATracks[2] }));

        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        sut.SetPlaybackList(albumATracks, "Album A", "Artist A");

        // Act - 予約キュー再生の右ペイン自動追従を模擬して PlaybackListName を別アルバム B へ書き換えた後に D&D 移動を実行
        sut.PlaybackListName = "Album B";
        sut.MoveAlbumQueueItemToUserQueue(0, 0);

        // Assert
        Assert.Equal("Album B", sut.PlaybackListName);
        Assert.Equal("Album A", sut.AlbumQueueSourceName);
        Assert.Equal("次のアルバムから: Album A", sut.AlbumQueueTitle);
    }

    /// <summary>
    /// SetPlaybackList で別の新しいアルバムを再生開始した場合、AlbumQueueTitle が新しいアルバム名へ正しく更新され PropertyChanged が通知されることを検証します。
    /// </summary>
    [Fact]
    public void AlbumQueueTitle_SetPlaybackListで別アルバム再生開始時_新しいアルバム名へ更新される()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var albumATracks = new[] { new Track { FilePath = @"C:\Music\a1.mp3", Title = "A1", Album = "Album A" } };
        var albumBTracks = new[] { new Track { FilePath = @"C:\Music\b1.mp3", Title = "B1", Album = "Album B" } };
        sut.SetPlaybackList(albumATracks, "Album A", "Artist A");

        var notifiedProperties = new List<string?>();
        sut.PropertyChanged += (_, e) => notifiedProperties.Add(e.PropertyName);

        // Act
        sut.SetPlaybackList(albumBTracks, "Album B", "Artist B");

        // Assert
        Assert.Equal("Album B", sut.AlbumQueueSourceName);
        Assert.Equal("次のアルバムから: Album B", sut.AlbumQueueTitle);
        Assert.Contains(nameof(PlayerControlViewModel.AlbumQueueTitle), notifiedProperties);
    }

    /// <summary>
    /// ClearQueue 実行時、AlbumQueueSourceName がリセットされ AlbumQueueTitle がデフォルト見出しへ戻ることを検証します。
    /// </summary>
    [Fact]
    public void AlbumQueueTitle_ClearQueue実行時_デフォルトタイトルへ戻る()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var tracks = new[] { new Track { FilePath = @"C:\Music\a1.mp3", Title = "A1", Album = "Album A" } };
        sut.SetPlaybackList(tracks, "Album A", "Artist A");

        // Act
        sut.ClearQueue();

        // Assert
        Assert.Equal(string.Empty, sut.AlbumQueueSourceName);
        Assert.Equal("アルバムの残り曲", sut.AlbumQueueTitle);
    }

    /// <summary>
    /// AudioService.TrackChanged で null を受信した際（再生終了時）、AlbumQueueSourceName がリセットされ AlbumQueueTitle がデフォルト見出しへ戻ることを検証します。
    /// </summary>
    [Fact]
    public void AlbumQueueTitle_TrackChangedでNull受信時_デフォルトタイトルへ戻る()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var tracks = new[] { new Track { FilePath = @"C:\Music\a1.mp3", Title = "A1", Album = "Album A" } };
        sut.SetPlaybackList(tracks, "Album A", "Artist A");

        // Act
        _audioServiceMock.Raise(a => a.TrackChanged += null, (Track?)null!);

        // Assert
        Assert.Equal(string.Empty, sut.AlbumQueueSourceName);
        Assert.Equal("アルバムの残り曲", sut.AlbumQueueTitle);
    }

    /// <summary>
    /// AlbumQueueSourceName が空であっても AlbumQueue にトラックが存在する場合、先頭トラックの Album 名へフォールバックされることを検証します。
    /// </summary>
    [Fact]
    public void AlbumQueueTitle_AlbumQueueSourceNameが空でAlbumQueueに曲がある場合_先頭曲のアルバム名へフォールバックする()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var track = new Track { FilePath = @"C:\Music\f1.mp3", Title = "F1", Album = "Fallback Album" };

        // Act
        sut.AlbumQueue = new ObservableCollection<Track> { track };

        // Assert
        Assert.Equal(string.Empty, sut.AlbumQueueSourceName);
        Assert.Equal("次のアルバムから: Fallback Album", sut.AlbumQueueTitle);
    }

    /// <summary>
    /// ToggleMiniPlayerQueueCommand および CloseMiniPlayerQueueCommand の実行により、IsMiniPlayerQueueOpen が期待通り切り替わり通知されることを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerQueueCommands_開閉コマンド実行時_IsMiniPlayerQueueOpenが切り替わり変更通知が発火する()
    {
        // Arrange
        using var sut = new PlayerControlViewModel(
            _audioServiceMock.Object,
            _audioEngineMock.Object,
            _eventBus,
            _settingsServiceMock.Object);

        var notifiedProperties = new List<string?>();
        sut.PropertyChanged += (_, e) => notifiedProperties.Add(e.PropertyName);

        // Act & Assert (初期状態)
        Assert.False(sut.IsMiniPlayerQueueOpen);

        // Act & Assert (トグルで展開)
        sut.ToggleMiniPlayerQueueCommand.Execute(null);
        Assert.True(sut.IsMiniPlayerQueueOpen);
        Assert.Contains(nameof(PlayerControlViewModel.IsMiniPlayerQueueOpen), notifiedProperties);

        // Act & Assert (クローズで格納)
        sut.CloseMiniPlayerQueueCommand.Execute(null);
        Assert.False(sut.IsMiniPlayerQueueOpen);

        // Act & Assert (再度トグルで展開→再度トグルで格納)
        sut.ToggleMiniPlayerQueueCommand.Execute(null);
        Assert.True(sut.IsMiniPlayerQueueOpen);
        sut.ToggleMiniPlayerQueueCommand.Execute(null);
        Assert.False(sut.IsMiniPlayerQueueOpen);
    }
}

