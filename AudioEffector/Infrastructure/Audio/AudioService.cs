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
    private readonly HashSet<string> _playedTrackPaths = new(StringComparer.OrdinalIgnoreCase);

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

    private Track? _lastPlayingTrack;
    private bool _stopRequested;

    /// <summary>
    /// トラックが変更された際に発生するイベント（未選択・キュー空時は null）
    /// </summary>
    public event Action<Track?>? TrackChanged;

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
            List<Track>? changedPlaylist = null;
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
                    changedPlaylist = new List<Track>(_playlist);
                }
            }

            if (changedPlaylist != null)
            {
                PlaylistChanged?.Invoke(changedPlaylist);
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
    /// <param name="startTrack">最初に再生対象とするトラック（省略可）</param>
    public void SetPlaylist(List<Track> tracks, Track? startTrack = null)
    {
        bool isEmpty = (tracks == null || tracks.Count == 0);

        lock (_lock)
        {
            _playedTrackPaths.Clear();
            _lastPlayingTrack = null;

            if (isEmpty)
            {
                _originalPlaylist = new List<Track>();
                _playlist = new List<Track>();
                _currentIndex = -1;
            }
            else
            {
                var currentTrack = startTrack ?? (_currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null);

                _originalPlaylist = new List<Track>(tracks!);
                if (_isShuffleEnabled)
                {
                    ShufflePlaylist(currentTrack);
                }
                else
                {
                    _playlist = new List<Track>(tracks!);
                    if (currentTrack != null)
                    {
                        var newIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
                        _currentIndex = newIndex >= 0 ? newIndex : -1;
                    }
                    else
                    {
                        _currentIndex = -1;
                    }
                }
            }
        }

        if (isEmpty)
        {
            Stop();
            TrackChanged?.Invoke(null);
            PlaylistChanged?.Invoke(new List<Track>());
            return;
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
    }

    /// <summary>
    /// トラックコレクションをキューに追加します（単曲またはアルバム）
    /// </summary>
    /// <param name="tracks">追加するトラックコレクション</param>
    /// <param name="playNext">trueの場合、現在再生中の楽曲の直後に追加（次に再生）。falseの場合、キュー末尾に追加。</param>
    public void EnqueueTracks(IReadOnlyList<Track> tracks, bool playNext)
    {
        if (tracks == null || tracks.Count == 0) return;

        lock (_lock)
        {
            // 元の順序リスト（_originalPlaylist）には、アルバムを追加した順（各アルバム内はトラック順）で末尾に追加
            _originalPlaylist.AddRange(tracks);

            if (_playlist.Count == 0)
            {
                if (_isShuffleEnabled)
                {
                    var shuffled = new List<Track>(tracks);
                    var rng = new Random();
                    int n = shuffled.Count;
                    while (n > 1)
                    {
                        n--;
                        int k = rng.Next(n + 1);
                        (shuffled[k], shuffled[n]) = (shuffled[n], shuffled[k]);
                    }

                    _playlist = shuffled;
                }
                else
                {
                    _playlist = new List<Track>(tracks);
                }

                _currentIndex = 0;
            }
            else
            {
                if (_isShuffleEnabled)
                {
                    if (playNext)
                    {
                        // 仕様: シャッフル再生中にアルバム単位で「次に再生」を行うと、
                        // アルバムの順番がランダムな状態で、まとめて現在再生中の曲の直後に追加される。
                        var tracksToAdd = new List<Track>(tracks);
                        if (tracksToAdd.Count > 1)
                        {
                            var rng = new Random();
                            int n = tracksToAdd.Count;
                            while (n > 1)
                            {
                                n--;
                                int k = rng.Next(n + 1);
                                (tracksToAdd[k], tracksToAdd[n]) = (tracksToAdd[n], tracksToAdd[k]);
                            }
                        }

                        int insertIndex = (_currentIndex >= 0 && _currentIndex < _playlist.Count)
                            ? _currentIndex + 1
                            : _playlist.Count;

                        _playlist.InsertRange(insertIndex, tracksToAdd);
                    }
                    else
                    {
                        // 仕様: 「キューに追加」は現状通り（現在再生中以外の全キューリストと再シャッフル）
                        Track? currentTrack = (_currentIndex >= 0 && _currentIndex < _playlist.Count)
                            ? _playlist[_currentIndex] : null;

                        var others = new List<Track>(_playlist);
                        if (currentTrack != null)
                        {
                            others.RemoveAt(_currentIndex);
                        }

                        others.AddRange(tracks);

                        var rng = new Random();
                        int n = others.Count;
                        while (n > 1)
                        {
                            n--;
                            int k = rng.Next(n + 1);
                            (others[k], others[n]) = (others[n], others[k]);
                        }

                        if (currentTrack != null)
                        {
                            others.Insert(0, currentTrack);
                            _currentIndex = 0;
                        }

                        _playlist = others;
                    }
                }
                else
                {
                    // シャッフルOFF時
                    if (playNext)
                    {
                        int insertIndex = (_currentIndex >= 0 && _currentIndex < _playlist.Count)
                            ? _currentIndex + 1
                            : _playlist.Count;
                        _playlist.InsertRange(insertIndex, tracks);
                    }
                    else
                    {
                        _playlist.AddRange(tracks);
                    }
                }
            }
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
    }

    private void ShufflePlaylist(Track? keepFirstTrack = null)
    {
        if (_originalPlaylist.Count <= 1)
        {
            _playlist = new List<Track>(_originalPlaylist);
            _currentIndex = _playlist.Count > 0 ? 0 : -1;
            return;
        }

        Track? currentTrack = keepFirstTrack;
        if (currentTrack == null && _currentIndex >= 0 && _currentIndex < _playlist.Count)
        {
            currentTrack = _playlist[_currentIndex];
        }

        var rng = new Random();
        var shuffled = new List<Track>(_originalPlaylist);

        if (currentTrack != null)
        {
            shuffled.RemoveAll(t => t.FilePath == currentTrack.FilePath);
        }

        int n = shuffled.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (shuffled[k], shuffled[n]) = (shuffled[n], shuffled[k]);
        }

        if (currentTrack != null)
        {
            shuffled.Insert(0, currentTrack);
            _currentIndex = 0;
        }
        else
        {
            _currentIndex = -1;
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

        if (currentTrack == null)
        {
            _playlist = new List<Track>(_originalPlaylist);
            _currentIndex = _playlist.Count > 0 ? 0 : -1;
            return;
        }

        int originalIndex = _originalPlaylist.FindIndex(t => t.FilePath == currentTrack.FilePath);
        if (originalIndex < 0)
        {
            _playlist = new List<Track>(_originalPlaylist);
            _currentIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
            if (_currentIndex < 0 && _playlist.Count > 0) _currentIndex = 0;
            return;
        }

        // 仕様:
        // ・アルバムの再生中に解除した場合、元のアルバム収録順に再生キューの順番を戻す。
        // ・シャッフル再生により、既に再生済みのもの（_playedTrackPathsに含まれる曲）は履歴に残したまま、再生キューはその曲を穴あき（除外）とする。
        // ・未再生の曲については除外せず、再生中の曲の上（現在曲より手前）および下に残す。
        // ・現在再生中の曲のインデックス（_currentIndex）を復元後キュー内の位置に正しく設定する。
        var newPlaylist = new List<Track>();
        foreach (var track in _originalPlaylist)
        {
            if (track.FilePath == currentTrack.FilePath || !_playedTrackPaths.Contains(track.FilePath))
            {
                newPlaylist.Add(track);
            }
        }

        _playlist = newPlaylist;
        _currentIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
        if (_currentIndex < 0 && _playlist.Count > 0)
        {
            _currentIndex = 0;
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

            if (_lastPlayingTrack != null && trackToPlay != null && _lastPlayingTrack.FilePath != trackToPlay.FilePath)
            {
                _playedTrackPaths.Add(_lastPlayingTrack.FilePath);
            }
            _lastPlayingTrack = trackToPlay;
        }

        if (trackToPlay == null)
        {
            Stop();
            TrackChanged?.Invoke(null);
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
        try
        {
            if (_outputDevice != null)
            {
                _outputDevice.PlaybackStopped -= OnPlaybackStopped;
                _outputDevice.Stop();
                _outputDevice.Dispose();
                _outputDevice = null;
            }
        }
        catch { }

        try
        {
            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
        }
        catch { }

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
        lock (_lock)
        {
            if (_playlist.Count == 0) return;
            if (_currentIndex > 0)
            {
                _currentIndex--;
            }
            else
            {
                if (IsRepeatEnabled)
                {
                    _currentIndex = _playlist.Count - 1;
                }
                else
                {
                    _currentIndex = 0;
                }
            }
            PlayCurrent();
        }

        await Task.Delay(100);
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    /// <summary>
    /// 再生を停止します
    /// </summary>
    /// <param name="internalStop">内部要因による停止かどうか</param>
    public void Stop(bool internalStop = false)
    {
        bool playlistEmpty = false;
        lock (_lock)
        {
            if (internalStop) _stopRequested = true;

            StopInternal();
            _currentIndex = -1;
            _stopRequested = false;
            playlistEmpty = (_playlist.Count == 0);
        }

        PlaybackStopped?.Invoke();
        PlaybackStateChanged?.Invoke(false);
        if (playlistEmpty)
        {
            TrackChanged?.Invoke(null);
        }
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
