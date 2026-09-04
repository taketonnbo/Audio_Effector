using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioEffector.Infrastructure.Audio;

/// <summary>
/// オーディオ再生、プレイリスト管理、イコライザー処理を統括するコアサービス具象クラス
/// NAudioを使用しています
/// </summary>
public class AudioService : IAudioService
{
    private readonly object _lock = new();

    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFile;
    private EqualizerDsp? _equalizer;
    private List<Track> _playlist = new();
    private List<Track> _originalPlaylist = new();
    private int _currentIndex = -1;
    private bool _isShuffleEnabled;
    private bool _wasPlayingBeforeSeek;
    private Guid _currentPlaybackId;

    private int _sampleRate = 44100;
    private int _bufferSizeMs = 100;
    private WdlResamplingSampleProvider? _resampler;
    private VolumeSampleProvider? _masterVolumeProvider;

    private bool _stopRequested;

    /// <summary>
    /// トラックが変更された際に発生するイベント
    /// </summary>
    public event Action<Track>? TrackChanged;

    /// <summary>
    /// 再生状態（再生中/停止）が変更された際に発生するイベント
    /// </summary>
    public event Action<bool>? PlaybackStateChanged;

    /// <summary>
    /// 再生が停止した際に発生するイベント
    /// </summary>
    public event Action? PlaybackStopped;

    /// <summary>
    /// プレイリストの最後（リピートなし）に到達した際に発生するイベント
    /// </summary>
    public event EventHandler? PlaylistEnded;

    /// <summary>
    /// FFT計算結果が利用可能になった際に発生するイベント
    /// </summary>
    public event EventHandler<FftEventArgs>? FftCalculated;

    /// <summary>
    /// プレイリスト（再生キュー）の順序や内容が変更された際に発生するイベント
    /// </summary>
    public event Action<List<Track>>? PlaylistChanged;

    /// <summary>
    /// 音量が変更された際に発生するイベント
    /// </summary>
    public event Action<float>? VolumeChanged;

    /// <summary>
    /// イコライザーの周波数帯域定義（10バンド）
    /// </summary>
    public float[] Frequencies { get; } = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    /// <summary>
    /// 現在再生中かどうかを取得します
    /// </summary>
    public bool IsPlaying => _outputDevice?.PlaybackState == NAudio.Wave.PlaybackState.Playing;

    /// <summary>
    /// シャッフル再生が有効かどうかを取得または設定します
    /// </summary>
    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        set
        {
            lock (_lock)
            {
                if (_isShuffleEnabled != value)
                {
                    _isShuffleEnabled = value;
                    if (_isShuffleEnabled)
                    {
                        ShufflePlaylist();
                    }
                    else
                    {
                        RestorePlaylist();
                    }
                }
            }
        }
    }

    /// <summary>
    /// リピート再生が有効かどうかを取得または設定します
    /// </summary>
    public bool IsRepeatEnabled { get; set; }

    /// <summary>
    /// プレイリストを設定します
    /// </summary>
    /// <param name="tracks">トラックリスト</param>
    public void SetPlaylist(List<Track> tracks)
    {
        lock (_lock)
        {
            var currentTrack = _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

            _originalPlaylist = new List<Track>(tracks);
            if (_isShuffleEnabled)
            {
                ShufflePlaylist();
            }
            else
            {
                _playlist = new List<Track>(tracks);
            }

            if (currentTrack != null)
            {
                var newIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
                if (newIndex >= 0)
                {
                    _currentIndex = newIndex;
                }
                else
                {
                    _currentIndex = -1;
                }
            }
            else
            {
                _currentIndex = -1;
            }
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
    }

    private void ShufflePlaylist()
    {
        if (_playlist.Count <= 1) return;

        Track? currentTrack = null;
        if (_currentIndex >= 0 && _currentIndex < _playlist.Count)
        {
            currentTrack = _playlist[_currentIndex];
        }

        var rng = new Random();
        var shuffled = new List<Track>(_originalPlaylist);
        int n = shuffled.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (shuffled[k], shuffled[n]) = (shuffled[n], shuffled[k]);
        }

        if (currentTrack != null)
        {
            shuffled.RemoveAll(t => t.FilePath == currentTrack.FilePath);
            shuffled.Insert(0, currentTrack);
            _currentIndex = 0;
        }

        _playlist = shuffled;
    }

    private void RestorePlaylist()
    {
        Track? currentTrack = null;
        if (_currentIndex >= 0 && _currentIndex < _playlist.Count)
        {
            currentTrack = _playlist[_currentIndex];
        }

        _playlist = new List<Track>(_originalPlaylist);

        if (currentTrack != null)
        {
            _currentIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
        }
    }

    /// <summary>
    /// 指定された楽曲を再生します
    /// </summary>
    /// <param name="track">再生対象のトラック</param>
    public void PlayTrack(Track track)
    {
        lock (_lock)
        {
            int index = _playlist.FindIndex(t => t.FilePath == track.FilePath);
            if (index >= 0)
            {
                _currentIndex = index;
            }
            else
            {
                _playlist.Insert(0, track);
                _originalPlaylist.Insert(0, track);
                _currentIndex = 0;
                PlaylistChanged?.Invoke(new List<Track>(_playlist));
            }
        }
        PlayCurrent();
    }

    private async void PlayCurrent()
    {
        Guid thisPlaybackId = Guid.NewGuid();
        lock (_lock)
        {
            _currentPlaybackId = thisPlaybackId;
        }

        Track? trackToPlay = null;
        lock (_lock)
        {
            if (_currentIndex >= 0 && _currentIndex < _playlist.Count)
            {
                trackToPlay = _playlist[_currentIndex];
            }
        }

        if (trackToPlay == null)
        {
            Stop();
            return;
        }

        TrackChanged?.Invoke(trackToPlay);

        await Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    if (_currentPlaybackId != thisPlaybackId) return;

                    StopInternal();

                    _audioFile = new AudioFileReader(trackToPlay.FilePath);
                    ISampleProvider sourceProvider = _audioFile;

                    // Apply Peak Normalization if enabled
                    var settings = new AudioEffector.Application.ApplicationServices.SettingsApplicationService(new AudioEffector.Infrastructure.Repository.JsonSettingsRepository()).LoadSettings();
                    if (settings.EnableNormalize)
                    {
                        float maxPeak = 0;
                        using (var tempReader = new AudioFileReader(trackToPlay.FilePath))
                        {
                            float[] buffer = new float[tempReader.WaveFormat.SampleRate * tempReader.WaveFormat.Channels];
                            int read;
                            while ((read = tempReader.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                for (int i = 0; i < read; i++)
                                {
                                    var abs = Math.Abs(buffer[i]);
                                    if (abs > maxPeak) maxPeak = abs;
                                }
                            }
                        }

                        float normalizeGain = 1.0f;
                        if (maxPeak > 0)
                        {
                            normalizeGain = 0.98f / maxPeak;
                        }

                        var volumeProvider = new VolumeSampleProvider(sourceProvider) { Volume = normalizeGain };
                        sourceProvider = volumeProvider;
                    }

                    if (_audioFile.WaveFormat.SampleRate != _sampleRate)
                    {
                        _resampler = new WdlResamplingSampleProvider(sourceProvider, _sampleRate);
                        sourceProvider = _resampler;
                    }

                    // Setup EQ
                    _equalizer = new EqualizerDsp(sourceProvider, Frequencies);

                    // Setup SampleAggregator for FFT
                    var aggregator = new SampleAggregator(_equalizer);
                    aggregator.ComplexFftCalculated += (s, e) => FftCalculated?.Invoke(this, e);

                    // Setup Master Volume Provider
                    _masterVolumeProvider = new VolumeSampleProvider(aggregator)
                    {
                        Volume = _volume
                    };

                    var endDetector = new EndOfStreamProvider(_masterVolumeProvider);
                    endDetector.EndOfStream += () =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnTrackEnded();
                        });
                    };

                    var waveOut = new WaveOutEvent
                    {
                        DesiredLatency = _bufferSizeMs,
                        NumberOfBuffers = 3
                    };
                    _outputDevice = waveOut;
                    _outputDevice.Init(endDetector);
                    _outputDevice.PlaybackStopped += OnPlaybackStopped;

                    if (_currentPlaybackId != thisPlaybackId)
                    {
                        StopInternal();
                        return;
                    }

                    _outputDevice.Play();
                }
            }
            catch
            {
                lock (_lock)
                {
                    if (_currentPlaybackId == thisPlaybackId)
                    {
                        StopInternal();
                    }
                }
            }
        });

        await Task.Delay(100);
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    private void OnTrackEnded()
    {
        lock (_lock)
        {
            if (_stopRequested) return;

            if (_currentIndex < _playlist.Count - 1)
            {
                _currentIndex++;
                PlayCurrent();
            }
            else if (IsRepeatEnabled && _playlist.Count > 0)
            {
                _currentIndex = 0;
                PlayCurrent();
            }
            else
            {
                Stop();
                PlaylistEnded?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackStopped?.Invoke();
        PlaybackStateChanged?.Invoke(false);
    }

    private void StopInternal()
    {
        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Stop();
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        if (_audioFile != null)
        {
            _audioFile.Dispose();
            _audioFile = null;
        }

        _resampler = null;
        _masterVolumeProvider = null;
        _equalizer = null;
    }

    /// <summary>
    /// 再生と一時停止を切り替えます
    /// </summary>
    public async void TogglePlayPause()
    {
        lock (_lock)
        {
            if (_outputDevice == null && _playlist.Count > 0)
            {
                if (_currentIndex == -1) _currentIndex = 0;
                PlayCurrent();
                return;
            }

            if (_outputDevice != null)
            {
                if (_outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                {
                    _outputDevice.Pause();
                }
                else if (_outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Paused)
                {
                    _outputDevice.Play();
                }
            }
        }

        await Task.Delay(100);
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    /// <summary>
    /// 次の楽曲へ進みます
    /// </summary>
    public async void Next()
    {
        lock (_lock)
        {
            if (_playlist.Count == 0) return;
            _currentIndex++;
            if (_currentIndex >= _playlist.Count)
            {
                if (IsRepeatEnabled)
                {
                    _currentIndex = 0;
                }
                else
                {
                    _currentIndex = _playlist.Count - 1;
                    Stop();
                    PlaylistEnded?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
            PlayCurrent();
        }

        await Task.Delay(100);
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    /// <summary>
    /// 前の楽曲に戻ります
    /// </summary>
    public async void Previous()
    {
        if (_playlist.Count == 0) return;
        _currentIndex--;
        if (_currentIndex < 0) _currentIndex = _playlist.Count - 1;
        PlayCurrent();
        await Task.Delay(100);
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    /// <summary>
    /// 再生を停止します
    /// </summary>
    /// <param name="internalStop">内部要因による停止かどうか</param>
    public void Stop(bool internalStop = false)
    {
        lock (_lock)
        {
            if (internalStop) _stopRequested = true;

            StopInternal();
            _currentIndex = -1;
            _stopRequested = false;
        }

        PlaybackStopped?.Invoke();
        PlaybackStateChanged?.Invoke(false);
    }

    /// <summary>
    /// 指定位置（パーセンテージ）へシークします
    /// </summary>
    /// <param name="percentage">シーク位置（0.0〜1.0）</param>
    public void SeekTo(double percentage)
    {
        lock (_lock)
        {
            if (_audioFile != null)
            {
                var targetTime = TimeSpan.FromSeconds(_audioFile.TotalTime.TotalSeconds * Math.Clamp(percentage, 0.0, 1.0));
                _audioFile.CurrentTime = targetTime;
            }
        }
    }

    /// <summary>
    /// イコライザー特定バンドのゲインを設定します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス（0〜9）</param>
    /// <param name="gain">ゲイン値（dB）</param>
    public void SetGain(int bandIndex, float gain)
    {
        _equalizer?.UpdateGain(bandIndex, gain);
    }

    /// <summary>
    /// 現在の再生時間位置
    /// </summary>
    public TimeSpan CurrentTime
    {
        get
        {
            lock (_lock)
            {
                return _audioFile?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// 現在ロード中の楽曲の総再生時間
    /// </summary>
    public TimeSpan TotalTime
    {
        get
        {
            lock (_lock)
            {
                return _audioFile?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// シーク操作のために再生を一時停止します
    /// </summary>
    public void PauseForSeek()
    {
        lock (_lock)
        {
            _wasPlayingBeforeSeek = IsPlaying;
            if (_wasPlayingBeforeSeek)
            {
                _outputDevice?.Pause();
            }
        }
    }

    /// <summary>
    /// シーク操作完了後に再生を再開します
    /// </summary>
    public void ResumeAfterSeek()
    {
        lock (_lock)
        {
            if (_wasPlayingBeforeSeek && _outputDevice != null)
            {
                _outputDevice.Play();
            }
        }
    }

    private float _volume = 1.0f;

    /// <summary>
    /// 音量値（0.0〜1.0）
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            float newVol;
            lock (_lock)
            {
                _volume = Math.Min(1.0f, Math.Max(0.0f, value));
                if (_masterVolumeProvider != null)
                {
                    _masterVolumeProvider.Volume = _volume;
                }
                newVol = _volume;
            }
            VolumeChanged?.Invoke(newVol);
        }
    }

    /// <summary>
    /// サンプリングレートおよびバッファサイズを更新します
    /// </summary>
    /// <param name="sampleRate">サンプリングレート（Hz）</param>
    /// <param name="bufferSizeMs">バッファサイズ（ミリ秒）</param>
    public void UpdateAudioProperties(int sampleRate, int bufferSizeMs)
    {
        lock (_lock)
        {
            _sampleRate = sampleRate;
            _bufferSizeMs = bufferSizeMs;
        }
    }

    /// <summary>
    /// アンマネージドリソースおよびオーディオエンジンを解放します
    /// </summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ストリーム末尾を検知するためのISampleProviderラッパークラス
/// </summary>
public class EndOfStreamProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private bool _endReached;

    /// <summary>
    /// ストリーム末尾到達時に発生するイベント
    /// </summary>
    public event Action? EndOfStream;

    /// <summary>
    /// EndOfStreamProviderを初期化します
    /// </summary>
    /// <param name="source">ラップ対象のサンプルプロバイダー</param>
    public EndOfStreamProvider(ISampleProvider source)
    {
        _source = source;
    }

    /// <summary>
    /// 波形フォーマット
    /// </summary>
    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// 音声サンプルデータを読み込みます
    /// </summary>
    /// <param name="buffer">読み込み先バッファ</param>
    /// <param name="offset">バッファ内の開始オフセット</param>
    /// <param name="count">読み込みサンプル数</param>
    /// <returns>実際に読み込まれたサンプル数</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read == 0 && !_endReached)
        {
            _endReached = true;
            EndOfStream?.Invoke();
        }
        return read;
    }
}
