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

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
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
