using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="ShortcutKeyHelper"/> の実キー解決・メディアキー判定・表示整形・フォーカス判定を検証するテストクラス
/// </summary>
public sealed class ShortcutKeyHelperTests
{
    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw threadEx;
        }
    }

    /// <summary>
    /// SystemKey・ImeProcessedKey・DeadCharProcessedKey を含む各種キー入力が正しい実キーへ解決されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(Key.System, Key.F10, Key.None, Key.None, Key.F10)]
    [InlineData(Key.ImeProcessed, Key.None, Key.P, Key.None, Key.P)]
    [InlineData(Key.DeadCharProcessed, Key.None, Key.None, Key.A, Key.A)]
    [InlineData(Key.MediaPlayPause, Key.None, Key.None, Key.None, Key.MediaPlayPause)]
    public void ResolveKey_特殊キー状態の場合_実キーへ解決されること(
        Key key,
        Key systemKey,
        Key imeProcessedKey,
        Key deadCharProcessedKey,
        Key expected)
    {
        // Arrange & Act
        var actual = ShortcutKeyHelper.ResolveKey(key, systemKey, imeProcessedKey, deadCharProcessedKey);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 修飾キー単体や IME モード切替キーが無視対象と判定され、通常の文字キーやメディアキーは有効と判定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(Key.LeftCtrl, true)]
    [InlineData(Key.RightShift, true)]
    [InlineData(Key.ImeConvert, true)]
    [InlineData(Key.KanaMode, true)]
    [InlineData(Key.CapsLock, true)]
    [InlineData(Key.P, false)]
    [InlineData(Key.Space, false)]
    [InlineData(Key.MediaPlayPause, false)]
    public void IsIgnoredKey_各種キーを指定した場合_期待通りの無視判定を返すこと(Key key, bool expected)
    {
        // Arrange & Act
        bool actual = ShortcutKeyHelper.IsIgnoredKey(key);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// WM_APPCOMMAND の lParam から正しいアプリケーションコマンド番号を抽出し、対応するメディアキーへ変換できることを検証します。
    /// </summary>
    [Theory]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE, Key.MediaPlayPause)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY, Key.MediaPlayPause)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_MEDIA_PAUSE, Key.MediaPlayPause)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_MEDIA_STOP, Key.MediaStop)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_MEDIA_NEXTTRACK, Key.MediaNextTrack)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_MEDIA_PREVIOUSTRACK, Key.MediaPreviousTrack)]
    public void TryMapAppCommandToKey_サポート対象のコマンドの場合_対応するメディアキーを返すこと(int appCommand, Key expectedKey)
    {
        // Arrange
        IntPtr lParam = new IntPtr((appCommand | 0x8000) << 16);

        // Act
        int extracted = ShortcutKeyHelper.GetAppCommand(lParam);
        bool mapped = ShortcutKeyHelper.TryMapAppCommandToKey(extracted, out Key actualKey);

        // Assert
        Assert.Equal(appCommand, extracted);
        Assert.True(mapped);
        Assert.Equal(expectedKey, actualKey);
    }

    /// <summary>
    /// ハードウェアのミュート・音量上げ・音量下げコマンドは OS のシステムオーディオ操作へ委譲するため対象外と判定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_VOLUME_MUTE)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_VOLUME_UP)]
    [InlineData(ShortcutKeyHelper.APPCOMMAND_VOLUME_DOWN)]
    public void TryMapAppCommandToKey_システム音量ミュート系コマンドの場合_対象外としてFalseを返すこと(int appCommand)
    {
        // Arrange & Act
        bool mapped = ShortcutKeyHelper.TryMapAppCommandToKey(appCommand, out Key actualKey);

        // Assert
        Assert.False(mapped);
        Assert.Equal(Key.None, actualKey);
    }

    /// <summary>
    /// メディアキーおよび修飾キー付きショートカットの表示文字列が可読形式で整形されることを検証します。
    /// </summary>
    [Fact]
    public void FormatShortcutText_メディアキーと通常キーを指定した場合_期待通りの表示テキストを返すこと()
    {
        // Arrange
        var mediaConfig = ShortcutKeyHelper.CreateConfig(Key.MediaPlayPause, ModifierKeys.None);
        var singleKeyConfig = ShortcutKeyHelper.CreateConfig(Key.P, ModifierKeys.None);
        var modifiedConfig = ShortcutKeyHelper.CreateConfig(Key.Right, ModifierKeys.Control | ModifierKeys.Shift);

        // Act
        string mediaText = ShortcutKeyHelper.FormatShortcutText(mediaConfig);
        string singleText = ShortcutKeyHelper.FormatShortcutText(singleKeyConfig);
        string modifiedText = ShortcutKeyHelper.FormatShortcutText(modifiedConfig);
        string emptyText = ShortcutKeyHelper.FormatShortcutText(null);

        // Assert
        Assert.Equal("MediaPlayPause (再生/一時停止)", mediaText);
        Assert.Equal("P", singleText);
        Assert.Equal("Ctrl + Shift + Right", modifiedText);
        Assert.Equal("割り当てなし", emptyText);
    }

    /// <summary>
    /// 未定義の整数キーコードを指定した場合、ToWpfKey および FormatShortcutText が例外を送出せず Key.None / 割り当てなしへ安全にフォールバックすることを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(9999)]
    [InlineData(int.MaxValue)]
    public void ToWpfKey_未定義の整数キーコードを指定した場合_KeyNoneへ安全にフォールバックすること(int invalidKeyCode)
    {
        // Arrange
        var invalidConfig = new ShortcutKeyConfig
        {
            Key = invalidKeyCode,
            Modifiers = ShortcutModifiers.Control
        };

        // Act
        Key actualKey = ShortcutKeyHelper.ToWpfKey(invalidKeyCode);
        string formatted = ShortcutKeyHelper.FormatShortcutText(invalidConfig);

        // Assert
        Assert.Equal(Key.None, actualKey);
        Assert.Equal("割り当てなし", formatted);
    }

    /// <summary>
    /// WPF の Key / ModifierKeys とドメイン層の int / ShortcutModifiers が相互変換で完全に一致することを検証します。
    /// </summary>
    [Fact]
    public void CreateConfig_WPFキーと修飾キーを指定した場合_相互変換で値が一致すること()
    {
        // Arrange
        const Key expectedKey = Key.Space;
        const ModifierKeys expectedModifiers = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;

        // Act
        ShortcutKeyConfig config = ShortcutKeyHelper.CreateConfig(expectedKey, expectedModifiers);
        Key restoredKey = ShortcutKeyHelper.ToWpfKey(config.Key);
        ModifierKeys restoredModifiers = ShortcutKeyHelper.ToWpfModifiers(config.Modifiers);

        // Assert
        Assert.Equal((int)expectedKey, config.Key);
        Assert.Equal((int)expectedModifiers, (int)config.Modifiers);
        Assert.Equal(expectedKey, restoredKey);
        Assert.Equal(expectedModifiers, restoredModifiers);
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

    /// <summary>
    /// 旧形式 settings.json から復元した AppSettings のキー・修飾キーが WPF の Key / ModifierKeys へ正しく変換されることを検証します。
    /// </summary>
    [Fact]
    public void ToWpfKeyとToWpfModifiers_旧形式settingsJsonのAppSettingsを渡した場合_WPF型へ正しく変換されること()
    {
        // Arrange
        int expectedSpaceKey = (int)Key.Space;
        int expectedCtrlShift = (int)(ModifierKeys.Control | ModifierKeys.Shift);
        string legacyJson = $$"""
        {
          "PlayPauseShortcut": { "Key": {{expectedSpaceKey}}, "Modifiers": {{expectedCtrlShift}} }
        }
        """;

        // Act
        AppSettings? settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(legacyJson);

        // Assert
        Assert.NotNull(settings);
        Assert.Equal(Key.Space, ShortcutKeyHelper.ToWpfKey(settings.PlayPauseShortcut.Key));
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, ShortcutKeyHelper.ToWpfModifiers(settings.PlayPauseShortcut.Modifiers));
    }

    /// <summary>
    /// ToWpfModifiers に定義外ビットを含む値が渡された場合、有効フラグのビットマスクで安全に正規化されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(16, ModifierKeys.None)]
    [InlineData(18, ModifierKeys.Control)]
    [InlineData(-1, ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)]
    public void ToWpfModifiers_定義外ビットを含む値を指定した場合_有効フラグのみに正規化されること(int rawModifiers, ModifierKeys expected)
    {
        // Arrange
        var modifiers = (ShortcutModifiers)rawModifiers;

        // Act
        ModifierKeys actual = ShortcutKeyHelper.ToWpfModifiers(modifiers);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 編集可能な TextBox フォーカス時は単一キーや Space がスキップされ、メディアキーおよび Ctrl 併用ショートカットはスキップされないことを検証します。
    /// </summary>
    [Fact]
    public void ShouldSkipForTextInput_編集可能なTextBoxフォーカス時_単一キーはスキップしメディアキーは許可すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var editableTextBox = new TextBox { IsReadOnly = false };
            var readOnlyTextBox = new TextBox { IsReadOnly = true };
            var button = new Button();

            // Act & Assert
            Assert.True(ShortcutKeyHelper.ShouldSkipForTextInput(Key.P, ModifierKeys.None, editableTextBox));
            Assert.True(ShortcutKeyHelper.ShouldSkipForTextInput(Key.Space, ModifierKeys.None, editableTextBox));
            Assert.True(ShortcutKeyHelper.ShouldSkipForTextInput(Key.P, ModifierKeys.Shift, editableTextBox));
            Assert.False(ShortcutKeyHelper.ShouldSkipForTextInput(Key.MediaPlayPause, ModifierKeys.None, editableTextBox));
            Assert.False(ShortcutKeyHelper.ShouldSkipForTextInput(Key.P, ModifierKeys.Control, editableTextBox));
            Assert.False(ShortcutKeyHelper.ShouldSkipForTextInput(Key.Space, ModifierKeys.None, readOnlyTextBox));
            Assert.False(ShortcutKeyHelper.ShouldSkipForTextInput(Key.Space, ModifierKeys.None, button));
        });
    }
}
