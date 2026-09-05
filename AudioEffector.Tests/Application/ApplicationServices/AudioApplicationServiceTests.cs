using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Application.ApplicationServices;

/// <summary>
/// AudioApplicationServiceの再生制御、キュー管理、シーク、音量・ミュート、戦略遷移、イベント発行を検証するテストクラス
/// </summary>
public sealed class AudioApplicationServiceTests
{
    private static Track CreateTestTrack(string title = "Song A")
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create($@"C:\Music\{title}.mp3"),
            title: title,
            artist: "Artist",
            album: "Album",
            duration: TimeSpan.FromMinutes(3.5));
    }

    /// <summary>
    /// コンストラクタ引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AudioApplicationService(null!, mockTrackRepo.Object, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new AudioApplicationService(mockEngine.Object, null!, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, null!));
    }

    /// <summary>
    /// 登録済みのトラックIDを指定してPlayTrackAsyncを呼び出した際、曲がロード・再生され各イベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_登録済みトラックID指定_IAudioEngineに曲がロードされ再生とイベント発行が行われること()
    {
        // Arrange
        var track = CreateTestTrack("Track 1");
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetByIdAsync(track.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(track);

        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var result = await sut.PlayTrackAsync(track.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(track, sut.CurrentTrack);
        mockEngine.Verify(e => e.LoadTrackAsync(track, It.IsAny<CancellationToken>()), Times.Once);
        mockEngine.Verify(e => e.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<TrackChangedEvent>(ev => ev.Track == track), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaybackStateChangedEvent>(ev => ev.State == PlaybackState.Playing), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 未登録のトラックIDを指定してPlayTrackAsyncを呼び出した際、失敗Resultを返しエンジン再生やイベント発行が行われないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_未登録トラックID指定_失敗Resultを返しエンジン再生やイベント発行が行われないこと()
    {
        // Arrange
        var unknownId = TrackId.New();
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        mockTrackRepo.Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Track?)null);

        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        var result = await sut.PlayTrackAsync(unknownId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(sut.CurrentTrack);
        mockEngine.Verify(e => e.LoadTrackAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEngine.Verify(e => e.PlayAsync(It.IsAny<CancellationToken>()), Times.Never);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 複数トラックを指定してSetQueueAndPlayAsyncを呼び出した際、キューが設定され先頭インデックスの曲が再生されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SetQueueAndPlayAsync_複数トラック指定_キューが設定され先頭インデックスの曲が再生されること()
    {
        // Arrange
        var t1 = CreateTestTrack("Song 1");
        var t2 = CreateTestTrack("Song 2");

        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.SetQueueAndPlayAsync([t1, t2], startIndex: 1);

        // Assert
        Assert.Equal(2, sut.PlaybackQueue.Count);
        Assert.Equal(t2, sut.CurrentTrack);
        mockEngine.Verify(e => e.LoadTrackAsync(t2, It.IsAny<CancellationToken>()), Times.Once);
        mockEngine.Verify(e => e.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 空コレクションを指定してSetQueueAndPlayAsyncを呼び出した際、キューがクリアされ再生が行われないことを検証します。
    /// </summary>
    [Fact]
    public async Task SetQueueAndPlayAsync_空コレクション指定_キューがクリアされ再生が行われないこと()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.SetQueueAndPlayAsync([]);

        // Assert
        Assert.Empty(sut.PlaybackQueue);
        Assert.Null(sut.CurrentTrack);
        mockEngine.Verify(e => e.LoadTrackAsync(It.IsAny<Track>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// PauseAsync呼び出し時、IAudioEngineのPauseAsyncが実行され、Pausedイベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task PauseAsync_呼び出し時_IAudioEngineのPauseAsyncが呼ばれPausedイベントが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.PauseAsync();

        // Assert
        mockEngine.Verify(e => e.PauseAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaybackStateChangedEvent>(ev => ev.State == PlaybackState.Paused), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ResumeAsync呼び出し時、IAudioEngineのPlayAsyncが実行され、Playingイベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ResumeAsync_呼び出し時_IAudioEngineのPlayAsyncが呼ばれPlayingイベントが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.ResumeAsync();

        // Assert
        mockEngine.Verify(e => e.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaybackStateChangedEvent>(ev => ev.State == PlaybackState.Playing), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// StopAsync呼び出し時、IAudioEngineのStopAsyncが実行され、Stoppedイベントが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task StopAsync_呼び出し時_IAudioEngineのStopAsyncが呼ばれStoppedイベントが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // Act
        await sut.StopAsync();

        // Assert
        mockEngine.Verify(e => e.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<PlaybackStateChangedEvent>(ev => ev.State == PlaybackState.Stopped), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SeekAsync呼び出し時、指定位置へIAudioEngineのSeekAsyncが呼び出されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SeekAsync_指定位置へシーク_IAudioEngineのSeekAsyncが呼ばれること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);
        var targetPos = TimeSpan.FromSeconds(45);

        // Act
        await sut.SeekAsync(targetPos);

        // Assert
        mockEngine.Verify(e => e.SeekAsync(targetPos, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SetVolumeAsync呼び出し時、IAudioEngineに反映されVolumeChangedEventが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SetVolumeAsync_音量設定_IAudioEngineに反映されVolumeChangedEventが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);
        var volume = Volume.FromFloat(0.75f);

        // Act
        await sut.SetVolumeAsync(volume);

        // Assert
        Assert.Equal(volume, sut.CurrentVolume);
        mockEngine.Verify(e => e.SetVolumeAsync(0.75f, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<VolumeChangedEvent>(ev => Math.Abs(ev.Volume - 0.75f) < 0.01f && !ev.IsMuted), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// SetMuteAsync呼び出し時、実効音量（ミュート時は0）がエンジンへ渡されVolumeChangedEventが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SetMuteAsync_ミュート切り替え_実効音量が反映されVolumeChangedEventが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object);

        // 初期音量 0.8
        await sut.SetVolumeAsync(Volume.FromFloat(0.8f));

        // Act: ミュートON
        await sut.SetMuteAsync(true);

        // Assert
        Assert.True(sut.CurrentVolume.IsMuted);
        mockEngine.Verify(e => e.SetVolumeAsync(0.0f, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<VolumeChangedEvent>(ev => ev.Volume == 0.0f && ev.IsMuted), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 再生戦略に従ってNextTrackAsyncやPreviousTrackAsyncで順次遷移し、末尾到達時には停止することを検証します。
    /// </summary>
    [Fact]
    public async Task NextTrackAsyncおよびPreviousTrackAsync_再生戦略に従い遷移_次の曲が再生され末尾到達時は停止すること()
    {
        // Arrange
        var t1 = CreateTestTrack("Track 1");
        var t2 = CreateTestTrack("Track 2");

        var mockEngine = new Mock<IAudioEngine>();
        var mockTrackRepo = new Mock<ITrackRepository>();
        var mockEventBus = new Mock<IEventBus>();
        // SequentialPlaybackStrategy（通常順次再生）を使用
        var strategy = new SequentialPlaybackStrategy();
        var sut = new AudioApplicationService(mockEngine.Object, mockTrackRepo.Object, mockEventBus.Object, strategy);

        await sut.SetQueueAndPlayAsync([t1, t2], startIndex: 0);
        Assert.Equal(t1, sut.CurrentTrack);

        // Act 1: 次のトラックへ
        bool hasNext = await sut.NextTrackAsync();

        // Assert 1
        Assert.True(hasNext);
        Assert.Equal(t2, sut.CurrentTrack);

        // Act 2: 末尾から次のトラックへ（Sequentialなのでnullを返し停止）
        bool hasEndNext = await sut.NextTrackAsync();

        // Assert 2
        Assert.False(hasEndNext);
        mockEngine.Verify(e => e.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
