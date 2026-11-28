using System;
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
    private readonly AppSettings _persistedSettings = new();
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
        _settingsServiceMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(_persistedSettings));
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
        Assert.False(sut.DisableMiniPlayerOnMinimize);
        Assert.Equal("横長（標準）", sut.SelectedMiniPlayerDesignStyle);
        Assert.False(sut.IsSquareMiniPlayerDesignSelected);
        Assert.True(sut.MiniPlayerRotateAlbumArt);
        Assert.Equal(200, sut.SelectedBufferSize);
        Assert.False(sut.EnableNormalize);
        Assert.False(sut.DetectMetadataChangesOnRescan);
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
        Assert.True(_persistedSettings.StartMinimized == true);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
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
        Assert.True(_persistedSettings.AudioBufferSizeMs == 300);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
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
        Assert.True(_persistedSettings.EnableNormalize == true);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// DetectMetadataChangesOnRescanプロパティを変更した際、設定サービスへ保存されることを検証します
    /// </summary>
    [Fact]
    public void DetectMetadataChangesOnRescan_変更時_設定サービスへ保存される()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);

        // Act
        sut.DetectMetadataChangesOnRescan = true;

        // Assert
        Assert.True(sut.DetectMetadataChangesOnRescan);
        Assert.True(_persistedSettings.DetectMetadataChangesOnRescan == true);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// DisableMiniPlayerOnMinimizeプロパティを変更した際、設定サービスへ保存されPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void DisableMiniPlayerOnMinimize_変更時_設定サービスへ保存されPropertyChangedが発火する()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.DisableMiniPlayerOnMinimize = true;
        sut.DisableMiniPlayerOnMinimize = true; // 同値再代入では再保存されないこと

        // Assert
        Assert.True(sut.DisableMiniPlayerOnMinimize);
        Assert.Contains(nameof(SettingsViewModel.DisableMiniPlayerOnMinimize), notified);
        Assert.Single(notified);
        Assert.True(_persistedSettings.DisableMiniPlayerOnMinimize == true);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// SelectedMiniPlayerDesignStyleプロパティを変更した際、設定サービスへ保存され関連するPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void SelectedMiniPlayerDesignStyle_変更時_設定サービスへ保存されPropertyChangedが発火する()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.SelectedMiniPlayerDesignStyle = "正方形（アルバムアート）";
        sut.SelectedMiniPlayerDesignStyle = "正方形（アルバムアート）"; // 同値再代入では再保存されないこと

        // Assert
        Assert.Equal("正方形（アルバムアート）", sut.SelectedMiniPlayerDesignStyle);
        Assert.True(sut.IsSquareMiniPlayerDesignSelected);
        Assert.Contains(nameof(SettingsViewModel.SelectedMiniPlayerDesignStyle), notified);
        Assert.Contains(nameof(SettingsViewModel.IsSquareMiniPlayerDesignSelected), notified);
        Assert.Equal(2, notified.Count);
        Assert.True(_persistedSettings.MiniPlayerDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// MiniPlayerRotateAlbumArtプロパティを変更した際、設定サービスへ保存されPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerRotateAlbumArt_変更時_設定サービスへ保存されPropertyChangedが発火する()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.MiniPlayerRotateAlbumArt = false;
        sut.MiniPlayerRotateAlbumArt = false; // 同値再代入では再保存されないこと

        // Assert
        Assert.False(sut.MiniPlayerRotateAlbumArt);
        Assert.Contains(nameof(SettingsViewModel.MiniPlayerRotateAlbumArt), notified);
        Assert.Single(notified);
        Assert.True(_persistedSettings.MiniPlayerRotateAlbumArt == false);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// ConvertMp4ToM4aプロパティを変更した際、設定サービスへ保存されPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void ConvertMp4ToM4a_変更時_設定サービスへ保存されPropertyChangedが発火する()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.ConvertMp4ToM4a = true;
        sut.ConvertMp4ToM4a = true; // 同値再代入では再保存されないこと

        // Assert
        Assert.True(sut.ConvertMp4ToM4a);
        Assert.Contains(nameof(SettingsViewModel.ConvertMp4ToM4a), notified);
        Assert.Single(notified);
        Assert.True(_persistedSettings.ConvertMp4ToM4a);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }
}
