using System;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using Microsoft.Win32;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// フォルダー選択ダイアログおよびローカルフォルダーナビゲーションを担当するViewModel
/// </summary>
public class FolderViewModel : ViewModelBase
{
    private readonly ISettingsService? _settingsService;
    private readonly Action<string>? _onFolderSelected;
    private string _currentFolderPath = string.Empty;

    /// <summary>
    /// 現在選択されているフォルダーパスを取得または設定します
    /// </summary>
    public string CurrentFolderPath
    {
        get => _currentFolderPath;
        set => SetProperty(ref _currentFolderPath, value);
    }

    /// <summary>
    /// フォルダー選択ダイアログを開き、新しいライブラリパスを設定するコマンド
    /// </summary>
    public ICommand OpenFolderCommand { get; }

    /// <summary>
    /// FolderViewModelを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス（null許容）</param>
    /// <param name="onFolderSelected">フォルダー選択時コールバック（null許容）</param>
    public FolderViewModel(ISettingsService? settingsService = null, Action<string>? onFolderSelected = null)
    {
        _settingsService = settingsService;
        _onFolderSelected = onFolderSelected;

        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
    }

    /// <summary>
    /// フォルダー選択ダイアログを開き、フォルダーを選択します
    /// </summary>
    public void OpenFolder()
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            string selectedPath = dialog.FolderName;
            CurrentFolderPath = selectedPath;

            if (_settingsService != null)
            {
                var settings = _settingsService.LoadSettings();
                settings.LastLibraryPath = selectedPath;
                _settingsService.SaveSettings(settings);
            }

            _onFolderSelected?.Invoke(selectedPath);
        }
    }
}
