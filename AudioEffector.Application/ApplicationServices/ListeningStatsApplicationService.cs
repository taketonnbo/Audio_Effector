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
/// 楽曲別統計および全体・アルバム別のよく聴く曲 TOP N を提供するアプリケーションサービスです。
/// </summary>
public sealed class ListeningStatsApplicationService : IDisposable
{
    /// <summary>
    /// よく聴く曲ランキングの既定表示件数
    /// </summary>
    public const int DefaultTopCount = 5;

    private const string UnknownAlbumName = "Unknown Album";
    private const string UnknownArtistName = "Unknown Artist";

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
    /// 累計再生回数降順でよく聴く曲 TOP N を取得します（ライブラリ全体対象）
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
            return SelectTopPlayed(data.TrackStats, count);
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"よく聴く曲ランキングの取得に失敗しました: {ex.Message}");
            return Array.Empty<TrackListeningStat>();
        }
    }

    /// <summary>
    /// 指定されたアルバムに属する楽曲のみを対象として、累計再生回数降順でよく聴く曲 TOP N を取得します
    /// </summary>
    /// <param name="albumTrackFilePaths">
    /// 対象アルバムに属する全楽曲のファイルパス一覧（指定された場合はファイルパス一致で厳密に絞り込みます）
    /// </param>
    /// <param name="albumName">
    /// ファイルパス一覧を解決できない場合のフォールバック用アルバム名（空または既定の不明アルバム名は無効）
    /// </param>
    /// <param name="artistName">
    /// フォールバック時に同名別アーティストのアルバム混入を防ぐためのアーティスト名（省略可）
    /// </param>
    /// <param name="count">取得件数上限（1 以上、既定 5 件）</param>
    /// <returns>対象アルバム内で再生回数降順に並んだ楽曲統計一覧</returns>
    public IReadOnlyList<TrackListeningStat> GetTopPlayedTracksForAlbum(
        IReadOnlyCollection<string>? albumTrackFilePaths,
        string? albumName = null,
        string? artistName = null,
        int count = DefaultTopCount)
    {
        if (count <= 0)
        {
            return Array.Empty<TrackListeningStat>();
        }

        HashSet<string>? pathSet = null;
        if (albumTrackFilePaths != null && albumTrackFilePaths.Count > 0)
        {
            pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in albumTrackFilePaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    pathSet.Add(path);
                }
            }
        }

        string? normalizedAlbum = NormalizeMeaningfulMetadata(albumName, UnknownAlbumName);
        string? normalizedArtist = NormalizeMeaningfulMetadata(artistName, UnknownArtistName);

        if ((pathSet == null || pathSet.Count == 0) && normalizedAlbum == null)
        {
            return Array.Empty<TrackListeningStat>();
        }

        try
        {
            ListeningStatsData data = _repository.Load();
            IEnumerable<TrackListeningStat> filtered;

            if (pathSet != null && pathSet.Count > 0)
            {
                filtered = data.TrackStats.Where(stat => pathSet.Contains(stat.FilePath));
            }
            else
            {
                filtered = data.TrackStats.Where(stat =>
                    string.Equals(stat.Album?.Trim(), normalizedAlbum, StringComparison.OrdinalIgnoreCase) &&
                    (normalizedArtist == null ||
                     string.Equals(stat.Artist?.Trim(), normalizedArtist, StringComparison.OrdinalIgnoreCase)));
            }

            return SelectTopPlayed(filtered, count);
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            Trace.TraceWarning($"アルバム内よく聴く曲ランキングの取得に失敗しました: {ex.Message}");
            return Array.Empty<TrackListeningStat>();
        }
    }

    private static List<TrackListeningStat> SelectTopPlayed(
        IEnumerable<TrackListeningStat> source,
        int count)
    {
        return source
            .Where(stat => stat.PlayCount > 0)
            .OrderByDescending(stat => stat.PlayCount)
            .ThenByDescending(stat => stat.TotalListeningDuration)
            .ThenByDescending(stat => stat.LastPlayedAt)
            .ThenBy(stat => stat.Title, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(stat => stat.Clone())
            .ToList();
    }

    private static string? NormalizeMeaningfulMetadata(string? value, string unknownSentinel)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return string.Equals(trimmed, unknownSentinel, StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
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
