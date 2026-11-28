using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Windows;
using AudioEffector.Presentation.Themes;
using AudioEffector.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;


namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// データ保存先ディレクトリ変更時のユーザー選択アクションを表します
/// </summary>
public enum DataMigrationChoice
{
    /// <summary>既存データをコピーして保存先を切り替える</summary>
    Copy,

    /// <summary>既存データを移動して保存先を切り替える</summary>
    Move,

    /// <summary>既存データを移行せず保存先のみ切り替える</summary>
    SwitchWithoutMigration,

    /// <summary>保存先の変更をキャンセルする</summary>
    Cancel
}

/// <summary>
/// アプリケーション設定画面のViewModel
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioService _audioService;
    private readonly IDataDirectoryProvider? _dataDirectoryProvider;
    private readonly IDataDirectoryMigrationService? _dataDirectoryMigrationService;
    private readonly ILogConfigurationManager? _logConfigurationManager;
    private readonly ListeningStatsApplicationService? _listeningStatsService;
    private readonly Func<string?, string, string?> _folderPicker;
    private readonly Func<string, string, bool, DataMigrationChoice> _migrationPrompt;
    private readonly Func<string, string, bool> _resetConfirmationPrompt;
    private readonly Action<string, string, bool> _messageNotifier;
    private readonly bool _hasCustomMessageNotifier;
    private readonly Action<string> _folderOpener;
    private readonly string _defaultDataDirectoryFallback;
    private readonly string _defaultLogDirectoryFallback;
    private AppSettings _appSettings;

    /// <summary>
    /// データ保存先ディレクトリが正常に変更された際に発火するイベント
    /// </summary>
    public event Action? DataDirectoryChanged;

    /// <summary>
    /// 再生履歴およびリスニング統計データが全件リセットされた際に発火するイベント
    /// </summary>
    public event Action? ListeningStatsReset;

    /// <summary>
    /// 設定カテゴリーの一覧を取得します
    /// </summary>
    public ObservableCollection<string> Categories { get; }

    private string _selectedCategory = string.Empty;

    /// <summary>
    /// 選択されている設定カテゴリーを取得または設定します
    /// </summary>
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 選択可能なテーマ一覧を取得します
    /// </summary>
    public IReadOnlyList<ThemeType> AvailableThemes { get; } = new[]
    {
        ThemeType.Light,
        ThemeType.Dark,
        ThemeType.System
    };

    /// <summary>
    /// 選択されているテーマを取得または設定します
    /// </summary>
    public ThemeType SelectedTheme
    {
        get => _appSettings.Theme;
        set
        {
            if (_appSettings.Theme != value)
            {
                _appSettings.Theme = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.Theme = value);
                ThemeManager.ApplyTheme(value);
            }
        }
    }

    /// <summary>
    /// OS起動時の自動起動が有効かどうかを取得または設定します
    /// </summary>
    public bool AutoStart
    {
        get => _appSettings.AutoStart;
        set
        {
            if (_appSettings.AutoStart != value)
            {
                _appSettings.AutoStart = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.AutoStart = value);
                StartupManager.SetAutoStart(value);
            }
        }
    }

    /// <summary>
    /// 起動時に最小化状態で開始するかどうかを取得または設定します
    /// </summary>
    public bool StartMinimized
    {
        get => _appSettings.StartMinimized;
        set
        {
            if (_appSettings.StartMinimized != value)
            {
                _appSettings.StartMinimized = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.StartMinimized = value);
            }
        }
    }

    /// <summary>
    /// 選択可能な最前面表示動作一覧を取得します
    /// </summary>
    public IReadOnlyList<string> AvailableTopmostBehaviors { get; } = new[]
    {
        "常に最前面に表示",
        "表示時のみ最前面に表示",
        "最前面に表示しない"
    };

    /// <summary>
    /// 選択されているミニプレイヤーの最前面表示動作を取得または設定します
    /// </summary>
    public string SelectedTopmostBehavior
    {
        get
        {
            switch (_appSettings.MiniPlayerTopmostBehavior)
            {
                case MiniPlayerTopmostBehavior.AlwaysOnTop: return AvailableTopmostBehaviors[0];
                case MiniPlayerTopmostBehavior.OnDisplayOnly: return AvailableTopmostBehaviors[1];
                case MiniPlayerTopmostBehavior.None: return AvailableTopmostBehaviors[2];
                default: return AvailableTopmostBehaviors[2];
            }
        }
        set
        {
            MiniPlayerTopmostBehavior newValue = MiniPlayerTopmostBehavior.None;
            if (value == AvailableTopmostBehaviors[0]) newValue = MiniPlayerTopmostBehavior.AlwaysOnTop;
            else if (value == AvailableTopmostBehaviors[1]) newValue = MiniPlayerTopmostBehavior.OnDisplayOnly;

            if (_appSettings.MiniPlayerTopmostBehavior != newValue)
            {
                _appSettings.MiniPlayerTopmostBehavior = newValue;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.MiniPlayerTopmostBehavior = newValue);

                // Update current mini player if it is open
                var miniPlayer = System.Windows.Application.Current.Windows.OfType<AudioEffector.Presentation.Views.MiniPlayerWindow>().FirstOrDefault();
                if (miniPlayer != null)
                {
                    miniPlayer.UpdateTopmostBehavior(newValue);
                }
            }
        }
    }

    /// <summary>
    /// メイン画面最小化時にコンパクトプレイヤーを表示しないかどうかを取得または設定します
    /// </summary>
    public bool DisableMiniPlayerOnMinimize
    {
        get => _appSettings.DisableMiniPlayerOnMinimize;
        set
        {
            if (_appSettings.DisableMiniPlayerOnMinimize != value)
            {
                _appSettings.DisableMiniPlayerOnMinimize = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.DisableMiniPlayerOnMinimize = value);
            }
        }
    }

    /// <summary>
    /// 選択可能なミニプレイヤーデザイン一覧を取得します
    /// </summary>
    public IReadOnlyList<string> AvailableMiniPlayerDesignStyles { get; } = new[]
    {
        "横長（標準）",
        "正方形（アルバムアート）"
    };

    /// <summary>
    /// 選択されているミニプレイヤーデザインを取得または設定します
    /// </summary>
    public string SelectedMiniPlayerDesignStyle
    {
        get => _appSettings.MiniPlayerDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt
            ? AvailableMiniPlayerDesignStyles[1]
            : AvailableMiniPlayerDesignStyles[0];
        set
        {
            MiniPlayerDesignStyle newValue = value == AvailableMiniPlayerDesignStyles[1]
                ? MiniPlayerDesignStyle.SquareAlbumArt
                : MiniPlayerDesignStyle.Standard;

            if (_appSettings.MiniPlayerDesignStyle != newValue)
            {
                _appSettings.MiniPlayerDesignStyle = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSquareMiniPlayerDesignSelected));
                _settingsService.UpdateSettings(settings => settings.MiniPlayerDesignStyle = newValue);
                NotifyMiniPlayerDesignChanged();
            }
        }
    }

    /// <summary>
    /// 正方形（アルバムアート）デザインが選択されているかどうかを取得します
    /// </summary>
    public bool IsSquareMiniPlayerDesignSelected => _appSettings.MiniPlayerDesignStyle == MiniPlayerDesignStyle.SquareAlbumArt;

    /// <summary>
    /// 正方形ミニプレイヤーで楽曲再生中にアルバムアートを回転させるかどうかを取得または設定します
    /// </summary>
    public bool MiniPlayerRotateAlbumArt
    {
        get => _appSettings.MiniPlayerRotateAlbumArt;
        set
        {
            if (_appSettings.MiniPlayerRotateAlbumArt != value)
            {
                _appSettings.MiniPlayerRotateAlbumArt = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.MiniPlayerRotateAlbumArt = value);
                NotifyMiniPlayerDesignChanged();
            }
        }
    }

    private void NotifyMiniPlayerDesignChanged()
    {
        var app = System.Windows.Application.Current;
        if (app == null || !app.CheckAccess())
        {
            return;
        }

        var miniPlayer = app.Windows.OfType<AudioEffector.Presentation.Views.MiniPlayerWindow>().FirstOrDefault();
        miniPlayer?.UpdateDesignSettings(_appSettings.MiniPlayerDesignStyle, _appSettings.MiniPlayerRotateAlbumArt);
    }

    /// <summary>
    /// マスター音量を取得または設定します
    /// </summary>
    public float MasterVolume
    {
        get => _appSettings.Volume;
        set
        {
            if (Math.Abs(_appSettings.Volume - value) > 0.001f)
            {
                _appSettings.Volume = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.Volume = value);
                if (_audioService != null)
                {
                    _audioService.Volume = value;
                }
            }
        }
    }

    /// <summary>
    /// 音量ノーマライズが有効かどうかを取得または設定します
    /// </summary>
    public bool EnableNormalize
    {
        get => _appSettings.EnableNormalize;
        set
        {
            if (_appSettings.EnableNormalize != value)
            {
                _appSettings.EnableNormalize = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.EnableNormalize = value);
            }
        }
    }

    /// <summary>
    /// 再スキャン時に更新された既存ファイルのメタデータを再解析するかどうかを取得または設定します
    /// </summary>
    public bool DetectMetadataChangesOnRescan
    {
        get => _appSettings.DetectMetadataChangesOnRescan;
        set
        {
            if (_appSettings.DetectMetadataChangesOnRescan != value)
            {
                _appSettings.DetectMetadataChangesOnRescan = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.DetectMetadataChangesOnRescan = value);
            }
        }
    }

    /// <summary>
    /// 選択可能なサンプリングレート一覧を取得します
    /// </summary>
    public IReadOnlyList<int> AvailableSampleRates { get; } = new[] { 44100, 48000, 88200, 96000, 192000 };

    /// <summary>
    /// 選択可能なバッファーサイズ一覧（ミリ秒）を取得します
    /// </summary>
    public IReadOnlyList<int> AvailableBufferSizes { get; } = new[] { 50, 100, 200, 300, 500 };

    /// <summary>
    /// 選択されているサンプリングレートを取得または設定します
    /// </summary>
    public int SelectedSampleRate
    {
        get => _appSettings.SampleRate;
        set
        {
            if (_appSettings.SampleRate != value)
            {
                _appSettings.SampleRate = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.SampleRate = value);
                _audioService?.UpdateAudioProperties(_appSettings.SampleRate, _appSettings.AudioBufferSizeMs);
            }
        }
    }

    /// <summary>
    /// 選択されているバッファーサイズ（ミリ秒）を取得または設定します
    /// </summary>
    public int SelectedBufferSize
    {
        get => _appSettings.AudioBufferSizeMs;
        set
        {
            if (_appSettings.AudioBufferSizeMs != value)
            {
                _appSettings.AudioBufferSizeMs = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.AudioBufferSizeMs = value);
                _audioService?.UpdateAudioProperties(_appSettings.SampleRate, _appSettings.AudioBufferSizeMs);
            }
        }
    }

    /// <summary>
    /// 機器転送時にMP4ファイルをM4A形式へ自動変換するかどうかを取得または設定します
    /// </summary>
    public bool ConvertMp4ToM4a
    {
        get => _appSettings.ConvertMp4ToM4a;
        set
        {
            if (_appSettings.ConvertMp4ToM4a != value)
            {
                _appSettings.ConvertMp4ToM4a = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.ConvertMp4ToM4a = value);
            }
        }
    }

    /// <summary>
    /// 機器転送時にFLACファイルをMP3形式へ自動変換してデータ量を削減するかどうかを取得または設定します
    /// </summary>
    public bool ConvertFlacToMp3
    {
        get => _appSettings.ConvertFlacToMp3;
        set
        {
            if (_appSettings.ConvertFlacToMp3 != value)
            {
                _appSettings.ConvertFlacToMp3 = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.ConvertFlacToMp3 = value);
            }
        }
    }

    /// <summary>
    /// 機器転送時に自動生成以外（手動作成・外部取得等）の .lrc 歌詞ファイルを同梱転送するかどうかを取得または設定します
    /// </summary>
    public bool TransferManualLrcFiles
    {
        get => _appSettings.TransferManualLrcFiles;
        set
        {
            if (_appSettings.TransferManualLrcFiles != value)
            {
                _appSettings.TransferManualLrcFiles = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.TransferManualLrcFiles = value);
            }
        }
    }

    /// <summary>
    /// 機器転送時にテキストファイルから自動生成した .lrc 歌詞ファイルを同梱転送するかどうかを取得または設定します
    /// </summary>
    public bool TransferTextAlignedLrcFiles
    {
        get => _appSettings.TransferTextAlignedLrcFiles;
        set
        {
            if (_appSettings.TransferTextAlignedLrcFiles != value)
            {
                _appSettings.TransferTextAlignedLrcFiles = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.TransferTextAlignedLrcFiles = value);
            }
        }
    }

    /// <summary>
    /// 機器転送時に音声から自動文字起こし生成した .lrc 歌詞ファイルを同梱転送するかどうかを取得または設定します
    /// </summary>
    public bool TransferTranscribedLrcFiles
    {
        get => _appSettings.TransferTranscribedLrcFiles;
        set
        {
            if (_appSettings.TransferTranscribedLrcFiles != value)
            {
                _appSettings.TransferTranscribedLrcFiles = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.TransferTranscribedLrcFiles = value);
            }
        }
    }

    // --- Shortcuts ---

    /// <summary>
    /// 再生/一時停止のショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig PlayPauseShortcut
    {
        get => _appSettings.PlayPauseShortcut;
        set
        {
            _appSettings.PlayPauseShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.PlayPauseShortcut = value);
        }
    }

    /// <summary>
    /// 停止のショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig StopShortcut
    {
        get => _appSettings.StopShortcut;
        set
        {
            _appSettings.StopShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.StopShortcut = value);
        }
    }

    /// <summary>
    /// 次の曲へ移動するショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig NextShortcut
    {
        get => _appSettings.NextShortcut;
        set
        {
            _appSettings.NextShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.NextShortcut = value);
        }
    }

    /// <summary>
    /// 前の曲へ移動するショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig PreviousShortcut
    {
        get => _appSettings.PreviousShortcut;
        set
        {
            _appSettings.PreviousShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.PreviousShortcut = value);
        }
    }

    /// <summary>
    /// ミュート切り替えのショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig MuteShortcut
    {
        get => _appSettings.MuteShortcut;
        set
        {
            _appSettings.MuteShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.MuteShortcut = value);
        }
    }

    /// <summary>
    /// 音量アップのショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig VolumeUpShortcut
    {
        get => _appSettings.VolumeUpShortcut;
        set
        {
            _appSettings.VolumeUpShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.VolumeUpShortcut = value);
        }
    }

    /// <summary>
    /// 音量ダウンのショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig VolumeDownShortcut
    {
        get => _appSettings.VolumeDownShortcut;
        set
        {
            _appSettings.VolumeDownShortcut = value;
            OnPropertyChanged();
            _settingsService.UpdateSettings(settings => settings.VolumeDownShortcut = value);
        }
    }

    // --- Data Directory ---

    /// <summary>
    /// 現在有効なデータ保存先ディレクトリのパスを取得します
    /// </summary>
    public string DataDirectoryPath
    {
        get
        {
            if (_dataDirectoryProvider != null)
            {
                return _dataDirectoryProvider.DataDirectory;
            }

            return !string.IsNullOrWhiteSpace(_appSettings.DataDirectoryPath)
                ? _appSettings.DataDirectoryPath
                : _defaultDataDirectoryFallback;
        }
    }

    /// <summary>
    /// 既定ディレクトリ以外のカスタムデータ保存先ディレクトリが設定されているかどうかを取得します
    /// </summary>
    public bool IsCustomDataDirectory =>
        _dataDirectoryProvider != null
            ? _dataDirectoryProvider.IsCustomDirectory
            : !string.IsNullOrWhiteSpace(_appSettings.DataDirectoryPath);

    /// <summary>
    /// データ保存先フォルダを参照・選択するコマンドを取得します
    /// </summary>
    public ICommand BrowseDataDirectoryCommand { get; }

    /// <summary>
    /// データ保存先フォルダを既定のフォルダへリセットするコマンドを取得します
    /// </summary>
    public ICommand ResetDataDirectoryCommand { get; }

    // --- Logging ---

    private static readonly IReadOnlyList<string> DefaultAvailableLogLevels =
    [
        "Trace",
        "Debug",
        "Info",
        "Warn",
        "Error",
        "Off",
    ];

    /// <summary>
    /// 選択可能なログ出力レベル一覧を取得します
    /// </summary>
    public IReadOnlyList<string> AvailableLogLevels => _logConfigurationManager?.AvailableLogLevels ?? DefaultAvailableLogLevels;

    /// <summary>
    /// 選択されているログ出力レベルを取得または設定します
    /// </summary>
    public string SelectedLogLevel
    {
        get => NormalizeLogLevel(_appSettings.LogLevel);
        set
        {
            string normalized = NormalizeLogLevel(value);
            if (!string.Equals(NormalizeLogLevel(_appSettings.LogLevel), normalized, StringComparison.OrdinalIgnoreCase))
            {
                _appSettings.LogLevel = normalized;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.LogLevel = normalized);
                if (_logConfigurationManager != null)
                {
                    string? activeLogDirectory = _logConfigurationManager.IsCustomLogDirectory
                        ? _logConfigurationManager.CurrentLogDirectory
                        : null;
                    LogConfigurationApplyResult applyResult = _logConfigurationManager.ApplyConfiguration(
                        normalized,
                        activeLogDirectory,
                        fallbackToDefaultOnError: true);
                    if (applyResult.UsedFallbackDirectory)
                    {
                        OnPropertyChanged(nameof(LogDirectoryPath));
                        OnPropertyChanged(nameof(IsCustomLogDirectory));
                        CommandManager.InvalidateRequerySuggested();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 現在有効なログ保存先ディレクトリのパスを取得します
    /// </summary>
    public string LogDirectoryPath
    {
        get
        {
            if (_logConfigurationManager != null)
            {
                return _logConfigurationManager.CurrentLogDirectory;
            }

            return !string.IsNullOrWhiteSpace(_appSettings.LogDirectoryPath)
                ? _appSettings.LogDirectoryPath
                : _defaultLogDirectoryFallback;
        }
    }

    /// <summary>
    /// 既定ディレクトリ以外のカスタムログ保存先ディレクトリが設定されているかどうかを取得します
    /// </summary>
    public bool IsCustomLogDirectory =>
        _logConfigurationManager != null
            ? _logConfigurationManager.IsCustomLogDirectory
            : !string.IsNullOrWhiteSpace(_appSettings.LogDirectoryPath);

    /// <summary>
    /// ログ保存先フォルダを参照・選択するコマンドを取得します
    /// </summary>
    public ICommand BrowseLogDirectoryCommand { get; }

    /// <summary>
    /// ログ保存先フォルダを既定のフォルダへリセットするコマンドを取得します
    /// </summary>
    public ICommand ResetLogDirectoryCommand { get; }

    /// <summary>
    /// 現在のログ保存先フォルダをエクスプローラーで開くコマンドを取得します
    /// </summary>
    public ICommand OpenLogFolderCommand { get; }

    /// <summary>
    /// 再生履歴およびリスニング統計データをすべてリセットするコマンドを取得します
    /// </summary>
    public ICommand ResetListeningStatsCommand { get; }

    /// <summary>
    /// 再生履歴・リスニング統計データの全リセット機能が利用可能かどうかを取得します
    /// </summary>
    public bool CanResetListeningStats => _listeningStatsService != null;

    /// <summary>
    /// 設定サービスとオーディオサービスを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="audioService">オーディオサービス</param>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ（任意）</param>
    /// <param name="dataDirectoryMigrationService">データディレクトリ変更・移行サービス（任意）</param>
    /// <param name="logConfigurationManager">ログ出力構成マネージャ（任意）</param>
    /// <param name="listeningStatsService">リスニング統計アプリケーションサービス（任意）</param>
    [ActivatorUtilitiesConstructor]
    public SettingsViewModel(
        ISettingsService settingsService,
        IAudioService audioService,
        IDataDirectoryProvider? dataDirectoryProvider = null,
        IDataDirectoryMigrationService? dataDirectoryMigrationService = null,
        ILogConfigurationManager? logConfigurationManager = null,
        ListeningStatsApplicationService? listeningStatsService = null)
        : this(
            settingsService,
            audioService,
            dataDirectoryProvider,
            dataDirectoryMigrationService,
            folderPicker: null,
            migrationPrompt: null,
            messageNotifier: null,
            logConfigurationManager: logConfigurationManager,
            folderOpener: null,
            listeningStatsService: listeningStatsService,
            resetConfirmationPrompt: null)
    {
    }

    /// <summary>
    /// テストおよびダイアログ差し替え用に依存デリゲートを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="audioService">オーディオサービス</param>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ</param>
    /// <param name="dataDirectoryMigrationService">データディレクトリ変更・移行サービス</param>
    /// <param name="folderPicker">フォルダ選択ダイアログデリゲート</param>
    /// <param name="migrationPrompt">既存データ移行確認ダイアログデリゲート</param>
    /// <param name="messageNotifier">結果・エラー通知デリゲート</param>
    /// <param name="logConfigurationManager">ログ出力構成マネージャ</param>
    /// <param name="folderOpener">フォルダを開く操作デリゲート</param>
    /// <param name="listeningStatsService">リスニング統計アプリケーションサービス（任意）</param>
    /// <param name="resetConfirmationPrompt">統計全リセット確認ダイアログデリゲート（任意）</param>
    internal SettingsViewModel(
        ISettingsService settingsService,
        IAudioService audioService,
        IDataDirectoryProvider? dataDirectoryProvider,
        IDataDirectoryMigrationService? dataDirectoryMigrationService,
        Func<string?, string?>? folderPicker,
        Func<string, string, bool, DataMigrationChoice>? migrationPrompt,
        Action<string, string, bool>? messageNotifier,
        ILogConfigurationManager? logConfigurationManager = null,
        Action<string>? folderOpener = null,
        ListeningStatsApplicationService? listeningStatsService = null,
        Func<string, string, bool>? resetConfirmationPrompt = null)
    {
        _settingsService = settingsService;
        _audioService = audioService;
        _dataDirectoryProvider = dataDirectoryProvider;
        _dataDirectoryMigrationService = dataDirectoryMigrationService;
        _logConfigurationManager = logConfigurationManager;
        _listeningStatsService = listeningStatsService;
        _folderPicker = folderPicker != null
            ? (initialDir, _) => folderPicker(initialDir)
            : ShowDefaultFolderPicker;
        _migrationPrompt = migrationPrompt ?? ShowDefaultMigrationPrompt;
        _resetConfirmationPrompt = resetConfirmationPrompt ?? ShowDefaultResetConfirmationPrompt;
        _hasCustomMessageNotifier = messageNotifier != null;
        _messageNotifier = messageNotifier ?? ShowDefaultMessage;
        _folderOpener = folderOpener ?? OpenFolderInExplorer;
        _defaultDataDirectoryFallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioEffector");
        _defaultLogDirectoryFallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "logs"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _appSettings = _settingsService.LoadSettings();

        BrowseDataDirectoryCommand = new RelayCommand(_ => BrowseDataDirectory());
        ResetDataDirectoryCommand = new RelayCommand(_ => ResetDataDirectory(), _ => IsCustomDataDirectory);
        BrowseLogDirectoryCommand = new RelayCommand(_ => BrowseLogDirectory());
        ResetLogDirectoryCommand = new RelayCommand(_ => ResetLogDirectory(), _ => IsCustomLogDirectory);
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        ResetListeningStatsCommand = new RelayCommand(_ => ResetListeningStats(), _ => CanResetListeningStats);

        Categories = new ObservableCollection<string>
        {
            "一般",
            "オーディオデバイス",
            "エフェクト・再生",
            "ショートカット",
            "データ管理・その他"
        };

        SelectedCategory = Categories[0];
    }

    private void BrowseDataDirectory()
    {
        string? selectedPath = _folderPicker(DataDirectoryPath, "データ保存先フォルダを選択してください");
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        ApplyDataDirectoryChange(selectedPath);
    }

    private void ResetDataDirectory()
    {
        if (!IsCustomDataDirectory)
        {
            return;
        }

        ApplyDataDirectoryChange(null);
    }

    private void ApplyDataDirectoryChange(string? targetDirectory)
    {
        string currentDirectory = DataDirectoryPath;
        string defaultDirectory = _dataDirectoryProvider?.DefaultDataDirectory ?? _defaultDataDirectoryFallback;
        string displayTarget = string.IsNullOrWhiteSpace(targetDirectory)
            ? defaultDirectory
            : targetDirectory.Trim();

        if (_dataDirectoryMigrationService != null)
        {
            if (_dataDirectoryMigrationService.IsSameDirectory(currentDirectory, displayTarget))
            {
                return;
            }

            DataMigrationMode migrationMode = DataMigrationMode.None;
            if (_dataDirectoryMigrationService.HasExistingDataFiles(currentDirectory))
            {
                bool destinationHasExisting = _dataDirectoryMigrationService.HasExistingDataFiles(displayTarget);
                DataMigrationChoice choice = _migrationPrompt(currentDirectory, displayTarget, destinationHasExisting);
                if (choice == DataMigrationChoice.Cancel)
                {
                    return;
                }

                migrationMode = choice switch
                {
                    DataMigrationChoice.Copy => DataMigrationMode.Copy,
                    DataMigrationChoice.Move => DataMigrationMode.Move,
                    _ => DataMigrationMode.None,
                };
            }

            DataDirectoryChangeResult result = _dataDirectoryMigrationService.ChangeDataDirectory(targetDirectory, migrationMode);
            if (!result.Succeeded)
            {
                _messageNotifier(
                    result.ErrorMessage ?? "データ保存先の変更に失敗しました。",
                    "データ保存先の変更エラー",
                    true);
                return;
            }

            if (result.Changed)
            {
                _appSettings.DataDirectoryPath = IsCustomDataDirectory ? result.EffectivePath : null;
                OnPropertyChanged(nameof(DataDirectoryPath));
                OnPropertyChanged(nameof(IsCustomDataDirectory));
                CommandManager.InvalidateRequerySuggested();
                DataDirectoryChanged?.Invoke();

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    _messageNotifier(result.ErrorMessage, "データ保存先の変更", false);
                }
            }

            return;
        }

        string? nextPersistedPath = string.IsNullOrWhiteSpace(targetDirectory)
            ? null
            : string.Equals(
                targetDirectory.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                defaultDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
                ? null
                : targetDirectory.Trim();

        if (string.Equals(_appSettings.DataDirectoryPath, nextPersistedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _appSettings.DataDirectoryPath = nextPersistedPath;
        _settingsService.UpdateSettings(settings => settings.DataDirectoryPath = nextPersistedPath);
        _dataDirectoryProvider?.UpdateDataDirectory(nextPersistedPath);
        OnPropertyChanged(nameof(DataDirectoryPath));
        OnPropertyChanged(nameof(IsCustomDataDirectory));
        CommandManager.InvalidateRequerySuggested();
        DataDirectoryChanged?.Invoke();
    }

    private static string? ShowDefaultFolderPicker(string? initialDirectory, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static DataMigrationChoice ShowDefaultMigrationPrompt(string sourceDirectory, string targetDirectory, bool destinationHasExistingData)
    {
        var dialog = new DataMigrationDialog(sourceDirectory, targetDirectory, destinationHasExistingData);
        var owner = System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? System.Windows.Application.Current?.MainWindow;
        if (owner != null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog.SelectedChoice;
    }

    private static void ShowDefaultMessage(string message, string title, bool isError)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Warning);
    }

    private string NormalizeLogLevel(string? logLevel)
    {
        if (_logConfigurationManager != null)
        {
            return _logConfigurationManager.NormalizeLogLevel(logLevel);
        }

        if (string.IsNullOrWhiteSpace(logLevel))
        {
            return "Info";
        }

        string trimmed = logLevel.Trim();
        foreach (string supported in DefaultAvailableLogLevels)
        {
            if (string.Equals(supported, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return "Info";
    }

    private void BrowseLogDirectory()
    {
        string? selectedPath = _folderPicker(LogDirectoryPath, "ログ保存先フォルダを選択してください");
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        ApplyLogDirectoryChange(selectedPath);
    }

    private void ResetLogDirectory()
    {
        if (!IsCustomLogDirectory)
        {
            return;
        }

        ApplyLogDirectoryChange(null);
    }

    private void ApplyLogDirectoryChange(string? targetDirectory)
    {
        string currentDirectory = LogDirectoryPath;
        string defaultDirectory = _logConfigurationManager?.DefaultLogDirectory ?? _defaultLogDirectoryFallback;
        string rawTarget = string.IsNullOrWhiteSpace(targetDirectory)
            ? defaultDirectory
            : targetDirectory.Trim();

        if (AreSameDirectoryPaths(currentDirectory, rawTarget))
        {
            return;
        }

        if (_logConfigurationManager != null)
        {
            LogConfigurationApplyResult result = _logConfigurationManager.ApplyConfiguration(
                SelectedLogLevel,
                targetDirectory,
                fallbackToDefaultOnError: false);
            if (!result.Succeeded || result.UsedFallbackDirectory)
            {
                OnPropertyChanged(nameof(LogDirectoryPath));
                OnPropertyChanged(nameof(IsCustomLogDirectory));
                CommandManager.InvalidateRequerySuggested();
                _messageNotifier(
                    result.WarningMessage ?? "指定されたログ保存先フォルダへ書き込めませんでした。",
                    "ログ保存先の変更エラー",
                    true);
                return;
            }

            string? persistedPath = _logConfigurationManager.IsCustomLogDirectory ? result.EffectiveLogDirectory : null;
            _appSettings.LogDirectoryPath = persistedPath;
            _settingsService.UpdateSettings(settings => settings.LogDirectoryPath = persistedPath);
            OnPropertyChanged(nameof(LogDirectoryPath));
            OnPropertyChanged(nameof(IsCustomLogDirectory));
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        string? nextPersistedPath = string.IsNullOrWhiteSpace(targetDirectory) || AreSameDirectoryPaths(rawTarget, defaultDirectory)
            ? null
            : rawTarget;

        _appSettings.LogDirectoryPath = nextPersistedPath;
        _settingsService.UpdateSettings(settings => settings.LogDirectoryPath = nextPersistedPath);
        OnPropertyChanged(nameof(LogDirectoryPath));
        OnPropertyChanged(nameof(IsCustomLogDirectory));
        CommandManager.InvalidateRequerySuggested();
    }

    private static bool AreSameDirectoryPaths(string pathA, string pathB)
    {
        try
        {
            string normalizedA = NormalizeComparisonPath(pathA);
            string normalizedB = NormalizeComparisonPath(pathB);
            return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(
                pathA.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                pathB.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeComparisonPath(string path)
    {
        string full = Path.GetFullPath(path.Trim());
        string? root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void OpenLogFolder()
    {
        string targetDirectory = LogDirectoryPath;
        try
        {
            Directory.CreateDirectory(targetDirectory);
            _folderOpener(targetDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _messageNotifier(
                $"ログ保存先フォルダを開けませんでした: {ex.Message}",
                "ログフォルダを開くエラー",
                true);
        }
    }

    private static void OpenFolderInExplorer(string directoryPath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(directoryPath);
        using var process = System.Diagnostics.Process.Start(startInfo);
    }

    private void ResetListeningStats()
    {
        if (_listeningStatsService == null)
        {
            return;
        }

        bool confirmed = _resetConfirmationPrompt(
            "すべての再生履歴・リスニング統計・推移データが削除され、元に戻せません。\n（音楽ファイル本体・プレイリスト・お気に入りには影響しません）\n本当にリセットしますか？",
            "再生履歴・リスニング統計のリセット確認");
        if (!confirmed)
        {
            return;
        }

        bool succeeded = _listeningStatsService.ResetAllStats();
        if (!succeeded)
        {
            _messageNotifier(
                "再生履歴・リスニング統計データのリセットに失敗しました。ファイルが他のプロセスで使用中でないか確認してください。",
                "リセットエラー",
                true);
            return;
        }

        ListeningStatsReset?.Invoke();

        const string completionMessage = "すべての再生履歴およびリスニング統計データをリセットしました。";
        const string completionTitle = "リセット完了";
        if (_hasCustomMessageNotifier)
        {
            _messageNotifier(completionMessage, completionTitle, false);
        }
        else
        {
            MessageBox.Show(completionMessage, completionTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static bool ShowDefaultResetConfirmationPrompt(string message, string title)
    {
        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
