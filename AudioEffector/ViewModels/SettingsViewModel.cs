using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioEffector.Services;

namespace AudioEffector.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IAudioService _audioService;
        private AppSettings _appSettings;

        public ObservableCollection<string> Categories { get; }

        private string _selectedCategory;
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

        public IReadOnlyList<ThemeType> AvailableThemes { get; } = new[]
        {
            ThemeType.Light,
            ThemeType.Dark,
            ThemeType.System
        };

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

        public IReadOnlyList<string> AvailableTopmostBehaviors { get; } = new[]
        {
            "常に最前面に表示",
            "表示時のみ最前面に表示",
            "最前面に表示しない"
        };

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
                    var miniPlayer = System.Windows.Application.Current.Windows.OfType<Views.MiniPlayerWindow>().FirstOrDefault();
                    if (miniPlayer != null)
                    {
                        miniPlayer.UpdateTopmostBehavior(newValue);
                    }
                }
            }
        }

        public IReadOnlyList<int> AvailableSampleRates { get; } = new[] { 44100, 48000, 88200, 96000, 192000 };
        public IReadOnlyList<int> AvailableBufferSizes { get; } = new[] { 50, 100, 200, 300, 500 };

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
}
