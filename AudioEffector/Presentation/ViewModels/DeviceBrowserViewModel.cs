using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using MediaDevices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
public class DeviceBrowserViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<Album>> _getAlbums;
    private readonly Action _onCheckDeviceAlbumsRequested;
    private CancellationTokenSource? _checkDeviceAlbumsCts;

    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".mp4", ".aac", ".ogg", ".wma", ".aiff", ".aif", ".alac", ".opus", ".dsf", ".dff"
    };

    /// <summary>
    /// 接続されている外部デバイス一覧を取得または設定します
    /// </summary>
    public ObservableCollection<MainViewModel.DeviceViewModel> RemovableDrives { get; set; } = new ObservableCollection<MainViewModel.DeviceViewModel>();

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
                if (_selectedDevice?.Type == MainViewModel.DeviceType.MTP && _selectedDevice.MtpDevice != null && _selectedDevice.MtpDevice.IsConnected)
                {
                    try { _selectedDevice.MtpDevice.Disconnect(); } catch { }
                }

                _selectedDevice = value;
                OnPropertyChanged();

                if (_selectedDevice != null)
                {
                    if (_selectedDevice.Type == MainViewModel.DeviceType.MTP && _selectedDevice.MtpDevice != null)
                    {
                        try
                        {
                            _selectedDevice.MtpDevice.Connect();
                            LoadDeviceDirectories(@"\"); // Root for MTP
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to connect to device: {ex.Message}");
                        }
                    }
                    else if (_selectedDevice.Type == MainViewModel.DeviceType.FileSystem && _selectedDevice.Drive != null)
                    {
                        LoadDeviceDirectories(_selectedDevice.Drive.RootDirectory.FullName);
                    }
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
            _currentDevicePath = value;
            OnPropertyChanged();
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
    public DeviceBrowserViewModel(
        ObservableCollection<Album> albumsRef,
        Func<IReadOnlyList<Album>> getAlbums,
        Action onCheckDeviceAlbumsRequested)
    {
        _albumsRef = albumsRef ?? throw new ArgumentNullException(nameof(albumsRef));
        _getAlbums = getAlbums ?? throw new ArgumentNullException(nameof(getAlbums));
        _onCheckDeviceAlbumsRequested = onCheckDeviceAlbumsRequested ?? throw new ArgumentNullException(nameof(onCheckDeviceAlbumsRequested));

        RefreshDrivesCommand = new RelayCommand(o => RefreshDrives());
        TransferSelectedCommand = new RelayCommand(o => TransferSelected());
        NavigateDirectoryCommand = new RelayCommand(o => NavigateDirectory(o as MainViewModel.DirectoryItem));
        NavigateUpCommand = new RelayCommand(o => NavigateUp());
        RefreshDirectoryCommand = new RelayCommand(o => LoadDeviceDirectories(CurrentDevicePath));
        ShowDeviceManagerCommand = new RelayCommand(o => ShowDeviceManager());
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
            RemovableDrives.Add(new MainViewModel.DeviceViewModel
            {
                Name = $"{drive.VolumeLabel} ({drive.Name})",
                Type = MainViewModel.DeviceType.FileSystem,
                Drive = drive,
                RootPath = drive.RootDirectory.FullName
            });
        }

        // Add MTP Devices
        // MTPデバイスの追加
        try
        {
            var devices = MediaDevice.GetDevices();
            foreach (var device in devices)
            {
                RemovableDrives.Add(new MainViewModel.DeviceViewModel
                {
                    Name = device.FriendlyName,
                    Type = MainViewModel.DeviceType.MTP,
                    MtpDevice = device,
                    RootPath = @"\" // MTP root
                });
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
            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP && SelectedDevice.MtpDevice != null)
            {
                if (SelectedDevice.MtpDevice.IsConnected)
                {
                    // Add Directories
                    var dirs = SelectedDevice.MtpDevice.GetDirectories(path);
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
                    var files = SelectedDevice.MtpDevice.GetFiles(path);
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
            MessageBox.Show($"Error loading directory: {ex.Message}");
        }

        CheckDeviceAlbums();
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
        _checkDeviceAlbumsCts?.Cancel();
        _checkDeviceAlbumsCts?.Dispose();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        _checkDeviceAlbumsCts = linkedCts;
        var ct = linkedCts.Token;

        var selectedDevice = SelectedDevice;
        var currentPath = CurrentDevicePath;
        var albums = _getAlbums();

        if (selectedDevice == null || string.IsNullOrEmpty(currentPath))
        {
            RunOnUiThread(() => ApplyDeviceTrackIndex(albums, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            return;
        }

        try
        {
            bool recursive = !IsDeviceRootPath(selectedDevice, currentPath);

            var index = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (selectedDevice.Type == MainViewModel.DeviceType.FileSystem)
                {
                    return BuildFileSystemAudioIndex(currentPath, recursive, ct);
                }

                if (selectedDevice.Type == MainViewModel.DeviceType.MTP
                    && selectedDevice.MtpDevice != null
                    && selectedDevice.MtpDevice.IsConnected)
                {
                    return BuildMtpAudioIndex(selectedDevice.MtpDevice, currentPath, recursive, ct);
                }

                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            RunOnUiThread(() => ApplyDeviceTrackIndex(albums, index));
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
    {
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return index;
        }

        var pendingDirs = new Stack<string>();
        pendingDirs.Push(rootPath);

        while (pendingDirs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pendingDirs.Pop();

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

                    string key = NormalizeTrackDuplicateKey(file);
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

            if (!recursive)
            {
                continue;
            }

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
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mtpDevice == null || !mtpDevice.IsConnected || string.IsNullOrWhiteSpace(rootPath))
        {
            return index;
        }

        var pendingDirs = new Stack<string>();
        pendingDirs.Push(rootPath);

        while (pendingDirs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pendingDirs.Pop();

            try
            {
                foreach (var file in mtpDevice.GetFiles(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string ext = Path.GetExtension(file);
                    if (!SupportedAudioExtensions.Contains(ext))
                    {
                        continue;
                    }

                    string key = NormalizeTrackDuplicateKey(file);
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

            if (!recursive)
            {
                continue;
            }

            try
            {
                foreach (var subDir in mtpDevice.GetDirectories(dir))
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

    /// <summary>
    /// 構築したデバイスファイルインデックスを用いて、各トラック・アルバムの IsOnDevice 状態を一括反映します。
    /// </summary>
    /// <param name="albums">状態更新対象のアルバム一覧</param>
    /// <param name="deviceFileNameKeys">デバイス上の正規化済みファイル名キーセット</param>
    internal static void ApplyDeviceTrackIndex(IReadOnlyList<Album> albums, IReadOnlySet<string> deviceFileNameKeys)
    {
        foreach (var album in albums)
        {
            if (album.Tracks != null && album.Tracks.Count > 0)
            {
                foreach (var track in album.Tracks)
                {
                    string key = NormalizeTrackDuplicateKey(track.FilePath);
                    bool exists = !string.IsNullOrEmpty(key) && deviceFileNameKeys.Contains(key);
                    track.IsOnDevice = exists;
                    if (exists)
                    {
                        track.IsSelected = false;
                    }
                }
            }

            album.UpdateIsOnDeviceState();
            if (album.IsOnDevice)
            {
                album.IsSelected = false;
            }
        }
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void NavigateDirectory(MainViewModel.DirectoryItem? dir)
    {
        if (dir == null || !dir.IsFolder) return;
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
                    LoadDeviceDirectories(parent.FullName);
                }
            }
            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP)
            {
                if (CurrentDevicePath == @"\" || CurrentDevicePath == "/") return;

                var parentPath = Path.GetDirectoryName(CurrentDevicePath);
                if (string.IsNullOrEmpty(parentPath)) parentPath = @"\";
                LoadDeviceDirectories(parentPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating up: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }

    /// <summary>
    /// 選択されたアルバムまたはトラックを、現在選択されているデバイスへ転送します。
    /// </summary>
    private async void TransferSelected()
    {
        if (SelectedDevice == null)
        {
            MessageBox.Show("Please select a device first.", "No Device Selected");
            return;
        }

        string destinationFolder = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;

        if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem && !destinationFolder.StartsWith(SelectedDevice.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Current folder is not on the selected drive.", "Error");
            return;
        }

        var albums = _getAlbums();
        var tracksToTransfer = CollectTransferTargetTracks(albums);

        if (tracksToTransfer.Count == 0)
        {
            MessageBox.Show("Please select at least one album or track to transfer.", "No Items Selected");
            return;
        }

        IsTransferring = true;
        TransferProgress = 0;

        try
        {
            var progress = new Progress<double>(p => TransferProgress = p);

            if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem)
            {
                await Task.Run(() =>
                {
                    int total = tracksToTransfer.Count;
                    int current = 0;
                    foreach (var track in tracksToTransfer)
                    {
                        if (!System.IO.File.Exists(track.FilePath)) continue;

                        string artist = SanitizeFileName(track.Artist);
                        string album = SanitizeFileName(track.Album);
                        string fileName = System.IO.Path.GetFileName(track.FilePath);

                        string targetDir = System.IO.Path.Combine(destinationFolder, artist, album);
                        string destPath = System.IO.Path.Combine(targetDir, fileName);

                        // Skip if already exists
                        if (System.IO.File.Exists(destPath))
                        {
                            current++;
                            ((IProgress<double>)progress).Report((double)current / total * 100);
                            continue;
                        }

                        if (!System.IO.Directory.Exists(targetDir))
                        {
                            System.IO.Directory.CreateDirectory(targetDir);
                        }

                        System.IO.File.Copy(track.FilePath, destPath, true);

                        current++;
                        ((IProgress<double>)progress).Report((double)current / total * 100);
                    }
                });
            }
            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP && SelectedDevice.MtpDevice != null)
            {
                var mtpDevice = SelectedDevice.MtpDevice;
                await Task.Run(() =>
                {
                    int total = tracksToTransfer.Count;
                    int current = 0;
                    foreach (var track in tracksToTransfer)
                    {
                        if (!System.IO.File.Exists(track.FilePath)) continue;

                        string artist = SanitizeFileName(track.Artist);
                        string album = SanitizeFileName(track.Album);
                        string fileName = System.IO.Path.GetFileName(track.FilePath);

                        string targetDir = System.IO.Path.Combine(destinationFolder, artist, album);

                        try
                        {
                            string artistDir = System.IO.Path.Combine(destinationFolder, artist);
                            if (!mtpDevice.DirectoryExists(artistDir))
                            {
                                mtpDevice.CreateDirectory(artistDir);
                            }
                            if (!mtpDevice.DirectoryExists(targetDir))
                            {
                                mtpDevice.CreateDirectory(targetDir);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error creating MTP directory: {ex.Message}");
                        }

                        string destPath = System.IO.Path.Combine(targetDir, fileName);

                        // Skip if already exists
                        bool fileExists = false;
                        try { fileExists = mtpDevice.FileExists(destPath); } catch { }

                        if (fileExists)
                        {
                            current++;
                            ((IProgress<double>)progress).Report((double)current / total * 100);
                            continue;
                        }

                        mtpDevice.UploadFile(track.FilePath, destPath);

                        current++;
                        ((IProgress<double>)progress).Report((double)current / total * 100);
                    }
                });
            }

            LoadDeviceDirectories(destinationFolder);

            MessageBox.Show("Transfer completed successfully!", "Success");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Transfer failed: {ex.Message}", "Error");
        }
        finally
        {
            IsTransferring = false;
            TransferProgress = 0;
        }
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

        // デバイス管理ダイアログを閉じた後、ディレクトリ表示およびデバイス上の曲・アルバム存在状態を最新化する
        if (!string.IsNullOrEmpty(CurrentDevicePath))
        {
            LoadDeviceDirectories(CurrentDevicePath);
        }
        else
        {
            CheckDeviceAlbums();
        }
    }
}
