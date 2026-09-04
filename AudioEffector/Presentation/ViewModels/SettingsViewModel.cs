using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioEffector.Infrastructure.Windows;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Themes;


namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// アプリケーション設定画面のViewModel
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioService _audioService;
    private AppSettings _appSettings;

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
                _settingsService.SaveSettings(_appSettings);
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
                _settingsService.SaveSettings(_appSettings);
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
                _settingsService.SaveSettings(_appSettings);
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
                _settingsService.SaveSettings(_appSettings);

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
                _settingsService.SaveSettings(_appSettings);
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
                _settingsService.SaveSettings(_appSettings);
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
                _settingsService.SaveSettings(_appSettings);
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
                _settingsService.SaveSettings(_appSettings);
                _audioService?.UpdateAudioProperties(_appSettings.SampleRate, _appSettings.AudioBufferSizeMs);
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
            _settingsService.SaveSettings(_appSettings);
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
            _settingsService.SaveSettings(_appSettings);
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
            _settingsService.SaveSettings(_appSettings);
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
            _settingsService.SaveSettings(_appSettings);
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
            _settingsService.SaveSettings(_appSettings);
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
            _settingsService.SaveSettings(_appSettings);
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
            _settingsService.SaveSettings(_appSettings);
        }
    }

    /// <summary>
    /// 設定サービスとオーディオサービスを指定してインスタンスを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="audioService">オーディオサービス</param>
    public SettingsViewModel(ISettingsService settingsService, IAudioService audioService)
    {
        _settingsService = settingsService;
        _audioService = audioService;
        _appSettings = _settingsService.LoadSettings();

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
}
