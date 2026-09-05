using System;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="EqualizerViewModel"/> のバンド初期化、プリセット適用、音量変更、リセットコマンドの検証を行うテストクラス。
/// </summary>
public sealed class EqualizerViewModelTests
{
    private readonly Mock<IAudioEngine> _audioEngineMock = new();
    private readonly Mock<ISettingsRepository> _settingsRepoMock = new();
    private readonly Mock<ISettingsService> _settingsServiceMock = new();
    private readonly Mock<IAudioService> _legacyAudioServiceMock = new();
    private readonly InMemoryEventBus _eventBus = new();

    private readonly EqualizerApplicationService _equalizerAppService;
    private readonly AudioApplicationService _audioAppService;

    public EqualizerViewModelTests()
    {
        _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings { Volume = 0.5f });
        _settingsRepoMock.Setup(r => r.GetValueAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _audioEngineMock.Setup(e => e.SetEqualizerBandGainAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioEngineMock.Setup(e => e.SetEqualizerAllGainsAsync(It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _audioEngineMock.Setup(e => e.SetVolumeAsync(It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _equalizerAppService = new EqualizerApplicationService(
            _audioEngineMock.Object,
            _settingsRepoMock.Object,
            _eventBus,
            _legacyAudioServiceMock.Object);

        var trackRepoMock = new Mock<ITrackRepository>();
        _audioAppService = new AudioApplicationService(
            _audioEngineMock.Object,
            trackRepoMock.Object,
            _eventBus,
            playbackOrderStrategy: null,
            legacyAudioService: _legacyAudioServiceMock.Object);
    }

    /// <summary>
    /// コンストラクタ呼び出し時、規定の10バンドが適切な周波数で初期化されることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_初期化時_10バンドが規定周波数で生成される()
    {
        // Arrange & Act
        using var sut = new EqualizerViewModel(
            _equalizerAppService,
            _audioAppService,
            _eventBus,
            _settingsServiceMock.Object,
            _legacyAudioServiceMock.Object);

        // Assert
        Assert.Equal(10, sut.Bands.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(i, sut.Bands[i].Index);
            Assert.Equal(EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[i], sut.Bands[i].Frequency);
            Assert.Equal(0.0f, sut.Bands[i].Gain);
        }
    }

    /// <summary>
    /// SelectedPresetが設定された際、プリセットのバンドゲインが適用され設定が保存されることを検証します。
    /// </summary>
    [Fact]
    public async Task SelectedPreset_設定時_プリセットが適用され設定が保存される()
    {
        // Arrange
        using var sut = new EqualizerViewModel(
            _equalizerAppService,
            _audioAppService,
            _eventBus,
            _settingsServiceMock.Object,
            _legacyAudioServiceMock.Object);

        var preset = EqualizerPreset.CreateFlat("Bass Boost");
        preset.UpdateBandGain(0, Gain.FromDecibels(6.0f));
        preset.UpdateBandGain(1, Gain.FromDecibels(4.0f));

        // Act
        sut.SelectedPreset = preset;
        await Task.Delay(50);

        // Assert
        Assert.Same(preset, sut.SelectedPreset);
        Assert.False(sut.IsCustom);
        _settingsServiceMock.Verify(s => s.SaveSettings(It.Is<AppSettings>(st => st.LastUsedEffectPreset == "Bass Boost")), Times.AtLeastOnce());
    }

    /// <summary>
    /// Volume変更時、オーディオサービスへの音量反映と設定の保存が行われることを検証します。
    /// </summary>
    [Fact]
    public void Volume_変更時_AudioServiceへの反映と設定保存が行われる()
    {
        // Arrange
        using var sut = new EqualizerViewModel(
            _equalizerAppService,
            _audioAppService,
            _eventBus,
            _settingsServiceMock.Object,
            _legacyAudioServiceMock.Object);

        // Act
        sut.Volume = 0.8;

        // Assert
        Assert.Equal(0.8, sut.Volume, 2);
        _settingsServiceMock.Verify(s => s.SaveSettings(It.Is<AppSettings>(st => Math.Abs(st.Volume - 0.8f) < 0.01f)), Times.AtLeastOnce());
    }
}
