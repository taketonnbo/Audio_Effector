using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DomainPlaybackState = AudioEffector.Domain.ValueObjects.PlaybackState;

namespace AudioEffector.Infrastructure.Audio;

/// <summary>
/// NAudioを利用して音声ファイルのデコード、DSPイコライザー処理、ボリューム制御、出力デバイス再生を行うエンジン具象クラス
/// </summary>
public class NAudioPlaybackEngine : IAudioEngine
{
    private readonly object _lock = new();
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFileReader;
    private EqualizerDsp? _equalizer;
    private VolumeSampleProvider? _volumeProvider;
    private SampleAggregator? _sampleAggregator;
    private float _currentVolume = 0.5f;
    private bool _disposed;

    /// <summary>
    /// 現在の再生状態
    /// </summary>
    public DomainPlaybackState CurrentState
    {
        get
        {
            lock (_lock)
            {
                if (_outputDevice == null) return DomainPlaybackState.Stopped;
                return _outputDevice.PlaybackState switch
                {
                    NAudio.Wave.PlaybackState.Playing => DomainPlaybackState.Playing,
                    NAudio.Wave.PlaybackState.Paused => DomainPlaybackState.Paused,
                    _ => DomainPlaybackState.Stopped
                };
            }
        }
    }

    /// <summary>
    /// 現在の再生時間位置
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                return _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// 総再生時間
    /// </summary>
    public TimeSpan TotalDuration
    {
        get
        {
            lock (_lock)
            {
                return _audioFileReader?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>
    /// 現在の音量設定値
    /// </summary>
    public float Volume => _currentVolume;

    /// <summary>
    /// FFT計算完了時に発生するイベント
    /// </summary>
    public event EventHandler<FftCalculatedEventArgs>? FftCalculated;

    /// <summary>
    /// 再生終了時に発生するイベント
    /// </summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>
    /// トラックをロードして再生パイプラインを初期化します
    /// </summary>
    /// <param name="track">再生対象のトラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task LoadTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        return Task.Run(() =>
        {
            lock (_lock)
            {
                CleanupCurrentPlayback();

                if (!File.Exists(track.FilePath))
                {
                    throw new FileNotFoundException($"音声ファイルが見つかりません: {track.FilePath}", track.FilePath);
                }

                _audioFileReader = new AudioFileReader(track.FilePath);

                // 10バンドEQの構築
                _equalizer = new EqualizerDsp(_audioFileReader, EqualizerPreset.STANDARD_10_BAND_FREQUENCIES);

                // 音量制御プロバイダー
                _volumeProvider = new VolumeSampleProvider(_equalizer)
                {
                    Volume = _currentVolume
                };

                // FFT集約プロバイダー
                _sampleAggregator = new SampleAggregator(_volumeProvider, 1024);
                _sampleAggregator.FftCalculated += OnSampleAggregatorFftCalculated;

                // 出力デバイスの初期化
                _outputDevice = new WaveOutEvent
                {
                    DesiredLatency = 100,
                    NumberOfBuffers = 3
                };

                _outputDevice.Init(_sampleAggregator);
                _outputDevice.PlaybackStopped += OnOutputDevicePlaybackStopped;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 音声の再生を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _outputDevice?.Play();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 音声の再生を一時停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _outputDevice?.Pause();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 音声の再生を停止し、位置を先頭に戻します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _outputDevice?.Stop();
                if (_audioFileReader != null)
                {
                    _audioFileReader.Position = 0;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 指定された時間位置へシークします
    /// </summary>
    /// <param name="position">シーク先時間位置</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                if (_audioFileReader != null)
                {
                    var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;
                    target = target > _audioFileReader.TotalTime ? _audioFileReader.TotalTime : target;
                    _audioFileReader.CurrentTime = target;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 音量を設定します
    /// </summary>
    /// <param name="volume">音量値（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _currentVolume = Math.Clamp(volume, 0.0f, 1.0f);
                if (_volumeProvider != null)
                {
                    _volumeProvider.Volume = _currentVolume;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// イコライザーの特定バンドのゲインを設定します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス（0〜9）</param>
    /// <param name="gainDb">ゲイン値（dB）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task SetEqualizerBandGainAsync(int bandIndex, float gainDb, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _equalizer?.SetBandGain(bandIndex, gainDb);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 10バンドすべてのイコライザーゲインを一括設定します
    /// </summary>
    /// <param name="gainsDb">ゲイン配列</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task SetEqualizerAllGainsAsync(float[] gainsDb, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                _equalizer?.SetAllGains(gainsDb);
            }
        }, cancellationToken);
    }

    private void OnSampleAggregatorFftCalculated(object? sender, FftCalculatedEventArgs e)
    {
        FftCalculated?.Invoke(this, e);
    }

    private void OnOutputDevicePlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 自然に末尾まで再生されたかを判定
        if (_audioFileReader != null && _audioFileReader.Position >= _audioFileReader.Length)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CleanupCurrentPlayback()
    {
        if (_sampleAggregator != null)
        {
            _sampleAggregator.FftCalculated -= OnSampleAggregatorFftCalculated;
            _sampleAggregator = null;
        }

        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnOutputDevicePlaybackStopped;
            _outputDevice.Stop();
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _audioFileReader?.Dispose();
        _audioFileReader = null;
        _equalizer = null;
        _volumeProvider = null;
    }

    /// <summary>
    /// リソースを破棄します
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// アンマネージ/マネージドリソースの破棄処理を行います
    /// </summary>
    /// <param name="disposing">明示的な破棄かどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            lock (_lock)
            {
                CleanupCurrentPlayback();
            }
        }

        _disposed = true;
    }
}
