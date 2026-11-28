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

    /// <summary>
    /// ConvertFlacToMp3プロパティを変更した際、設定サービスへ保存されPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void ConvertFlacToMp3_変更時_設定サービスへ保存されPropertyChangedが発火する()
    {
        // Arrange
        var sut = new SettingsViewModel(_settingsServiceMock.Object, _audioServiceMock.Object);
        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.ConvertFlacToMp3 = true;
        sut.ConvertFlacToMp3 = true; // 同値再代入では再保存されないこと

        // Assert
        Assert.True(sut.ConvertFlacToMp3);
        Assert.Contains(nameof(SettingsViewModel.ConvertFlacToMp3), notified);
        Assert.Single(notified);
        Assert.True(_persistedSettings.ConvertFlacToMp3);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// BrowseDataDirectoryCommand 実行時、既存データがあれば移行確認プロンプトの結果（Copy）に従って移行サービスが呼ばれ、変更通知とイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void BrowseDataDirectoryCommand_既存データありでコピー選択時_移行サービスがCopyで実行され通知が発火すること()
    {
        // Arrange
        var providerMock = new Mock<AudioEffector.Application.Common.IDataDirectoryProvider>();
        string currentPath = @"C:\Users\Test\AppData\Roaming\AudioEffector";
        bool isCustom = false;
        providerMock.SetupGet(p => p.DefaultDataDirectory).Returns(currentPath);
        providerMock.SetupGet(p => p.DataDirectory).Returns(() => currentPath);
        providerMock.SetupGet(p => p.IsCustomDirectory).Returns(() => isCustom);

        var migrationMock = new Mock<IDataDirectoryMigrationService>();
        migrationMock.Setup(m => m.IsSameDirectory(It.IsAny<string?>(), It.IsAny<string?>())).Returns(false);
        migrationMock.Setup(m => m.HasExistingDataFiles(currentPath)).Returns(true);
        migrationMock.Setup(m => m.HasExistingDataFiles(@"D:\SyncData")).Returns(true);
        migrationMock.Setup(m => m.ChangeDataDirectory(@"D:\SyncData", DataMigrationMode.Copy))
            .Callback(() =>
            {
                currentPath = @"D:\SyncData";
                isCustom = true;
            })
            .Returns(new DataDirectoryChangeResult(true, true, @"D:\SyncData", 3));

        bool? observedDestinationHasExisting = null;
        var sut = new SettingsViewModel(
            _settingsServiceMock.Object,
            _audioServiceMock.Object,
            providerMock.Object,
            migrationMock.Object,
            folderPicker: _ => @"D:\SyncData",
            migrationPrompt: (_, _, destExists) =>
            {
                observedDestinationHasExisting = destExists;
                return DataMigrationChoice.Copy;
            },
            messageNotifier: (_, _, _) => { });

        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        int changedEventCount = 0;
        sut.DataDirectoryChanged += () => changedEventCount++;

        // Act
        sut.BrowseDataDirectoryCommand.Execute(null);

        // Assert
        Assert.True(observedDestinationHasExisting);
        Assert.Equal(@"D:\SyncData", sut.DataDirectoryPath);
        Assert.True(sut.IsCustomDataDirectory);
        Assert.Contains(nameof(SettingsViewModel.DataDirectoryPath), notified);
        Assert.Contains(nameof(SettingsViewModel.IsCustomDataDirectory), notified);
        Assert.Equal(1, changedEventCount);
        migrationMock.Verify(m => m.ChangeDataDirectory(@"D:\SyncData", DataMigrationMode.Copy), Times.Once);
    }

    /// <summary>
    /// 移行確認ダイアログでキャンセルを選択した場合、データディレクトリ変更が中止されることを検証します。
    /// </summary>
    [Fact]
    public void BrowseDataDirectoryCommand_移行確認でキャンセル時_変更処理が実行されないこと()
    {
        // Arrange
        var providerMock = new Mock<AudioEffector.Application.Common.IDataDirectoryProvider>();
        providerMock.SetupGet(p => p.DefaultDataDirectory).Returns(@"C:\Default");
        providerMock.SetupGet(p => p.DataDirectory).Returns(@"C:\Default");
        providerMock.SetupGet(p => p.IsCustomDirectory).Returns(false);

        var migrationMock = new Mock<IDataDirectoryMigrationService>();
        migrationMock.Setup(m => m.IsSameDirectory(It.IsAny<string?>(), It.IsAny<string?>())).Returns(false);
        migrationMock.Setup(m => m.HasExistingDataFiles(@"C:\Default")).Returns(true);

        var sut = new SettingsViewModel(
            _settingsServiceMock.Object,
            _audioServiceMock.Object,
            providerMock.Object,
            migrationMock.Object,
            folderPicker: _ => @"D:\Other",
            migrationPrompt: (_, _, _) => DataMigrationChoice.Cancel,
            messageNotifier: (_, _, _) => { });

        // Act
        sut.BrowseDataDirectoryCommand.Execute(null);

        // Assert
        migrationMock.Verify(m => m.ChangeDataDirectory(It.IsAny<string?>(), It.IsAny<DataMigrationMode>()), Times.Never);
    }

    /// <summary>
    /// ResetDataDirectoryCommand がカスタムディレクトリ設定中のみ実行可能であり、実行時に既定ディレクトリへ復帰することを検証します。
    /// </summary>
    [Fact]
    public void ResetDataDirectoryCommand_カスタム設定中のみ実行可能で既定ディレクトリへ復帰すること()
    {
        // Arrange
        var providerMock = new Mock<AudioEffector.Application.Common.IDataDirectoryProvider>();
        string defaultPath = @"C:\Default";
        string currentPath = @"D:\Custom";
        bool isCustom = true;
        providerMock.SetupGet(p => p.DefaultDataDirectory).Returns(defaultPath);
        providerMock.SetupGet(p => p.DataDirectory).Returns(() => currentPath);
        providerMock.SetupGet(p => p.IsCustomDirectory).Returns(() => isCustom);

        var migrationMock = new Mock<IDataDirectoryMigrationService>();
        migrationMock.Setup(m => m.IsSameDirectory(It.IsAny<string?>(), It.IsAny<string?>())).Returns(false);
        migrationMock.Setup(m => m.HasExistingDataFiles(@"D:\Custom")).Returns(false);
        migrationMock.Setup(m => m.ChangeDataDirectory(null, DataMigrationMode.None))
            .Callback(() =>
            {
                currentPath = defaultPath;
                isCustom = false;
            })
            .Returns(new DataDirectoryChangeResult(true, true, defaultPath, 0));

        var sut = new SettingsViewModel(
            _settingsServiceMock.Object,
            _audioServiceMock.Object,
            providerMock.Object,
            migrationMock.Object,
            folderPicker: _ => null,
            migrationPrompt: (_, _, _) => DataMigrationChoice.Cancel,
            messageNotifier: (_, _, _) => { });

        // Act
        bool canExecuteBefore = sut.ResetDataDirectoryCommand.CanExecute(null);
        sut.ResetDataDirectoryCommand.Execute(null);
        bool canExecuteAfter = sut.ResetDataDirectoryCommand.CanExecute(null);

        // Assert
        Assert.True(canExecuteBefore);
        Assert.False(canExecuteAfter);
        Assert.Equal(defaultPath, sut.DataDirectoryPath);
        Assert.False(sut.IsCustomDataDirectory);
        migrationMock.Verify(m => m.ChangeDataDirectory(null, DataMigrationMode.None), Times.Once);
    }

    /// <summary>
    /// SelectedLogLevel を変更した際、設定サービスへ保存され ILogConfigurationManager.ApplyConfiguration が即時呼び出されることを検証します。
    /// </summary>
    [Fact]
    public void SelectedLogLevel_変更時_設定保存およびログ構成マネージャへ即時反映されること()
    {
        // Arrange
        var logManagerMock = new Mock<AudioEffector.Application.Common.ILogConfigurationManager>();
        logManagerMock.SetupGet(m => m.AvailableLogLevels).Returns(["Trace", "Debug", "Info", "Warn", "Error", "Off"]);
        logManagerMock.SetupGet(m => m.DefaultLogDirectory).Returns(@"C:\App\logs");
        logManagerMock.SetupGet(m => m.CurrentLogDirectory).Returns(@"C:\App\logs");
        logManagerMock.Setup(m => m.NormalizeLogLevel(It.IsAny<string?>()))
            .Returns<string?>(v => string.IsNullOrWhiteSpace(v) ? "Info" : v);
        logManagerMock.Setup(m => m.ApplyConfiguration(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns<string?, string?>((lvl, dir) => new AudioEffector.Application.Common.LogConfigurationApplyResult(true, lvl ?? "Info", dir ?? @"C:\App\logs", false));

        var sut = new SettingsViewModel(
            _settingsServiceMock.Object,
            _audioServiceMock.Object,
            dataDirectoryProvider: null,
            dataDirectoryMigrationService: null,
            logConfigurationManager: logManagerMock.Object);

        var notified = new List<string?>();
        sut.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Act
        sut.SelectedLogLevel = "Debug";
        sut.SelectedLogLevel = "Debug"; // 同値再代入では再適用されないこと

        // Assert
        Assert.Equal("Debug", sut.SelectedLogLevel);
        Assert.Equal("Debug", _persistedSettings.LogLevel);
        Assert.Single(notified, p => p == nameof(SettingsViewModel.SelectedLogLevel));
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
        logManagerMock.Verify(m => m.ApplyConfiguration("Debug", null), Times.Once);
    }

    /// <summary>
    /// BrowseLogDirectoryCommand で有効なフォルダを選択した場合に設定保存・構成反映され、
    /// ResetLogDirectoryCommand で既定フォルダへ復帰することを検証します。
    /// </summary>
    [Fact]
    public void BrowseAndResetLogDirectoryCommand_カスタム設定とリセット時_設定保存と構成反映が行われること()
    {
        // Arrange
        string defaultLogs = @"C:\App\logs";
        string currentLogs = defaultLogs;
        bool isCustom = false;
        var logManagerMock = new Mock<AudioEffector.Application.Common.ILogConfigurationManager>();
        logManagerMock.SetupGet(m => m.AvailableLogLevels).Returns(["Trace", "Debug", "Info", "Warn", "Error", "Off"]);
        logManagerMock.SetupGet(m => m.DefaultLogDirectory).Returns(defaultLogs);
        logManagerMock.SetupGet(m => m.CurrentLogDirectory).Returns(() => currentLogs);
        logManagerMock.SetupGet(m => m.IsCustomLogDirectory).Returns(() => isCustom);
        logManagerMock.Setup(m => m.NormalizeLogLevel(It.IsAny<string?>())).Returns<string?>(v => v ?? "Info");
        logManagerMock.Setup(m => m.ApplyConfiguration(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns<string?, string?>((lvl, dir) =>
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    currentLogs = defaultLogs;
                    isCustom = false;
                }
                else
                {
                    currentLogs = dir;
                    isCustom = true;
                }

                return new AudioEffector.Application.Common.LogConfigurationApplyResult(true, lvl ?? "Info", currentLogs, false);
            });

        var sut = new SettingsViewModel(
            _settingsServiceMock.Object,
            _audioServiceMock.Object,
            dataDirectoryProvider: null,
            dataDirectoryMigrationService: null,
            folderPicker: _ => @"D:\DebugLogs",
            migrationPrompt: null,
            messageNotifier: (_, _, _) => { },
            logConfigurationManager: logManagerMock.Object,
            folderOpener: _ => { });

        // Act
        Assert.False(sut.ResetLogDirectoryCommand.CanExecute(null));
        sut.BrowseLogDirectoryCommand.Execute(null);
        string afterBrowsePath = sut.LogDirectoryPath;
        bool canResetAfterBrowse = sut.ResetLogDirectoryCommand.CanExecute(null);
        sut.ResetLogDirectoryCommand.Execute(null);

        // Assert
        Assert.Equal(@"D:\DebugLogs", afterBrowsePath);
        Assert.True(canResetAfterBrowse);
        Assert.Equal(defaultLogs, sut.LogDirectoryPath);
        Assert.False(sut.IsCustomLogDirectory);
        Assert.False(sut.ResetLogDirectoryCommand.CanExecute(null));
        Assert.Null(_persistedSettings.LogDirectoryPath);
    }

    /// <summary>
    /// BrowseLogDirectoryCommand で書き込み不可フォルダが選択されフォールバックが発生した場合、エラー通知され設定値が汚染されないことを検証します。
    /// </summary>
    [Fact]
    public void BrowseLogDirectoryCommand_書き込み不可フォルダ選択時_エラー通知され設定が書き換わらないこと()
    {
        // Arrange
        var logManagerMock = new Mock<AudioEffector.Application.Common.ILogConfigurationManager>();
        logManagerMock.SetupGet(m => m.AvailableLogLevels).Returns(["Trace", "Debug", "Info", "Warn", "Error", "Off"]);
        logManagerMock.SetupGet(m => m.DefaultLogDirectory).Returns(@"C:\App\logs");
        logManagerMock.SetupGet(m => m.CurrentLogDirectory).Returns(@"C:\App\logs");
        logManagerMock.SetupGet(m => m.IsCustomLogDirectory).Returns(false);
        logManagerMock.Setup(m => m.NormalizeLogLevel(It.IsAny<string?>())).Returns<string?>(v => v ?? "Info");
        logManagerMock.Setup(m => m.ApplyConfiguration("Info", @"Z:\ReadOnlyLogs"))
            .Returns(new AudioEffector.Application.Common.LogConfigurationApplyResult(
                Succeeded: true,
                AppliedLogLevel: "Info",
                EffectiveLogDirectory: @"C:\App\logs",
                UsedFallbackDirectory: true,
                WarningMessage: "アクセス拒否"));

        bool notifiedError = false;
        var sut = new SettingsViewModel(
            _settingsServiceMock.Object,
            _audioServiceMock.Object,
            dataDirectoryProvider: null,
            dataDirectoryMigrationService: null,
            folderPicker: _ => @"Z:\ReadOnlyLogs",
            migrationPrompt: null,
            messageNotifier: (_, _, isError) => notifiedError = isError,
            logConfigurationManager: logManagerMock.Object,
            folderOpener: _ => { });

        // Act
        sut.BrowseLogDirectoryCommand.Execute(null);

        // Assert
        Assert.True(notifiedError);
        Assert.Null(_persistedSettings.LogDirectoryPath);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Never);
    }

    /// <summary>
    /// OpenLogFolderCommand 実行時、現在のログ保存先フォルダパスでフォルダを開くデリゲートが呼ばれることを検証します。
    /// </summary>
    [Fact]
    public void OpenLogFolderCommand_実行時_現在のログ保存先フォルダでエクスプローラー起動デリゲートが呼ばれること()
    {
        // Arrange
        string tempLogDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AudioEffector_OpenLog_{Guid.NewGuid():N}");
        try
        {
            var logManagerMock = new Mock<AudioEffector.Application.Common.ILogConfigurationManager>();
            logManagerMock.SetupGet(m => m.CurrentLogDirectory).Returns(tempLogDir);
            logManagerMock.SetupGet(m => m.DefaultLogDirectory).Returns(tempLogDir);

            string? openedPath = null;
            var sut = new SettingsViewModel(
                _settingsServiceMock.Object,
                _audioServiceMock.Object,
                dataDirectoryProvider: null,
                dataDirectoryMigrationService: null,
                folderPicker: null,
                migrationPrompt: null,
                messageNotifier: (_, _, _) => { },
                logConfigurationManager: logManagerMock.Object,
                folderOpener: p => openedPath = p);

            // Act
            sut.OpenLogFolderCommand.Execute(null);

            // Assert
            Assert.Equal(tempLogDir, openedPath);
            Assert.True(System.IO.Directory.Exists(tempLogDir));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempLogDir))
            {
                System.IO.Directory.Delete(tempLogDir, recursive: true);
            }
        }
    }
}
