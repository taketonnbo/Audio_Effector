using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 楽曲の再生完了イベントを購読して累計統計・日時毎再生履歴を永続化し、
/// 楽曲別統計およびよく聴く曲 TOP N を提供するアプリケーションサービスです。
/// </summary>
public sealed class ListeningStatsApplicationService : IDisposable
{
    /// <summary>
    /// よく聴く曲ランキングの既定表示件数
    /// </summary>
    public const int DefaultTopCount = 5;

    private readonly IListeningStatsRepository _repository;
    private readonly IAudioService _audioService;
    private readonly Func<DateTime> _clock;
    private bool _disposed;

    /// <summary>
    /// リスニング統計が更新された際に発火するイベント（更新対象の曲別統計を通知）
    /// </summary>
    public event Action<TrackListeningStat>? StatsUpdated;

    /// <summary>
    /// <see cref="ListeningStatsApplicationService"/> を初期化します
    /// </summary>
    /// <param name="repository">リスニング統計リポジトリ</param>
    /// <param name="audioService">音声再生サービス</param>
    /// <param name="clock">現在時刻プロバイダ（省略時は <see cref="DateTime.Now"/>）</param>
    public ListeningStatsApplicationService(
        IListeningStatsRepository repository,
        IAudioService audioService,
        Func<DateTime>? clock = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _clock = clock ?? (() => DateTime.Now);

        _audioService.TrackPlaybackEnded += OnTrackPlaybackEnded;
    }

    /// <summary>
    /// 再生完了した楽曲の累計再生回数・累計再生時間・最終再生日時および日時毎再生履歴を記録・永続化します
    /// </summary>
    /// <param name="track">再生完了した楽曲</param>
    /// <returns>更新後の楽曲統計（対象外または永続化不可時は null）</returns>
    public TrackListeningStat? RecordPlaybackCompleted(Track? track)
    {
        if (_disposed || track == null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return null;
        }

        DateTime playedAt = _clock();
        TimeSpan playedDuration = track.Duration > TimeSpan.Zero ? track.Duration : TimeSpan.Zero;
        TrackListeningStat? updatedStat = null;

        try
        {
            _repository.Update(data =>
            {
                TrackListeningStat? existing = data.FindTrackStat(track.FilePath);
                if (existing == null)
                {
                    existing = TrackListeningStat.FromTrack(track);
                    data.TrackStats.Add(existing);
                }

                existing.RecordPlaybackCompleted(track, playedAt, playedDuration);
                data.PlayEvents.Add(TrackPlayEvent.FromTrack(track, playedAt, playedDuration));
                updatedStat = existing.Clone();
            });
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"リスニング統計の保存に失敗しました: {ex.Message}");
            return null;
        }

        if (updatedStat != null)
        {
            StatsUpdated?.Invoke(updatedStat.Clone());
        }

        return updatedStat;
    }

    /// <summary>
    /// 指定されたファイルパスに紐づく楽曲の累計統計を取得します
    /// </summary>
    /// <param name="filePath">検索対象の音声ファイルパス</param>
    /// <returns>該当楽曲の累計統計（未記録またはファイル読込失敗時は null）</returns>
    public TrackListeningStat? GetTrackStat(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            ListeningStatsData data = _repository.Load();
            return data.FindTrackStat(filePath)?.Clone();
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"リスニング統計の読み込みに失敗しました: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 累計再生回数降順でよく聴く曲 TOP N を取得します
    /// </summary>
    /// <param name="count">取得件数上限（1 以上、既定 5 件）</param>
    /// <returns>再生回数降順に並んだ楽曲統計一覧</returns>
    public IReadOnlyList<TrackListeningStat> GetTopPlayedTracks(int count = DefaultTopCount)
    {
        if (count <= 0)
        {
            return Array.Empty<TrackListeningStat>();
        }

        try
        {
            ListeningStatsData data = _repository.Load();
            return data.TrackStats
                .Where(stat => stat.PlayCount > 0)
                .OrderByDescending(stat => stat.PlayCount)
                .ThenByDescending(stat => stat.TotalListeningDuration)
                .ThenByDescending(stat => stat.LastPlayedAt)
                .ThenBy(stat => stat.Title, StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .Select(stat => stat.Clone())
                .ToList();
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"よく聴く曲ランキングの取得に失敗しました: {ex.Message}");
            return Array.Empty<TrackListeningStat>();
        }
    }

    /// <summary>
    /// リスニング統計データ全体のスナップショットを取得します
    /// </summary>
    /// <returns>リスニング統計データ（取得失敗時は空データ）</returns>
    public ListeningStatsData GetAllStatsSnapshot()
    {
        try
        {
            return _repository.Load();
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"リスニング統計スナップショットの取得に失敗しました: {ex.Message}");
            return new ListeningStatsData();
        }
    }

    private void OnTrackPlaybackEnded(Track track)
    {
        RecordPlaybackCompleted(track);
    }

    private static bool IsStorageUnavailable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or TimeoutException or JsonException or OverflowException;

    /// <summary>
    /// イベント購読を解除してリソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _audioService.TrackPlaybackEnded -= OnTrackPlaybackEnded;
        }
    }
}
