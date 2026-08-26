using NAudio.Wave;
using NAudio.Dsp;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Models;
using System.Threading.Tasks;

namespace AudioEffector.Services
{
    /// <summary>
    /// オーディオ再生、プレイリスト管理、イコライザー処理を統括するコアサービス。
    /// NAudioを使用しています。
    /// </summary>
    public class AudioService : IAudioService
    {
        private IWavePlayer _outputDevice;
        private AudioFileReader _audioFile;
        private Equalizer _equalizer;
        private List<Track> _playlist = new List<Track>();
        private List<Track> _originalPlaylist = new List<Track>();
        private int _currentIndex = -1;
        private bool _isShuffleEnabled;
        private bool _wasPlayingBeforeSeek = false;
        private Guid _currentPlaybackId;

        private int _sampleRate = 44100;
        private int _bufferSizeMs = 100;
        private WdlResamplingSampleProvider? _resampler;

        private bool _stopRequested;
        private readonly object _lock = new object();

        /// <summary>
        /// トラックが変更された際に発生するイベント。
        /// </summary>
        public event Action<Track> TrackChanged;

        /// <summary>
        /// 再生状態（再生中/停止）が変更された際に発生するイベント。
        /// </summary>
        public event Action<bool> PlaybackStateChanged;

        /// <summary>
        /// 再生が停止した際に発生するイベント。
        /// </summary>
        public event Action PlaybackStopped;

        /// <summary>
        /// プレイリストの最後（リピートなし）に到達した際に発生するイベント。
        /// </summary>
        public event EventHandler PlaylistEnded;

        /// <summary>
        /// FFT計算結果が利用可能になった際に発生するイベント。
        /// </summary>
        public event EventHandler<FftEventArgs>? FftCalculated;

        /// <summary>
        /// プレイリスト（再生キュー）の順序や内容が変更された際に発生するイベント。
        /// </summary>
        public event Action<List<Track>> PlaylistChanged;

        /// <summary>
        /// 音量が変更された際に発生するイベント。
        /// </summary>
        public event Action<float> VolumeChanged;

        // 10-band EQ frequencies
        /// <summary>
        /// イコライザーの周波数帯域定義（10バンド）。
        /// </summary>
        public float[] Frequencies { get; } = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

        public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing;

        /// <summary>
        /// シャッフル再生が有効かどうかを取得または設定します。
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

        public bool IsRepeatEnabled { get; set; }

        /// <summary>
        /// プレイリストを設定します。
        /// シャッフルが有効な場合は即座にシャッフルされます。
        /// </summary>
        /// <param name="tracks">トラックリスト。</param>
        public void SetPlaylist(List<Track> tracks)
        {
            lock (_lock)
            {
                // Capture current track before updating
                // 現在再生中のトラックを保持
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

                // Restore index if current track still exists
                // 現在のトラックが新しいプレイリストにも含まれていればインデックスを復元
                if (currentTrack != null)
                {
                    var newIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
                    if (newIndex >= 0)
                    {
                        _currentIndex = newIndex;
                    }
                    else
                    {
                        _currentIndex = -1; // Track removed / 削除された場合
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
            if (_originalPlaylist == null || !_originalPlaylist.Any()) return;

            var currentTrack = _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

            var rng = new Random();
            _playlist = _originalPlaylist.OrderBy(x => rng.Next()).ToList();

            if (currentTrack != null)
            {
                _currentIndex = _playlist.IndexOf(currentTrack);
            }
            else
            {
                _currentIndex = -1;
            }

            PlaylistChanged?.Invoke(new List<Track>(_playlist));
        }

        private void RestorePlaylist()
        {
            if (_originalPlaylist == null || !_originalPlaylist.Any()) return;

            var currentTrack = _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

            _playlist = new List<Track>(_originalPlaylist);

            if (currentTrack != null)
            {
                _currentIndex = _playlist.IndexOf(currentTrack);
            }
            else
            {
                _currentIndex = -1;
            }

            PlaylistChanged?.Invoke(new List<Track>(_playlist));
        }

        /// <summary>
        /// 指定されたトラックを再生します。
        /// </summary>
        public async void PlayTrack(Track track)
        {
            int index = _playlist.IndexOf(track);
            if (index >= 0)
            {
                _currentIndex = index;
                PlayCurrent();
                // Wait for PlaybackState to update
                // 再生状態の更新待機
                await Task.Delay(100);
                PlaybackStateChanged?.Invoke(IsPlaying);
            }
        }

        private void PlayCurrent()
        {
            lock (_lock)
            {
                if (_currentIndex < 0 || _currentIndex >= _playlist.Count) return;

                // Stop explicitly without triggering Next
                // 次の曲への自動遷移をトリガーせずに停止
                Stop(true);

                // Generate new session ID
                // 新しいセッションIDを生成
                _currentPlaybackId = Guid.NewGuid();

                var track = _playlist[_currentIndex];
                try
                {
                    _audioFile = new AudioFileReader(track.FilePath);
                    _audioFile.Volume = _volume;

                    ISampleProvider sourceProvider = _audioFile;

                    // Apply Peak Normalization if enabled
                    var settings = new SettingsService().LoadSettings();
                    if (settings.EnableNormalize)
                    {
                        float maxPeak = 0;
                        using (var tempReader = new AudioFileReader(track.FilePath))
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
                            // Target 0dBFS (1.0f). Reduce slightly to avoid clipping on resampling.
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
                    // イコライザーの設定
                    _equalizer = new Equalizer(sourceProvider, Frequencies);

                    // Setup SampleAggregator for FFT
                    // FFT用のサンプル集計器を設定
                    var aggregator = new SampleAggregator(_equalizer);
                    aggregator.FftCalculated += (s, e) => FftCalculated?.Invoke(this, e);

                    // Wrap with EndOfStreamProvider to detect end of playback reliably
                    // 再生終了を確実に検知するためにラップする
                    var endOfStreamProvider = new EndOfStreamProvider(aggregator);
                    endOfStreamProvider.EndOfStream += OnEndOfStream;

                    _outputDevice = new WaveOutEvent() { DesiredLatency = _bufferSizeMs };
                    _outputDevice.Init(endOfStreamProvider);
                    _outputDevice.PlaybackStopped += OnPlaybackStopped;

                    TrackChanged?.Invoke(track);
                    _outputDevice.Play();
                    PlaybackStateChanged?.Invoke(true);
                }
                catch (Exception ex)
                {
                    // Handle error (e.g. file not found)
                    System.Diagnostics.Debug.WriteLine($"Error playing file: {ex.Message}");
                    // Ensure cleanup if initialization fails
                    Stop(true);
                }
            }
        }

        private void OnEndOfStream()
        {
            // Capture the current ID
            var sessionId = _currentPlaybackId;

            // Trigger Next() when the stream ends (0 bytes read)
            // Run asynchronously to avoid blocking the audio thread
            // ストリーム終了時に非同期で次へ遷移
            Task.Run(() =>
            {
                // Add a small delay to allow the last buffer to play out
                // 最後のバッファが再生されるのを少し待つ
                System.Threading.Thread.Sleep(500);

                // Check if the session is still valid
                lock (_lock)
                {
                    if (_currentPlaybackId != sessionId) return;
                }

                Next();
            });
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (_stopRequested)
            {
                _stopRequested = false;
                PlaybackStopped?.Invoke();
                PlaybackStateChanged?.Invoke(false);
                return;
            }

            if (e.Exception == null)
            {
                // Natural end of track, play next asynchronously to avoid disposing active device in event handler
                // 正常終了の場合、非同期で次へ
                Task.Run(() =>
                {
                    Next();
                });
            }
            else
            {
                // Error
                PlaybackStopped?.Invoke();
                PlaybackStateChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// 再生/一時停止を切り替えます。
        /// </summary>
        public void TogglePlayPause()
        {
            lock (_lock)
            {
                if (_outputDevice == null)
                {
                    if (_playlist.Any() && _currentIndex == -1)
                    {
                        _currentIndex = 0;
                        PlayCurrent();
                    }
                    return;
                }

                try
                {
                    if (_outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        _outputDevice.Pause();
                        PlaybackStateChanged?.Invoke(false);
                    }
                    else
                    {
                        _outputDevice.Play();
                        PlaybackStateChanged?.Invoke(true);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in TogglePlayPause: {ex.Message}");
                    // If device is in bad state, stop and cleanup
                    Stop(true);
                    PlaybackStateChanged?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// 次の曲へ進みます。
        /// </summary>
        public async void Next()
        {
            if (_playlist.Count == 0) return;

            if (_currentIndex < _playlist.Count - 1)
            {
                _currentIndex++;
                PlayCurrent();
            }
            else
            {
                // End of playlist
                // プレイリスト終了
                if (IsRepeatEnabled)
                {
                    _currentIndex = 0;
                    PlayCurrent();
                }
                else
                {
                    Stop(true); // Stop and reset position
                    PlaylistEnded?.Invoke(this, EventArgs.Empty);
                }
            }

            // Wait for PlaybackState to update
            await Task.Delay(100);
            PlaybackStateChanged?.Invoke(IsPlaying);
        }

        /// <summary>
        /// 前の曲に戻ります。
        /// </summary>
        public async void Previous()
        {
            if (_playlist.Count == 0) return;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _playlist.Count - 1; // Loop
            PlayCurrent();
            // Wait for PlaybackState to update
            await Task.Delay(100);
            PlaybackStateChanged?.Invoke(IsPlaying);
        }

        /// <summary>
        /// 再生を停止します。
        /// </summary>
        /// <param name="internalStop">内部呼び出しによる停止かどうか。</param>
        public void Stop(bool internalStop = false)
        {
            lock (_lock)
            {
                if (internalStop) _stopRequested = true;

                if (_outputDevice != null)
                {
                    _outputDevice.Stop();
                    _outputDevice.Dispose();
                    _outputDevice = null;
                }
                if (_resampler != null)
                {
                    _resampler = null;
                }
                if (_audioFile != null)
                {
                    _audioFile.Dispose();
                    _audioFile = null;
                }

                if (!internalStop)
                {
                    PlaybackStateChanged?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// 指定位置（パーセンテージ）へシークします。
        /// </summary>
        public void SeekTo(double percentage)
        {
            lock (_lock)
            {
                if (_audioFile != null)
                {
                    long position = (long)(_audioFile.Length * (percentage / 100));
                    _audioFile.Position = position;
                }
            }
        }

        /// <summary>
        /// 指定バンドのゲインを設定します。
        /// </summary>
        public void SetGain(int bandIndex, float gain)
        {
            _equalizer?.UpdateGain(bandIndex, gain);
        }

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
        /// シーク操作のために一時停止します。
        /// シーク開始時に呼び出してください。
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
        /// シーク後の再生再開を行います。
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
        public float Volume
        {
            get => _volume;
            set
            {
                lock (_lock)
                {
                    _volume = Math.Min(1.0f, Math.Max(0.0f, value));
                    if (_audioFile != null)
                    {
                        _audioFile.Volume = _volume;
                    }
                    VolumeChanged?.Invoke(_volume);
                }
            }
        }

        public void UpdateAudioProperties(int sampleRate, int bufferSizeMs)
        {
            lock (_lock)
            {
                _sampleRate = sampleRate;
                _bufferSizeMs = bufferSizeMs;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    // Helper class to detect end of stream
    public class EndOfStreamProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private bool _endReached;

        public event Action EndOfStream;

        public EndOfStreamProvider(ISampleProvider source)
        {
            _source = source;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

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
}
