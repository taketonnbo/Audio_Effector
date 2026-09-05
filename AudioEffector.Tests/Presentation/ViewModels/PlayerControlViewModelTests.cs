using System;
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

}
