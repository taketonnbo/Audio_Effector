using AudioEffector.Application.ApplicationServices;
using AudioEffector.Presentation.ViewModels;
using Microsoft.Win32;
using System;
using System.Windows.Input;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// フォルダーブラウズ機能を管理するViewModel。
/// フォルダー選択ダイアログの表示・設定保存を担当し、
/// ライブラリ読み込み処理はコールバック経由で親に委譲します。
/// </summary>
public class FolderViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly Action<string> _loadLibraryCallback;

    /// <summary>
    /// 音楽フォルダーを開くコマンドを取得します
    /// </summary>
    public ICommand OpenFolderCommand { get; }

    /// <summary>
    /// FolderViewModelのインスタンスを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="loadLibraryCallback">フォルダ選択後にライブラリを読み込むコールバック</param>
    public FolderViewModel(ISettingsService settingsService, Action<string> loadLibraryCallback)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _loadLibraryCallback = loadLibraryCallback ?? throw new ArgumentNullException(nameof(loadLibraryCallback));

        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    /// <summary>
    /// フォルダー選択ダイアログを開き、新しいライブラリパスを設定します。
    /// </summary>
    private void OpenFolder(object? obj)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            string selectedPath = dialog.FolderName;

            var settings = _settingsService.LoadSettings();
            settings.LastLibraryPath = selectedPath;
            _settingsService.SaveSettings(settings);

            _loadLibraryCallback(selectedPath);
        }
    }
}
