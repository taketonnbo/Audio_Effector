using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 音声再生、キュー順序管理、シーク、音量制御などの操作シナリオを統括するアプリケーションサービス
/// </summary>
public class AudioApplicationService
{
    private readonly IAudioEngine _audioEngine;
    private readonly IAudioService? _legacyAudioService;
    private readonly ITrackRepository _trackRepository;
    private readonly IEventBus _eventBus;
    private readonly object _lock = new();
    private readonly List<Track> _playbackQueue = new();

    private IPlaybackOrderStrategy _playbackOrderStrategy;
    private int _currentIndex = -1;
    private Track? _currentTrack;
    private Volume _currentVolume = Volume.FromFloat(0.5f);

    /// <summary>
    /// 現在再生中のトラック
    /// </summary>
    public Track? CurrentTrack
    {
        get
        {
            lock (_lock) return _currentTrack;
        }
    }

    /// <summary>
    /// 現在の再生状態
    /// </summary>
    public PlaybackState CurrentState => _audioEngine.CurrentState;

    /// <summary>
    /// 現在の再生時間位置
    /// </summary>
    public TimeSpan CurrentPosition => _audioEngine.CurrentPosition;

    /// <summary>
    /// 現在の音量設定値
    /// </summary>
    public Volume CurrentVolume
    {
        get
        {
            lock (_lock) return _currentVolume;
        }
    }

    /// <summary>
    /// 現在の再生キュー
    /// </summary>
    public IReadOnlyList<Track> PlaybackQueue
    {
        get
        {
            lock (_lock) return _playbackQueue.AsReadOnly();
        }
    }

    /// <summary>
    /// AudioApplicationServiceを初期化します
    /// </summary>
    /// <param name="audioEngine">オーディオ再生エンジン</param>
    /// <param name="trackRepository">トラックリポジトリ</param>
    /// <param name="eventBus">イベントバス</param>
    /// <param name="playbackOrderStrategy">再生順序戦略（未指定時はSequentialPlaybackStrategy）</param>
    /// <param name="legacyAudioService">移行期間中の既存再生サービス</param>
    public AudioApplicationService(
        IAudioEngine audioEngine,
        ITrackRepository trackRepository,
        IEventBus eventBus,
        IPlaybackOrderStrategy? playbackOrderStrategy = null,
        IAudioService? legacyAudioService = null)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _trackRepository = trackRepository ?? throw new ArgumentNullException(nameof(trackRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _playbackOrderStrategy = playbackOrderStrategy ?? new SequentialPlaybackStrategy();
        _legacyAudioService = legacyAudioService;

        _audioEngine.PlaybackEnded += OnPlaybackEnded;
    }

    /// <summary>
    /// 再生順序戦略（通常、シャッフル、リピート等）を設定します
    /// </summary>
    /// <param name="strategy">再生順序戦略</param>
    public void SetPlaybackOrderStrategy(IPlaybackOrderStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        lock (_lock)
        {
            _playbackOrderStrategy = strategy;
        }
    }

    /// <summary>
    /// 再生キューを設定し、初期インデックスを指定して再生を開始します
    /// </summary>
    /// <param name="tracks">再生対象トラックのコレクション</param>
    /// <param name="startIndex">開始トラックのインデックス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SetQueueAndPlayAsync(IEnumerable<Track> tracks, int startIndex = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        Track? targetTrack = null;
        lock (_lock)
        {
            _playbackQueue.Clear();
            _playbackQueue.AddRange(tracks);

            if (_playbackQueue.Count > 0)
            {
                _currentIndex = Math.Clamp(startIndex, 0, _playbackQueue.Count - 1);
                targetTrack = _playbackQueue[_currentIndex];
                _currentTrack = targetTrack;
            }
            else
            {
                _currentIndex = -1;
                _currentTrack = null;
            }
        }

        if (targetTrack != null)
        {
            await PlayTrackInternalAsync(targetTrack, cancellationToken);
        }
    }

    /// <summary>
    /// 指定されたIDのトラックをロードして再生します
    /// </summary>
    /// <param name="trackId">トラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task<Result> PlayTrackAsync(TrackId trackId, CancellationToken cancellationToken = default)
    {
        var track = await _trackRepository.GetByIdAsync(trackId, cancellationToken);
        if (track == null)
        {
            return Result.Failure($"トラックが見つかりません: ID {trackId.Value}");
        }

        lock (_lock)
        {
            _currentTrack = track;
            int foundIndex = _playbackQueue.FindIndex(t => t.Id == track.Id);
            if (foundIndex >= 0)
            {
                _currentIndex = foundIndex;
            }
            else
            {
                _playbackQueue.Clear();
                _playbackQueue.Add(track);
                _currentIndex = 0;
            }
        }

        await PlayTrackInternalAsync(track, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// 音声の再生を一時停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _audioEngine.PauseAsync(cancellationToken);
        await _eventBus.PublishAsync(new PlaybackStateChangedEvent(PlaybackState.Paused), cancellationToken);
    }

    /// <summary>
    /// 一時停止中の音声を再開します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _audioEngine.PlayAsync(cancellationToken);
        await _eventBus.PublishAsync(new PlaybackStateChangedEvent(PlaybackState.Playing), cancellationToken);
    }

    /// <summary>
    /// 音声の再生を停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _audioEngine.StopAsync(cancellationToken);
        await _eventBus.PublishAsync(new PlaybackStateChangedEvent(PlaybackState.Stopped), cancellationToken);
    }

    /// <summary>
    /// 指定された時間位置へシークします
    /// </summary>
    /// <param name="position">シーク先時間位置</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        await _audioEngine.SeekAsync(position, cancellationToken);
    }

    /// <summary>
    /// 音量を設定します
    /// </summary>
    /// <param name="volume">設定する音量値</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SetVolumeAsync(Volume volume, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _currentVolume = volume;
        }

        await _audioEngine.SetVolumeAsync(volume.EffectiveVolume, cancellationToken);
        if (_legacyAudioService != null)
        {
            _legacyAudioService.Volume = volume.EffectiveVolume;
        }

        await _eventBus.PublishAsync(new VolumeChangedEvent(volume.EffectiveVolume, volume.IsMuted), cancellationToken);
    }

    /// <summary>
    /// ミュート状態を切り替えます
    /// </summary>
    /// <param name="isMuted">ミュート状態</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SetMuteAsync(bool isMuted, CancellationToken cancellationToken = default)
    {
        Volume newVolume;
        lock (_lock)
        {
            newVolume = _currentVolume.WithMute(isMuted);
            _currentVolume = newVolume;
        }

        await _audioEngine.SetVolumeAsync(newVolume.EffectiveVolume, cancellationToken);
        if (_legacyAudioService != null)
        {
            _legacyAudioService.Volume = newVolume.EffectiveVolume;
        }

        await _eventBus.PublishAsync(new VolumeChangedEvent(newVolume.EffectiveVolume, newVolume.IsMuted), cancellationToken);
    }

    /// <summary>
    /// 再生戦略に従って次のトラックへ遷移し再生します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task<bool> NextTrackAsync(CancellationToken cancellationToken = default)
    {
        Track? nextTrack = null;
        lock (_lock)
        {
            int? nextIndex = _playbackOrderStrategy.GetNextIndex(_currentIndex, _playbackQueue.Count);
            if (nextIndex.HasValue && nextIndex.Value >= 0 && nextIndex.Value < _playbackQueue.Count)
            {
                _currentIndex = nextIndex.Value;
                nextTrack = _playbackQueue[_currentIndex];
                _currentTrack = nextTrack;
            }
        }

        if (nextTrack != null)
        {
            await PlayTrackInternalAsync(nextTrack, cancellationToken);
            return true;
        }

        await StopAsync(cancellationToken);
        return false;
    }

    /// <summary>
    /// 再生戦略に従って前のトラックへ遷移し再生します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task<bool> PreviousTrackAsync(CancellationToken cancellationToken = default)
    {
        Track? prevTrack = null;
        lock (_lock)
        {
            int? prevIndex = _playbackOrderStrategy.GetPreviousIndex(_currentIndex, _playbackQueue.Count);
            if (prevIndex.HasValue && prevIndex.Value >= 0 && prevIndex.Value < _playbackQueue.Count)
            {
                _currentIndex = prevIndex.Value;
                prevTrack = _playbackQueue[_currentIndex];
                _currentTrack = prevTrack;
            }
        }

        if (prevTrack != null)
        {
            await PlayTrackInternalAsync(prevTrack, cancellationToken);
            return true;
        }

        await StopAsync(cancellationToken);
        return false;
    }

    private async Task PlayTrackInternalAsync(Track track, CancellationToken cancellationToken)
    {
        await _audioEngine.LoadTrackAsync(track, cancellationToken);
        await _audioEngine.PlayAsync(cancellationToken);

        await _eventBus.PublishAsync(new TrackChangedEvent(track, track.Duration), cancellationToken);
        await _eventBus.PublishAsync(new PlaybackStateChangedEvent(PlaybackState.Playing), cancellationToken);
    }

    private async void OnPlaybackEnded(object? sender, EventArgs e)
    {
        await NextTrackAsync();
    }
}
