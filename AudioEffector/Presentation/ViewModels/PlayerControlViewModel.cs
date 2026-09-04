using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 音声の再生・一時停止・停止・シーク・音量制御・再生キュー・スペクトラム描画データバインディングを担当するViewModel
/// </summary>
public class PlayerControlViewModel : ViewModelBase,
    IHandle<TrackChangedEvent>,
    IHandle<PlaybackStateChangedEvent>,
    IHandle<VolumeChangedEvent>
{
    private readonly AudioApplicationService? _audioAppService;
    private readonly IAudioService? _audioService;
    private readonly IEventBus? _eventBus;
    private readonly ISpectrumCalculator _spectrumCalculator;

    private Track? _currentTrack;
    private bool _isPlaying;
    private double _position;
    private double _progress;
    private bool _isDraggingProgress;
    private double _volume = 0.5;
    private bool _isMuted;
    private bool _isShuffle;
    private int _repeatMode; // 0: None, 1: All, 2: One
    private bool _isAlbumRepeat;
    private string _currentTime = "00:00";
    private string _totalTime = "00:00";
    private string _currentTimeDisplay = "00:00";
    private string _totalTimeDisplay = "00:00";
    private string _playbackListName = "No Album Selected";
    private string _playbackListSubtitle = string.Empty;

    #region Public Properties

    /// <summary>
    /// スペクトラムバーの描画値コレクション（64バンド）
    /// </summary>
    public ObservableCollection<double> SpectrumValues { get; } = [];

    /// <summary>
    /// 再生予定キューコレクション
    /// </summary>
    public ObservableCollection<Track> PlayQueue { get; } = [];

    /// <summary>
    /// 現在表示中のトラックリスト（アルバム・プレイリスト）
    /// </summary>
    public ObservableCollection<Track> PlaybackListTracks { get; } = [];

    /// <summary>
    /// 再生リスト名称
    /// </summary>
    public string PlaybackListName
    {
        get => _playbackListName;
        set => SetProperty(ref _playbackListName, value);
    }

    /// <summary>
    /// 再生リストサブタイトル
    /// </summary>
    public string PlaybackListSubtitle
    {
        get => _playbackListSubtitle;
        set => SetProperty(ref _playbackListSubtitle, value);
    }

    /// <summary>
    /// トラックリストの総曲数表示文字列
    /// </summary>
    public string PlaybackListTracksCountText
    {
        get
        {
            int count = PlaybackListTracks.Count;
            return count == 1 ? "Tracklist (1 track)" : $"Tracklist ({count} tracks)";
        }
    }

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
                if (_audioAppService != null)
                {
                    _ = _audioAppService.SeekAsync(targetTime);
                }
                else
                {
                    _audioService?.SeekTo(value);
                }
            }
        }
    }

    /// <summary>
    /// 再生進捗パーセンテージ（0〜100）
    /// </summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value) && !_isDraggingProgress && _audioService != null)
            {
                _audioService.SeekTo(value / 100.0);
            }
        }
    }

    /// <summary>
    /// プログレスバードラッグ中かどうか
    /// </summary>
    public bool IsDraggingProgress
    {
        get => _isDraggingProgress;
        set => SetProperty(ref _isDraggingProgress, value);
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
                OnPropertyChanged(nameof(VolumePercent));
                if (_audioAppService != null)
                {
                    _ = _audioAppService.SetVolumeAsync(Domain.ValueObjects.Volume.FromFloat((float)value, _isMuted));
                }
                else if (_audioService != null)
                {
                    _audioService.Volume = (float)value;
                }
            }
        }
    }

    /// <summary>
    /// 音量パーセンテージ文字列
    /// </summary>
    public string VolumePercent => $"{(int)(_volume * 100)}%";

    private double _preMuteVolume = 1.0;

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
                if (_audioAppService != null)
                {
                    _ = _audioAppService.SetMuteAsync(value);
                }
                else if (_audioService != null)
                {
                    if (value)
                    {
                        _preMuteVolume = _volume > 0 ? _volume : 1.0;
                        Volume = 0;
                    }
                    else
                    {
                        Volume = _preMuteVolume;
                    }
                }
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
    /// アルバムリピートモードが有効かどうか
    /// </summary>
    public bool IsAlbumRepeat
    {
        get => _isAlbumRepeat;
        set => SetProperty(ref _isAlbumRepeat, value);
    }

    /// <summary>
    /// 現在の再生時間文字列（mm:ss）
    /// </summary>
    public string CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }

    /// <summary>
    /// 総再生時間文字列（mm:ss）
    /// </summary>
    public string TotalTime
    {
        get => _totalTime;
        set => SetProperty(ref _totalTime, value);
    }

    /// <summary>
    /// 現在の再生時間表示用文字列
    /// </summary>
    public string CurrentTimeDisplay
    {
        get => _currentTimeDisplay;
        set => SetProperty(ref _currentTimeDisplay, value);
    }

    /// <summary>
    /// 総再生時間表示用文字列
    /// </summary>
    public string TotalTimeDisplay
    {
        get => _totalTimeDisplay;
        set => SetProperty(ref _totalTimeDisplay, value);
    }

    #endregion

    #region Commands

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
    /// 指定トラック再生コマンド
    /// </summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>
    /// 音量アップコマンド
    /// </summary>
    public ICommand IncreaseVolumeCommand { get; }

    /// <summary>
    /// 音量ダウンコマンド
    /// </summary>
    public ICommand DecreaseVolumeCommand { get; }

    /// <summary>
    /// ミュート切り替えコマンド
    /// </summary>
    public ICommand ToggleMuteCommand { get; }

    /// <summary>
    /// キューからトラック再生コマンド
    /// </summary>
    public ICommand PlayFromQueueCommand { get; }

    /// <summary>
    /// 次に再生コマンド
    /// </summary>
    public ICommand PlayNextCommand { get; }

    /// <summary>
    /// キュー末尾追加コマンド
    /// </summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>
    /// 再生キューダイアログ表示コマンド
    /// </summary>
    public ICommand ShowQueueDialogCommand { get; }

    #endregion

    #region Events for View Coordination

    /// <summary>
    /// 再生キュートラック選択イベント
    /// </summary>
    public event Action<Track>? PlayFromQueueRequested;

    /// <summary>
    /// 次再生追加要求イベント
    /// </summary>
    public event Action<Track>? PlayNextRequested;

    /// <summary>
    /// キュー追加要求イベント
    /// </summary>
    public event Action<Track>? EnqueueTrackRequested;

    /// <summary>
    /// キューダイアログ表示要求イベント
    /// </summary>
    public event Action? ShowQueueDialogRequested;

    #endregion

    /// <summary>
    /// PlayerControlViewModelを初期化します
    /// </summary>
    /// <param name="audioAppService">オーディオ再生アプリケーションサービス（null許容）</param>
    /// <param name="eventBus">イベントバス（null許容）</param>
    /// <param name="spectrumCalculator">スペクトラム計算サービス（null許容）</param>
    /// <param name="audioService">オーディオインターフェース（null許容）</param>
    public PlayerControlViewModel(
        AudioApplicationService? audioAppService = null,
        IEventBus? eventBus = null,
        ISpectrumCalculator? spectrumCalculator = null,
        IAudioService? audioService = null)
    {
        _audioAppService = audioAppService;
        _audioService = audioService;
        _eventBus = eventBus;
        _spectrumCalculator = spectrumCalculator ?? new SpectrumCalculator();

        for (int i = 0; i < SpectrumCalculator.DEFAULT_BAR_COUNT; i++)
        {
            SpectrumValues.Add(0.0);
        }

        TogglePlayPauseCommand = new RelayCommand(async _ => await ExecuteTogglePlayPauseAsync());
        StopCommand = new RelayCommand(async _ =>
        {
            if (_audioAppService != null) await _audioAppService.StopAsync();
            else _audioService?.Stop();
        });
        NextCommand = new RelayCommand(async _ =>
        {
            if (_audioAppService != null) await _audioAppService.NextTrackAsync();
            else _audioService?.Next();
        });
        PreviousCommand = new RelayCommand(async _ =>
        {
            if (_audioAppService != null) await _audioAppService.PreviousTrackAsync();
            else _audioService?.Previous();
        });
        ToggleShuffleCommand = new RelayCommand(_ => IsShuffle = !IsShuffle);
        ToggleRepeatCommand = new RelayCommand(_ =>
        {
            if (_audioAppService != null)
            {
                RepeatMode = (RepeatMode + 1) % 3;
            }
            else
            {
                IsAlbumRepeat = !IsAlbumRepeat;
                if (_audioService != null)
                {
                    _audioService.IsRepeatEnabled = IsAlbumRepeat;
                }
            }
        });

        PlayTrackCommand = new RelayCommand(p =>
        {
            if (p is Track track && _audioService != null)
            {
                _audioService.PlayTrack(track);
            }
        });

        IncreaseVolumeCommand = new RelayCommand(_ => Volume = Math.Min(1.0, Volume + 0.05));
        DecreaseVolumeCommand = new RelayCommand(_ => Volume = Math.Max(0.0, Volume - 0.05));
        ToggleMuteCommand = new RelayCommand(_ => IsMuted = !IsMuted);

        PlayFromQueueCommand = new RelayCommand(p =>
        {
            if (p is Track track) PlayFromQueueRequested?.Invoke(track);
        });

        PlayNextCommand = new RelayCommand(p =>
        {
            if (p is Track track) PlayNextRequested?.Invoke(track);
        });

        EnqueueTrackCommand = new RelayCommand(p =>
        {
            if (p is Track track) EnqueueTrackRequested?.Invoke(track);
        });

        ShowQueueDialogCommand = new RelayCommand(_ => ShowQueueDialogRequested?.Invoke());

        _eventBus?.Subscribe<TrackChangedEvent>(HandleAsync);
        _eventBus?.Subscribe<PlaybackStateChangedEvent>(HandleAsync);
        _eventBus?.Subscribe<VolumeChangedEvent>(HandleAsync);
    }

    private async Task ExecuteTogglePlayPauseAsync()
    {
        if (_audioAppService != null)
        {
            if (IsPlaying)
            {
                await _audioAppService.PauseAsync();
            }
            else if (CurrentTrack != null)
            {
                await _audioAppService.ResumeAsync();
            }
        }
        else if (_audioService != null)
        {
            _audioService.TogglePlayPause();
        }
    }

    private void UpdatePlaybackStrategy()
    {
        if (_audioAppService == null) return;

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

        _audioAppService.SetPlaybackOrderStrategy(strategy);
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
            TotalTime = domainEvent.Track?.Duration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "00:00";
            TotalTimeDisplay = TotalTime;
            CurrentTime = "00:00";
            CurrentTimeDisplay = "00:00";
            _position = 0.0;
            _progress = 0.0;
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Progress));
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
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(IsMuted));
        });
        return Task.CompletedTask;
    }
}
