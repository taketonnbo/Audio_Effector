using System;
using System.IO;
using System.Text.Json;

namespace AudioEffector.Services
{
    public enum ThemeType
    {
        Dark,
        Light,
        System
    }

    /// <summary>
    /// ミニプレイヤーの最前面表示の挙動。
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
    /// アプリケーションの設定情報を保持するモデルクラス。
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// アプリケーションのUIテーマ。
        /// </summary>
        public ThemeType Theme { get; set; } = ThemeType.System;

        /// <summary>
        /// 最後に開いていたライブラリのパス。
        /// </summary>
        public string? LastLibraryPath { get; set; }

        /// <summary>
        /// 左カラム（サイドバー）の幅。
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
        /// OS起動時に自動起動するかどうか。
        /// </summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>
        /// アプリ起動時に最小化状態で開始するかどうか。
        /// </summary>
        public bool StartMinimized { get; set; } = false;

        /// <summary>
        /// オーディオのサンプリングレート（Hz）。
        /// </summary>
        public int SampleRate { get; set; } = 44100;

        /// <summary>
        /// オーディオ再生のバッファサイズ（ミリ秒）。
        /// </summary>
        public int AudioBufferSizeMs { get; set; } = 100;

        /// <summary>
        /// 前回終了時に適用されていたエフェクトプリセットの名前。
        /// </summary>
        public string? LastUsedEffectPreset { get; set; }

        /// <summary>
        /// ミニプレイヤーの最前面表示の挙動。
        /// </summary>
        public MiniPlayerTopmostBehavior MiniPlayerTopmostBehavior { get; set; } = MiniPlayerTopmostBehavior.None;

        /// <summary>
        /// ミニプレイヤーの前回の表示位置（Y座標）。
        /// </summary>
        public double? MiniPlayerTop { get; set; }

        /// <summary>
        /// ミニプレイヤーの前回の表示位置（X座標）。
        /// </summary>
        public double? MiniPlayerLeft { get; set; }

        // --- ショートカット設定 ---
        public ShortcutKeyConfig PlayPauseShortcut { get; set; } = new ShortcutKeyConfig();
        public ShortcutKeyConfig StopShortcut { get; set; } = new ShortcutKeyConfig();
        public ShortcutKeyConfig NextShortcut { get; set; } = new ShortcutKeyConfig();
        public ShortcutKeyConfig PreviousShortcut { get; set; } = new ShortcutKeyConfig();
        public ShortcutKeyConfig MuteShortcut { get; set; } = new ShortcutKeyConfig();
        public ShortcutKeyConfig VolumeUpShortcut { get; set; } = new ShortcutKeyConfig();
        public ShortcutKeyConfig VolumeDownShortcut { get; set; } = new ShortcutKeyConfig();
    }

    /// <summary>
    /// アプリケーション設定の読み込み・保存を行うサービス。
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService()
        {
#if DEBUG
            var folderName = "AudioEffector_Debug";
#else
            var folderName = "AudioEffector";
#endif
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                folderName);
            Directory.CreateDirectory(appDataPath);
            _settingsFilePath = Path.Combine(appDataPath, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // Silent fail for now
            }
        }
    }
}
