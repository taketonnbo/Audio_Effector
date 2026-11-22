using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using Microsoft.Win32;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// フォルダー選択および指定済みフォルダの差分再スキャン・ライブラリ同期を管理するViewModel。
/// </summary>
public class FolderViewModel : ViewModelBase, IDisposable
{
    private const string UnsetFolderDisplayText = "未設定（フォルダが選択されていません）";

    private readonly ISettingsService _settingsService;
    private readonly LibraryViewModel? _libraryViewModel;
    private readonly Action<string>? _loadLibraryCallback;
    private readonly Func<string?, string?> _selectFolderDialog;

    private string _currentFolderPath = string.Empty;
    private bool _isScanning;
    private double _scanProgress;
    private string _scanStatusText = string.Empty;
    private bool _disposed;

    /// <summary>
    /// 現在アプリに登録されているスキャン対象フォルダパスを取得または設定します
    /// </summary>
    public string CurrentFolderPath
    {
        get => _currentFolderPath;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _currentFolderPath, normalized))
            {
                OnPropertyChanged(nameof(HasCurrentFolderPath));
                OnPropertyChanged(nameof(DisplayFolderPath));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>
    /// スキャン対象フォルダが設定されているかどうかを取得します
    /// </summary>
    public bool HasCurrentFolderPath => !string.IsNullOrWhiteSpace(_currentFolderPath);

    /// <summary>
    /// 画面表示用のフォルダパス文字列（未設定時は案内文言）を取得します
    /// </summary>
    public string DisplayFolderPath => HasCurrentFolderPath ? _currentFolderPath : UnsetFolderDisplayText;

    /// <summary>
    /// 現在スキャン処理を実行中かどうかを取得します
    /// </summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsIndeterminateProgress));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>
    /// スキャン進捗率（0.0〜1.0）を取得します
    /// </summary>
    public double ScanProgress
    {
        get => _scanProgress;
        private set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref _scanProgress, clamped))
            {
                OnPropertyChanged(nameof(ScanProgressPercent));
                OnPropertyChanged(nameof(IsIndeterminateProgress));
            }
        }
    }

    /// <summary>
    /// スキャン進捗のパーセント値（0〜100）を取得します
    /// </summary>
    public int ScanProgressPercent => (int)Math.Round(_scanProgress * 100.0);

    /// <summary>
    /// プログレスバーを不確定（Indeterminate）アニメーション表示にするかどうかを取得します
    /// </summary>
    public bool IsIndeterminateProgress => _isScanning && _scanProgress <= 0.0;

    /// <summary>
    /// スキャン状態または同期結果を表すステータステキストを取得します
    /// </summary>
    public string ScanStatusText
    {
        get => _scanStatusText;
        private set => SetProperty(ref _scanStatusText, value ?? string.Empty);
    }

    /// <summary>
    /// フォルダ選択のみを行うコマンド（SELECT FOLDER）を取得します
    /// </summary>
    public ICommand SelectFolderCommand { get; }

    /// <summary>
    /// フォルダ選択コマンド（既存バインディング互換エイリアス）を取得します
    /// </summary>
    public ICommand OpenFolderCommand { get; }

    /// <summary>
    /// フォルダ選択ダイアログを開き、新たに指定したフォルダをスキャン・登録するコマンドを取得します
    /// </summary>
    public ICommand ScanNewFolderCommand { get; }

    /// <summary>
    /// 現在指定済みのフォルダを再スキャンし、ライブラリを最新状態へ同期するコマンドを取得します
    /// </summary>
    public ICommand RescanCurrentFolderCommand { get; }

    /// <summary>
    /// LibraryViewModelと連携してFolderViewModelを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="libraryViewModel">ライブラリViewModel</param>
    /// <param name="selectFolderDialog">フォルダ選択ダイアログ関数（テスト用差し替え可、省略時はOpenFolderDialogを使用）</param>
    public FolderViewModel(
        ISettingsService settingsService,
        LibraryViewModel libraryViewModel,
        Func<string?, string?>? selectFolderDialog = null)
        : this(
            settingsService,
            libraryViewModel ?? throw new ArgumentNullException(nameof(libraryViewModel)),
            loadLibraryCallback: null,
            selectFolderDialog)
    {
    }

    /// <summary>
    /// コールバックデリゲートを指定してFolderViewModelを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="loadLibraryCallback">フォルダスキャンを実行するコールバック</param>
    /// <param name="selectFolderDialog">フォルダ選択ダイアログ関数（テスト用差し替え可、省略時はOpenFolderDialogを使用）</param>
    public FolderViewModel(
        ISettingsService settingsService,
        Action<string> loadLibraryCallback,
        Func<string?, string?>? selectFolderDialog = null)
        : this(
            settingsService,
            libraryViewModel: null,
            loadLibraryCallback ?? throw new ArgumentNullException(nameof(loadLibraryCallback)),
            selectFolderDialog)
    {
    }

    private FolderViewModel(
        ISettingsService settingsService,
        LibraryViewModel? libraryViewModel,
        Action<string>? loadLibraryCallback,
        Func<string?, string?>? selectFolderDialog)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _libraryViewModel = libraryViewModel;
        _loadLibraryCallback = loadLibraryCallback;
        _selectFolderDialog = selectFolderDialog ?? ShowOpenFolderDialog;

        var initialSettings = _settingsService.LoadSettings();
        _currentFolderPath = initialSettings?.LastLibraryPath?.Trim() ?? string.Empty;

        if (_libraryViewModel != null)
        {
            _isScanning = _libraryViewModel.IsLoading;
            _scanProgress = _libraryViewModel.ScanProgress;
            _libraryViewModel.PropertyChanged += OnLibraryViewModelPropertyChanged;
            _libraryViewModel.LibrarySynced += OnLibrarySynced;
        }

        SelectFolderCommand = new RelayCommand(_ => SelectFolderOnly(), _ => !IsScanning);
        OpenFolderCommand = SelectFolderCommand;
        ScanNewFolderCommand = new RelayCommand(_ => ScanNewFolder(), _ => !IsScanning);
        RescanCurrentFolderCommand = new RelayCommand(_ => RescanCurrentFolder(), _ => HasCurrentFolderPath && !IsScanning);
    }

    /// <summary>
    /// フォルダ選択ダイアログを開き、選択されたパスを設定へ保存します（スキャンは実行しません）。
    /// </summary>
    public void SelectFolderOnly()
    {
        if (IsScanning)
        {
            return;
        }

        string? selectedPath = _selectFolderDialog(HasCurrentFolderPath ? CurrentFolderPath : null);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        PersistSelectedFolderPath(selectedPath);
        ScanStatusText = "スキャン対象フォルダを変更しました。「指定済みのフォルダをスキャン」でライブラリと同期できます。";
    }

    /// <summary>
    /// フォルダ選択ダイアログを開き、新たに指定されたフォルダを保存して即時スキャンを実行します。
    /// </summary>
    public void ScanNewFolder()
    {
        if (IsScanning)
        {
            return;
        }

        string? selectedPath = _selectFolderDialog(HasCurrentFolderPath ? CurrentFolderPath : null);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        PersistSelectedFolderPath(selectedPath);
        ExecuteFolderScan(CurrentFolderPath);
    }

    /// <summary>
    /// 現在指定済みのフォルダを再スキャンし、追加・削除された楽曲やアルバムをライブラリへ差分同期します。
    /// </summary>
    public void RescanCurrentFolder()
    {
        if (IsScanning || !HasCurrentFolderPath)
        {
            return;
        }

        if (!Directory.Exists(CurrentFolderPath))
        {
            ScanStatusText = "指定済みのフォルダが見つかりません。フォルダの存在を確認するか、新しくフォルダを選択してください。";
            return;
        }

        ExecuteFolderScan(CurrentFolderPath);
    }

    private void PersistSelectedFolderPath(string selectedPath)
    {
        string normalized = selectedPath.Trim();
        var settings = _settingsService.LoadSettings();
        settings.LastLibraryPath = normalized;
        _settingsService.SaveSettings(settings);
        CurrentFolderPath = normalized;
    }

    private void ExecuteFolderScan(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        ScanStatusText = "フォルダをスキャンしています...";

        if (_libraryViewModel != null)
        {
            _libraryViewModel.LoadLibrary(folderPath);
        }
        else if (_loadLibraryCallback != null)
        {
            _loadLibraryCallback(folderPath);
            ScanStatusText = "スキャンを実行しました。";
        }
    }

    private void OnLibraryViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _libraryViewModel == null)
        {
            return;
        }

        if (e.PropertyName == nameof(LibraryViewModel.IsLoading) ||
            e.PropertyName == nameof(LibraryViewModel.IsScanning))
        {
            IsScanning = _libraryViewModel.IsLoading;
            if (IsScanning && string.IsNullOrWhiteSpace(ScanStatusText))
            {
                ScanStatusText = "フォルダをスキャンしています...";
            }
        }
        else if (e.PropertyName == nameof(LibraryViewModel.ScanProgress))
        {
            ScanProgress = _libraryViewModel.ScanProgress;
        }
        else if (e.PropertyName == nameof(LibraryViewModel.CurrentLibraryPath))
        {
            if (!string.IsNullOrWhiteSpace(_libraryViewModel.CurrentLibraryPath))
            {
                CurrentFolderPath = _libraryViewModel.CurrentLibraryPath;
            }
        }
    }

    private void OnLibrarySynced(LibrarySyncResult result)
    {
        if (_disposed)
        {
            return;
        }

        if (result.AddedCount == 0 && result.RemovedCount == 0 && result.UpdatedCount == 0)
        {
            ScanStatusText = string.Format(
                CultureInfo.InvariantCulture,
                "同期完了：変更はありません（全 {0:N0} 曲 / {1:N0} アルバム）",
                result.TotalTrackCount,
                result.TotalAlbumCount);
        }
        else if (result.UpdatedCount > 0)
        {
            ScanStatusText = string.Format(
                CultureInfo.InvariantCulture,
                "同期完了：追加 +{0:N0} 曲 / 削除 -{1:N0} 曲 / 更新 {2:N0} 曲（全 {3:N0} 曲 / {4:N0} アルバム）",
                result.AddedCount,
                result.RemovedCount,
                result.UpdatedCount,
                result.TotalTrackCount,
                result.TotalAlbumCount);
        }
        else
        {
            ScanStatusText = string.Format(
                CultureInfo.InvariantCulture,
                "同期完了：追加 +{0:N0} 曲 / 削除 -{1:N0} 曲（全 {2:N0} 曲 / {3:N0} アルバム）",
                result.AddedCount,
                result.RemovedCount,
                result.TotalTrackCount,
                result.TotalAlbumCount);
        }
    }

    private static string? ShowOpenFolderDialog(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>
    /// イベント購読を解除しリソースを解放します
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// マネージドリソースを解放します
    /// </summary>
    /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing && _libraryViewModel != null)
            {
                _libraryViewModel.PropertyChanged -= OnLibraryViewModelPropertyChanged;
                _libraryViewModel.LibrarySynced -= OnLibrarySynced;
            }
            _disposed = true;
        }
    }
}
