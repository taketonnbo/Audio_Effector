using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ハードウェアメディアキー（<c>WM_APPCOMMAND</c> / <see cref="Key"/>）およびキーボードショートカットの解決・判定を行うヘルパークラス
/// </summary>
public static class ShortcutKeyHelper
{
    /// <summary>
    /// Win32 アプリケーションコマンドメッセージ (<c>WM_APPCOMMAND</c>)
    /// </summary>
    public const int WM_APPCOMMAND = 0x0319;

    /// <summary>
    /// ミュート切替のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_VOLUME_MUTE = 8;

    /// <summary>
    /// 音量下げのアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_VOLUME_DOWN = 9;

    /// <summary>
    /// 音量上げのアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_VOLUME_UP = 10;

    /// <summary>
    /// 次の曲のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_MEDIA_NEXTTRACK = 11;

    /// <summary>
    /// 前の曲のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

    /// <summary>
    /// 停止のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_MEDIA_STOP = 13;

    /// <summary>
    /// 再生/一時停止のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

    /// <summary>
    /// 再生のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_MEDIA_PLAY = 46;

    /// <summary>
    /// 一時停止のアプリケーションコマンド定数
    /// </summary>
    public const int APPCOMMAND_MEDIA_PAUSE = 47;

    private const int APPCOMMAND_MASK = 0x0FFF;

    /// <summary>
    /// <c>WM_APPCOMMAND</c> の <paramref name="lParam"/> からアプリケーションコマンド番号を抽出します
    /// </summary>
    /// <param name="lParam">Win32 メッセージの lParam</param>
    /// <returns>抽出されたコマンド番号</returns>
    public static int GetAppCommand(IntPtr lParam)
    {
        return (int)((lParam.ToInt64() >> 16) & APPCOMMAND_MASK);
    }

    /// <summary>
    /// アプリケーションコマンド番号を対応する WPF のメディア <see cref="Key"/> へ変換します
    /// </summary>
    /// <param name="appCommand">アプリケーションコマンド番号</param>
    /// <param name="key">変換後のメディアキー</param>
    /// <returns>対応するメディアキーが存在する場合は <c>true</c></returns>
    public static bool TryMapAppCommandToKey(int appCommand, out Key key)
    {
        switch (appCommand)
        {
            case APPCOMMAND_MEDIA_PLAY_PAUSE:
            case APPCOMMAND_MEDIA_PLAY:
            case APPCOMMAND_MEDIA_PAUSE:
                key = Key.MediaPlayPause;
                return true;
            case APPCOMMAND_MEDIA_STOP:
                key = Key.MediaStop;
                return true;
            case APPCOMMAND_MEDIA_NEXTTRACK:
                key = Key.MediaNextTrack;
                return true;
            case APPCOMMAND_MEDIA_PREVIOUSTRACK:
                key = Key.MediaPreviousTrack;
                return true;
            default:
                key = Key.None;
                return false;
        }
    }

    /// <summary>
    /// <see cref="KeyEventArgs"/> から IME 処理中・システムキー・デッドキーを考慮した実キーを解決します
    /// </summary>
    /// <param name="e">キーイベント引数</param>
    /// <returns>解決された実キー</returns>
    public static Key ResolveKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return ResolveKey(e.Key, e.SystemKey, e.ImeProcessedKey, e.DeadCharProcessedKey);
    }

    /// <summary>
    /// システムキー・IME処理キー・デッドキーを考慮して実キーを解決します
    /// </summary>
    /// <param name="key">基本キー</param>
    /// <param name="systemKey">システムキー</param>
    /// <param name="imeProcessedKey">IME処理キー</param>
    /// <param name="deadCharProcessedKey">デッドキー処理キー</param>
    /// <returns>解決された実キー</returns>
    public static Key ResolveKey(Key key, Key systemKey, Key imeProcessedKey, Key deadCharProcessedKey)
    {
        return key switch
        {
            Key.System => systemKey,
            Key.ImeProcessed => imeProcessedKey,
            Key.DeadCharProcessed => deadCharProcessedKey,
            _ => key
        };
    }

    /// <summary>
    /// ショートカットの主キーとして無視すべきキー（修飾キー単体やIMEモード切替キー等）かどうかを判定します
    /// </summary>
    /// <param name="key">判定対象キー</param>
    /// <returns>無視すべきキーの場合は <c>true</c></returns>
    public static bool IsIgnoredKey(Key key)
    {
        return key switch
        {
            Key.None => true,
            Key.System => true,
            Key.ImeProcessed => true,
            Key.DeadCharProcessed => true,
            Key.LeftCtrl => true,
            Key.RightCtrl => true,
            Key.LeftAlt => true,
            Key.RightAlt => true,
            Key.LeftShift => true,
            Key.RightShift => true,
            Key.LWin => true,
            Key.RWin => true,
            Key.ImeConvert => true,
            Key.ImeNonConvert => true,
            Key.ImeAccept => true,
            Key.ImeModeChange => true,
            Key.KanaMode => true,
            Key.KanjiMode => true,
            Key.JunjaMode => true,
            Key.FinalMode => true,
            Key.CapsLock => true,
            Key.NumLock => true,
            Key.Scroll => true,
            _ => false
        };
    }

    /// <summary>
    /// 指定されたキーがアプリ側で標準対応するハードウェアメディアキー（再生制御系）かどうかを判定します
    /// </summary>
    /// <param name="key">判定対象キー</param>
    /// <returns>標準対応するハードウェアメディアキーの場合は <c>true</c></returns>
    public static bool IsMediaKey(Key key)
    {
        return key switch
        {
            Key.MediaPlayPause => true,
            Key.MediaStop => true,
            Key.MediaNextTrack => true,
            Key.MediaPreviousTrack => true,
            _ => false
        };
    }

    /// <summary>
    /// ショートカット設定を表示用の文字列へ整形します
    /// </summary>
    /// <param name="config">ショートカット設定</param>
    /// <returns>表示用テキスト</returns>
    public static string FormatShortcutText(ShortcutKeyConfig? config)
    {
        if (config == null || config.Key == Key.None)
        {
            return "割り当てなし";
        }

        string mods = string.Empty;
        if (config.Modifiers.HasFlag(ModifierKeys.Control)) mods += "Ctrl + ";
        if (config.Modifiers.HasFlag(ModifierKeys.Shift)) mods += "Shift + ";
        if (config.Modifiers.HasFlag(ModifierKeys.Alt)) mods += "Alt + ";
        if (config.Modifiers.HasFlag(ModifierKeys.Windows)) mods += "Win + ";

        return mods + FormatKeyName(config.Key);
    }

    /// <summary>
    /// キーを表示用の可読名称へ整形します
    /// </summary>
    /// <param name="key">対象キー</param>
    /// <returns>キーの表示名</returns>
    public static string FormatKeyName(Key key)
    {
        return key switch
        {
            Key.MediaPlayPause => "MediaPlayPause (再生/一時停止)",
            Key.MediaStop => "MediaStop (停止)",
            Key.MediaNextTrack => "MediaNextTrack (次の曲)",
            Key.MediaPreviousTrack => "MediaPreviousTrack (前の曲)",
            Key.VolumeMute => "VolumeMute (ミュート)",
            Key.VolumeUp => "VolumeUp (音量上げ)",
            Key.VolumeDown => "VolumeDown (音量下げ)",
            _ => key.ToString()
        };
    }

    /// <summary>
    /// 指定された要素が編集可能な文字入力コントロールかどうかを判定します
    /// </summary>
    /// <param name="focusedElement">現在フォーカスを持つ要素</param>
    /// <returns>編集可能な文字入力コントロールの場合は <c>true</c></returns>
    public static bool IsTextInputFocused(IInputElement? focusedElement)
    {
        return focusedElement switch
        {
            TextBoxBase textBoxBase => !textBoxBase.IsReadOnly,
            PasswordBox => true,
            ComboBox comboBox => comboBox.IsEditable,
            _ => false
        };
    }

    /// <summary>
    /// 文字入力コントロールへのフォーカス中に、指定されたキー入力のショートカット発火をスキップすべきかどうかを判定します
    /// </summary>
    /// <param name="key">押下された実キー</param>
    /// <param name="modifiers">押下中の修飾キー</param>
    /// <param name="focusedElement">現在フォーカスを持つ要素</param>
    /// <returns>文字入力を優先してショートカット発火をスキップすべき場合は <c>true</c></returns>
    public static bool ShouldSkipForTextInput(Key key, ModifierKeys modifiers, IInputElement? focusedElement)
    {
        if (!IsTextInputFocused(focusedElement))
        {
            return false;
        }

        // ハードウェアメディアキーはテキスト入力中でも常に有効とする
        if (IsMediaKey(key))
        {
            return false;
        }

        // 修飾キーなし、または Shift のみの入力は通常の文字入力・Space・矢印移動を妨げないようスキップする
        return modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift;
    }
}
