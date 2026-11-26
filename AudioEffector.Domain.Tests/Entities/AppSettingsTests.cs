using System.Text.Json;
using AudioEffector.Domain.Entities;
using Xunit;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// <see cref="AppSettings"/> および <see cref="ShortcutKeyConfig"/> の JSON シリアライズ後方互換性を検証するテストクラス
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
}
