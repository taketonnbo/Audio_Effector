using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using MediaDevices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    /// ライブラリ内のアルバムがデバイス上に存在するかどうかをチェックし、ステータスを更新します。
    /// </summary>
    private async void CheckDeviceAlbums()
    {
        if (SelectedDevice == null || string.IsNullOrEmpty(CurrentDevicePath)) return;

        var albums = _getAlbums();

        await Task.Run(() =>
        {
            foreach (var album in albums)
            {
                bool allTracksExist = true;

                if (album.Tracks != null && album.Tracks.Count > 0)
                {
                    foreach (var track in album.Tracks)
                    {
                        string artist = SanitizeFileName(track.Artist);
                        string albumName = SanitizeFileName(track.Album);
                        string fileName = System.IO.Path.GetFileName(track.FilePath);
                        bool trackExists = false;

                        try
                        {
                            if (SelectedDevice.Type == MainViewModel.DeviceType.FileSystem)
                            {
                                string path = System.IO.Path.Combine(CurrentDevicePath, artist, albumName, fileName);
                                trackExists = System.IO.File.Exists(path);
                            }
                            else if (SelectedDevice.Type == MainViewModel.DeviceType.MTP && SelectedDevice.MtpDevice != null && SelectedDevice.MtpDevice.IsConnected)
                            {
                                string path = System.IO.Path.Combine(CurrentDevicePath, artist, albumName, fileName);
                                trackExists = SelectedDevice.MtpDevice.FileExists(path);
                            }
                        }
                        catch { }

                        if (!trackExists)
                        {
                            allTracksExist = false;
                            break;
                        }
                    }
                }
                else
                {
                    allTracksExist = false;
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    album.IsOnDevice = allTracksExist;
                    if (allTracksExist) album.IsSelected = false;
                });
            }
        });
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
        var tracksToTransfer = new List<Track>();

        foreach (var album in albums.Where(a => a.IsSelected))
        {
            tracksToTransfer.AddRange(album.Tracks);
        }

        foreach (var album in albums)
        {
            foreach (var track in album.Tracks.Where(t => t.IsSelected))
            {
                if (!tracksToTransfer.Contains(track))
                {
                    tracksToTransfer.Add(track);
                }
            }
        }

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
            CheckDeviceAlbums(); // Refresh status

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

    private void ShowDeviceManager()
    {
        if (SelectedDevice == null) return;

        string basePath = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;
        var albums = _getAlbums();
        var dialogViewModel = new DeviceManagerViewModel(SelectedDevice, basePath, albums.ToList());
        var dialog = new DeviceManagerDialog(dialogViewModel);
        dialog.ShowDialog();

        // デバイス管理ダイアログを閉じた後、デバイス上のアルバム状況が変更されている可能性があるためチェックを再実行
        CheckDeviceAlbums();

        // 現在のディレクトリ表示も更新する
        if (!string.IsNullOrEmpty(CurrentDevicePath))
        {
            LoadDeviceDirectories(CurrentDevicePath);
        }
    }
}
