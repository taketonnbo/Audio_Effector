using System;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// UIテーマの種類を表す列挙体
/// </summary>
public enum ThemeType
{
    Dark,
    Light,
    System
}

/// <summary>
/// ミニプレイヤーの最前面表示の挙動
/// </summary>
public enum MiniPlayerTopmostBehavior
{
    /// <summary>常に最前面に表示する</summary>
    AlwaysOnTop,
    /// <summary>表示された時のみ最前面とし、他選択で解除</summary>
    OnDisplayOnly,
    /// <summary>最前面に表示しない（通常のウィンドウ）</summary>
    None
}

/// <summary>
/// キーボードショートカットの設定を保持するクラス
/// </summary>
public class ShortcutKeyConfig
{
    public System.Windows.Input.Key Key { get; set; } = System.Windows.Input.Key.None;
    public System.Windows.Input.ModifierKeys Modifiers { get; set; } = System.Windows.Input.ModifierKeys.None;
}

/// <summary>
/// アプリケーションの設定情報を保持するモデルクラス
/// </summary>
public class AppSettings
{
    /// <summary>
    /// アプリケーションのUIテーマ
    /// </summary>
    public ThemeType Theme { get; set; } = ThemeType.System;

    /// <summary>
    /// 最後に開いていたライブラリのパス
    /// </summary>
    public string? LastLibraryPath { get; set; }

    /// <summary>
    /// 左カラム（サイドバー）の幅
    /// </summary>
    public double LeftColumnWidth { get; set; } = 300;

    /// <summary>
    /// マスター音量（0.0〜1.0）
    /// </summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// ノーマライズ（音量正規化）を有効にするかどうか
    /// </summary>
    public bool EnableNormalize { get; set; } = false;

    /// <summary>
    /// OS起動時に自動起動するかどうか
    /// </summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// アプリ起動時に最小化状態で開始するかどうか
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// オーディオのサンプリングレート（Hz）
    /// </summary>
    public int SampleRate { get; set; } = 44100;

    /// <summary>
    /// オーディオ再生のバッファサイズ（ミリ秒）
    /// </summary>
    public int AudioBufferSizeMs { get; set; } = 100;

    /// <summary>
    /// 前回終了時に適用されていたエフェクトプリセットの名前
    /// </summary>
    public string? LastUsedEffectPreset { get; set; }

    /// <summary>
    /// ミニプレイヤーの最前面表示の挙動
    /// </summary>
    public MiniPlayerTopmostBehavior MiniPlayerTopmostBehavior { get; set; } = MiniPlayerTopmostBehavior.None;

    /// <summary>
    /// ミニプレイヤーの前回の表示位置（Y座標）
    /// </summary>
    public double? MiniPlayerTop { get; set; }

    /// <summary>
    /// ミニプレイヤーの前回の表示位置（X座標）
    /// </summary>
    public double? MiniPlayerLeft { get; set; }

    // --- ショートカット設定 ---
    public ShortcutKeyConfig PlayPauseShortcut { get; set; } = new();
    public ShortcutKeyConfig StopShortcut { get; set; } = new();
    public ShortcutKeyConfig NextShortcut { get; set; } = new();
    public ShortcutKeyConfig PreviousShortcut { get; set; } = new();
    public ShortcutKeyConfig MuteShortcut { get; set; } = new();
    public ShortcutKeyConfig VolumeUpShortcut { get; set; } = new();
    public ShortcutKeyConfig VolumeDownShortcut { get; set; } = new();
}
