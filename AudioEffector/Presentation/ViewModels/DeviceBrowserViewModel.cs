using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Presentation.Views;
using MediaDevices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 外部デバイスのブラウズ・楽曲転送機能を管理するViewModel。
/// DeviceSyncView の DataContext として直接バインドされます。
/// MainViewModel から分離されたデバイス同期ロジックの完全な実装を担当します。
/// </summary>
public class DeviceBrowserViewModel : ViewModelBase, IDisposable
{
    private readonly Func<IReadOnlyList<Album>> _getAlbums;
    private readonly Action _onCheckDeviceAlbumsRequested;
    private readonly ISettingsService? _settingsService;
    private readonly IAudioTranscodeService _audioTranscodeService;
    private readonly Func<string, long> _getFileSizeBytes;
    private readonly Func<MainViewModel.DeviceViewModel, string, (bool HasInfo, long TotalBytes, long FreeBytes)> _readDeviceStorageSpace;
    private readonly Func<MainViewModel.DeviceViewModel, IMtpDeviceClient?> _resolveMtpClient;
    private readonly Action<string, string> _showMessage;
    private readonly Dictionary<string, long> _fileSizeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Album> _subscribedAlbums = new();
    private readonly HashSet<Track> _subscribedTracks = new();
    private Dictionary<string, string>? _defaultFoldersCache;
    private CancellationTokenSource? _checkDeviceAlbumsCts;
    private CancellationTokenSource? _storageRefreshCts;
    private bool _suppressSelectionSummaryUpdates;

    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".mp4", ".aac", ".ogg", ".wma", ".aiff", ".aif", ".alac", ".opus", ".dsf", ".dff"
    };

    /// <summary>
    /// 接続されている外部デバイス一覧を取得または設定します
    /// </summary>
    public ObservableCollection<MainViewModel.DeviceViewModel> RemovableDrives { get; set; } = new ObservableCollection<MainViewModel.DeviceViewModel>();

    /// <summary>
    /// 接続中の外部デバイスが 1 台以上存在するかどうかを取得します
    /// </summary>
    public bool HasConnectedDevices => RemovableDrives.Count > 0;

    private int _selectedAlbumCount;
    /// <summary>
    /// 転送対象として選択されているアルバム数を取得します
    /// </summary>
    public int SelectedAlbumCount
    {
        get => _selectedAlbumCount;
        private set => SetProperty(ref _selectedAlbumCount, value);
    }

    private int _selectedTrackCount;
    /// <summary>
    /// 転送対象として選択されている未転送トラック数を取得します
    /// </summary>
    public int SelectedTrackCount
    {
        get => _selectedTrackCount;
        private set => SetProperty(ref _selectedTrackCount, value);
    }

    private long _selectedTotalBytes;
    /// <summary>
    /// 転送対象として選択されている楽曲の合計ファイルサイズ（バイト）を取得します
    /// </summary>
    public long SelectedTotalBytes
    {
        get => _selectedTotalBytes;
        private set => SetProperty(ref _selectedTotalBytes, value);
    }

    private bool _isInsufficientSpace;
    /// <summary>
    /// 選択中楽曲の合計サイズが接続機器の空き容量を超過しているかどうかを取得します
    /// </summary>
    public bool IsInsufficientSpace
    {
        get => _isInsufficientSpace;
        private set => SetProperty(ref _isInsufficientSpace, value);
    }

    private string _selectedSizeSummaryText = "選択中: 0 曲";
    /// <summary>
    /// 選択中楽曲の件数・合計サイズ・空き容量判定のサマリ文字列を取得します
    /// </summary>
    public string SelectedSizeSummaryText
    {
        get => _selectedSizeSummaryText;
        private set => SetProperty(ref _selectedSizeSummaryText, value);
    }

    private bool _isCurrentFolderDefault;
    /// <summary>
    /// 現在表示中のフォルダが選択中機器の既定転送先フォルダとして登録されているかどうかを取得します
    /// </summary>
    public bool IsCurrentFolderDefault
    {
        get => _isCurrentFolderDefault;
        private set => SetProperty(ref _isCurrentFolderDefault, value);
    }

    private string _currentDeviceDefaultFolderPath = string.Empty;
    /// <summary>
    /// 選択中機器に登録されている既定転送先フォルダパス（未登録時は空文字）を取得します
    /// </summary>
    public string CurrentDeviceDefaultFolderPath
    {
        get => _currentDeviceDefaultFolderPath;
        private set
        {
            if (SetProperty(ref _currentDeviceDefaultFolderPath, value))
            {
                OnPropertyChanged(nameof(HasDefaultFolderForSelectedDevice));
            }
        }
    }

    /// <summary>
    /// 選択中機器に既定転送先フォルダが登録されているかどうかを取得します
    /// </summary>
    public bool HasDefaultFolderForSelectedDevice => !string.IsNullOrEmpty(_currentDeviceDefaultFolderPath);

    private string _defaultFolderStatusMessage = string.Empty;
    /// <summary>
    /// 既定フォルダ自動遷移時のフォールバック通知メッセージを取得します
    /// </summary>
    public string DefaultFolderStatusMessage
    {
        get => _defaultFolderStatusMessage;
        private set
        {
            if (SetProperty(ref _defaultFolderStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasDefaultFolderStatusMessage));
            }
        }
    }

    /// <summary>
    /// 既定フォルダに関するステータス通知メッセージが存在するかどうかを取得します
    /// </summary>
    public bool HasDefaultFolderStatusMessage => !string.IsNullOrEmpty(_defaultFolderStatusMessage);

    private MainViewModel.DeviceViewModel? _selectedDevice;

    /// <summary>
    /// 現在選択されている同期対象デバイス。
    /// 変更時にデバイスへの接続や初期ディレクトリ読み込みを行います。
    /// </summary>
    public MainViewModel.DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice != value)
            {
                // Disconnect previous MTP device if applicable
                if (_selectedDevice?.Type == MainViewModel.DeviceType.MTP)
                {
                    var previousMtpClient = _resolveMtpClient(_selectedDevice);
                    if (previousMtpClient != null && previousMtpClient.IsConnected)
                    {
                        try { previousMtpClient.Disconnect(); } catch { }
                    }
                }

                _selectedDevice = value;
                OnPropertyChanged();

                if (_selectedDevice != null)
                {
                    DefaultFolderStatusMessage = string.Empty;

                    if (_selectedDevice.Type == MainViewModel.DeviceType.MTP)
                    {
                        var mtpClient = _resolveMtpClient(_selectedDevice);
                        if (mtpClient != null)
                        {
                            try
                            {
                                mtpClient.Connect();
                                string initialPath = ResolveInitialDirectoryPath(
                                    _selectedDevice,
                                    @"\",
                                    p =>
                                    {
                                        try { return mtpClient.DirectoryExists(p); }
                                        catch { return false; }
                                    },
                                    out bool fellBack);
                                if (fellBack)
                                {
                                    DefaultFolderStatusMessage = "記憶していた既定フォルダが見つからないため、ルートフォルダを表示しました。";
                                }

                                LoadDeviceDirectories(initialPath);
                            }
                            catch (Exception ex)
                            {
                                DeviceDirectories.Clear();
                                CurrentDevicePath = string.Empty;
                                UpdateDefaultFolderState();
                                CheckDeviceAlbums();
                                _showMessage($"Failed to connect to device: {ex.Message}", string.Empty);
                            }
                        }
                    }
                    else if (_selectedDevice.Type == MainViewModel.DeviceType.FileSystem)
                    {
                        string rootPath = _selectedDevice.Drive?.RootDirectory.FullName ?? _selectedDevice.RootPath;
                        if (!string.IsNullOrEmpty(rootPath))
                        {
                            string initialPath = ResolveInitialDirectoryPath(
                                _selectedDevice,
                                rootPath,
                                p =>
                                {
                                    try { return Directory.Exists(p); }
                                    catch { return false; }
                                },
                                out bool fellBack);
                            if (fellBack)
                            {
                                DefaultFolderStatusMessage = "記憶していた既定フォルダが見つからないため、ルートフォルダを表示しました。";
                            }

                            LoadDeviceDirectories(initialPath);
                        }
                    }

                    UpdateDefaultFolderState();
                    _ = RefreshSelectedDeviceStorageAsync();
                }
                else
                {
                    CancelPendingStorageRefresh();
                    DefaultFolderStatusMessage = string.Empty;
                    DeviceDirectories.Clear();
                    CurrentDevicePath = string.Empty;
                    UpdateDefaultFolderState();
                    CheckDeviceAlbums();
                    UpdateSelectionSummary();
                }
            }
        }
    }

    private double _transferProgress;
    /// <summary>
    /// ファイル転送の進捗状況（0-100）。
    /// </summary>
    public double TransferProgress
    {
        get => _transferProgress;
        set
        {
            _transferProgress = value;
            OnPropertyChanged();
        }
    }

    private bool _isTransferring;
    /// <summary>
    /// ファイル転送中かどうか。
    /// </summary>
    public bool IsTransferring
    {
        get => _isTransferring;
        set
        {
            _isTransferring = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// デバイス内のディレクトリアイテム一覧を取得または設定します
    /// </summary>
    public ObservableCollection<MainViewModel.DirectoryItem> DeviceDirectories { get; set; } = new ObservableCollection<MainViewModel.DirectoryItem>();

    private string _currentDevicePath = string.Empty;
    /// <summary>
    /// 現在デバイス上で表示しているパス
    /// </summary>
    public string CurrentDevicePath
    {
        get => _currentDevicePath;
        set
        {
            if (_currentDevicePath != value)
            {
                _currentDevicePath = value;
                OnPropertyChanged();
                UpdateDefaultFolderState();
            }
        }
    }

    private MainViewModel.DirectoryItem? _selectedDeviceDirectory;

    /// <summary>
    /// 選択されているディレクトリアイテムを取得または設定します
    /// </summary>
    public MainViewModel.DirectoryItem? SelectedDeviceDirectory
    {
        get => _selectedDeviceDirectory;
        set
        {
            _selectedDeviceDirectory = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 選択された楽曲を外部デバイスへ転送するコマンドを取得します
    /// </summary>
    public ICommand TransferSelectedCommand { get; }

    /// <summary>
    /// 接続中の外部ドライブ一覧を再検出するコマンドを取得します
    /// </summary>
    public ICommand RefreshDrivesCommand { get; }

    /// <summary>
    /// デバイス内の指定ディレクトリへ移動するコマンドを取得します
    /// </summary>
    public ICommand NavigateDirectoryCommand { get; }

    /// <summary>
    /// デバイス内の親ディレクトリへ移動するコマンドを取得します
    /// </summary>
    public ICommand NavigateUpCommand { get; }

    /// <summary>
    /// ディレクトリ一覧を再読み込みするコマンドを取得します
    /// </summary>
    public ICommand RefreshDirectoryCommand { get; }

    /// <summary>
    /// 外部デバイス管理ダイアログを表示するコマンドを取得します
    /// </summary>
    public ICommand ShowDeviceManagerCommand { get; }

    /// <summary>
    /// 現在表示中のフォルダを選択中機器の既定転送先フォルダとして保存するコマンドを取得します
    /// </summary>
    public ICommand SetDefaultFolderCommand { get; }

    /// <summary>
    /// 選択中機器の既定転送先フォルダ設定を解除するコマンドを取得します
    /// </summary>
    public ICommand ClearDefaultFolderCommand { get; }

    /// <summary>
    /// 現在表示中のフォルダに対する既定転送先フォルダ設定の登録／解除を切り替えるコマンドを取得します
    /// </summary>
    public ICommand ToggleDefaultFolderCommand { get; }

    /// <summary>
    /// PCアルバム一覧（MainViewModelから委譲されたAlbumsへの読み取り専用参照）
    /// </summary>
    public ObservableCollection<Album> Albums => _albumsRef;
    private readonly ObservableCollection<Album> _albumsRef;

    /// <summary>
    /// DeviceBrowserViewModelのインスタンスを初期化します
    /// </summary>
    /// <param name="albumsRef">MainViewModelのAlbumsコレクションへの直接参照</param>
    /// <param name="getAlbums">PCアルバム一覧を取得するコールバック</param>
    /// <param name="onCheckDeviceAlbumsRequested">デバイスアルバムチェック要求コールバック</param>
    /// <param name="getFileSizeBytes">トラックのファイルサイズ取得関数（テスト差し替え用、省略時はFileInfoを使用）</param>
    /// <param name="readDeviceStorageSpace">接続機器の容量取得関数（テスト差し替え用、省略時は実ストレージ読取を使用）</param>
    /// <param name="settingsService">アプリケーション設定永続化サービス（既定フォルダ記憶・形式変換設定用）</param>
    /// <param name="audioTranscodeService">転送時の音声フォーマット変換サービス（省略時は変換なしのパススルー動作）</param>
    public DeviceBrowserViewModel(
        ObservableCollection<Album> albumsRef,
        Func<IReadOnlyList<Album>> getAlbums,
        Action onCheckDeviceAlbumsRequested,
        Func<string, long>? getFileSizeBytes = null,
        Func<MainViewModel.DeviceViewModel, string, (bool HasInfo, long TotalBytes, long FreeBytes)>? readDeviceStorageSpace = null,
        ISettingsService? settingsService = null,
        IAudioTranscodeService? audioTranscodeService = null)
        : this(
            albumsRef,
            getAlbums,
            onCheckDeviceAlbumsRequested,
            getFileSizeBytes,
            readDeviceStorageSpace,
            settingsService,
            audioTranscodeService,
            resolveMtpClient: null,
            showMessage: null)
    {
    }

    /// <summary>
    /// テスト差し替え用の MTP クライアント解決・メッセージ通知デリゲートを含む内部コンストラクタ
    /// </summary>
    /// <param name="albumsRef">MainViewModelのAlbumsコレクションへの直接参照</param>
    /// <param name="getAlbums">PCアルバム一覧を取得するコールバック</param>
    /// <param name="onCheckDeviceAlbumsRequested">デバイスアルバムチェック要求コールバック</param>
    /// <param name="getFileSizeBytes">トラックのファイルサイズ取得関数</param>
    /// <param name="readDeviceStorageSpace">接続機器の容量取得関数</param>
    /// <param name="settingsService">アプリケーション設定永続化サービス</param>
    /// <param name="audioTranscodeService">転送時の音声フォーマット変換サービス</param>
    /// <param name="resolveMtpClient">MTPデバイスクライアント解決関数</param>
    /// <param name="showMessage">ユーザー通知メッセージ表示関数</param>
    internal DeviceBrowserViewModel(
        ObservableCollection<Album> albumsRef,
        Func<IReadOnlyList<Album>> getAlbums,
        Action onCheckDeviceAlbumsRequested,
        Func<string, long>? getFileSizeBytes,
        Func<MainViewModel.DeviceViewModel, string, (bool HasInfo, long TotalBytes, long FreeBytes)>? readDeviceStorageSpace,
        ISettingsService? settingsService,
        IAudioTranscodeService? audioTranscodeService,
        Func<MainViewModel.DeviceViewModel, IMtpDeviceClient?>? resolveMtpClient,
        Action<string, string>? showMessage)
    {
        _albumsRef = albumsRef ?? throw new ArgumentNullException(nameof(albumsRef));
        _getAlbums = getAlbums ?? throw new ArgumentNullException(nameof(getAlbums));
        _onCheckDeviceAlbumsRequested = onCheckDeviceAlbumsRequested ?? throw new ArgumentNullException(nameof(onCheckDeviceAlbumsRequested));
        _getFileSizeBytes = getFileSizeBytes ?? DefaultGetFileSizeBytes;
        _readDeviceStorageSpace = readDeviceStorageSpace ?? ReadDeviceStorageSpace;
        _settingsService = settingsService;
        _audioTranscodeService = audioTranscodeService ?? PassthroughAudioTranscodeService.Instance;
        _resolveMtpClient = resolveMtpClient ?? DefaultResolveMtpClient;
        _showMessage = showMessage ?? DefaultShowMessage;

        RemovableDrives.CollectionChanged += OnRemovableDrivesCollectionChanged;
        _albumsRef.CollectionChanged += OnAlbumsCollectionChanged;
        SyncAlbumSubscriptions(_albumsRef);
        UpdateSelectionSummary();

        RefreshDrivesCommand = new RelayCommand(o => RefreshDrives());
        TransferSelectedCommand = new RelayCommand(o => TransferSelected());
        NavigateDirectoryCommand = new RelayCommand(o => NavigateDirectory(o as MainViewModel.DirectoryItem));
        NavigateUpCommand = new RelayCommand(o => NavigateUp());
        RefreshDirectoryCommand = new RelayCommand(o => LoadDeviceDirectories(CurrentDevicePath));
        ShowDeviceManagerCommand = new RelayCommand(o => ShowDeviceManager());
        SetDefaultFolderCommand = new RelayCommand(o => SetCurrentFolderAsDefault());
        ClearDefaultFolderCommand = new RelayCommand(o => ClearDefaultFolderForSelectedDevice());
        ToggleDefaultFolderCommand = new RelayCommand(o => ToggleDefaultFolderForCurrentPath());
    }

    /// <summary>
    /// 接続されているリムーバブルドライブとMTPデバイスを検出し、リストを更新します。
    /// </summary>
    public void RefreshDrives()
    {
        if (IsTransferring) return; // 転送中の切断を防止

        RemovableDrives.Clear();

        // Add File System Drives
        // ファイルシステムドライブの追加
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).ToList();
        foreach (var drive in drives)
        {
            var deviceVm = new MainViewModel.DeviceViewModel
            {
                Name = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.VolumeLabel} ({drive.Name})"
                    : $"リムーバブルディスク ({drive.Name})",
                Type = MainViewModel.DeviceType.FileSystem,
                Drive = drive,
                RootPath = drive.RootDirectory.FullName
            };
            deviceVm.DeviceKey = ResolveDeviceKey(deviceVm);
            TryPopulateFileSystemStorage(deviceVm);
            RemovableDrives.Add(deviceVm);
        }

        // Add MTP Devices
        // MTPデバイスの追加
        try
        {
            var devices = MediaDevice.GetDevices();
            foreach (var device in devices)
            {
                var deviceVm = new MainViewModel.DeviceViewModel
                {
                    Name = device.FriendlyName,
                    Type = MainViewModel.DeviceType.MTP,
                    MtpDevice = device,
                    RootPath = @"\" // MTP root
                };
                deviceVm.DeviceKey = ResolveDeviceKey(deviceVm);
                RemovableDrives.Add(deviceVm);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error listing MTP devices: {ex.Message}");
        }

        SelectedDevice = RemovableDrives.FirstOrDefault();
    }

    /// <summary>
    /// 指定されたパスのディレクトリとファイルをロードし、デバイスディレクトリリストを更新します。
    /// </summary>
    /// <param name="path">ロード対象のディレクトリパス。</param>
    private void LoadDeviceDirectories(string path)
    {
        LoadDeviceDirectoriesCore(path);
        CheckDeviceAlbums();
        _ = RefreshSelectedDeviceStorageAsync();
    }

    /// <summary>
    /// 指定されたパスのディレクトリとファイルをデバイスディレクトリリストへ反映します（再走査は呼び出し元で制御します）。
    /// </summary>
    /// <param name="path">ロード対象のディレクトリパス。</param>
    /// <param name="showErrorMessage">読み込み失敗時にエラー通知ダイアログを表示する場合はtrue</param>
    private void LoadDeviceDirectoriesCore(string path, bool showErrorMessage = true)
    {
        try
        {
            DeviceDirectories.Clear();
            CurrentDevicePath = path;

            if (SelectedDevice == null) return;

            if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem)
            {
                if (Directory.Exists(path))
                {
                    // Add Directories
                    // ディレクトリの追加
                    var dirs = Directory.GetDirectories(path);
                    foreach (var dir in dirs)
                    {
                        DeviceDirectories.Add(new MainViewModel.DirectoryItem
                        {
                            Name = Path.GetFileName(dir),
                            FullPath = dir,
                            IsFolder = true
                        });
                    }

                    // Add Files
                    // ファイルの追加
                    var files = Directory.GetFiles(path);
                    foreach (var file in files)
                    {
                        DeviceDirectories.Add(new MainViewModel.DirectoryItem
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file,
                            IsFolder = false
                        });
                    }
                }
            }
            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP)
            {
                var mtpClient = _resolveMtpClient(SelectedDevice);
                if (mtpClient != null && mtpClient.IsConnected)
                {
                    // Add Directories
                    var dirs = mtpClient.GetDirectories(path);
                    foreach (var dir in dirs)
                    {
                        DeviceDirectories.Add(new MainViewModel.DirectoryItem
                        {
                            Name = Path.GetFileName(dir),
                            FullPath = dir,
                            IsFolder = true
                        });
                    }

                    // Add Files
                    // ファイルの追加
                    var files = mtpClient.GetFiles(path);
                    foreach (var file in files)
                    {
                        DeviceDirectories.Add(new MainViewModel.DirectoryItem
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file,
                            IsFolder = false
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading directories: {ex.Message}");
            if (showErrorMessage)
            {
                _showMessage($"Error loading directory: {ex.Message}", string.Empty);
            }
        }
    }

    /// <summary>
    /// ライブラリ内のアルバム・楽曲がデバイス上の指定フォルダ配下に存在するかどうかを高速インデックス化して判定し、ステータスを更新します。
    /// </summary>
    private void CheckDeviceAlbums()
    {
        _ = CheckDeviceAlbumsAsync();
    }

    /// <summary>
    /// 指定フォルダ配下の既存音声ファイルを非同期でインデックス化し、曲単位・アルバム単位の IsOnDevice 状態を更新します。
    /// </summary>
    /// <param name="externalCancellationToken">外部キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    internal async Task CheckDeviceAlbumsAsync(CancellationToken externalCancellationToken = default)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        var previousCts = Interlocked.Exchange(ref _checkDeviceAlbumsCts, linkedCts);
        if (previousCts != null)
        {
            try { previousCts.Cancel(); } catch (ObjectDisposedException) { }
            previousCts.Dispose();
        }

        var ct = linkedCts.Token;

        var selectedDevice = SelectedDevice;
        var currentPath = CurrentDevicePath;
        var albums = _getAlbums();

        if (selectedDevice == null || string.IsNullOrEmpty(currentPath))
        {
            RunOnUiThread(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                ApplyDeviceTrackIndexWithBatchSummary(albums, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            });
            return;
        }

        try
        {
            bool recursive = !IsDeviceRootPath(selectedDevice, currentPath);
            var knownTargetFolders = recursive ? null : CollectKnownTrackFolderTargets(albums);

            var index = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (selectedDevice.Type == MainViewModel.DeviceType.FileSystem)
                {
                    return BuildFileSystemAudioIndex(currentPath, recursive, knownTargetFolders, ct);
                }

                if (selectedDevice.Type == MainViewModel.DeviceType.MTP)
                {
                    var mtpClient = _resolveMtpClient(selectedDevice);
                    if (mtpClient != null && mtpClient.IsConnected)
                    {
                        return BuildMtpAudioIndex(mtpClient, currentPath, recursive, knownTargetFolders, ct);
                    }
                }

                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                ApplyDeviceTrackIndexWithBatchSummary(albums, index);
            });
        }
        catch (OperationCanceledException)
        {
            // 新しいフォルダ遷移・再スキャンによりキャンセルされた場合は無視
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking device albums: {ex.Message}");
        }
    }

    /// <summary>
    /// 重複判定用にファイルパスまたはファイル名から拡張子を除去・正規化したキーを生成します。
    /// </summary>
    /// <param name="fileNameOrPath">ファイル名またはファイルパス</param>
    /// <returns>正規化された重複判定キー（空文字の場合は判定不可）</returns>
    internal static string NormalizeTrackDuplicateKey(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return string.Empty;
        }

        string? nameWithoutExt = Path.GetFileNameWithoutExtension(fileNameOrPath);
        return string.IsNullOrWhiteSpace(nameWithoutExt) ? string.Empty : nameWithoutExt.Trim();
    }

    /// <summary>
    /// 指定ルートパスからの相対パスと拡張子除去済みファイル名を組み合わせた重複判定キーを生成します。
    /// </summary>
    /// <param name="rootPath">走査基準フォルダパス</param>
    /// <param name="fullFilePath">デバイス上のフルファイルパス</param>
    /// <returns>正規化された相対パス基準キー（例: Artist\Album\01_Song または 01_Song）</returns>
    internal static string BuildDeviceRelativeDuplicateKey(string rootPath, string fullFilePath)
    {
        string fileKey = NormalizeTrackDuplicateKey(fullFilePath);
        if (string.IsNullOrEmpty(fileKey))
        {
            return string.Empty;
        }

        string normalizedRoot = (rootPath ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
        string normalizedFull = (fullFilePath ?? string.Empty).Replace('/', '\\');
        string relativeDir = string.Empty;

        int lastSep = normalizedFull.LastIndexOf('\\');
        if (lastSep >= 0)
        {
            string fullDir = normalizedFull.Substring(0, lastSep).TrimEnd('\\');
            if (string.IsNullOrEmpty(normalizedRoot))
            {
                relativeDir = fullDir.TrimStart('\\');
            }
            else if (fullDir.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && (fullDir.Length == normalizedRoot.Length || fullDir[normalizedRoot.Length] == '\\'))
            {
                relativeDir = fullDir.Substring(normalizedRoot.Length).TrimStart('\\');
            }
        }

        return string.IsNullOrEmpty(relativeDir) ? fileKey : $"{relativeDir}\\{fileKey}";
    }

    /// <summary>
    /// トラックの転送先階層（代表Artist\Album\ファイル名）に対応する重複判定キーを生成します。
    /// </summary>
    /// <param name="track">対象トラック</param>
    /// <param name="representativeArtist">所属アルバムの代表アーティスト名（省略時は Track.AlbumArtist 優先、未設定時は Track.Artist を使用）</param>
    /// <param name="representativeAlbum">所属アルバム名（省略時は Track.Album を使用）</param>
    /// <returns>相対パス基準の重複判定キー（ファイル名が空の場合は空文字）</returns>
    internal static string BuildExpectedTrackRelativeKey(
        Track track,
        string? representativeArtist = null,
        string? representativeAlbum = null)
    {
        string fileKey = NormalizeTrackDuplicateKey(track.FilePath);
        if (string.IsNullOrEmpty(fileKey))
        {
            return string.Empty;
        }

        string effectiveArtist = ResolveEffectiveDestinationArtist(track, representativeArtist);
        string effectiveAlbum = ResolveEffectiveDestinationAlbum(track, representativeAlbum);
        string artist = SanitizeFileName(effectiveArtist);
        string album = SanitizeFileName(effectiveAlbum);
        return $"{artist}\\{album}\\{fileKey}";
    }

    /// <summary>
    /// トラックの正規化済み転送先キー（代表アーティスト配下）に加え、旧フォルダ名規則（末尾ピリオド保持）および旧配置（曲別アーティスト配下）の互換キーを列挙します。
    /// </summary>
    /// <param name="track">対象トラック</param>
    /// <param name="representativeArtist">所属アルバムの代表アーティスト名（省略時は Track.AlbumArtist 優先、未設定時は Track.Artist を使用）</param>
    /// <param name="representativeAlbum">所属アルバム名（省略時は Track.Album を使用）</param>
    /// <returns>照合対象となる相対パス基準キー一覧</returns>
    internal static IReadOnlyList<string> BuildExpectedTrackRelativeKeys(
        Track track,
        string? representativeArtist = null,
        string? representativeAlbum = null)
    {
        string fileKey = NormalizeTrackDuplicateKey(track.FilePath);
        if (string.IsNullOrEmpty(fileKey))
        {
            return Array.Empty<string>();
        }

        var candidateDirs = BuildTrackCandidateDirectories(track, representativeArtist, representativeAlbum);
        var keys = new List<string>(candidateDirs.Count);
        foreach (var (artistFolder, albumFolder) in candidateDirs)
        {
            keys.Add($"{artistFolder}\\{albumFolder}\\{fileKey}");
        }

        return keys;
    }

    /// <summary>
    /// トラックの新配置（代表アーティスト配下）および互換配置（旧末尾ピリオド規則・旧曲別アーティスト配下）の候補フォルダ一覧を重複なく生成します。
    /// </summary>
    /// <param name="track">対象トラック</param>
    /// <param name="representativeArtist">所属アルバムの代表アーティスト名（省略可）</param>
    /// <param name="representativeAlbum">所属アルバム名（省略可）</param>
    /// <returns>候補となる (アーティストフォルダ名, アルバムフォルダ名) の一覧（先頭が正規の新配置）</returns>
    internal static IReadOnlyList<(string ArtistFolder, string AlbumFolder)> BuildTrackCandidateDirectories(
        Track track,
        string? representativeArtist = null,
        string? representativeAlbum = null)
    {
        var candidates = new List<(string ArtistFolder, string AlbumFolder)>(4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string artistFolder, string albumFolder)
        {
            if (string.IsNullOrWhiteSpace(artistFolder) || string.IsNullOrWhiteSpace(albumFolder))
            {
                return;
            }

            string key = $"{artistFolder}\\{albumFolder}";
            if (seen.Add(key))
            {
                candidates.Add((artistFolder, albumFolder));
            }
        }

        string effectiveArtist = ResolveEffectiveDestinationArtist(track, representativeArtist);
        string effectiveAlbum = ResolveEffectiveDestinationAlbum(track, representativeAlbum);

        // 1. 新配置（代表アーティスト・正規化フォルダ名）
        AddCandidate(SanitizeFileName(effectiveArtist), SanitizeFileName(effectiveAlbum));

        // 2. 新配置（代表アーティスト・旧末尾ピリオド保持フォルダ名）
        AddCandidate(SanitizeLegacyFileName(effectiveArtist), SanitizeLegacyFileName(effectiveAlbum));

        // 3. 旧配置（曲別アーティスト・正規化フォルダ名）
        AddCandidate(SanitizeFileName(track.Artist), SanitizeFileName(track.Album));

        // 4. 旧配置（曲別アーティスト・旧末尾ピリオド保持フォルダ名）
        AddCandidate(SanitizeLegacyFileName(track.Artist), SanitizeLegacyFileName(track.Album));

        return candidates;
    }

    private static string ResolveEffectiveDestinationArtist(Track track, string? representativeArtist)
    {
        if (!string.IsNullOrWhiteSpace(representativeArtist))
        {
            return representativeArtist.Trim();
        }

        if (!string.IsNullOrWhiteSpace(track.AlbumArtist))
        {
            return track.AlbumArtist.Trim();
        }

        return track.Artist;
    }

    private static string ResolveEffectiveDestinationAlbum(Track track, string? representativeAlbum)
    {
        if (!string.IsNullOrWhiteSpace(representativeAlbum))
        {
            return representativeAlbum.Trim();
        }

        return track.Album;
    }

    private static string ResolveAlbumRepresentativeArtist(
        Album album,
        Track track,
        IReadOnlyDictionary<AudioEffector.Domain.ValueObjects.TrackId, ResolvedAlbumIdentity>? fallbackIdentities)
    {
        if (!string.IsNullOrWhiteSpace(album.Artist))
        {
            return album.Artist.Trim();
        }

        if (fallbackIdentities != null && fallbackIdentities.TryGetValue(track.Id, out var identity) && !string.IsNullOrWhiteSpace(identity.AlbumArtist))
        {
            return identity.AlbumArtist;
        }

        return ResolveEffectiveDestinationArtist(track, representativeArtist: null);
    }

    private static IReadOnlyDictionary<AudioEffector.Domain.ValueObjects.TrackId, ResolvedAlbumIdentity>? BuildFallbackAlbumIdentitiesIfNeeded(
        IReadOnlyList<Album> albums)
    {
        List<Track>? tracksNeedingResolution = null;
        foreach (var album in albums)
        {
            if (string.IsNullOrWhiteSpace(album.Artist) && album.Tracks != null && album.Tracks.Count > 0)
            {
                tracksNeedingResolution ??= new List<Track>();
                tracksNeedingResolution.AddRange(album.Tracks);
            }
        }

        return tracksNeedingResolution != null ? AlbumGroupingResolver.Resolve(tracksNeedingResolution) : null;
    }

    private static Dictionary<Track, (string RepresentativeArtist, string RepresentativeAlbum)> BuildTrackDestinationMetadataMap(
        IReadOnlyList<Album> albums,
        IReadOnlyList<Track> tracksToTransfer)
    {
        var map = new Dictionary<Track, (string RepresentativeArtist, string RepresentativeAlbum)>(ReferenceEqualityComparer.Instance);
        var fallbackIdentities = BuildFallbackAlbumIdentitiesIfNeeded(albums);

        foreach (var album in albums)
        {
            if (album.Tracks == null)
            {
                continue;
            }

            foreach (var track in album.Tracks)
            {
                string repArtist = ResolveAlbumRepresentativeArtist(album, track, fallbackIdentities);
                string repAlbum = ResolveEffectiveDestinationAlbum(track, album.Title);
                map.TryAdd(track, (repArtist, repAlbum));
            }
        }

        List<Track>? unmappedTracks = null;
        foreach (var track in tracksToTransfer)
        {
            if (!map.ContainsKey(track))
            {
                unmappedTracks ??= new List<Track>();
                unmappedTracks.Add(track);
            }
        }

        if (unmappedTracks != null)
        {
            var resolved = AlbumGroupingResolver.Resolve(unmappedTracks);
            foreach (var track in unmappedTracks)
            {
                string repArtist = resolved.TryGetValue(track.Id, out var identity) && !string.IsNullOrWhiteSpace(identity.AlbumArtist)
                    ? identity.AlbumArtist
                    : ResolveEffectiveDestinationArtist(track, representativeArtist: null);
                map.TryAdd(track, (repArtist, track.Album));
            }
        }

        return map;
    }

    /// <summary>
    /// PCライブラリ内の楽曲から、転送先となり得る既知の「アーティストフォルダ -> アルバムフォルダ集合」マップを構築します。
    /// 機器ルート選択時に全階層再帰走査を避けつつ既知の転送先（代表アーティスト新配置および曲別アーティスト旧配置）のみを照合するために使用します。
    /// </summary>
    /// <param name="albums">対象アルバム一覧</param>
    /// <returns>アーティストフォルダ名をキー、配下の候補アルバムフォルダ名集合を値とするマップ</returns>
    internal static Dictionary<string, HashSet<string>> CollectKnownTrackFolderTargets(IReadOnlyList<Album> albums)
    {
        var targets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var fallbackIdentities = BuildFallbackAlbumIdentitiesIfNeeded(albums);

        static void AddTarget(Dictionary<string, HashSet<string>> map, string artistFolder, string albumFolder)
        {
            if (string.IsNullOrWhiteSpace(artistFolder) || string.IsNullOrWhiteSpace(albumFolder))
            {
                return;
            }

            if (!map.TryGetValue(artistFolder, out var albumFolders))
            {
                albumFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[artistFolder] = albumFolders;
            }

            albumFolders.Add(albumFolder);
        }

        foreach (var album in albums)
        {
            if (album.Tracks == null)
            {
                continue;
            }

            foreach (var track in album.Tracks)
            {
                string repArtist = ResolveAlbumRepresentativeArtist(album, track, fallbackIdentities);
                string repAlbum = ResolveEffectiveDestinationAlbum(track, album.Title);
                foreach (var (artistFolder, albumFolder) in BuildTrackCandidateDirectories(track, repArtist, repAlbum))
                {
                    AddTarget(targets, artistFolder, albumFolder);
                }
            }
        }

        return targets;
    }

    /// <summary>
    /// 指定パスがデバイスのルートディレクトリ直下であるかを判定します。
    /// ルート直下での不要な全階層再帰スキャンを防止するために使用します。
    /// </summary>
    /// <param name="device">対象デバイス</param>
    /// <param name="path">判定対象のディレクトリパス</param>
    /// <returns>ルートディレクトリ直下の場合はtrue、それ以外はfalse</returns>
    internal static bool IsDeviceRootPath(MainViewModel.DeviceViewModel device, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string normalizedPath = path.Trim().TrimEnd('\\', '/');
        if (device.Type == MainViewModel.DeviceType.MTP)
        {
            return string.IsNullOrEmpty(normalizedPath);
        }

        string normalizedRoot = (device.RootPath ?? string.Empty).Trim().TrimEnd('\\', '/');
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ファイルシステム上の指定フォルダ配下にある音声ファイルの重複判定インデックスを構築します。
    /// </summary>
    /// <param name="rootPath">走査対象のルートパス</param>
    /// <param name="recursive">サブフォルダを再帰走査する場合はtrue</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>正規化された重複判定キーのセット</returns>
    internal static HashSet<string> BuildFileSystemAudioIndex(
        string rootPath,
        bool recursive,
        CancellationToken cancellationToken = default)
        => BuildFileSystemAudioIndex(rootPath, recursive, knownTargetFolders: null, cancellationToken);

    /// <summary>
    /// ファイルシステム上の指定フォルダ配下にある音声ファイルの重複判定インデックスを構築します。
    /// </summary>
    /// <param name="rootPath">走査対象のルートパス</param>
    /// <param name="recursive">サブフォルダを再帰走査する場合はtrue</param>
    /// <param name="knownTargetFolders">非再帰走査（ルート選択時）に限定照合する既知のアーティスト／アルバムフォルダマップ（省略時は直下のみ）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>正規化された重複判定キーのセット</returns>
    internal static HashSet<string> BuildFileSystemAudioIndex(
        string rootPath,
        bool recursive,
        IReadOnlyDictionary<string, HashSet<string>>? knownTargetFolders,
        CancellationToken cancellationToken = default)
    {
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return index;
        }

        if (!recursive)
        {
            IndexFileSystemDirectoryFiles(rootPath, rootPath, index, cancellationToken);
            if (knownTargetFolders != null && knownTargetFolders.Count > 0)
            {
                IndexKnownFileSystemTargetFolders(rootPath, knownTargetFolders, index, cancellationToken);
            }

            return index;
        }

        var pendingDirs = new Stack<string>();
        pendingDirs.Push(rootPath);

        while (pendingDirs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pendingDirs.Pop();

            IndexFileSystemDirectoryFiles(rootPath, dir, index, cancellationToken);

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pendingDirs.Push(subDir);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating subdirectories in {dir}: {ex.Message}");
            }
        }

        return index;
    }

    private static void IndexFileSystemDirectoryFiles(
        string rootPath,
        string dir,
        HashSet<string> index,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string ext = Path.GetExtension(file);
                if (!SupportedAudioExtensions.Contains(ext))
                {
                    continue;
                }

                string key = BuildDeviceRelativeDuplicateKey(rootPath, file);
                if (!string.IsNullOrEmpty(key))
                {
                    index.Add(key);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating files in {dir}: {ex.Message}");
        }
    }

    private static void IndexKnownFileSystemTargetFolders(
        string rootPath,
        IReadOnlyDictionary<string, HashSet<string>> knownTargetFolders,
        HashSet<string> index,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var artistDir in Directory.EnumerateDirectories(rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string artistFolderName = GetDirectoryLeafName(artistDir);
                if (!knownTargetFolders.TryGetValue(artistFolderName, out var expectedAlbums))
                {
                    continue;
                }

                try
                {
                    foreach (var albumDir in Directory.EnumerateDirectories(artistDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string albumFolderName = GetDirectoryLeafName(albumDir);
                        if (!expectedAlbums.Contains(albumFolderName))
                        {
                            continue;
                        }

                        IndexFileSystemDirectoryFiles(rootPath, albumDir, index, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error enumerating known artist directory {artistDir}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating root directories in {rootPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// MTP デバイスの指定フォルダ配下にある音声ファイルの重複判定インデックスを構築します。
    /// </summary>
    /// <param name="mtpDevice">接続済みのMTPデバイス</param>
    /// <param name="rootPath">走査対象のルートパス</param>
    /// <param name="recursive">サブフォルダを再帰走査する場合はtrue</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>正規化された重複判定キーのセット</returns>
    internal static HashSet<string> BuildMtpAudioIndex(
        MediaDevice mtpDevice,
        string rootPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        if (mtpDevice == null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return BuildMtpAudioIndex(new MediaDeviceClientAdapter(mtpDevice), rootPath, recursive, knownTargetFolders: null, cancellationToken);
    }

    /// <summary>
    /// 抽象化された MTP デバイスクライアントを用いて、指定フォルダ配下の音声ファイル重複判定インデックスを構築します。
    /// </summary>
    /// <param name="mtpClient">接続済みのMTPデバイスクライアント</param>
    /// <param name="rootPath">走査対象のルートパス</param>
    /// <param name="recursive">サブフォルダを再帰走査する場合はtrue</param>
    /// <param name="knownTargetFolders">非再帰走査（ルート選択時）に限定照合する既知のアーティスト／アルバムフォルダマップ（省略時は直下のみ）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>正規化された重複判定キーのセット</returns>
    internal static HashSet<string> BuildMtpAudioIndex(
        IMtpDeviceClient mtpClient,
        string rootPath,
        bool recursive,
        IReadOnlyDictionary<string, HashSet<string>>? knownTargetFolders = null,
        CancellationToken cancellationToken = default)
    {
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mtpClient == null || !mtpClient.IsConnected || string.IsNullOrWhiteSpace(rootPath))
        {
            return index;
        }

        if (!recursive)
        {
            IndexMtpDirectoryFiles(mtpClient, rootPath, rootPath, index, cancellationToken);
            if (knownTargetFolders != null && knownTargetFolders.Count > 0)
            {
                IndexKnownMtpTargetFolders(mtpClient, rootPath, knownTargetFolders, index, cancellationToken);
            }

            return index;
        }

        var pendingDirs = new Stack<string>();
        pendingDirs.Push(rootPath);

        while (pendingDirs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pendingDirs.Pop();

            IndexMtpDirectoryFiles(mtpClient, rootPath, dir, index, cancellationToken);

            try
            {
                foreach (var subDir in mtpClient.GetDirectories(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pendingDirs.Push(subDir);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating MTP subdirectories in {dir}: {ex.Message}");
            }
        }

        return index;
    }

    private static void IndexMtpDirectoryFiles(
        IMtpDeviceClient mtpClient,
        string rootPath,
        string dir,
        HashSet<string> index,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var file in mtpClient.GetFiles(dir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string ext = Path.GetExtension(file);
                if (!SupportedAudioExtensions.Contains(ext))
                {
                    continue;
                }

                string key = BuildDeviceRelativeDuplicateKey(rootPath, file);
                if (!string.IsNullOrEmpty(key))
                {
                    index.Add(key);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating MTP files in {dir}: {ex.Message}");
        }
    }

    private static void IndexKnownMtpTargetFolders(
        IMtpDeviceClient mtpClient,
        string rootPath,
        IReadOnlyDictionary<string, HashSet<string>> knownTargetFolders,
        HashSet<string> index,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var artistDir in mtpClient.GetDirectories(rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string artistFolderName = GetDirectoryLeafName(artistDir);
                if (!knownTargetFolders.TryGetValue(artistFolderName, out var expectedAlbums))
                {
                    continue;
                }

                try
                {
                    foreach (var albumDir in mtpClient.GetDirectories(artistDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string albumFolderName = GetDirectoryLeafName(albumDir);
                        if (!expectedAlbums.Contains(albumFolderName))
                        {
                            continue;
                        }

                        IndexMtpDirectoryFiles(mtpClient, rootPath, albumDir, index, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error enumerating known MTP artist directory {artistDir}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating MTP root directories in {rootPath}: {ex.Message}");
        }
    }

    private static string GetDirectoryLeafName(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return string.Empty;
        }

        string trimmed = directoryPath.TrimEnd('\\', '/');
        return Path.GetFileName(trimmed);
    }

    /// <summary>
    /// 構築したデバイスファイルインデックスを用いて、各トラック・アルバムの IsOnDevice 状態を一括反映します。
    /// </summary>
    /// <param name="albums">状態更新対象のアルバム一覧</param>
    /// <param name="deviceFileKeys">デバイス上の正規化済み相対パス／ファイル名キーセット</param>
    internal static void ApplyDeviceTrackIndex(IReadOnlyList<Album> albums, IReadOnlySet<string> deviceFileKeys)
    {
        var fallbackIdentities = BuildFallbackAlbumIdentitiesIfNeeded(albums);

        foreach (var album in albums)
        {
            album.ApplyTracksOnDeviceState(track =>
            {
                string repArtist = ResolveAlbumRepresentativeArtist(album, track, fallbackIdentities);
                string repAlbum = ResolveEffectiveDestinationAlbum(track, album.Title);
                foreach (var relativeKey in BuildExpectedTrackRelativeKeys(track, repArtist, repAlbum))
                {
                    if (!string.IsNullOrEmpty(relativeKey) && deviceFileKeys.Contains(relativeKey))
                    {
                        return true;
                    }
                }

                // 指定フォルダ直下にフラット配置された既存音声ファイルとの照合フォールバック
                string flatKey = NormalizeTrackDuplicateKey(track.FilePath);
                return !string.IsNullOrEmpty(flatKey) && deviceFileKeys.Contains(flatKey);
            });
        }
    }

    /// <summary>
    /// 進行中の非同期重複スキャン・容量取得をキャンセルし、保持しているイベント購読およびリソースを解放します。
    /// </summary>
    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _checkDeviceAlbumsCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        var storageCts = Interlocked.Exchange(ref _storageRefreshCts, null);
        if (storageCts != null)
        {
            try { storageCts.Cancel(); } catch (ObjectDisposedException) { }
            storageCts.Dispose();
        }

        RemovableDrives.CollectionChanged -= OnRemovableDrivesCollectionChanged;
        _albumsRef.CollectionChanged -= OnAlbumsCollectionChanged;
        ClearAllAlbumSubscriptions();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 現在選択されている接続機器の空き容量・総容量を非同期で最新化します。
    /// </summary>
    /// <param name="externalCancellationToken">外部キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    internal async Task RefreshSelectedDeviceStorageAsync(CancellationToken externalCancellationToken = default)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        var previousCts = Interlocked.Exchange(ref _storageRefreshCts, linkedCts);
        if (previousCts != null)
        {
            try { previousCts.Cancel(); } catch (ObjectDisposedException) { }
            previousCts.Dispose();
        }

        var ct = linkedCts.Token;
        var device = SelectedDevice;
        var currentPath = CurrentDevicePath;

        if (device == null)
        {
            RunOnUiThread(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    UpdateSelectionSummary();
                }
            });
            return;
        }

        try
        {
            var (hasInfo, totalBytes, freeBytes) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return _readDeviceStorageSpace(device, currentPath);
            }, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (hasInfo)
                {
                    device.UpdateStorageSpace(totalBytes, freeBytes);
                }
                else
                {
                    device.ClearStorageSpace();
                }

                UpdateSelectionSummary();
            });
        }
        catch (OperationCanceledException)
        {
            // 新しい機器選択・更新によりキャンセルされた場合は無視
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing device storage info: {ex.Message}");
            RunOnUiThread(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    device.ClearStorageSpace();
                    UpdateSelectionSummary();
                }
            });
        }
    }

    /// <summary>
    /// 現在のアルバム／楽曲選択状態および接続機器の空き容量から、選択サマリ（件数・合計サイズ・容量不足判定）を再計算します。
    /// </summary>
    public void UpdateSelectionSummary()
    {
        var albums = _getAlbums();
        var targets = CollectTransferTargetTracks(albums);
        var targetSet = new HashSet<Track>(targets);

        int selectedAlbumCount = 0;
        foreach (var album in albums)
        {
            if (album.Tracks != null && album.Tracks.Any(t => targetSet.Contains(t)))
            {
                selectedAlbumCount++;
            }
        }

        long totalBytes = 0L;
        foreach (var track in targets)
        {
            totalBytes += GetCachedFileSizeBytes(track.FilePath);
        }

        bool hasStorageInfo = SelectedDevice?.HasStorageInfo == true;
        long freeBytes = SelectedDevice?.FreeSpaceBytes ?? 0L;
        bool isInsufficient = hasStorageInfo && totalBytes > freeBytes;

        SelectedAlbumCount = selectedAlbumCount;
        SelectedTrackCount = targets.Count;
        SelectedTotalBytes = totalBytes;
        IsInsufficientSpace = isInsufficient;
        SelectedSizeSummaryText = FormatSelectionSummaryText(
            selectedAlbumCount,
            targets.Count,
            totalBytes,
            hasStorageInfo,
            isInsufficient);
    }

    /// <summary>
    /// 選択中楽曲の件数・合計サイズ・空き容量判定から表示用サマリテキストを生成します。
    /// </summary>
    /// <param name="albumCount">選択対象アルバム数</param>
    /// <param name="trackCount">選択対象トラック数</param>
    /// <param name="totalBytes">選択対象合計バイト数</param>
    /// <param name="hasStorageInfo">接続機器の容量情報が取得できているか</param>
    /// <param name="isInsufficientSpace">空き容量が不足しているか</param>
    /// <returns>表示用サマリテキスト</returns>
    internal static string FormatSelectionSummaryText(
        int albumCount,
        int trackCount,
        long totalBytes,
        bool hasStorageInfo,
        bool isInsufficientSpace)
    {
        if (trackCount <= 0)
        {
            return "選択中: 0 曲";
        }

        string formattedSize = MainViewModel.DeviceViewModel.FormatByteSize(totalBytes);
        if (!hasStorageInfo)
        {
            return $"選択中: {albumCount} アルバム, {trackCount} 曲 ({formattedSize})";
        }

        string capacityStatus = isInsufficientSpace ? "容量不足" : "空き十分";
        return $"選択中: {albumCount} アルバム, {trackCount} 曲 ({formattedSize} / {capacityStatus})";
    }

    private static (bool HasInfo, long TotalBytes, long FreeBytes) ReadDeviceStorageSpace(
        MainViewModel.DeviceViewModel device,
        string currentPath)
    {
        if (device.Type == MainViewModel.DeviceType.FileSystem && device.Drive != null)
        {
            try
            {
                if (device.Drive.IsReady && device.Drive.TotalSize > 0)
                {
                    return (true, device.Drive.TotalSize, device.Drive.AvailableFreeSpace);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading filesystem drive space: {ex.Message}");
            }

            return (false, 0L, 0L);
        }

        if (device.Type == MainViewModel.DeviceType.MTP && device.MtpDevice != null && device.MtpDevice.IsConnected)
        {
            try
            {
                var drives = device.MtpDevice.GetDrives();
                if (drives != null && drives.Length > 0)
                {
                    var normalizedPath = (currentPath ?? string.Empty).Replace('/', '\\').TrimStart('\\');
                    var matchedDrive = !string.IsNullOrEmpty(normalizedPath)
                        ? drives.FirstOrDefault(d =>
                            !string.IsNullOrWhiteSpace(d.Name) &&
                            normalizedPath.StartsWith(d.Name.Trim('\\'), StringComparison.OrdinalIgnoreCase))
                        : null;

                    if (matchedDrive != null && matchedDrive.TotalSize > 0)
                    {
                        return (true, matchedDrive.TotalSize, matchedDrive.AvailableFreeSpace);
                    }

                    long total = 0L;
                    long free = 0L;
                    foreach (var d in drives)
                    {
                        if (d.TotalSize > 0)
                        {
                            total += d.TotalSize;
                            free += Math.Max(0L, d.AvailableFreeSpace);
                        }
                    }

                    if (total > 0)
                    {
                        return (true, total, free);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading MTP storage info: {ex.Message}");
            }
        }

        return (false, 0L, 0L);
    }

    private static void TryPopulateFileSystemStorage(MainViewModel.DeviceViewModel deviceVm)
    {
        var (hasInfo, totalBytes, freeBytes) = ReadDeviceStorageSpace(deviceVm, deviceVm.RootPath);
        if (hasInfo)
        {
            deviceVm.UpdateStorageSpace(totalBytes, freeBytes);
        }
        else
        {
            deviceVm.ClearStorageSpace();
        }
    }

    private static long DefaultGetFileSizeBytes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return 0L;
        }

        try
        {
            return File.Exists(filePath) ? new FileInfo(filePath).Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    private void CancelPendingStorageRefresh()
    {
        var storageCts = Interlocked.Exchange(ref _storageRefreshCts, null);
        if (storageCts != null)
        {
            try { storageCts.Cancel(); } catch (ObjectDisposedException) { }
            storageCts.Dispose();
        }
    }

    private void ApplyDeviceTrackIndexWithBatchSummary(IReadOnlyList<Album> albums, IReadOnlySet<string> index)
    {
        _suppressSelectionSummaryUpdates = true;
        try
        {
            ApplyDeviceTrackIndex(albums, index);
        }
        finally
        {
            _suppressSelectionSummaryUpdates = false;
        }

        UpdateSelectionSummary();
    }

    private long GetCachedFileSizeBytes(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return 0L;
        }

        if (_fileSizeCache.TryGetValue(filePath, out long cached))
        {
            return cached;
        }

        long size = Math.Max(0L, _getFileSizeBytes(filePath));
        _fileSizeCache[filePath] = size;
        return size;
    }

    private void OnRemovableDrivesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasConnectedDevices));
    }

    private void OnAlbumsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is Album album)
                {
                    SubscribeAlbumAndTracks(album);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is Album album)
                {
                    UnsubscribeAlbumAndTracks(album);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Replace)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is Album album)
                    {
                        UnsubscribeAlbumAndTracks(album);
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is Album album)
                    {
                        SubscribeAlbumAndTracks(album);
                    }
                }
            }
        }
        else
        {
            _fileSizeCache.Clear();
            SyncAlbumSubscriptions(_albumsRef);
        }

        if (!_suppressSelectionSummaryUpdates)
        {
            UpdateSelectionSummary();
        }
    }

    private void SubscribeAlbumAndTracks(Album album)
    {
        if (_subscribedAlbums.Add(album))
        {
            album.PropertyChanged += OnAlbumSelectionPropertyChanged;
        }

        if (album.Tracks != null)
        {
            foreach (var track in album.Tracks)
            {
                if (_subscribedTracks.Add(track))
                {
                    track.PropertyChanged += OnTrackSelectionPropertyChanged;
                }
            }
        }
    }

    private void UnsubscribeAlbumAndTracks(Album album)
    {
        if (_subscribedAlbums.Remove(album))
        {
            album.PropertyChanged -= OnAlbumSelectionPropertyChanged;
        }

        if (album.Tracks != null)
        {
            foreach (var track in album.Tracks)
            {
                if (_subscribedTracks.Remove(track))
                {
                    track.PropertyChanged -= OnTrackSelectionPropertyChanged;
                }
            }
        }
    }

    private void SyncAlbumSubscriptions(IEnumerable<Album> currentAlbums)
    {
        var activeAlbums = new HashSet<Album>(currentAlbums);
        foreach (var staleAlbum in _subscribedAlbums.Where(a => !activeAlbums.Contains(a)).ToList())
        {
            staleAlbum.PropertyChanged -= OnAlbumSelectionPropertyChanged;
            _subscribedAlbums.Remove(staleAlbum);
        }

        var activeTracks = new HashSet<Track>();
        foreach (var album in activeAlbums)
        {
            if (_subscribedAlbums.Add(album))
            {
                album.PropertyChanged += OnAlbumSelectionPropertyChanged;
            }

            if (album.Tracks != null)
            {
                foreach (var track in album.Tracks)
                {
                    activeTracks.Add(track);
                    if (_subscribedTracks.Add(track))
                    {
                        track.PropertyChanged += OnTrackSelectionPropertyChanged;
                    }
                }
            }
        }

        foreach (var staleTrack in _subscribedTracks.Where(t => !activeTracks.Contains(t)).ToList())
        {
            staleTrack.PropertyChanged -= OnTrackSelectionPropertyChanged;
            _subscribedTracks.Remove(staleTrack);
        }
    }

    private void ClearAllAlbumSubscriptions()
    {
        foreach (var album in _subscribedAlbums)
        {
            album.PropertyChanged -= OnAlbumSelectionPropertyChanged;
        }

        _subscribedAlbums.Clear();

        foreach (var track in _subscribedTracks)
        {
            track.PropertyChanged -= OnTrackSelectionPropertyChanged;
        }

        _subscribedTracks.Clear();
    }

    private void OnAlbumSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Album.Tracks))
        {
            SyncAlbumSubscriptions(_albumsRef);
            if (!_suppressSelectionSummaryUpdates)
            {
                UpdateSelectionSummary();
            }
        }
        else if (e.PropertyName == nameof(Album.IsSelected) || e.PropertyName == nameof(Album.IsOnDevice))
        {
            if (!_suppressSelectionSummaryUpdates)
            {
                UpdateSelectionSummary();
            }
        }
    }

    private void OnTrackSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Track.IsSelected) || e.PropertyName == nameof(Track.IsOnDevice))
        {
            if (!_suppressSelectionSummaryUpdates)
            {
                UpdateSelectionSummary();
            }
        }
    }

    /// <summary>
    /// 接続機器を一意に識別するキー文字列を解決します。
    /// </summary>
    /// <param name="device">対象デバイス</param>
    /// <returns>永続化用の機器識別子（解決不可時は空文字）</returns>
    internal static string ResolveDeviceKey(MainViewModel.DeviceViewModel? device)
    {
        if (device == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(device.DeviceKey))
        {
            return device.DeviceKey.Trim();
        }

        if (device.Type == MainViewModel.DeviceType.MTP)
        {
            string? deviceId = null;
            try { deviceId = device.MtpDevice?.DeviceId; } catch { }
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return $"MTP:{deviceId.Trim()}";
            }

            string? friendlyName = null;
            try { friendlyName = device.MtpDevice?.FriendlyName; } catch { }
            string fallbackName = !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName.Trim() : device.Name.Trim();
            return string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : $"MTP:{fallbackName}";
        }

        string label = string.Empty;
        string root = device.RootPath?.Trim() ?? string.Empty;
        if (device.Drive != null)
        {
            try
            {
                root = device.Drive.RootDirectory.FullName;
                if (device.Drive.IsReady && !string.IsNullOrWhiteSpace(device.Drive.VolumeLabel))
                {
                    label = device.Drive.VolumeLabel.Trim();
                }
            }
            catch
            {
                // ドライブ未準備時はフォールバックキーを使用
            }
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            root = device.Name.Trim();
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(label) ? $"FS:{root}" : $"FS:{label}|{root}";
    }

    /// <summary>
    /// 現在表示中のフォルダを選択中機器の既定転送先フォルダとして保存します。
    /// </summary>
    public void SetCurrentFolderAsDefault()
    {
        if (SelectedDevice == null || string.IsNullOrWhiteSpace(CurrentDevicePath))
        {
            return;
        }

        string deviceKey = ResolveDeviceKey(SelectedDevice);
        if (string.IsNullOrEmpty(deviceKey))
        {
            return;
        }

        string targetPath = CurrentDevicePath;
        EnsureDefaultFoldersCacheLoaded()[deviceKey] = targetPath;
        _settingsService?.UpdateSettings(settings =>
        {
            settings.DeviceDefaultTransferFolders[deviceKey] = targetPath;
        });

        DefaultFolderStatusMessage = string.Empty;
        UpdateDefaultFolderState();
    }

    /// <summary>
    /// 選択中機器の既定転送先フォルダ設定を解除します。
    /// </summary>
    public void ClearDefaultFolderForSelectedDevice()
    {
        if (SelectedDevice == null)
        {
            return;
        }

        string deviceKey = ResolveDeviceKey(SelectedDevice);
        if (string.IsNullOrEmpty(deviceKey))
        {
            return;
        }

        EnsureDefaultFoldersCacheLoaded().Remove(deviceKey);
        _settingsService?.UpdateSettings(settings =>
        {
            settings.DeviceDefaultTransferFolders.Remove(deviceKey);
        });

        DefaultFolderStatusMessage = string.Empty;
        UpdateDefaultFolderState();
    }

    /// <summary>
    /// 現在表示中のフォルダが既定フォルダであれば解除し、そうでなければ既定フォルダとして登録します。
    /// </summary>
    public void ToggleDefaultFolderForCurrentPath()
    {
        if (IsCurrentFolderDefault)
        {
            ClearDefaultFolderForSelectedDevice();
        }
        else
        {
            SetCurrentFolderAsDefault();
        }
    }

    /// <summary>
    /// 選択中機器の登録済み既定フォルダ有無・存在確認に基づき、初期遷移先フォルダパスを決定します。
    /// </summary>
    /// <param name="device">対象デバイス</param>
    /// <param name="fallbackRootPath">フォールバック先のルートパス</param>
    /// <param name="directoryExists">対象パスが存在するかを返す判定関数</param>
    /// <param name="fellBackToRoot">登録済みフォルダが存在せずルートへフォールバックした場合はtrue</param>
    /// <returns>初期遷移先ディレクトリパス</returns>
    internal string ResolveInitialDirectoryPath(
        MainViewModel.DeviceViewModel device,
        string fallbackRootPath,
        Func<string, bool> directoryExists,
        out bool fellBackToRoot)
    {
        fellBackToRoot = false;
        string savedPath = GetSavedDefaultFolder(device);
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return fallbackRootPath;
        }

        try
        {
            if (directoryExists(savedPath))
            {
                return savedPath;
            }
        }
        catch
        {
            // 存在確認に失敗した場合は安全にルートへフォールバック
        }

        fellBackToRoot = true;
        return fallbackRootPath;
    }

    private Dictionary<string, string> EnsureDefaultFoldersCacheLoaded()
    {
        if (_defaultFoldersCache != null)
        {
            return _defaultFoldersCache;
        }

        var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_settingsService != null)
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                foreach (var kv in settings.DeviceDefaultTransferFolders)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    {
                        loaded[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading default device folder: {ex.Message}");
            }
        }

        _defaultFoldersCache = loaded;
        return _defaultFoldersCache;
    }

    private string GetSavedDefaultFolder(MainViewModel.DeviceViewModel? device)
    {
        if (device == null || _settingsService == null)
        {
            return string.Empty;
        }

        string deviceKey = ResolveDeviceKey(device);
        if (string.IsNullOrEmpty(deviceKey))
        {
            return string.Empty;
        }

        var cache = EnsureDefaultFoldersCacheLoaded();
        return cache.TryGetValue(deviceKey, out var savedPath) && !string.IsNullOrWhiteSpace(savedPath)
            ? savedPath
            : string.Empty;
    }

    private void UpdateDefaultFolderState()
    {
        string savedPath = GetSavedDefaultFolder(SelectedDevice);
        CurrentDeviceDefaultFolderPath = savedPath;
        IsCurrentFolderDefault = !string.IsNullOrEmpty(savedPath)
            && !string.IsNullOrEmpty(CurrentDevicePath)
            && PathsEqualNormalized(savedPath, CurrentDevicePath);
    }

    private static bool PathsEqualNormalized(string path1, string path2)
    {
        string norm1 = NormalizeComparableDevicePath(path1);
        string norm2 = NormalizeComparableDevicePath(path2);
        return string.Equals(norm1, norm2, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparableDevicePath(string path)
    {
        string normalized = path.Trim().Replace('/', '\\');
        if (normalized == @"\")
        {
            return @"\";
        }

        return normalized.TrimEnd('\\');
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }
        else
        {
            action();
        }
    }

    private void NavigateDirectory(MainViewModel.DirectoryItem? dir)
    {
        if (dir == null || !dir.IsFolder) return;
        DefaultFolderStatusMessage = string.Empty;
        LoadDeviceDirectories(dir.FullPath);
    }

    private void NavigateUp()
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentDevicePath) || SelectedDevice == null) return;

            if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem)
            {
                var parent = Directory.GetParent(CurrentDevicePath);
                if (parent != null)
                {
                    DefaultFolderStatusMessage = string.Empty;
                    LoadDeviceDirectories(parent.FullName);
                }
            }
            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP)
            {
                if (CurrentDevicePath == @"\" || CurrentDevicePath == "/") return;

                var parentPath = Path.GetDirectoryName(CurrentDevicePath);
                if (string.IsNullOrEmpty(parentPath)) parentPath = @"\";
                DefaultFolderStatusMessage = string.Empty;
                LoadDeviceDirectories(parentPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating up: {ex.Message}");
        }
    }

    /// <summary>
    /// 転送先フォルダ名に使用できるよう無効文字および末尾のピリオド・空白を除去して正規化します。
    /// Windows ファイルシステムが末尾のピリオドや空白を暗黙除去する仕様と保存・照合規則を一致させます。
    /// </summary>
    /// <param name="name">元のアーティスト名またはアルバム名</param>
    /// <returns>安全なフォルダ名（空になる場合は "Unknown"）</returns>
    internal static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        cleaned = TrimTrailingDotsAndWhitespace(cleaned);
        return string.IsNullOrEmpty(cleaned) ? "Unknown" : cleaned;
    }

    private static string SanitizeLegacyFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrEmpty(TrimTrailingDotsAndWhitespace(cleaned)))
        {
            return "Unknown";
        }

        return cleaned;
    }

    private static string TrimTrailingDotsAndWhitespace(string value)
    {
        int end = value.Length;
        while (end > 0 && (value[end - 1] == '.' || char.IsWhiteSpace(value[end - 1])))
        {
            end--;
        }

        return end == value.Length ? value : value.Substring(0, end);
    }

    /// <summary>
    /// 選択されたアルバムまたはトラックを、現在選択されているデバイスへ転送します。
    /// </summary>
    private async void TransferSelected()
    {
        await TransferSelectedAsync();
    }

    /// <summary>
    /// 選択されたアルバムまたはトラックを現在選択されているデバイスへ転送し、再走査による状態反映完了後に完了通知を行います。
    /// </summary>
    /// <returns>非同期タスク</returns>
    internal async Task TransferSelectedAsync()
    {
        if (IsTransferring)
        {
            return;
        }

        if (SelectedDevice == null)
        {
            _showMessage("Please select a device first.", "No Device Selected");
            return;
        }

        string destinationFolder = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;

        if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem && !destinationFolder.StartsWith(SelectedDevice.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            _showMessage("Current folder is not on the selected drive.", "Error");
            return;
        }

        IMtpDeviceClient? mtpClient = null;
        if (SelectedDevice.Type == MainViewModel.DeviceType.MTP)
        {
            mtpClient = _resolveMtpClient(SelectedDevice);
            if (mtpClient == null || !mtpClient.IsConnected)
            {
                _showMessage("MTP device is not connected.", "Error");
                return;
            }
        }

        var albums = _getAlbums();
        var tracksToTransfer = CollectTransferTargetTracks(albums);

        if (tracksToTransfer.Count == 0)
        {
            _showMessage("Please select at least one album or track to transfer.", "No Items Selected");
            return;
        }

        IsTransferring = true;
        TransferProgress = 0;

        try
        {
            var progress = new Progress<double>(p => TransferProgress = p);
            var transcodeOptions = GetCurrentTranscodeOptions();

            if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem)
            {
                await TransferTracksToFileSystemAsync(tracksToTransfer, destinationFolder, transcodeOptions, progress);
            }
            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP)
            {
                if (mtpClient == null || !mtpClient.IsConnected)
                {
                    throw new InvalidOperationException("MTP device is not connected.");
                }

                await TransferTracksToMtpAsync(mtpClient, tracksToTransfer, destinationFolder, transcodeOptions, progress);
            }

            LoadDeviceDirectoriesCore(destinationFolder);
            _ = RefreshSelectedDeviceStorageAsync();
            await CheckDeviceAlbumsAsync();

            _showMessage("Transfer completed successfully!", "Success");
        }
        catch (Exception ex)
        {
            try
            {
                LoadDeviceDirectoriesCore(destinationFolder, showErrorMessage: false);
                _ = RefreshSelectedDeviceStorageAsync();
                await CheckDeviceAlbumsAsync();
            }
            catch
            {
                // 転送失敗後の再走査エラーは元の失敗通知を優先して握りつぶす
            }

            _showMessage($"Transfer failed: {ex.Message}", "Error");
        }
        finally
        {
            IsTransferring = false;
            TransferProgress = 0;
        }
    }

    /// <summary>
    /// MTP デバイスへトラック群を転送します（設定に応じて形式変換および一時ファイル後始末を実施）。
    /// </summary>
    /// <param name="mtpClient">接続済みのMTPデバイスクライアント</param>
    /// <param name="tracksToTransfer">転送対象トラック一覧</param>
    /// <param name="destinationFolder">転送先フォルダパス</param>
    /// <param name="transcodeOptions">音声フォーマット変換オプション</param>
    /// <param name="progress">進捗通知（0〜100）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    internal Task TransferTracksToMtpAsync(
        IMtpDeviceClient mtpClient,
        IReadOnlyList<Track> tracksToTransfer,
        string destinationFolder,
        AudioTranscodeOptions transcodeOptions,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mtpClient);

        var destinationMetadataByTrack = BuildTrackDestinationMetadataMap(_getAlbums(), tracksToTransfer);

        return Task.Run(async () =>
        {
            int total = tracksToTransfer.Count;
            int current = 0;
            foreach (var track in tracksToTransfer)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(track.FilePath)) continue;

                var (repArtist, repAlbum) = destinationMetadataByTrack.TryGetValue(track, out var metadata)
                    ? metadata
                    : (ResolveEffectiveDestinationArtist(track, representativeArtist: null), track.Album);
                var candidateDirs = BuildTrackCandidateDirectories(track, repArtist, repAlbum);
                var (artist, album) = candidateDirs[0];

                string targetFileName = _audioTranscodeService.ResolveTargetFileName(track.FilePath, transcodeOptions);
                string originalFileName = Path.GetFileName(track.FilePath);

                string artistDir = Path.Combine(destinationFolder, artist);
                string targetDir = Path.Combine(artistDir, album);
                string destPath = Path.Combine(targetDir, targetFileName);

                // 変換後または元ファイル名のいずれかが新配置または旧配置に既に存在する場合はスキップ
                bool fileExists = CheckMtpTrackAlreadyExists(
                    mtpClient,
                    destinationFolder,
                    candidateDirs,
                    targetFileName,
                    originalFileName);

                if (fileExists)
                {
                    current++;
                    progress?.Report((double)current / total * 100);
                    continue;
                }

                try
                {
                    if (!mtpClient.DirectoryExists(artistDir))
                    {
                        mtpClient.CreateDirectory(artistDir);
                    }
                    if (!mtpClient.DirectoryExists(targetDir))
                    {
                        mtpClient.CreateDirectory(targetDir);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating MTP directory: {ex.Message}");
                }

                using var prepared = await _audioTranscodeService.PrepareForTransferAsync(track.FilePath, transcodeOptions, cancellationToken).ConfigureAwait(false);
                mtpClient.UploadFile(prepared.OutputFilePath, destPath);

                current++;
                progress?.Report((double)current / total * 100);
            }
        }, cancellationToken);
    }

    private static bool CheckMtpTrackAlreadyExists(
        IMtpDeviceClient mtpClient,
        string destinationFolder,
        IReadOnlyList<(string ArtistFolder, string AlbumFolder)> candidateDirs,
        string targetFileName,
        string originalFileName)
    {
        foreach (var (artistFolder, albumFolder) in candidateDirs)
        {
            string dir = Path.Combine(destinationFolder, artistFolder, albumFolder);
            string destPath = Path.Combine(dir, targetFileName);
            string originalDestPath = Path.Combine(dir, originalFileName);
            try
            {
                if (mtpClient.FileExists(destPath)
                    || (!string.Equals(destPath, originalDestPath, StringComparison.OrdinalIgnoreCase) && mtpClient.FileExists(originalDestPath)))
                {
                    return true;
                }
            }
            catch
            {
                // 単一候補の確認失敗時は他候補チェックを継続
            }
        }

        return false;
    }

    private static bool CheckFileSystemTrackAlreadyExists(
        string destinationFolder,
        IReadOnlyList<(string ArtistFolder, string AlbumFolder)> candidateDirs,
        string targetFileName,
        string originalFileName)
    {
        foreach (var (artistFolder, albumFolder) in candidateDirs)
        {
            string dir = Path.Combine(destinationFolder, artistFolder, albumFolder);
            string destPath = Path.Combine(dir, targetFileName);
            string originalDestPath = Path.Combine(dir, originalFileName);
            if (File.Exists(destPath)
                || (!string.Equals(destPath, originalDestPath, StringComparison.OrdinalIgnoreCase) && File.Exists(originalDestPath)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 現在の設定から音声フォーマット変換オプションを取得します。
    /// </summary>
    /// <returns>音声フォーマット変換オプション</returns>
    internal AudioTranscodeOptions GetCurrentTranscodeOptions()
    {
        if (_settingsService == null)
        {
            return new AudioTranscodeOptions();
        }

        try
        {
            var settings = _settingsService.LoadSettings();
            return new AudioTranscodeOptions
            {
                ConvertMp4ToM4a = settings.ConvertMp4ToM4a,
                ConvertFlacToMp3 = settings.ConvertFlacToMp3
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading transcode settings: {ex.Message}");
            return new AudioTranscodeOptions();
        }
    }

    /// <summary>
    /// ファイルシステムデバイスへトラック群を転送します（設定に応じて形式変換および一時ファイル後始末を実施）。
    /// </summary>
    /// <param name="tracksToTransfer">転送対象トラック一覧</param>
    /// <param name="destinationFolder">転送先ルートフォルダ</param>
    /// <param name="transcodeOptions">音声フォーマット変換オプション</param>
    /// <param name="progress">進捗通知（0〜100）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    internal Task TransferTracksToFileSystemAsync(
        IReadOnlyList<Track> tracksToTransfer,
        string destinationFolder,
        AudioTranscodeOptions transcodeOptions,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destinationMetadataByTrack = BuildTrackDestinationMetadataMap(_getAlbums(), tracksToTransfer);

        return Task.Run(async () =>
        {
            int total = tracksToTransfer.Count;
            int current = 0;
            foreach (var track in tracksToTransfer)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!System.IO.File.Exists(track.FilePath)) continue;

                var (repArtist, repAlbum) = destinationMetadataByTrack.TryGetValue(track, out var metadata)
                    ? metadata
                    : (ResolveEffectiveDestinationArtist(track, representativeArtist: null), track.Album);
                var candidateDirs = BuildTrackCandidateDirectories(track, repArtist, repAlbum);
                var (artist, album) = candidateDirs[0];
                string targetFileName = _audioTranscodeService.ResolveTargetFileName(track.FilePath, transcodeOptions);
                string originalFileName = System.IO.Path.GetFileName(track.FilePath);

                string targetDir = System.IO.Path.Combine(destinationFolder, artist, album);
                string destPath = System.IO.Path.Combine(targetDir, targetFileName);

                // 変換後または元ファイル名のいずれかが新配置または旧配置に既に存在する場合はスキップ（#170 / #398 重複判定整合）
                if (CheckFileSystemTrackAlreadyExists(destinationFolder, candidateDirs, targetFileName, originalFileName))
                {
                    current++;
                    progress?.Report((double)current / total * 100);
                    continue;
                }

                if (!System.IO.Directory.Exists(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);
                }

                using var prepared = await _audioTranscodeService.PrepareForTransferAsync(track.FilePath, transcodeOptions, cancellationToken).ConfigureAwait(false);
                System.IO.File.Copy(prepared.OutputFilePath, destPath, true);

                current++;
                progress?.Report((double)current / total * 100);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 選択中のアルバム・トラックから未転送（IsOnDevice == false）の転送対象トラック一覧を重複なく収集します。
    /// </summary>
    /// <param name="albums">対象アルバム一覧</param>
    /// <returns>未転送の転送対象トラック一覧</returns>
    internal static List<Track> CollectTransferTargetTracks(IReadOnlyList<Album> albums)
    {
        var tracksToTransfer = new List<Track>();

        foreach (var album in albums.Where(a => a.IsSelected && !a.IsOnDevice))
        {
            foreach (var track in album.Tracks.Where(t => !t.IsOnDevice))
            {
                if (!tracksToTransfer.Contains(track))
                {
                    tracksToTransfer.Add(track);
                }
            }
        }

        foreach (var album in albums)
        {
            foreach (var track in album.Tracks.Where(t => t.IsSelected && !t.IsOnDevice))
            {
                if (!tracksToTransfer.Contains(track))
                {
                    tracksToTransfer.Add(track);
                }
            }
        }

        return tracksToTransfer;
    }

    private void ShowDeviceManager()
    {
        if (SelectedDevice == null) return;

        string basePath = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;
        var albums = _getAlbums();
        var dialogViewModel = new DeviceManagerViewModel(SelectedDevice, basePath, albums.ToList());
        var dialog = new DeviceManagerDialog(dialogViewModel);
        dialog.ShowDialog();

        // デバイス管理ダイアログを閉じた後、ディレクトリ表示・重複状態・空き容量を最新化する
        if (!string.IsNullOrEmpty(CurrentDevicePath))
        {
            LoadDeviceDirectories(CurrentDevicePath);
        }
        else
        {
            CheckDeviceAlbums();
        }

        _ = RefreshSelectedDeviceStorageAsync();
    }

    private static MediaDeviceClientAdapter? DefaultResolveMtpClient(MainViewModel.DeviceViewModel device)
        => device.MtpDevice != null ? new MediaDeviceClientAdapter(device.MtpDevice) : null;

    private static void DefaultShowMessage(string message, string caption)
    {
        if (string.IsNullOrEmpty(caption))
        {
            MessageBox.Show(message);
        }
        else
        {
            MessageBox.Show(message, caption);
        }
    }

    /// <summary>
    /// MTP デバイス操作を抽象化する内部クライアントインターフェース
    /// </summary>
    internal interface IMtpDeviceClient
    {
        /// <summary>
        /// デバイスへ接続済みかどうかを取得します。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// デバイスへ接続します。
        /// </summary>
        void Connect();

        /// <summary>
        /// デバイスから切断します。
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 指定ディレクトリが存在するかどうかを判定します。
        /// </summary>
        /// <param name="path">判定対象パス</param>
        /// <returns>存在する場合はtrue</returns>
        bool DirectoryExists(string path);

        /// <summary>
        /// 指定ディレクトリを作成します。
        /// </summary>
        /// <param name="path">作成対象パス</param>
        void CreateDirectory(string path);

        /// <summary>
        /// 指定ディレクトリ直下のサブディレクトリ一覧を取得します。
        /// </summary>
        /// <param name="path">走査対象パス</param>
        /// <returns>サブディレクトリパス一覧</returns>
        IEnumerable<string> GetDirectories(string path);

        /// <summary>
        /// 指定ディレクトリ直下のファイル一覧を取得します。
        /// </summary>
        /// <param name="path">走査対象パス</param>
        /// <returns>ファイルパス一覧</returns>
        IEnumerable<string> GetFiles(string path);

        /// <summary>
        /// 指定ファイルが存在するかどうかを判定します。
        /// </summary>
        /// <param name="path">判定対象ファイルパス</param>
        /// <returns>存在する場合はtrue</returns>
        bool FileExists(string path);

        /// <summary>
        /// ローカルファイルをデバイスへアップロードします。
        /// </summary>
        /// <param name="sourceFilePath">転送元ローカルファイルパス</param>
        /// <param name="destinationFilePath">転送先デバイスファイルパス</param>
        void UploadFile(string sourceFilePath, string destinationFilePath);
    }

    private sealed class MediaDeviceClientAdapter : IMtpDeviceClient
    {
        private readonly MediaDevice _device;

        public MediaDeviceClientAdapter(MediaDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public bool IsConnected => _device.IsConnected;

        public void Connect() => _device.Connect();

        public void Disconnect() => _device.Disconnect();

        public bool DirectoryExists(string path) => _device.DirectoryExists(path);

        public void CreateDirectory(string path) => _device.CreateDirectory(path);

        public IEnumerable<string> GetDirectories(string path) => _device.GetDirectories(path);

        public IEnumerable<string> GetFiles(string path) => _device.GetFiles(path);

        public bool FileExists(string path) => _device.FileExists(path);

        public void UploadFile(string sourceFilePath, string destinationFilePath) => _device.UploadFile(sourceFilePath, destinationFilePath);
    }

    private sealed class PassthroughAudioTranscodeService : IAudioTranscodeService
    {
        public static readonly PassthroughAudioTranscodeService Instance = new();

        public bool NeedsTranscode(string sourceFilePath, AudioTranscodeOptions options) => false;

        public string ResolveTargetFileName(string sourceFilePath, AudioTranscodeOptions options)
            => Path.GetFileName(sourceFilePath ?? string.Empty);

        public Task<TranscodedAudioResult> PrepareForTransferAsync(
            string sourceFilePath,
            AudioTranscodeOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(TranscodedAudioResult.Passthrough(sourceFilePath));
    }
}
