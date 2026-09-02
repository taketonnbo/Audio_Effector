using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.ViewModels;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// ポータブルデバイス（MTP）の接続検出、デバイス内楽曲一覧、転送進捗を担当するViewModel
/// </summary>
public class DeviceSyncViewModel : ViewModelBase
{
    private readonly DataTransferApplicationService _dataTransferService;

    private bool _isDeviceConnected;
    private bool _isTransferring;
    private double _transferProgress;
    private string _statusMessage = "デバイス未接続";

    /// <summary>
    /// デバイス上のトラックコレクション
    /// </summary>
    public ObservableCollection<DeviceTrack> DeviceTracks { get; } = new();

    /// <summary>
    /// デバイス上のアルバムコレクション
    /// </summary>
    public ObservableCollection<DeviceAlbum> DeviceAlbums { get; } = new();

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
    /// 転送進捗率（0.0〜1.0）
    /// </summary>
    public double TransferProgress
    {
        get => _transferProgress;
        set => SetProperty(ref _transferProgress, value);
    }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

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

    /// <summary>
    /// DeviceSyncViewModelを初期化します
    /// </summary>
    /// <param name="dataTransferService">データ転送アプリケーションサービス</param>
    public DeviceSyncViewModel(DataTransferApplicationService dataTransferService)
    {
        _dataTransferService = dataTransferService ?? throw new ArgumentNullException(nameof(dataTransferService));

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

        _ = CheckAndLoadDeviceAsync();
    }

    /// <summary>
    /// デバイスの接続状態を確認し、楽曲一覧を読み込みます
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task CheckAndLoadDeviceAsync()
    {
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
        if (!IsDeviceConnected) return;

        IsTransferring = true;
        TransferProgress = 0.0;
        StatusMessage = "転送中...";

        var progress = new Progress<double>(p => TransferProgress = p);

        try
        {
            int count = await _dataTransferService.TransferTracksAsync(tracks, "Music", progress);
            StatusMessage = $"転送完了: {count} 曲転送しました";
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
    /// <param name="track">削除対象トラック</param>
    /// <returns>非同期タスク</returns>
    public async Task DeleteDeviceTrackAsync(DeviceTrack track)
    {
        bool deleted = await _dataTransferService.DeleteDeviceTrackAsync(track.Path);
        if (deleted)
        {
            DeviceTracks.Remove(track);
        }
    }
}
