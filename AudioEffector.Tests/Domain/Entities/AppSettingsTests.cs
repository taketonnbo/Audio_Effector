using System.Text.Json;
using System.Windows.Input;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Domain.Entities;

/// <summary>
/// <see cref="AppSettings"/> および <see cref="ShortcutKeyConfig"/> の JSON シリアライズ後方互換性を検証するテストクラス
/// </summary>
public sealed class AppSettingsTests
{
    /// <summary>
    /// 旧形式の settings.json（WPF の Key / ModifierKeys 整数値表現）をデシリアライズおよび再シリアライズしても数値・意味ともに完全一致することを検証します。
    /// </summary>
    [Fact]
    public void JsonSerializer_旧形式のsettingsJsonを読み書きした場合_キーコードと修飾キーの数値表現が完全互換であること()
    {
        // Arrange
        int expectedSpaceKey = (int)Key.Space;
        int expectedCtrlShift = (int)(ModifierKeys.Control | ModifierKeys.Shift);
        int expectedMediaStopKey = (int)Key.MediaStop;
        int expectedAlt = (int)ModifierKeys.Alt;

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
        Assert.Equal(Key.Space, ShortcutKeyHelper.ToWpfKey(sut.PlayPauseShortcut.Key));
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, ShortcutKeyHelper.ToWpfModifiers(sut.PlayPauseShortcut.Modifiers));

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
    /// ShortcutModifiers の全ビット値が WPF の ModifierKeys と 1:1 で一致することを検証します。
    /// </summary>
    [Fact]
    public void ShortcutModifiers_全フラグ値_WPFModifierKeysとビット値が一致すること()
    {
        // Arrange & Act & Assert
        Assert.Equal((int)ModifierKeys.None, (int)ShortcutModifiers.None);
        Assert.Equal((int)ModifierKeys.Alt, (int)ShortcutModifiers.Alt);
        Assert.Equal((int)ModifierKeys.Control, (int)ShortcutModifiers.Control);
        Assert.Equal((int)ModifierKeys.Shift, (int)ShortcutModifiers.Shift);
        Assert.Equal((int)ModifierKeys.Windows, (int)ShortcutModifiers.Windows);
    }
}
