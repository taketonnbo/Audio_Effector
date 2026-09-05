using System.Collections.Generic;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="SettingsViewModel"/> の設定読み込み、カテゴリ選択、および各種設定値変更・保存処理を検証するテストクラス。
/// </summary>
public class SettingsViewModelTests
{
    private readonly Mock<ISettingsService> _settingsServiceMock = new();
    private readonly Mock<IAudioService> _audioServiceMock = new();
    private readonly AppSettings _initialSettings;

    public SettingsViewModelTests()
    {
        _initialSettings = new AppSettings
        {
            Volume = 0.6f,
            StartMinimized = false,
            AudioBufferSizeMs = 200,
            EnableNormalize = false
        };
        _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(_initialSettings);
    }

    /// <summary>
    /// コンストラクタ呼び出し時、設定サービスから初期値がロードされることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_初期設定値がプロパティにロードされる()
    {
        // Arrange & Act
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);

        // Assert
        Assert.False(sut.StartMinimized);
        Assert.Equal(200, sut.SelectedBufferSize);
        Assert.False(sut.EnableNormalize);
        _settingsServiceMock.Verify(s => s.LoadSettings(), Times.Once);
    }

    /// <summary>
    /// SelectedCategoryプロパティを変更した際、PropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void SelectedCategory_変更時_PropertyChangedが発火する()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);
        string? changedProp = null;
        sut.PropertyChanged += (s, e) => changedProp = e.PropertyName;

        // Act
        sut.SelectedCategory = "オーディオデバイス";

        // Assert
        Assert.Equal("オーディオデバイス", sut.SelectedCategory);
        Assert.Equal(nameof(SettingsViewModel.SelectedCategory), changedProp);
    }

    /// <summary>
    /// StartMinimizedプロパティを変更した際、設定サービスへ保存されることを検証します。
    /// </summary>
    [Fact]
    public void StartMinimized_変更時_設定サービスへ保存される()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);

        // Act
        sut.StartMinimized = true;

        // Assert
        Assert.True(sut.StartMinimized);
        _settingsServiceMock.Verify(s => s.SaveSettings(It.Is<AppSettings>(st => st.StartMinimized == true)), Times.Once);
    }

    /// <summary>
    /// SelectedBufferSizeプロパティを変更した際、設定サービスへ保存されることを検証します。
    /// </summary>
    [Fact]
    public void SelectedBufferSize_変更時_設定サービスへ保存される()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);

        // Act
        sut.SelectedBufferSize = 300;

        // Assert
        Assert.Equal(300, sut.SelectedBufferSize);
        _settingsServiceMock.Verify(s => s.SaveSettings(It.Is<AppSettings>(st => st.AudioBufferSizeMs == 300)), Times.Once);
    }

    /// <summary>
    /// EnableNormalizeプロパティを変更した際、設定サービスへ保存されることを検証します。
    /// </summary>
    [Fact]
    public void EnableNormalize_変更時_設定サービスへ保存される()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);

        // Act
        sut.EnableNormalize = true;

        // Assert
        Assert.True(sut.EnableNormalize);
        _settingsServiceMock.Verify(s => s.SaveSettings(It.Is<AppSettings>(st => st.EnableNormalize == true)), Times.Once);
    }
}
