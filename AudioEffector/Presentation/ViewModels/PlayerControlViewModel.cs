using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.ViewModels;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 音声の再生・一時停止・停止・シーク・音量制御・スペクトラム描画データバインディングを担当するViewModel
/// </summary>
public class PlayerControlViewModel : ViewModelBase,
    IHandle<TrackChangedEvent>,
    IHandle<PlaybackStateChangedEvent>,
    IHandle<VolumeChangedEvent>
{
    private readonly AudioApplicationService _audioService;
    private readonly IEventBus _eventBus;
    private readonly ISpectrumCalculator _spectrumCalculator;

    private Track? _currentTrack;
    private bool _isPlaying;
    private double _position;
    private double _volume = 0.5;
    private bool _isMuted;
    private bool _isShuffle;
    private int _repeatMode; // 0: None, 1: All, 2: One
    private string _currentTime = "00:00";
    private string _totalTime = "00:00";

    /// <summary>
    /// スペクトラムバーの描画値コレクション（64バンド）
    /// </summary>
    public ObservableCollection<double> SpectrumValues { get; } = new();

    /// <summary>
    /// 現在再生中のトラック
    /// </summary>
    public Track? CurrentTrack
    {
        get => _currentTrack;
        set => SetProperty(ref _currentTrack, value);
    }

    /// <summary>
    /// 再生中フラグ
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>
    /// 再生進捗位置（0.0〜1.0）
    /// </summary>
    public double Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value) && CurrentTrack != null && CurrentTrack.Duration > TimeSpan.Zero)
            {
                var targetTime = TimeSpan.FromTicks((long)(CurrentTrack.Duration.Ticks * value));
                _ = _audioService.SeekAsync(targetTime);
            }
        }
    }

    /// <summary>
    /// 音量値（0.0〜1.0）
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _ = _audioService.SetVolumeAsync(Domain.ValueObjects.Volume.FromFloat((float)value, _isMuted));
            }
        }
    }

    /// <summary>
    /// ミュート状態
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _ = _audioService.SetMuteAsync(value);
            }
        }
    }

    /// <summary>
    /// シャッフル再生が有効かどうか
    /// </summary>
    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (SetProperty(ref _isShuffle, value))
            {
                UpdatePlaybackStrategy();
            }
        }
    }

    /// <summary>
    /// リピートモード（0: なし, 1: 全曲, 2: 1曲）
    /// </summary>
    public int RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                UpdatePlaybackStrategy();
            }
        }
    }

    /// <summary>
    /// 現在の再生時間文字列
    /// </summary>
    public string CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }

    /// <summary>
    /// 総再生時間文字列
    /// </summary>
    public string TotalTime
    {
        get => _totalTime;
        set => SetProperty(ref _totalTime, value);
    }

    /// <summary>
    /// 再生/一時停止トグルコマンド
    /// </summary>
    public ICommand TogglePlayPauseCommand { get; }

    /// <summary>
    /// 停止コマンド
    /// </summary>
    public ICommand StopCommand { get; }

    /// <summary>
    /// 次曲コマンド
    /// </summary>
    public ICommand NextCommand { get; }

    /// <summary>
    /// 前曲コマンド
    /// </summary>
    public ICommand PreviousCommand { get; }

    /// <summary>
    /// シャッフルトグルコマンド
    /// </summary>
    public ICommand ToggleShuffleCommand { get; }

    /// <summary>
    /// リピートトグルコマンド
    /// </summary>
    public ICommand ToggleRepeatCommand { get; }

    /// <summary>
    /// PlayerControlViewModelを初期化します
    /// </summary>
    /// <param name="audioService">オーディオ再生アプリケーションサービス</param>
    /// <param name="eventBus">イベントバス</param>
    /// <param name="spectrumCalculator">スペクトラム計算ドメインサービス</param>
    public PlayerControlViewModel(
        AudioApplicationService audioService,
        IEventBus eventBus,
        ISpectrumCalculator? spectrumCalculator = null)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _spectrumCalculator = spectrumCalculator ?? new SpectrumCalculator();

        // 64バンドのスペクトラムバッファ初期化
        for (int i = 0; i < SpectrumCalculator.DEFAULT_BAR_COUNT; i++)
        {
            SpectrumValues.Add(0.0);
        }

        // コマンド初期化
        TogglePlayPauseCommand = new RelayCommand(async _ => await ExecuteTogglePlayPauseAsync());
        StopCommand = new RelayCommand(async _ => await _audioService.StopAsync());
        NextCommand = new RelayCommand(async _ => await _audioService.NextTrackAsync());
        PreviousCommand = new RelayCommand(async _ => await _audioService.PreviousTrackAsync());
        ToggleShuffleCommand = new RelayCommand(_ => IsShuffle = !IsShuffle);
        ToggleRepeatCommand = new RelayCommand(_ => RepeatMode = (RepeatMode + 1) % 3);

        // イベント購読登録
        _eventBus.Subscribe<TrackChangedEvent>(HandleAsync);
        _eventBus.Subscribe<PlaybackStateChangedEvent>(HandleAsync);
        _eventBus.Subscribe<VolumeChangedEvent>(HandleAsync);
    }

    private async Task ExecuteTogglePlayPauseAsync()
    {
        if (IsPlaying)
        {
            await _audioService.PauseAsync();
        }
        else if (CurrentTrack != null)
        {
            await _audioService.ResumeAsync();
        }
    }

    private void UpdatePlaybackStrategy()
    {
        IPlaybackOrderStrategy strategy;
        if (RepeatMode == 2)
        {
            strategy = new RepeatPlaybackStrategy(Domain.Services.RepeatMode.One);
        }
        else if (IsShuffle)
        {
            strategy = new ShufflePlaybackStrategy();
        }
        else if (RepeatMode == 1)
        {
            strategy = new RepeatPlaybackStrategy(Domain.Services.RepeatMode.All);
        }
        else
        {
            strategy = new SequentialPlaybackStrategy();
        }

        _audioService.SetPlaybackOrderStrategy(strategy);
    }

    /// <summary>
    /// FFT計算結果からスペクトラムバーの描画値を更新します
    /// </summary>
    /// <param name="fftMagnitudes">FFT振幅配列</param>
    /// <param name="sampleRate">サンプリングレート</param>
    public void UpdateSpectrum(double[] fftMagnitudes, int sampleRate)
    {
        var bars = _spectrumCalculator.CalculateBars(fftMagnitudes, sampleRate);
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            int count = Math.Min(bars.Length, SpectrumValues.Count);
            for (int i = 0; i < count; i++)
            {
                SpectrumValues[i] = bars[i];
            }
        });
    }

    /// <summary>
    /// トラック変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">トラック変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(TrackChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            CurrentTrack = domainEvent.Track;
            TotalTime = domainEvent.Track.Duration.ToString(@"mm\:ss");
            CurrentTime = "00:00";
            _position = 0.0;
            OnPropertyChanged(nameof(Position));
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 再生状態変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">再生状態変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(PlaybackStateChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsPlaying = domainEvent.State == PlaybackState.Playing;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 音量変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">音量変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(VolumeChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _volume = domainEvent.Volume;
            _isMuted = domainEvent.IsMuted;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(IsMuted));
        });
        return Task.CompletedTask;
    }
}
