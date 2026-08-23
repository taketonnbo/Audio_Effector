using System;
using System.Collections.ObjectModel;

namespace AudioEffector.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
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

        public SettingsViewModel()
        {
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
