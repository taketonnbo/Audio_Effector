using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Presentation.Views;
using MediaDevices;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// ポータブルデバイス（MTP）およびマスストレージの接続検出、デバイス内楽曲・ディレクトリブラウズ、転送進捗を担当するViewModel
/// </summary>
public class DeviceSyncViewModel : ViewModelBase
{
    private readonly DataTransferApplicationService? _dataTransferService;

    private bool _isDeviceConnected;
    private bool _isTransferring;
    private double _transferProgress;
    private string _transferStatus = string.Empty;
    private string _statusMessage = "デバイス未接続";
    private string _currentDevicePath = string.Empty;
    private DeviceViewModel? _selectedDevice;
    private DirectoryItem? _selectedDeviceDirectory;

    #region Public Properties

    /// <summary>
    /// 接続されている外部デバイス一覧
    /// </summary>
    public ObservableCollection<DeviceViewModel> RemovableDrives { get; } = [];

    /// <summary>
    /// 現在選択されている同期対象デバイス
    /// </summary>
    public DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnSelectedDeviceChanged(value);
            }
        }
    }

    /// <summary>
    /// デバイス内のディレクトリアイテム一覧
    /// </summary>
    public ObservableCollection<DirectoryItem> DeviceDirectories { get; } = [];

    /// <summary>
    /// 現在選択されているデバイス内フォルダー
    /// </summary>
    public DirectoryItem? SelectedDeviceDirectory
    {
        get => _selectedDeviceDirectory;
        set => SetProperty(ref _selectedDeviceDirectory, value);
    }

    /// <summary>
    /// 現在のデバイス内閲覧パス
    /// </summary>
    public string CurrentDevicePath
    {
        get => _currentDevicePath;
        set => SetProperty(ref _currentDevicePath, value);
    }

    /// <summary>
    /// デバイス上のトラックコレクション
    /// </summary>
    public ObservableCollection<DeviceTrack> DeviceTracks { get; } = [];

    /// <summary>
    /// デバイス上のアルバムコレクション
    /// </summary>
    public ObservableCollection<DeviceAlbum> DeviceAlbums { get; } = [];

    /// <summary>
    /// デバイスが接続されているかどうか
    /// </summary>
    public bool IsDeviceConnected
    {
        get => _isDeviceConnected;
        set => SetProperty(ref _isDeviceConnected, value);
    }

    /// <summary>
    /// データ転送中かどうか
    /// </summary>
    public bool IsTransferring
    {
        get => _isTransferring;
        set => SetProperty(ref _isTransferring, value);
    }

    /// <summary>
    /// 転送進捗率（0.0〜100.0）
    /// </summary>
    public double TransferProgress
    {
        get => _transferProgress;
        set => SetProperty(ref _transferProgress, value);
    }

    /// <summary>
    /// 転送状態テキスト
    /// </summary>
    public string TransferStatus
    {
        get => _transferStatus;
        set => SetProperty(ref _transferStatus, value);
    }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    #endregion

    #region Commands

    /// <summary>
    /// ドライブ再検出コマンド
    /// </summary>
    public ICommand RefreshDrivesCommand { get; }

    /// <summary>
    /// ディレクトリ移動コマンド
    /// </summary>
    public ICommand NavigateDirectoryCommand { get; }

    /// <summary>
    /// 上の階層へ移動コマンド
    /// </summary>
    public ICommand NavigateUpCommand { get; }

    /// <summary>
    /// ディレクトリ再読み込みコマンド
    /// </summary>
    public ICommand RefreshDirectoryCommand { get; }

    /// <summary>
    /// デバイスマネージャー表示コマンド
    /// </summary>
    public ICommand ShowDeviceManagerCommand { get; }

    /// <summary>
    /// 選択楽曲転送コマンド
    /// </summary>
    public ICommand TransferSelectedCommand { get; }

    /// <summary>
    /// デバイス情報再取得コマンド
    /// </summary>
    public ICommand RefreshDeviceCommand { get; }

    /// <summary>
    /// 選択トラック転送コマンド
    /// </summary>
    public ICommand TransferTracksCommand { get; }

    /// <summary>
    /// デバイストラック削除コマンド
    /// </summary>
    public ICommand DeleteDeviceTrackCommand { get; }

    #endregion

    /// <summary>
    /// DeviceSyncViewModelを初期化します
    /// </summary>
    /// <param name="dataTransferService">データ転送アプリケーションサービス（null許容）</param>
    public DeviceSyncViewModel(DataTransferApplicationService? dataTransferService = null)
    {
        _dataTransferService = dataTransferService;

        RefreshDrivesCommand = new RelayCommand(_ => RefreshDrives());
        NavigateDirectoryCommand = new RelayCommand(o => NavigateDirectory(o as DirectoryItem));
        NavigateUpCommand = new RelayCommand(_ => NavigateUp());
        RefreshDirectoryCommand = new RelayCommand(_ => LoadDeviceDirectories(CurrentDevicePath));
        ShowDeviceManagerCommand = new RelayCommand(_ => ShowDeviceManager());
        TransferSelectedCommand = new RelayCommand(_ => { });
        RefreshDeviceCommand = new RelayCommand(async _ => await CheckAndLoadDeviceAsync());

        TransferTracksCommand = new RelayCommand(async tracks =>
        {
            if (tracks is IEnumerable<Track> tList)
            {
                await TransferTracksAsync(tList);
            }
        });

        DeleteDeviceTrackCommand = new RelayCommand(async track =>
        {
            if (track is DeviceTrack dt)
            {
                await DeleteDeviceTrackAsync(dt);
            }
        });

        RefreshDrives();
        _ = CheckAndLoadDeviceAsync();
    }

    #region Device Explorer Methods

    /// <summary>
    /// 接続されている外部デバイス一覧を再検出します
    /// </summary>
    public void RefreshDrives()
    {
        try
        {
            RemovableDrives.Clear();

            // 1. FileSystem Drives
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.IsReady && drive.DriveType == DriveType.Removable)
                {
                    string label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Removable Disk" : drive.VolumeLabel;
                    RemovableDrives.Add(new DeviceViewModel
                    {
                        Name = $"{label} ({drive.Name.TrimEnd('\\')})",
                        Type = DeviceType.FileSystem,
                        Drive = drive,
                        RootPath = drive.RootDirectory.FullName
                    });
                }
            }

            // 2. MTP Devices
            try
            {
                var mtpDevices = MediaDevice.GetDevices();
                foreach (var dev in mtpDevices)
                {
                    RemovableDrives.Add(new DeviceViewModel
                    {
                        Name = dev.Description,
                        Type = DeviceType.MTP,
                        MtpDevice = dev,
                        RootPath = @"\"
                    });
                }
            }
            catch
            {
                // MTP detection failed or unsupported
            }

            IsDeviceConnected = RemovableDrives.Count > 0;
            SelectedDevice = RemovableDrives.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"ドライブ検出エラー: {ex.Message}";
        }
    }

    private void OnSelectedDeviceChanged(DeviceViewModel? newDevice)
    {
        if (newDevice != null)
        {
            if (newDevice.Type == DeviceType.MTP && newDevice.MtpDevice != null)
            {
                try
                {
                    if (!newDevice.MtpDevice.IsConnected)
                    {
                        newDevice.MtpDevice.Connect();
                    }
                    LoadDeviceDirectories(@"\");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"MTP接続失敗: {ex.Message}";
                }
            }
            else if (newDevice.Type == DeviceType.FileSystem && newDevice.Drive != null)
            {
                LoadDeviceDirectories(newDevice.Drive.RootDirectory.FullName);
            }
        }
        else
        {
            DeviceDirectories.Clear();
            CurrentDevicePath = string.Empty;
        }
    }

    /// <summary>
    /// 指定されたパスのデバイス内ディレクトリ一覧を読み込みます
    /// </summary>
    /// <param name="path">読み込み対象パス</param>
    public void LoadDeviceDirectories(string path)
    {
        if (SelectedDevice == null || string.IsNullOrEmpty(path)) return;

        try
        {
            DeviceDirectories.Clear();
            CurrentDevicePath = path;

            if (SelectedDevice.Type == DeviceType.FileSystem)
            {
                if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name))
                    {
                        if ((dir.Attributes & FileAttributes.Hidden) != 0) continue;
                        DeviceDirectories.Add(new DirectoryItem
                        {
                            Name = dir.Name,
                            FullPath = dir.FullName,
                            IsFolder = true
                        });
                    }

                    foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name))
                    {
                        if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                        DeviceDirectories.Add(new DirectoryItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsFolder = false
                        });
                    }
                }
            }
            else if (SelectedDevice.Type == DeviceType.MTP && SelectedDevice.MtpDevice != null)
            {
                if (SelectedDevice.MtpDevice.DirectoryExists(path))
                {
                    var dirs = SelectedDevice.MtpDevice.GetDirectories(path);
                    foreach (var dir in dirs.OrderBy(d => d))
                    {
                        string name = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(name)) name = dir;
                        DeviceDirectories.Add(new DirectoryItem
                        {
                            Name = name,
                            FullPath = dir,
                            IsFolder = true
                        });
                    }

                    var files = SelectedDevice.MtpDevice.GetFiles(path);
                    foreach (var file in files.OrderBy(f => f))
                    {
                        string name = Path.GetFileName(file);
                        if (string.IsNullOrEmpty(name)) name = file;
                        DeviceDirectories.Add(new DirectoryItem
                        {
                            Name = name,
                            FullPath = file,
                            IsFolder = false
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"ディレクトリ読込エラー: {ex.Message}";
        }
    }

    /// <summary>
    /// 指定されたディレクトリへ移動します
    /// </summary>
    /// <param name="dir">移動先ディレクトリアイテム</param>
    public void NavigateDirectory(DirectoryItem? dir)
    {
        if (dir != null && dir.IsFolder)
        {
            LoadDeviceDirectories(dir.FullPath);
        }
    }

    /// <summary>
    /// 1つ親の階層へ移動します
    /// </summary>
    public void NavigateUp()
    {
        if (SelectedDevice == null) return;

        if (SelectedDevice.Type == DeviceType.FileSystem)
        {
            if (!string.IsNullOrEmpty(CurrentDevicePath))
            {
                var parent = Directory.GetParent(CurrentDevicePath);
                if (parent != null && parent.FullName.StartsWith(SelectedDevice.RootPath, StringComparison.OrdinalIgnoreCase))
                {
                    LoadDeviceDirectories(parent.FullName);
                }
            }
        }
        else if (SelectedDevice.Type == DeviceType.MTP)
        {
            if (CurrentDevicePath != @"\" && !string.IsNullOrEmpty(CurrentDevicePath))
            {
                string? parentPath = Path.GetDirectoryName(CurrentDevicePath);
                if (string.IsNullOrEmpty(parentPath)) parentPath = @"\";
                LoadDeviceDirectories(parentPath);
            }
        }
    }

    /// <summary>
    /// デバイスストレージマネージャーダイアログを開きます
    /// </summary>
    /// <param name="pcAlbums">PC側のアルバム一覧（null許容）</param>
    public void ShowDeviceManager(List<Album>? pcAlbums = null)
    {
        if (SelectedDevice == null)
        {
            MessageBox.Show("デバイスが選択されていません。\nNo device selected.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string basePath = SelectedDevice.RootPath;
            if (SelectedDevice.Type == DeviceType.MTP)
            {
                basePath = string.IsNullOrEmpty(CurrentDevicePath) ? @"\" : CurrentDevicePath;
            }

            var vm = new DeviceManagerViewModel(SelectedDevice, basePath, pcAlbums ?? []);
            var dialog = new DeviceManagerDialog(vm);
            dialog.ShowDialog();
            LoadDeviceDirectories(CurrentDevicePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"デバイスマネージャーの起動に失敗しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Data Transfer Methods

    /// <summary>
    /// デバイスの接続状態を確認し、楽曲一覧を読み込みます
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task CheckAndLoadDeviceAsync()
    {
        if (_dataTransferService == null) return;

        IsDeviceConnected = await _dataTransferService.IsDeviceConnectedAsync();
        if (!IsDeviceConnected)
        {
            StatusMessage = "デバイスが接続されていません";
            DeviceTracks.Clear();
            DeviceAlbums.Clear();
            return;
        }

        StatusMessage = "デバイス読み込み中...";
        var tracks = await _dataTransferService.GetDeviceTracksAsync();
        var albums = await _dataTransferService.GetDeviceAlbumsAsync();

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            DeviceTracks.Clear();
            foreach (var t in tracks)
            {
                DeviceTracks.Add(t);
            }

            DeviceAlbums.Clear();
            foreach (var a in albums)
            {
                DeviceAlbums.Add(a);
            }

            StatusMessage = $"接続中: {DeviceTracks.Count} 曲検出";
        });
    }

    /// <summary>
    /// 指定されたトラックコレクションをデバイスへ転送します
    /// </summary>
    /// <param name="tracks">転送対象トラック</param>
    /// <returns>非同期タスク</returns>
    public async Task TransferTracksAsync(IEnumerable<Track> tracks)
    {
        if (!IsDeviceConnected || _dataTransferService == null) return;

        IsTransferring = true;
        TransferProgress = 0.0;
        StatusMessage = "転送中...";
        TransferStatus = "転送中...";

        var progress = new Progress<double>(p =>
        {
            TransferProgress = p;
            StatusMessage = $"転送中: {(int)(p * 100)}%";
            TransferStatus = $"転送中: {(int)(p * 100)}%";
        });

        try
        {
            int count = await _dataTransferService.TransferTracksAsync(tracks, "Music", progress);
            StatusMessage = $"転送完了: {count} 曲転送しました";
            TransferStatus = $"転送完了: {count} 曲転送しました";
            await CheckAndLoadDeviceAsync();
        }
        finally
        {
            IsTransferring = false;
        }
    }

    /// <summary>
    /// デバイス上の指定トラックを削除します
    /// </summary>
    /// <param name="track">削除対象デバイストラック</param>
    /// <returns>非同期タスク</returns>
    public async Task DeleteDeviceTrackAsync(DeviceTrack track)
    {
        if (!IsDeviceConnected || _dataTransferService == null) return;
        try
        {
            await _dataTransferService.DeleteDeviceTrackAsync(track.Path);
            DeviceTracks.Remove(track);
            StatusMessage = $"削除完了: {track.Title}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"削除エラー: {ex.Message}";
        }
    }

    #endregion
}
