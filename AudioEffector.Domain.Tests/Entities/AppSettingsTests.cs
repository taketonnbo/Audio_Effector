using System.Collections.Generic;
using System.Text.Json;
using AudioEffector.Domain.Entities;
using Xunit;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// <see cref="AppSettings"/> および <see cref="ShortcutKeyConfig"/> の JSON シリアライズ後方互換性と既定転送先フォルダ辞書を検証するテストクラス
/// </summary>
public sealed class AppSettingsTests
{
    /// <summary>
    /// 旧形式の settings.json（キーコード・修飾キーの整数値表現）をデシリアライズおよび再シリアライズしても数値・意味ともに完全一致することを検証します。
    /// </summary>
    [Fact]
    public void JsonSerializer_旧形式のsettingsJsonを読み書きした場合_キーコードと修飾キーの数値表現が完全互換であること()
    {
        // Arrange（WPF Key.Space=18, Key.MediaStop=136 / ModifierKeys.Alt=1, Control=2, Shift=4 互換の整数値）
        const int expectedSpaceKey = 18;
        const int expectedCtrlShift = (int)(ShortcutModifiers.Control | ShortcutModifiers.Shift);
        const int expectedMediaStopKey = 136;
        const int expectedAlt = (int)ShortcutModifiers.Alt;

        string legacyJson = $$"""
        {
          "Theme": 0,
          "Volume": 0.75,
          "PlayPauseShortcut": { "Key": {{expectedSpaceKey}}, "Modifiers": {{expectedCtrlShift}} },
          "StopShortcut": { "Key": {{expectedMediaStopKey}}, "Modifiers": {{expectedAlt}} },
          "NextShortcut": { "Key": 0, "Modifiers": 0 }
        }
        """;

        // Act
        AppSettings? sut = JsonSerializer.Deserialize<AppSettings>(legacyJson);
        Assert.NotNull(sut);

        string reserializedJson = JsonSerializer.Serialize(sut);
        using JsonDocument doc = JsonDocument.Parse(reserializedJson);

        // Assert
        Assert.Equal(expectedSpaceKey, sut.PlayPauseShortcut.Key);
        Assert.Equal(ShortcutModifiers.Control | ShortcutModifiers.Shift, sut.PlayPauseShortcut.Modifiers);

        Assert.Equal(expectedMediaStopKey, sut.StopShortcut.Key);
        Assert.Equal(ShortcutModifiers.Alt, sut.StopShortcut.Modifiers);

        Assert.Equal(ShortcutKeyConfig.NoneKey, sut.NextShortcut.Key);
        Assert.Equal(ShortcutModifiers.None, sut.NextShortcut.Modifiers);

        JsonElement playPauseElement = doc.RootElement.GetProperty("PlayPauseShortcut");
        Assert.Equal(expectedSpaceKey, playPauseElement.GetProperty("Key").GetInt32());
        Assert.Equal(expectedCtrlShift, playPauseElement.GetProperty("Modifiers").GetInt32());

        JsonElement stopElement = doc.RootElement.GetProperty("StopShortcut");
        Assert.Equal(expectedMediaStopKey, stopElement.GetProperty("Key").GetInt32());
        Assert.Equal(expectedAlt, stopElement.GetProperty("Modifiers").GetInt32());
    }

    /// <summary>
    /// 既定コンストラクタおよび null 代入時に DeviceDefaultTransferFolders が空辞書として安全に初期化されることを検証します。
    /// </summary>
    [Fact]
    public void DeviceDefaultTransferFolders_Null代入時_空の辞書へ安全に初期化されること()
    {
        // Arrange
        var sut = new AppSettings();

        // Act
        sut.DeviceDefaultTransferFolders = null!;

        // Assert
        Assert.NotNull(sut.DeviceDefaultTransferFolders);
        Assert.Empty(sut.DeviceDefaultTransferFolders);
    }

    /// <summary>
    /// DeviceDefaultTransferFolders が含まれる場合および旧形式JSON（キー欠落・null）から安全にデシリアライズできることを検証します。
    /// </summary>
    [Fact]
    public void DeviceDefaultTransferFolders_JSONシリアライズおよび旧形式デシリアライズ_互換性が維持されること()
    {
        // Arrange
        var sut = new AppSettings
        {
            DeviceDefaultTransferFolders = new Dictionary<string, string>
            {
                ["MTP:WALKMAN_01"] = @"\Internal\Music",
                ["FS:USB_DRIVE|E:\\"] = @"E:\Music"
            }
        };

        // Act
        string json = JsonSerializer.Serialize(sut);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);
        var legacyRestored = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":0,\"DeviceDefaultTransferFolders\":null}");

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(@"\Internal\Music", restored!.DeviceDefaultTransferFolders["mtp:walkman_01"]);
        Assert.Equal(@"E:\Music", restored.DeviceDefaultTransferFolders["FS:USB_DRIVE|E:\\"]);

        Assert.NotNull(legacyRestored);
        Assert.NotNull(legacyRestored!.DeviceDefaultTransferFolders);
        Assert.Empty(legacyRestored.DeviceDefaultTransferFolders);
    }

    /// <summary>
    /// HasAcceptedLrcCopyrightGuideline の既定値 false、true のシリアライズ／デシリアライズ、および旧形式 JSON（キー欠落）からの後方互換読み込みを検証します。
    /// </summary>
    [Fact]
    public void HasAcceptedLrcCopyrightGuideline_JSONシリアライズおよび旧形式デシリアライズ_既定値falseとtrue復元が正しく動作すること()
    {
        // Arrange
        var defaultSettings = new AppSettings();
        var sut = new AppSettings
        {
            HasAcceptedLrcCopyrightGuideline = true
        };

        // Act
        string json = JsonSerializer.Serialize(sut);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);
        var legacyRestored = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":0,\"Volume\":0.8}");

        // Assert
        Assert.False(defaultSettings.HasAcceptedLrcCopyrightGuideline);
        Assert.NotNull(restored);
        Assert.True(restored!.HasAcceptedLrcCopyrightGuideline);
        Assert.NotNull(legacyRestored);
        Assert.False(legacyRestored!.HasAcceptedLrcCopyrightGuideline);
    }

    /// <summary>
    /// DataDirectoryPath の既定値が null であり、JSON ラウンドトリップおよび旧形式 JSON（キー欠落）からのデシリアライズ互換性が保たれることを検証します。
    /// </summary>
    [Fact]
    public void DataDirectoryPath_JSONシリアライズおよび旧形式デシリアライズ_既定値nullと互換性が維持されること()
    {
        // Arrange
        var defaultSettings = new AppSettings();
        var sut = new AppSettings
        {
            DataDirectoryPath = @"D:\CloudSync\AudioEffector"
        };

        // Act
        string json = JsonSerializer.Serialize(sut);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);
        var legacyRestored = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":1}");

        // Assert
        Assert.Null(defaultSettings.DataDirectoryPath);
        Assert.NotNull(restored);
        Assert.Equal(@"D:\CloudSync\AudioEffector", restored!.DataDirectoryPath);
        Assert.NotNull(legacyRestored);
        Assert.Null(legacyRestored!.DataDirectoryPath);
    }

    /// <summary>
    /// LogLevel と LogDirectoryPath の既定値、JSON ラウンドトリップ、および旧形式 JSON（キー欠落）からのデシリアライズ互換性を検証します。
    /// </summary>
    [Fact]
    public void LogSettings_JSONシリアライズおよび旧形式デシリアライズ_既定値と互換性が維持されること()
    {
        // Arrange
        var defaultSettings = new AppSettings();
        var sut = new AppSettings
        {
            LogLevel = "Debug",
            LogDirectoryPath = @"D:\Logs\AudioEffector"
        };

        // Act
        string json = JsonSerializer.Serialize(sut);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);
        var legacyRestored = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":0}");

        // Assert
        Assert.Equal("Info", defaultSettings.LogLevel);
        Assert.Null(defaultSettings.LogDirectoryPath);
        Assert.NotNull(restored);
        Assert.Equal("Debug", restored!.LogLevel);
        Assert.Equal(@"D:\Logs\AudioEffector", restored.LogDirectoryPath);
        Assert.NotNull(legacyRestored);
        Assert.Equal("Info", legacyRestored!.LogLevel);
        Assert.Null(legacyRestored.LogDirectoryPath);
    }

    /// <summary>
    /// LRC 歌詞ファイル同梱転送設定 3 項目の既定値（false）、JSON ラウンドトリップ、および旧形式 JSON（キー欠落）からのデシリアライズ互換性を検証します。
    /// </summary>
    [Fact]
    public void LrcTransferSettings_JSONシリアライズおよび旧形式デシリアライズ_既定値falseと互換性が維持されること()
    {
        // Arrange
        var defaultSettings = new AppSettings();
        var sut = new AppSettings
        {
            TransferManualLrcFiles = true,
            TransferTextAlignedLrcFiles = true,
            TransferTranscribedLrcFiles = true
        };

        // Act
        string json = JsonSerializer.Serialize(sut);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);
        var legacyRestored = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":0,\"ConvertMp4ToM4a\":true}");

        // Assert
        Assert.False(defaultSettings.TransferManualLrcFiles);
        Assert.False(defaultSettings.TransferTextAlignedLrcFiles);
        Assert.False(defaultSettings.TransferTranscribedLrcFiles);

        Assert.NotNull(restored);
        Assert.True(restored!.TransferManualLrcFiles);
        Assert.True(restored.TransferTextAlignedLrcFiles);
        Assert.True(restored.TransferTranscribedLrcFiles);

        Assert.NotNull(legacyRestored);
        Assert.False(legacyRestored!.TransferManualLrcFiles);
        Assert.False(legacyRestored.TransferTextAlignedLrcFiles);
        Assert.False(legacyRestored.TransferTranscribedLrcFiles);
    }
}
