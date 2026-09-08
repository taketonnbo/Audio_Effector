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
    private List<Track> _userQueue = new();
    private List<Track> _albumQueue = new();
    private List<Track> _originalAlbumTracks = new();
    private List<Track> _playlist = new();
    private Track? _currentlyPlayingTrack;
    private bool _isShuffleEnabled;
    private bool _wasPlayingBeforeSeek;
    private Guid _currentPlaybackId;

    private int _sampleRate = 44100;
    private int _bufferSizeMs = 100;
    private WdlResamplingSampleProvider? _resampler;
    private VolumeSampleProvider? _masterVolumeProvider;

    /// <summary>
    /// ユーザーが手動で追加した予約キュー
    /// </summary>
    public IReadOnlyList<Track> UserQueue
    {
        get { lock (_lock) { return new List<Track>(_userQueue); } }
    }

    /// <summary>
    /// アルバムから引き続いて再生される予定のキュー
    /// </summary>
    public IReadOnlyList<Track> AlbumQueue
    {
        get { lock (_lock) { return new List<Track>(_albumQueue); } }
    }

    /// <summary>
    /// 予約キューのみを全クリアします
    /// </summary>
    public void ClearUserQueue()
    {
        List<Track>? changedPlaylist = null;
        lock (_lock)
        {
            if (_userQueue.Count > 0)
            {
                _userQueue.Clear();
                _playlist = new List<Track>(_albumQueue);
                _finalTrackOfQueue = _albumQueue.Count > 0 ? _albumQueue[^1] : _currentlyPlayingTrack;
                changedPlaylist = new List<Track>(_playlist);
            }
        }

        if (changedPlaylist != null)
        {
            PlaylistChanged?.Invoke(changedPlaylist);
        }
    }

    /// <summary>
    /// 再生履歴に追加するための最小再生時間（秒）
    /// </summary>
    public const double MinPlaybackSecondsForHistory = 5.0;

    private Track? _lastPlayingTrack;
    private Track? _finalTrackOfQueue;
    private bool _stopRequested;
    private bool _currentTrackReportedAsEnded;

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
    /// 楽曲の再生が終了（完奏または一定時間以上の再生後の遷移・停止）した際に発生するイベント
    /// </summary>
    public event Action<Track>? TrackPlaybackEnded;

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
        Track? endedTrack = null;

        lock (_lock)
        {
            endedTrack = CheckAndPreparePlaybackEnded(forceEnded: false);
            _playedTrackPaths.Clear();
            _lastPlayingTrack = null;
            _currentTrackReportedAsEnded = false;
            _userQueue.Clear();

            if (isEmpty)
            {
                _originalAlbumTracks.Clear();
                _albumQueue.Clear();
                _playlist.Clear();
                _currentlyPlayingTrack = null;
                _finalTrackOfQueue = null;
            }
            else
            {
                _originalAlbumTracks = new List<Track>(tracks!);
                var currentTrack = startTrack ?? tracks![0];
                _currentlyPlayingTrack = currentTrack;

                if (_isShuffleEnabled)
                {
                    var remaining = tracks!.Where(t => !string.Equals(t.FilePath, currentTrack.FilePath, StringComparison.OrdinalIgnoreCase)).ToList();
                    ShuffleList(remaining);
                    _albumQueue = remaining;
                    _finalTrackOfQueue = _albumQueue.Count > 0 ? _albumQueue[^1] : currentTrack;
                }
                else
                {
                    int startIdx = tracks!.FindIndex(t => string.Equals(t.FilePath, currentTrack.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (startIdx >= 0)
                    {
                        _albumQueue = tracks!.Skip(startIdx + 1).ToList();
                    }
                    else
                    {
                        _albumQueue = tracks!.Where(t => !string.Equals(t.FilePath, currentTrack.FilePath, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    _finalTrackOfQueue = _albumQueue.Count > 0 ? _albumQueue[^1] : currentTrack;
                }

                _playlist = new List<Track>(_albumQueue);
            }
        }

        if (endedTrack != null)
        {
            TrackPlaybackEnded?.Invoke(endedTrack);
        }

        if (isEmpty)
        {
            Stop();
            TrackChanged?.Invoke(null);
            PlaylistChanged?.Invoke(new List<Track>());
            return;
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
        if (_currentlyPlayingTrack != null)
        {
            TrackChanged?.Invoke(_currentlyPlayingTrack);
        }
    }

    /// <summary>
    /// トラックコレクションをキューに追加します（単曲またはアルバム）
    /// </summary>
    /// <param name="tracks">追加するトラックコレクション</param>
    /// <param name="playNext">trueの場合、現在再生中の楽曲の直後に追加（次に再生）。falseの場合、キュー末尾に追加（最後に再生）。</param>
    public void EnqueueTracks(IReadOnlyList<Track> tracks, bool playNext)
    {
        if (tracks == null || tracks.Count == 0) return;

        lock (_lock)
        {
            if (playNext)
            {
                // 仕様: 「次に再生」は予約キューの先頭に追加
                var tracksToAdd = new List<Track>(tracks);
                if (_isShuffleEnabled && tracksToAdd.Count > 1)
                {
                    // シャッフル中かつアルバム単位の場合、アルバム収録曲をシャッフルして追加
                    ShuffleList(tracksToAdd);
                }
                _userQueue.InsertRange(0, tracksToAdd);
            }
            else
            {
                // 仕様: 「最後に再生」を行うと、予約キューとアルバムの残りを統合し、まとめて予約キューとする
                _userQueue.AddRange(_albumQueue);
                _albumQueue.Clear();
                _originalAlbumTracks.Clear();

                var tracksToAdd = new List<Track>(tracks);
                if (_isShuffleEnabled && tracksToAdd.Count > 1)
                {
                    // アルバム単位の場合、アルバム収録曲をシャッフルして追加
                    ShuffleList(tracksToAdd);
                }
                _userQueue.AddRange(tracksToAdd);
            }

            _playlist = _userQueue.Concat(_albumQueue).ToList();
            _finalTrackOfQueue = _playlist.Count > 0 ? _playlist[^1] : _currentlyPlayingTrack;
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
    }

    /// <summary>
    /// 指定されたトラックをキューから削除します
    /// </summary>
    /// <param name="track">削除対象のトラック</param>
    public void RemoveTrack(Track track)
    {
        if (track == null) return;

        lock (_lock)
        {
            _userQueue.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
            _albumQueue.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
            _originalAlbumTracks.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));

            _playlist = _userQueue.Concat(_albumQueue).ToList();
            if (_finalTrackOfQueue != null && string.Equals(_finalTrackOfQueue.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _finalTrackOfQueue = _playlist.Count > 0 ? _playlist[^1] : _currentlyPlayingTrack;
            }
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
    }

    private void ShufflePlaylist(Track? keepFirstTrack = null)
    {
        // 仕様: シャッフルの適用範囲を「AlbumQueue」に限定
        if (_albumQueue.Count > 1)
        {
            ShuffleList(_albumQueue);
        }
        _playlist = _userQueue.Concat(_albumQueue).ToList();
        if (_albumQueue.Count > 0)
        {
            _finalTrackOfQueue = _albumQueue[^1];
        }
    }

    private static void ShuffleList(List<Track> list)
    {
        var rng = new Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    private void RestorePlaylist()
    {
        // 仕様: 予約キューはそのまま保持し、アルバム残り曲のみを元のアルバムトラック順序に復元
        if (_albumQueue.Count > 1 && _originalAlbumTracks.Count > 0)
        {
            _albumQueue = _albumQueue
                .OrderBy(t => t.TrackNumber > 0 ? (int)t.TrackNumber : int.MaxValue)
                .ThenBy(t => _originalAlbumTracks.FindIndex(o => string.Equals(o.FilePath, t.FilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        _playlist = _userQueue.Concat(_albumQueue).ToList();
        if (_albumQueue.Count > 0)
        {
            _finalTrackOfQueue = _albumQueue[^1];
        }
    }

    /// <summary>
    /// 指定された楽曲を再生します
    /// </summary>
    /// <param name="track">再生対象のトラック</param>
    public void PlayTrack(Track track)
    {
        PlayTrack(track, false);
    }

    /// <summary>
    /// 指定された楽曲を再生します
    /// </summary>
    /// <param name="track">再生対象のトラック</param>
    /// <param name="insertAtBeginning">再生キューの先頭に挿入して再生するかどうか</param>
    public void PlayTrack(Track track, bool insertAtBeginning)
    {
        lock (_lock)
        {
            _currentlyPlayingTrack = track;
            _userQueue.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
            _albumQueue.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
            _playlist = _userQueue.Concat(_albumQueue).ToList();
        }

        PlaylistChanged?.Invoke(new List<Track>(_playlist));
        PlayTrackInternal(track);
    }

    private Track? CheckAndPreparePlaybackEnded(bool forceEnded = false)
    {
        if (_currentTrackReportedAsEnded) return null;

        if (_lastPlayingTrack != null)
        {
            _currentTrackReportedAsEnded = true;
            return _lastPlayingTrack;
        }
        return null;
    }

    private void RemoveTrackFromQueueInternal(Track track)
    {
        _userQueue.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
        _albumQueue.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
        _originalAlbumTracks.RemoveAll(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
        _playlist = _userQueue.Concat(_albumQueue).ToList();
    }

    private void PlayCurrent()
    {
        Track? trackToPlay = null;
        lock (_lock)
        {
            if (_currentlyPlayingTrack != null)
            {
                trackToPlay = _currentlyPlayingTrack;
            }
            else if (_userQueue.Count > 0)
            {
                trackToPlay = _userQueue[0];
                _userQueue.RemoveAt(0);
            }
            else if (_albumQueue.Count > 0)
            {
                trackToPlay = _albumQueue[0];
                _albumQueue.RemoveAt(0);
            }
            _playlist = _userQueue.Concat(_albumQueue).ToList();
        }

        if (trackToPlay != null)
        {
            PlaylistChanged?.Invoke(new List<Track>(_playlist));
            PlayTrackInternal(trackToPlay);
        }
    }

    private async void PlayTrackInternal(Track trackToPlay)
    {
        Guid thisPlaybackId = Guid.NewGuid();
        Track? endedTrack = null;
        lock (_lock)
        {
            _currentPlaybackId = thisPlaybackId;
            if (_currentlyPlayingTrack != null && !string.Equals(_currentlyPlayingTrack.FilePath, trackToPlay.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                endedTrack = CheckAndPreparePlaybackEnded(forceEnded: false);
                _playedTrackPaths.Add(_currentlyPlayingTrack.FilePath);
            }
            _currentlyPlayingTrack = trackToPlay;
            _lastPlayingTrack = trackToPlay;
            _currentTrackReportedAsEnded = false;
        }

        if (endedTrack != null)
        {
            TrackPlaybackEnded?.Invoke(endedTrack);
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
            if (_outputDevice == null)
            {
                if (_currentlyPlayingTrack != null)
                {
                    PlayTrackInternal(_currentlyPlayingTrack);
                    return;
                }
                else if (_userQueue.Count > 0 || _albumQueue.Count > 0)
                {
                    PlayCurrent();
                    return;
                }
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
        OnTrackEnded();
        await Task.Delay(100);
        PlaybackStateChanged?.Invoke(IsPlaying);
    }

    private void OnTrackEnded()
    {
        Track? endedTrack = null;
        Track? nextTrack = null;
        bool playlistEmpty = false;
        List<Track>? newPlaylist = null;

        lock (_lock)
        {
            if (_stopRequested) return;

            endedTrack = CheckAndPreparePlaybackEnded(forceEnded: true);
            bool isFinalTrackEnded = endedTrack != null && _finalTrackOfQueue != null &&
                string.Equals(endedTrack.FilePath, _finalTrackOfQueue.FilePath, StringComparison.OrdinalIgnoreCase);

            if (_userQueue.Count > 0)
            {
                nextTrack = _userQueue[0];
                _userQueue.RemoveAt(0);
            }
            else if (_albumQueue.Count > 0)
            {
                nextTrack = _albumQueue[0];
                _albumQueue.RemoveAt(0);
            }

            _playlist = _userQueue.Concat(_albumQueue).ToList();
            newPlaylist = new List<Track>(_playlist);

            if (nextTrack == null || (!IsRepeatEnabled && isFinalTrackEnded))
            {
                if (IsRepeatEnabled && _originalAlbumTracks.Count > 0 && nextTrack == null)
                {
                    var repeatTracks = new List<Track>(_originalAlbumTracks);
                    if (_isShuffleEnabled)
                    {
                        ShuffleList(repeatTracks);
                    }
                    nextTrack = repeatTracks[0];
                    _albumQueue = repeatTracks.Skip(1).ToList();
                    _playlist = _userQueue.Concat(_albumQueue).ToList();
                    newPlaylist = new List<Track>(_playlist);
                }
                else
                {
                    playlistEmpty = true;
                    StopInternal();
                    _currentlyPlayingTrack = null;
                    _finalTrackOfQueue = null;
                }
            }
        }

        if (endedTrack != null)
        {
            TrackPlaybackEnded?.Invoke(endedTrack);
        }

        if (newPlaylist != null)
        {
            PlaylistChanged?.Invoke(newPlaylist);
        }

        if (playlistEmpty)
        {
            PlaylistEnded?.Invoke(this, EventArgs.Empty);

            // PlaylistEnded のハンドラ（次アルバム自動再生等）によって新しい再生が開始されていない場合のみ、停止イベントを発火する
            bool hasNewPlaybackStarted;
            lock (_lock)
            {
                hasNewPlaybackStarted = _playlist.Count > 0 || _currentlyPlayingTrack != null || _outputDevice != null;
            }

            if (!hasNewPlaybackStarted)
            {
                PlaybackStopped?.Invoke();
                PlaybackStateChanged?.Invoke(false);
                TrackChanged?.Invoke(null);
            }
        }
        else if (nextTrack != null)
        {
            PlayTrackInternal(nextTrack);
        }
    }

    /// <summary>
    /// 前の楽曲に戻ります
    /// </summary>
    public async void Previous()
    {
        Track? trackToPlay = null;
        lock (_lock)
        {
            if (_audioFile != null && _audioFile.CurrentTime.TotalSeconds > 3.0)
            {
                _audioFile.CurrentTime = TimeSpan.Zero;
            }
            else
            {
                if (IsRepeatEnabled && _originalAlbumTracks.Count > 0 &&
                    _currentlyPlayingTrack != null &&
                    string.Equals(_currentlyPlayingTrack.FilePath, _originalAlbumTracks[0].FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // 先頭曲再生中でリピート有効の場合、末尾曲へ循環
                    trackToPlay = _originalAlbumTracks[^1];
                    _albumQueue = _originalAlbumTracks.Take(_originalAlbumTracks.Count - 1).ToList();
                    _playlist = _userQueue.Concat(_albumQueue).ToList();
                }
                else if (_currentlyPlayingTrack != null)
                {
                    trackToPlay = _currentlyPlayingTrack;
                }
            }
        }

        if (trackToPlay != null)
        {
            PlayTrackInternal(trackToPlay);
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
        Track? endedTrack = null;
        lock (_lock)
        {
            if (internalStop) _stopRequested = true;

            endedTrack = CheckAndPreparePlaybackEnded(forceEnded: false);

            StopInternal();
            _currentlyPlayingTrack = null;
            _stopRequested = false;
            playlistEmpty = (_playlist.Count == 0);
        }

        if (endedTrack != null)
        {
            TrackPlaybackEnded?.Invoke(endedTrack);
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
        try
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0 && !_endReached)
            {
                _endReached = true;
                EndOfStream?.Invoke();
            }
            return read;
        }
        catch (System.Runtime.InteropServices.InvalidComObjectException)
        {
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
