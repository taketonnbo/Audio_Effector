using System.Collections.Generic;
using System.Text.Json;
using AudioEffector.Domain.Entities;
using Xunit;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// AppSettings エンティティの既定転送先フォルダ辞書および JSON 互換性を検証するテストクラス
/// </summary>
public sealed class AppSettingsTests
{
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
}
