using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.Views;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 音声の再生・一時停止・停止・シーク・キュー管理・音量制御・スペクトラム描画を担当する専門ViewModel。
/// 不正シーク防止機構および単一情報源（Single Source of Truth）を徹底し、安全な再生制御を提供します。
/// </summary>
public class PlayerControlViewModel : ViewModelBase, IDisposable,
    IHandle<TrackChangedEvent>,
    IHandle<PlaybackStateChangedEvent>,
    IHandle<VolumeChangedEvent>
{
    private readonly IAudioService _audioService;
    private readonly IAudioEngine _audioEngine;
    private readonly IEventBus _eventBus;
    private readonly ISettingsService _settingsService;
    private readonly ISpectrumCalculator _spectrumCalculator;
    private readonly AudioApplicationService? _appAudioService;

    // タイマーおよびシーク制御
    private readonly DispatcherTimer _timer;
    private bool _isUpdatingProgressInternally;
    private bool _isDraggingProgress;

    // スペクトラムアナライザー設定
    private const int SpectrumBarCount = SpectrumCalculator.DEFAULT_BAR_COUNT;
    private const double SpectrumBassScale = 0.55;
    private const double SpectrumMidScale = 0.90;
    private const double SpectrumTrebleScale = 2.90;
    private const double SpectrumTrebleTiltDb = 8.5;
    private const double SpectrumSensitivity = 1.65;
    private readonly TimeSpan _spectrumUpdateInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    // 状態フィールド
    private Track? _currentTrack;
    private bool _isPlaying;
    private double _progress;
    private float _volume = 0.5f;
    private bool _isMuted;
    private float _preMuteVolume = 1.0f;
    private bool _isShuffleEnabled;
    private bool _isAlbumRepeat;
    private int _repeatMode; // 0: None, 1: All, 2: One
    private string _currentTimeDisplay = "00:00";
    private string _totalTimeDisplay = "00:00";
    private ImageSource? _nowPlayingImage;

    // キュー関連フィールド
    private ObservableCollection<Track> _playQueue = new();
    private ObservableCollection<Track> _playbackListTracks = new();
    private string _playbackListName = "No Album Selected";
    private string _playbackListSubtitle = string.Empty;
    private PlayQueueDialog? _playQueueDialog;

    // スペクトラム描画フィールド
    private bool _isSpectrumVisible = true;
    private DateTime _lastSpectrumUpdateTime = DateTime.MinValue;
    private int _spectrumGeneration;
    private bool _disposed;

    #region 公開イベント

    /// <summary>
    /// 再生トラックが変更された際に発生するイベント
    /// </summary>
    public event Action<Track?>? TrackChanged;

    /// <summary>
    /// 再生状態（再生中/一時停止・停止）が変更された際に発生するイベント
    /// </summary>
    public event Action<bool>? PlaybackStateChanged;

    /// <summary>
    /// 外部ビュー（プレイリストやお気に入りなど）での再生要求を受け取るアクション
    /// </summary>
    public Func<Track, bool>? TrackPlayHandler { get; set; }

    /// <summary>
    /// お気に入り切り替え要求アクション
    /// </summary>
    public Action<Track>? FavoriteToggleRequested { get; set; }

    #endregion

    #region 公開プロパティ

    /// <summary>
    /// スペクトラムバーの描画値コレクション（64バンド）
    /// </summary>
    public ObservableCollection<SpectrumBarItem> SpectrumValues { get; } = new();

    /// <summary>
    /// スペクトラムアナライザーが表示されているかどうか
    /// </summary>
    public bool IsSpectrumVisible
    {
        get => _isSpectrumVisible;
        set => SetProperty(ref _isSpectrumVisible, value);
    }

    /// <summary>
    /// 現在再生中のトラック
    /// </summary>
    public Track? CurrentTrack
    {
        get => _currentTrack;
        set
        {
            if (SetProperty(ref _currentTrack, value))
            {
                UpdateTrackDisplays(value);
                SyncTrackPlayingStates(value);
                TrackChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// 再生中フラグ
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                PlaybackStateChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// 現在の再生位置（進捗、0.0〜100.0）
    /// 内部タイマーからの更新中はシーク処理を実行しません（不正シーク排除）
    /// </summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
            {
                // ドラッグ操作中かつ内部タイマー更新でない場合のみ即時シーク
                if (_isDraggingProgress && !_isUpdatingProgressInternally)
                {
                    _audioService.SeekTo(value);
                }
            }
        }
    }

    /// <summary>
    /// 既存互換用再生進捗位置（0.0〜1.0）
    /// </summary>
    public double Position
    {
        get => _progress / 100.0;
        set
        {
            if (value >= 0.0 && value <= 1.0)
            {
                Progress = value * 100.0;
            }
        }
    }

    /// <summary>
    /// シークバーがドラッグ操作中かどうか
    /// </summary>
    public bool IsDraggingProgress
    {
        get => _isDraggingProgress;
        set => SetProperty(ref _isDraggingProgress, value);
    }

    /// <summary>
    /// 音量値（0.0〜1.0）
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) > 0.0001f)
            {
                _volume = value;
                _audioService.Volume = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercent));

                // 設定の永続化
                try
                {
                    var settings = _settingsService.LoadSettings();
                    settings.Volume = value;
                    _settingsService.SaveSettings(settings);
                }
                catch
                {
                    // 設定保存失敗時は継続
                }
            }
        }
    }

    /// <summary>
    /// 音量のパーセント表示文字列（例: "50%"）
    /// </summary>
    public string VolumePercent => $"{(int)(Volume * 100)}%";

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
                if (value)
                {
                    _preMuteVolume = Volume > 0 ? Volume : 1.0f;
                    Volume = 0;
                }
                else
                {
                    Volume = _preMuteVolume;
                }
            }
        }
    }

    /// <summary>
    /// シャッフル再生が有効かどうか
    /// </summary>
    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        set
        {
            if (SetProperty(ref _isShuffleEnabled, value))
            {
                _audioService.IsShuffleEnabled = value;
                OnPropertyChanged(nameof(IsShuffle));
            }
        }
    }

    /// <summary>
    /// 既存互換用シャッフルフラグ
    /// </summary>
    public bool IsShuffle
    {
        get => IsShuffleEnabled;
        set => IsShuffleEnabled = value;
    }

    /// <summary>
    /// アルバムリピートモードが有効かどうか
    /// </summary>
    public bool IsAlbumRepeat
    {
        get => _isAlbumRepeat;
        set
        {
            if (SetProperty(ref _isAlbumRepeat, value))
            {
                _audioService.IsRepeatEnabled = value;
                _repeatMode = value ? 1 : 0;
                OnPropertyChanged(nameof(RepeatMode));
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
                IsAlbumRepeat = value > 0;
            }
        }
    }

    /// <summary>
    /// 現在の再生時間表示文字列（"mm:ss"）
    /// </summary>
    public string CurrentTimeDisplay
    {
        get => _currentTimeDisplay;
        set
        {
            if (SetProperty(ref _currentTimeDisplay, value))
            {
                OnPropertyChanged(nameof(CurrentTime));
            }
        }
    }

    /// <summary>
    /// 総再生時間表示文字列（"mm:ss"）
    /// </summary>
    public string TotalTimeDisplay
    {
        get => _totalTimeDisplay;
        set
        {
            if (SetProperty(ref _totalTimeDisplay, value))
            {
                OnPropertyChanged(nameof(TotalTime));
            }
        }
    }

    /// <summary>
    /// 既存互換用現在の再生時間文字列
    /// </summary>
    public string CurrentTime
    {
        get => _currentTimeDisplay;
        set => CurrentTimeDisplay = value;
    }

    /// <summary>
    /// 既存互換用総再生時間文字列
    /// </summary>
    public string TotalTime
    {
        get => _totalTimeDisplay;
        set => TotalTimeDisplay = value;
    }

    /// <summary>
    /// 現在再生中のアルバムアート画像
    /// </summary>
    public ImageSource? NowPlayingImage
    {
        get => _nowPlayingImage;
        set => SetProperty(ref _nowPlayingImage, value);
    }

    #endregion

    #region 再生キュー関連プロパティ

    /// <summary>
    /// 実際の再生予定キュー
    /// </summary>
    public ObservableCollection<Track> PlayQueue
    {
        get => _playQueue;
        set => SetProperty(ref _playQueue, value);
    }

    /// <summary>
    /// 再生リストの名称（アルバム名やプレイリスト名）
    /// </summary>
    public string PlaybackListName
    {
        get => _playbackListName;
        set => SetProperty(ref _playbackListName, value);
    }

    /// <summary>
    /// 再生リストのサブタイトル（アーティスト名など）
    /// </summary>
    public string PlaybackListSubtitle
    {
        get => _playbackListSubtitle;
        set => SetProperty(ref _playbackListSubtitle, value);
    }

    /// <summary>
    /// 現在画面右ペインに表示されているトラックのコレクション
    /// </summary>
    public ObservableCollection<Track> PlaybackListTracks
    {
        get => _playbackListTracks;
        set
        {
            if (SetProperty(ref _playbackListTracks, value))
            {
                SyncTrackPlayingStates(_currentTrack);
                OnPropertyChanged(nameof(PlaybackListTracksCountText));
            }
        }
    }

    /// <summary>
    /// トラックリストの総曲数表示文字列
    /// </summary>
    public string PlaybackListTracksCountText
    {
        get
        {
            int count = PlaybackListTracks?.Count ?? 0;
            return count == 1 ? "Tracklist (1 track)" : $"Tracklist ({count} tracks)";
        }
    }

    #endregion

    #region コマンド

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
    /// ミュート切り替えコマンド
    /// </summary>
    public ICommand ToggleMuteCommand { get; }

    /// <summary>
    /// 音量増加コマンド
    /// </summary>
    public ICommand IncreaseVolumeCommand { get; }

    /// <summary>
    /// 音量減少コマンド
    /// </summary>
    public ICommand DecreaseVolumeCommand { get; }

    /// <summary>
    /// トラック再生コマンド
    /// </summary>
    public ICommand PlayTrackCommand { get; }

    /// <summary>
    /// 次に再生コマンド
    /// </summary>
    public ICommand PlayNextCommand { get; }

    /// <summary>
    /// キューに追加コマンド
    /// </summary>
    public ICommand EnqueueTrackCommand { get; }

    /// <summary>
    /// キューから再生コマンド
    /// </summary>
    public ICommand PlayFromQueueCommand { get; }

    /// <summary>
    /// 再生キューダイアログ表示コマンド
    /// </summary>
    public ICommand ShowQueueDialogCommand { get; }

    /// <summary>
    /// キューからトラックを削除するコマンド
    /// </summary>
    public ICommand RemoveFromQueueCommand { get; }

    /// <summary>
    /// キューを全クリアするコマンド
    /// </summary>
    public ICommand ClearQueueCommand { get; }


    /// <summary>
    /// スペクトラムアナライザーを表示するコマンド
    /// </summary>
    public ICommand SwitchToSpectrumCommand { get; }

    /// <summary>
    /// スペクトラムアナライザーの表示状態を切り替えるコマンド
    /// </summary>
    public ICommand ToggleSpectrumCommand { get; }

    #endregion

    /// <summary>
    /// PlayerControlViewModelを初期化します
    /// </summary>
    /// <param name="audioService">オーディオ再生サービス</param>
    /// <param name="audioEngine">オーディオ再生エンジン</param>
    /// <param name="eventBus">イベントバス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="appAudioService">アプリケーションオーディオサービス（オプション）</param>
    /// <param name="spectrumCalculator">スペクトラム計算機（オプション）</param>
    public PlayerControlViewModel(
        IAudioService audioService,
        IAudioEngine audioEngine,
        IEventBus eventBus,
        ISettingsService settingsService,
        AudioApplicationService? appAudioService = null,
        ISpectrumCalculator? spectrumCalculator = null)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _appAudioService = appAudioService;
        _spectrumCalculator = spectrumCalculator ?? new SpectrumCalculator();

        // 64バンドのスペクトラムバッファ初期化
        for (int i = 0; i < SpectrumCalculator.DEFAULT_BAR_COUNT; i++)
        {
            SpectrumValues.Add(new SpectrumBarItem());
        }

        // コマンド初期化
        TogglePlayPauseCommand = new RelayCommand(_ => TogglePlayPause());
        StopCommand = new RelayCommand(_ => Stop());
        NextCommand = new RelayCommand(_ => Next());
        PreviousCommand = new RelayCommand(_ => Previous());
        ToggleShuffleCommand = new RelayCommand(_ => ToggleShuffle());
        ToggleRepeatCommand = new RelayCommand(_ => ToggleRepeat());
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        IncreaseVolumeCommand = new RelayCommand(_ => IncreaseVolume());
        DecreaseVolumeCommand = new RelayCommand(_ => DecreaseVolume());
        PlayTrackCommand = new RelayCommand(o => PlayTrack(o));
        PlayNextCommand = new RelayCommand(o => PlayNext(o));
        EnqueueTrackCommand = new RelayCommand(o => EnqueueTrack(o));
        PlayFromQueueCommand = new RelayCommand(o => PlayFromQueue(o));
        ShowQueueDialogCommand = new RelayCommand(_ => ShowQueueDialog());
        RemoveFromQueueCommand = new RelayCommand(o => RemoveFromQueue(o));
        ClearQueueCommand = new RelayCommand(_ => ClearQueue());
        SwitchToSpectrumCommand = new RelayCommand(_ => IsSpectrumVisible = true);
        ToggleSpectrumCommand = new RelayCommand(_ => IsSpectrumVisible = !IsSpectrumVisible);

        // イベント購読登録
        _eventBus.Subscribe<TrackChangedEvent>(HandleAsync);
        _eventBus.Subscribe<PlaybackStateChangedEvent>(HandleAsync);
        _eventBus.Subscribe<VolumeChangedEvent>(HandleAsync);

        _audioService.TrackChanged += OnAudioServiceTrackChanged;
        _audioService.PlaybackStateChanged += OnAudioServicePlaybackStateChanged;
        _audioService.PlaylistChanged += OnAudioServicePlaylistChanged;
        _audioService.VolumeChanged += OnAudioServiceVolumeChanged;
        _audioService.FftCalculated += OnLegacyFftCalculated;
        _audioEngine.FftCalculated += OnFftCalculated;

        // 設定の反映
        try
        {
            var settings = _settingsService.LoadSettings();
            _volume = settings.Volume;
            _audioService.Volume = settings.Volume;
        }
        catch
        {
            _volume = 0.5f;
        }

        // 定期タイマーの初期化（500ms）
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    #region 再生リスト一括設定

    /// <summary>
    /// 再生リスト（トラックコレクション、名称、サブタイトル）を一括設定します
    /// </summary>
    /// <param name="tracks">設定するトラックコレクション</param>
    /// <param name="name">再生リスト名</param>
    /// <param name="subtitle">サブタイトル</param>
    public void SetPlaybackList(IEnumerable<Track> tracks, string name, string subtitle)
    {
        var trackList = tracks.ToList();
        PlaybackListName = name;
        PlaybackListSubtitle = subtitle;
        PlaybackListTracks = new ObservableCollection<Track>(trackList);
        PlayQueue = new ObservableCollection<Track>(trackList);
        _audioService.SetPlaylist(trackList);
        SyncTrackPlayingStates(_currentTrack);
    }

    #endregion

    #region 再生制御ロジック

    /// <summary>
    /// 再生と一時停止を切り替えます
    /// </summary>
    public void TogglePlayPause()
    {
        _audioService.TogglePlayPause();
    }

    /// <summary>
    /// 再生を停止します
    /// </summary>
    public void Stop()
    {
        _audioService.Stop(false);
    }

    /// <summary>
    /// 次の曲を再生します
    /// </summary>
    public void Next()
    {
        _audioService.Next();
    }

    /// <summary>
    /// 前の曲を再生します
    /// </summary>
    public void Previous()
    {
        _audioService.Previous();
    }

    /// <summary>
    /// シャッフル再生の有効/無効を切り替えます
    /// </summary>
    public void ToggleShuffle()
    {
        IsShuffleEnabled = !IsShuffleEnabled;
    }

    /// <summary>
    /// リピート再生の有効/無効を切り替えます
    /// </summary>
    public void ToggleRepeat()
    {
        IsAlbumRepeat = !IsAlbumRepeat;
    }

    /// <summary>
    /// ミュート状態を切り替えます
    /// </summary>
    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    /// <summary>
    /// 音量を5%増加します
    /// </summary>
    public void IncreaseVolume()
    {
        Volume = Math.Min(1.0f, Volume + 0.05f);
    }

    /// <summary>
    /// 音量を5%減少します
    /// </summary>
    public void DecreaseVolume()
    {
        Volume = Math.Max(0.0f, Volume - 0.05f);
    }

    /// <summary>
    /// 指定されたトラックを再生します
    /// </summary>
    /// <param name="obj">再生対象のトラック</param>
    public void PlayTrack(object? obj)
    {
        if (obj is not Track track) return;

        // 外部ハンドラ（MainViewModelのアルバム・プレイリスト解決）があれば優先実行
        if (TrackPlayHandler != null && TrackPlayHandler(track))
        {
            return;
        }

        // 同一トラックなら再生/一時停止をトグル
        if (CurrentTrack != null && CurrentTrack.Equals(track))
        {
            _audioService.TogglePlayPause();
            return;
        }

        _audioService.PlayTrack(track);
    }

    /// <summary>
    /// 指定されたトラックを現在の再生曲の直後に挿入します
    /// </summary>
    /// <param name="obj">挿入対象のトラック</param>
    public void PlayNext(object? obj)
    {
        if (obj is not Track track) return;

        if (CurrentTrack != null && PlayQueue.Contains(CurrentTrack))
        {
            int currentIndex = PlayQueue.IndexOf(CurrentTrack);
            PlayQueue.Insert(currentIndex + 1, track);
        }
        else
        {
            PlayQueue.Insert(0, track);
        }
        _audioService.SetPlaylist(PlayQueue.ToList());
    }

    /// <summary>
    /// 指定されたトラックを再生キューの末尾に追加します
    /// </summary>
    /// <param name="obj">追加対象のトラック</param>
    public void EnqueueTrack(object? obj)
    {
        if (obj is not Track track) return;

        PlayQueue.Add(track);
        _audioService.SetPlaylist(PlayQueue.ToList());
    }

    /// <summary>
    /// 再生キュー内の指定トラックを再生します
    /// </summary>
    /// <param name="obj">再生対象のトラック</param>
    public void PlayFromQueue(object? obj)
    {
        if (obj is not Track track) return;

        if (track == CurrentTrack)
        {
            _audioService.TogglePlayPause();
        }
        else
        {
            _audioService.PlayTrack(track);
        }
    }

    /// <summary>
    /// 指定されたトラックを再生キューから削除します
    /// </summary>
    /// <param name="obj">削除対象のトラック</param>
    public void RemoveFromQueue(object? obj)
    {
        if (obj is not Track track) return;

        bool isCurrent = CurrentTrack != null && IsSameTrack(CurrentTrack, track);
        int oldIndex = PlayQueue.IndexOf(track);

        if (PlayQueue.Remove(track))
        {
            if (PlayQueue.Count == 0)
            {
                ClearQueue();
                return;
            }

            _audioService.SetPlaylist(PlayQueue.ToList());

            if (isCurrent)
            {
                int nextIndex = Math.Clamp(oldIndex, 0, PlayQueue.Count - 1);
                var nextTrack = PlayQueue[nextIndex];
                _audioService.PlayTrack(nextTrack);
            }
        }
    }

    /// <summary>
    /// 再生キューを全クリアします
    /// </summary>
    public void ClearQueue()
    {
        PlayQueue.Clear();
        _audioService.Stop(false);
        CurrentTrack = null;
        TotalTimeDisplay = "00:00";
        CurrentTimeDisplay = "00:00";
        Progress = 0.0;
        NowPlayingImage = null;
        _audioService.SetPlaylist(new List<Track>());
    }


    /// <summary>
    /// 再生キューダイアログを表示します（多重起動を防止し単一インスタンス管理）
    /// </summary>
    /// <param name="ownerDataContext">ダイアログのオーナーDataContext（未指定時は自身）</param>
    public void ShowQueueDialog(object? ownerDataContext = null)
    {
        if (_playQueueDialog == null || !_playQueueDialog.IsLoaded)
        {
            _playQueueDialog = new PlayQueueDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                DataContext = ownerDataContext ?? this
            };
            _playQueueDialog.Closed += (s, e) => _playQueueDialog = null;
            _playQueueDialog.Show();
        }
        else
        {
            if (_playQueueDialog.WindowState == WindowState.Minimized)
            {
                _playQueueDialog.WindowState = WindowState.Normal;
            }
            _playQueueDialog.Activate();
        }
    }

    #endregion

    #region シーク処理（不正シーク完全排除）

    /// <summary>
    /// スライダーのドラッグ操作を開始します
    /// </summary>
    public void StartDragging()
    {
        IsDraggingProgress = true;
        _audioService.PauseForSeek();
    }

    /// <summary>
    /// スライダーのドラッグ操作を完了し再生を再開します
    /// </summary>
    public void StopDragging()
    {
        IsDraggingProgress = false;
        _audioService.ResumeAfterSeek();
    }

    /// <summary>
    /// 指定パーセント位置へ確実にシークします
    /// </summary>
    /// <param name="percentage">シーク先位置（0.0〜100.0）</param>
    public void Seek(double percentage)
    {
        _isUpdatingProgressInternally = true;
        try
        {
            Progress = percentage;
        }
        finally
        {
            _isUpdatingProgressInternally = false;
        }
        _audioService.SeekTo(percentage);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_audioService == null) return;

        if (CurrentTrack == null)
        {
            CurrentTimeDisplay = "00:00";
            TotalTimeDisplay = "00:00";
            Progress = 0.0;
            return;
        }

        if (!_audioService.IsPlaying && !_isDraggingProgress)
        {
            return;
        }

        var current = _audioService.CurrentTime;
        var total = _audioService.TotalTime;

        CurrentTimeDisplay = current.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        TotalTimeDisplay = total.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

        if (total.TotalSeconds > 0 && !_isDraggingProgress)
        {
            _isUpdatingProgressInternally = true;
            try
            {
                Progress = (current.TotalSeconds / total.TotalSeconds) * 100.0;
            }
            finally
            {
                _isUpdatingProgressInternally = false;
            }
        }
    }

    #endregion

    #region イベント受信・UI反映

    private void OnAudioServiceTrackChanged(Track? track)
    {
        RunOnUiThread(() =>
        {
            ResetSpectrum();
            CurrentTrack = track;
            Progress = 0.0;
            if (track == null)
            {
                UpdateTrackDisplays(null);
            }
        });
    }

    private void OnAudioServicePlaybackStateChanged(bool isPlaying)
    {
        RunOnUiThread(() =>
        {
            IsPlaying = isPlaying;
        });
    }

    private void OnAudioServicePlaylistChanged(List<Track> playlist)
    {
        RunOnUiThread(() =>
        {
            PlayQueue = new ObservableCollection<Track>(playlist);
            SyncTrackPlayingStates(CurrentTrack);
        });
    }

    private void OnAudioServiceVolumeChanged(float volume)
    {
        RunOnUiThread(() =>
        {
            _volume = volume;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumePercent));
        });
    }

    private void UpdateTrackDisplays(Track? track)
    {
        if (track == null)
        {
            TotalTimeDisplay = "00:00";
            CurrentTimeDisplay = "00:00";
            Progress = 0.0;
            NowPlayingImage = null;
            return;
        }

        TotalTimeDisplay = track.Duration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        CurrentTimeDisplay = "00:00";
        Progress = 0.0;

        // カバーアート画像の設定
        if (track.CoverImage != null)
        {
            NowPlayingImage = track.CoverImage;
        }
        else if (File.Exists(track.FilePath))
        {
            Task.Run(() =>
            {
                try
                {
                    using var tfile = TagLib.File.Create(track.FilePath);
                    if (tfile.Tag.Pictures.Length > 0)
                    {
                        var bin = tfile.Tag.Pictures[0].Data.Data;
                        RunOnUiThread(() =>
                        {
                            try
                            {
                                var image = new BitmapImage();
                                using var mem = new MemoryStream(bin);
                                mem.Position = 0;
                                image.BeginInit();
                                image.DecodePixelWidth = 500;
                                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.StreamSource = mem;
                                image.EndInit();
                                image.Freeze();
                                NowPlayingImage = image;
                            }
                            catch
                            {
                                NowPlayingImage = null;
                            }
                        });
                    }
                }
                catch
                {
                    // 画像読み込み失敗時は無視
                }
            });
        }
        else
        {
            NowPlayingImage = null;
        }
    }

    /// <summary>
    /// PlaybackListTracks 内のトラックの再生中状態（IsPlaying）を同期します。
    /// 現在の再生中トラックのみを true にし、それ以外を false に排他制御します。
    /// プロパティ値に変更がある場合のみ代入することで、不要な PropertyChanged 通知を抑止します。
    /// </summary>
    /// <param name="currentTrack">現在再生中のトラック（未再生または停止時は null）</param>
    public void SyncTrackPlayingStates(Track? currentTrack)
    {
        var tracks = PlaybackListTracks;
        if (tracks == null) return;

        foreach (var track in tracks)
        {
            if (track == null) continue;
            bool shouldBePlaying = currentTrack != null && IsSameTrack(track, currentTrack);
            if (track.IsPlaying != shouldBePlaying)
            {
                track.IsPlaying = shouldBePlaying;
            }
        }
    }

    /// <summary>
    /// 2つのトラックが同一の楽曲であるかを判定します。
    /// オブジェクト参照、ファイルパス、トラックIDの順で判定します。
    /// </summary>
    /// <param name="a">比較対象のトラックA</param>
    /// <param name="b">比較対象のトラックB</param>
    /// <returns>同一楽曲と判定された場合は true、それ以外は false</returns>
    public static bool IsSameTrack(Track? a, Track? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (!string.IsNullOrEmpty(a.FilePath) && !string.IsNullOrEmpty(b.FilePath))
        {
            return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        return a.Id == b.Id;
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }

    #endregion

    #region スペクトラム描画ロジック

    /// <summary>
    /// FFT振幅配列からスペクトラムバーの描画値を計算・更新します
    /// </summary>
    /// <param name="fftMagnitudes">FFT振幅配列</param>
    /// <param name="sampleRate">サンプリング周波数</param>
    public void UpdateSpectrum(double[] fftMagnitudes, int sampleRate)
    {
        int currentGeneration = _spectrumGeneration;
        var bars = _spectrumCalculator.CalculateBars(
            fftMagnitudes,
            sampleRate,
            SpectrumBarCount,
            SpectrumSensitivity,
            SpectrumBassScale,
            SpectrumMidScale,
            SpectrumTrebleScale,
            SpectrumTrebleTiltDb);

        void ApplySpectrumValues()
        {
            if (currentGeneration != _spectrumGeneration)
            {
                return;
            }

            for (int i = 0; i < Math.Min(bars.Length, SpectrumValues.Count); i++)
            {
                var item = SpectrumValues[i];
                double current = item.Value;
                double target = Math.Min(78.0, bars[i]);
                item.Value = target > current
                    ? current + (target - current) * 0.45
                    : current - (current - target) * 0.075;

                if (item.Value >= item.PeakValue)
                {
                    item.PeakValue = item.Value;
                    item.PeakHoldCount = 14;
                }
                else if (item.PeakHoldCount > 0)
                {
                    item.PeakHoldCount--;
                }
                else
                {
                    item.PeakValue = Math.Max(item.Value, item.PeakValue - 1.3);
                }
            }
        }

        RunOnUiThread(ApplySpectrumValues);
    }

    private void OnFftCalculated(object? sender, FftCalculatedEventArgs e)
    {
        if (!IsSpectrumVisible) return;

        DateTime now = DateTime.UtcNow;
        if (now - _lastSpectrumUpdateTime < _spectrumUpdateInterval) return;

        _lastSpectrumUpdateTime = now;
        UpdateSpectrum(e.Magnitudes, e.SampleRate);
    }

    private void OnLegacyFftCalculated(object? sender, FftEventArgs e)
    {
        int halfLength = e.Result.Length / 2;
        var magnitudes = new double[halfLength];
        for (int i = 0; i < halfLength; i++)
        {
            double real = e.Result[i].X;
            double imaginary = e.Result[i].Y;
            magnitudes[i] = Math.Sqrt((real * real) + (imaginary * imaginary));
        }

        OnFftCalculated(sender, new FftCalculatedEventArgs(magnitudes, 44100));
    }

    private void ResetSpectrum()
    {
        Interlocked.Increment(ref _spectrumGeneration);

        RunOnUiThread(() =>
        {
            foreach (var item in SpectrumValues)
            {
                item.Value = 0.0;
                item.PeakValue = 0.0;
                item.PeakHoldCount = 0;
            }
        });
    }

    #endregion

    #region IHandle イベント購読

    /// <summary>
    /// トラック変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">トラック変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(TrackChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        RunOnUiThread(() =>
        {
            ResetSpectrum();
            CurrentTrack = domainEvent.Track;
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
        RunOnUiThread(() =>
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
        RunOnUiThread(() =>
        {
            Volume = (float)domainEvent.Volume;
            IsMuted = domainEvent.IsMuted;
        });
        return Task.CompletedTask;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// リソースを解放しイベント購読を解除します
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;

        _audioService.TrackChanged -= OnAudioServiceTrackChanged;
        _audioService.PlaybackStateChanged -= OnAudioServicePlaybackStateChanged;
        _audioService.PlaylistChanged -= OnAudioServicePlaylistChanged;
        _audioService.VolumeChanged -= OnAudioServiceVolumeChanged;
        _audioService.FftCalculated -= OnLegacyFftCalculated;
        _audioEngine.FftCalculated -= OnFftCalculated;

        _eventBus.Unsubscribe<TrackChangedEvent>(HandleAsync);
        _eventBus.Unsubscribe<PlaybackStateChangedEvent>(HandleAsync);
        _eventBus.Unsubscribe<VolumeChangedEvent>(HandleAsync);

        if (_playQueueDialog != null && _playQueueDialog.IsLoaded)
        {
            _playQueueDialog.Close();
            _playQueueDialog = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
