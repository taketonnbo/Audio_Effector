using System;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// 楽曲単位の累計リスニング統計および「最近再生した曲」表示用の再生日時を表すエンティティ
/// </summary>
public sealed class TrackListeningStat
{
    /// <summary>
    /// 音声ファイルパス（楽曲の識別キー）
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist { get; set; }

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album { get; set; }

    /// <summary>
    /// 楽曲の再生時間
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 累計再生回数（再生完了判定ベース）
    /// </summary>
    public int PlayCount { get; set; }

    /// <summary>
    /// 累計再生時間（再生完了判定ベース）
    /// </summary>
    public TimeSpan TotalListeningDuration { get; set; }

    /// <summary>
    /// 最終再生完了日時（未完了のみの場合は <see cref="DateTime.MinValue"/>）
    /// </summary>
    public DateTime LastPlayedAt { get; set; }

    /// <summary>
    /// 「最近再生した曲」一覧に表示するための最新再生日時（一覧から除外・クリアされた場合は null）
    /// </summary>
    public DateTime? RecentPlayedAt { get; set; }

    /// <summary>
    /// <see cref="TrackListeningStat"/> を初期化します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="title">楽曲タイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="duration">楽曲の再生時間</param>
    /// <param name="playCount">累計再生回数</param>
    /// <param name="totalListeningDuration">累計再生時間</param>
    /// <param name="lastPlayedAt">最終再生完了日時</param>
    /// <param name="recentPlayedAt">最近再生した曲一覧用の再生日時</param>
    public TrackListeningStat(
        string filePath,
        string title,
        string artist,
        string album,
        TimeSpan duration,
        int playCount = 0,
        TimeSpan totalListeningDuration = default,
        DateTime lastPlayedAt = default,
        DateTime? recentPlayedAt = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("FilePath cannot be null or whitespace.", nameof(filePath));
        }

        FilePath = filePath;
        Title = string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileNameWithoutExtension(filePath) : title;
        Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist;
        Album = string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album;
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        PlayCount = Math.Max(0, playCount);
        TotalListeningDuration = totalListeningDuration < TimeSpan.Zero ? TimeSpan.Zero : totalListeningDuration;
        LastPlayedAt = lastPlayedAt;
        RecentPlayedAt = recentPlayedAt;
    }

    /// <summary>
    /// <see cref="Track"/> エンティティから初期状態の統計エントリを生成します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <returns>生成された <see cref="TrackListeningStat"/></returns>
    public static TrackListeningStat FromTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new TrackListeningStat(
            track.FilePath,
            track.Title,
            track.Artist,
            track.Album,
            track.Duration);
    }

    /// <summary>
    /// 楽曲メタデータを最新情報へ更新します
    /// </summary>
    /// <param name="track">最新の楽曲エンティティ</param>
    public void UpdateMetadata(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!string.IsNullOrWhiteSpace(track.Title))
        {
            Title = track.Title;
        }

        if (!string.IsNullOrWhiteSpace(track.Artist))
        {
            Artist = track.Artist;
        }

        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            Album = track.Album;
        }

        if (track.Duration > TimeSpan.Zero)
        {
            Duration = track.Duration;
        }
    }

    /// <summary>
    /// 再生完了イベントに基づき累計再生回数・累計再生時間・最終再生日時を更新します
    /// </summary>
    /// <param name="track">再生完了した楽曲</param>
    /// <param name="playedAt">再生完了日時</param>
    /// <param name="playedDuration">今回の再生時間</param>
    public void RecordPlaybackCompleted(Track track, DateTime playedAt, TimeSpan playedDuration)
    {
        ArgumentNullException.ThrowIfNull(track);

        UpdateMetadata(track);
        PlayCount++;

        TimeSpan effectiveDuration = playedDuration > TimeSpan.Zero
            ? playedDuration
            : (Duration > TimeSpan.Zero ? Duration : TimeSpan.Zero);
        TotalListeningDuration += effectiveDuration;

        if (playedAt > LastPlayedAt)
        {
            LastPlayedAt = playedAt;
        }

        if (!RecentPlayedAt.HasValue || playedAt > RecentPlayedAt.Value)
        {
            RecentPlayedAt = playedAt;
        }
    }

    /// <summary>
    /// 「最近再生した曲」一覧用の再生日時のみを更新します（累計統計は変更しません）
    /// </summary>
    /// <param name="track">再生された楽曲</param>
    /// <param name="playedAt">再生日時</param>
    public void RecordRecentPlayed(Track track, DateTime playedAt)
    {
        ArgumentNullException.ThrowIfNull(track);

        UpdateMetadata(track);
        RecentPlayedAt = playedAt;
    }

    /// <summary>
    /// 保持しているメタデータから <see cref="Track"/> エンティティを生成します
    /// </summary>
    /// <returns>復元された <see cref="Track"/></returns>
    public Track ToTrack()
    {
        return new Track
        {
            FilePath = FilePath,
            Title = Title,
            Artist = Artist,
            Album = Album,
            Duration = Duration,
        };
    }

    /// <summary>
    /// 最近再生履歴表示用の <see cref="RecentTrackEntry"/> へ変換します
    /// </summary>
    /// <returns>変換された <see cref="RecentTrackEntry"/>（<see cref="RecentPlayedAt"/> が未設定の場合は null）</returns>
    public RecentTrackEntry? ToRecentTrackEntry()
    {
        if (!RecentPlayedAt.HasValue)
        {
            return null;
        }

        return new RecentTrackEntry(ToTrack(), RecentPlayedAt.Value);
    }

    /// <summary>
    /// 本インスタンスの独立した複製を生成します
    /// </summary>
    /// <returns>複製された <see cref="TrackListeningStat"/></returns>
    public TrackListeningStat Clone()
    {
        return new TrackListeningStat(
            FilePath,
            Title,
            Artist,
            Album,
            Duration,
            PlayCount,
            TotalListeningDuration,
            LastPlayedAt,
            RecentPlayedAt);
    }
}
