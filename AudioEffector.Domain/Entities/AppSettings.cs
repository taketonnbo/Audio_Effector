using System;
using System.Collections.Generic;

namespace AudioEffector.Domain.Entities;

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
    /// 再スキャン時に更新日時が変更された既存ファイルのメタデータを再解析するかどうか
    /// </summary>
    public bool DetectMetadataChangesOnRescan { get; set; }

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
    public bool EnableNormalize { get; set; }

    /// <summary>
    /// OS起動時に自動起動するかどうか
    /// </summary>
    public bool AutoStart { get; set; }

    /// <summary>
    /// アプリ起動時に最小化状態で開始するかどうか
    /// </summary>
    public bool StartMinimized { get; set; }

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
    /// メイン画面最小化時にコンパクトプレイヤーを表示しないかどうか
    /// </summary>
    public bool DisableMiniPlayerOnMinimize { get; set; }

    /// <summary>
    /// ミニプレイヤーのデザイン種別
    /// </summary>
    public MiniPlayerDesignStyle MiniPlayerDesignStyle { get; set; } = MiniPlayerDesignStyle.Standard;

    /// <summary>
    /// 正方形ミニプレイヤーで楽曲再生中にアルバムアートを時計回りに回転させるかどうか
    /// </summary>
    public bool MiniPlayerRotateAlbumArt { get; set; } = true;

    /// <summary>
    /// ミニプレイヤーの前回の表示位置（Y座標）
    /// </summary>
    public double? MiniPlayerTop { get; set; }

    /// <summary>
    /// ミニプレイヤーの前回の表示位置（X座標）
    /// </summary>
    public double? MiniPlayerLeft { get; set; }

    /// <summary>
    /// 再生/一時停止ショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig PlayPauseShortcut { get; set; } = new();

    /// <summary>
    /// 停止ショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig StopShortcut { get; set; } = new();

    /// <summary>
    /// 次へショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig NextShortcut { get; set; } = new();

    /// <summary>
    /// 前へショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig PreviousShortcut { get; set; } = new();

    /// <summary>
    /// ミュートショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig MuteShortcut { get; set; } = new();

    /// <summary>
    /// 音量アップショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig VolumeUpShortcut { get; set; } = new();

    private Dictionary<string, string> _deviceDefaultTransferFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 音量ダウンショートカットキー設定
    /// </summary>
    public ShortcutKeyConfig VolumeDownShortcut { get; set; } = new();

    /// <summary>
    /// ユーザープリセット・プレイリスト・お気に入り・再生履歴などのデータファイル保存先ディレクトリパス（未指定時は既定の AppData\AudioEffector）
    /// </summary>
    public string? DataDirectoryPath { get; set; }

    /// <summary>
    /// デバッグ用ログの出力レベル（Trace / Debug / Info / Warn / Error / Off、既定値は Info）
    /// </summary>
    public string LogLevel { get; set; } = "Info";

    /// <summary>
    /// デバッグ用ログファイルの保存先ディレクトリパス（未指定時は実行フォルダ配下の logs）
    /// </summary>
    public string? LogDirectoryPath { get; set; }

    /// <summary>
    /// 機器転送時にMP4ファイルをM4A形式へ自動変換するかどうか
    /// </summary>
    public bool ConvertMp4ToM4a { get; set; }

    /// <summary>
    /// 機器転送時にFLACファイルをMP3形式へ自動変換してデータ量を削減するかどうか
    /// </summary>
    public bool ConvertFlacToMp3 { get; set; }

    /// <summary>
    /// LRC 自動生成機能の著作権・利用ガイドラインを確認・承諾済みかどうか
    /// </summary>
    public bool HasAcceptedLrcCopyrightGuideline { get; set; }

    /// <summary>
    /// 機器転送時に自動生成以外（手動作成・外部取得等）の .lrc 歌詞ファイルを同梱転送するかどうか
    /// </summary>
    public bool TransferManualLrcFiles { get; set; }

    /// <summary>
    /// 機器転送時にテキストファイルから自動生成した .lrc 歌詞ファイルを同梱転送するかどうか
    /// </summary>
    public bool TransferTextAlignedLrcFiles { get; set; }

    /// <summary>
    /// 機器転送時に音声から自動文字起こし生成した .lrc 歌詞ファイルを同梱転送するかどうか
    /// </summary>
    public bool TransferTranscribedLrcFiles { get; set; }

    /// <summary>
    /// 接続機器識別子ごとの既定転送先フォルダパス辞書
    /// </summary>
    public Dictionary<string, string> DeviceDefaultTransferFolders
    {
        get => _deviceDefaultTransferFolders;
        set => _deviceDefaultTransferFolders = value != null
            ? new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// UIテーマの種類を表す列挙体
/// </summary>
public enum ThemeType
{
    /// <summary>ダークテーマ</summary>
    Dark,
    /// <summary>ライトテーマ</summary>
    Light,
    /// <summary>システム同期テーマ</summary>
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
/// ミニプレイヤーのデザイン種別
/// </summary>
public enum MiniPlayerDesignStyle
{
    /// <summary>従来の横長長方形デザイン</summary>
    Standard,
    /// <summary>アルバムアートを前面に表示する正方形デザイン</summary>
    SquareAlbumArt
}

/// <summary>
/// ショートカットキーの修飾キーフラグ（WPF ModifierKeys とビット値互換）
/// </summary>
[Flags]
public enum ShortcutModifiers
{
    /// <summary>修飾キーなし</summary>
    None = 0,
    /// <summary>Alt キー</summary>
    Alt = 1,
    /// <summary>Ctrl キー</summary>
    Control = 2,
    /// <summary>Shift キー</summary>
    Shift = 4,
    /// <summary>Windows キー</summary>
    Windows = 8
}

/// <summary>
/// キーボードショートカットの設定を保持するモデル（WPF非依存）
/// </summary>
public class ShortcutKeyConfig
{
    /// <summary>未割り当てを表すキーコード定数</summary>
    public const int NoneKey = 0;

    /// <summary>対象キーコード（WPF の Key 列挙体の整数値と 1:1 互換、0 は未割り当て）</summary>
    public int Key { get; set; } = NoneKey;

    /// <summary>修飾キーフラグ</summary>
    public ShortcutModifiers Modifiers { get; set; } = ShortcutModifiers.None;
}
