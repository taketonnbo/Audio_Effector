using System;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// 1回分の楽曲再生完了イベント（日時毎の再生履歴）を表すエンティティ。
/// 期間別集計・日別/月別推移・時間帯/曜日別傾向の分析基盤として利用されます。
/// </summary>
public sealed class TrackPlayEvent
{
    /// <summary>
    /// 再生日時（ローカル時刻）
    /// </summary>
    public DateTime PlayedAt { get; }

    /// <summary>
    /// 音声ファイルパス
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist { get; }

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album { get; }

    /// <summary>
    /// 再生された時間
    /// </summary>
    public TimeSpan PlayedDuration { get; }

    /// <summary>
    /// <see cref="TrackPlayEvent"/> を初期化します
    /// </summary>
    /// <param name="playedAt">再生日時</param>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="title">楽曲タイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="playedDuration">再生された時間</param>
    public TrackPlayEvent(
        DateTime playedAt,
        string filePath,
        string title,
        string artist,
        string album,
        TimeSpan playedDuration)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("FilePath cannot be null or whitespace.", nameof(filePath));
        }

        PlayedAt = playedAt;
        FilePath = filePath;
        Title = string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileNameWithoutExtension(filePath) : title;
        Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist;
        Album = string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album;
        PlayedDuration = playedDuration < TimeSpan.Zero ? TimeSpan.Zero : playedDuration;
    }

    /// <summary>
    /// <see cref="Track"/> エンティティから再生イベントを生成します
    /// </summary>
    /// <param name="track">再生された楽曲</param>
    /// <param name="playedAt">再生日時</param>
    /// <param name="playedDuration">再生時間（未指定時は <see cref="Track.Duration"/>）</param>
    /// <returns>生成された <see cref="TrackPlayEvent"/></returns>
    public static TrackPlayEvent FromTrack(Track track, DateTime playedAt, TimeSpan? playedDuration = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        TimeSpan duration = playedDuration ?? track.Duration;
        return new TrackPlayEvent(
            playedAt,
            track.FilePath,
            track.Title,
            track.Artist,
            track.Album,
            duration);
    }

    /// <summary>
    /// 本インスタンスの独立した複製を生成します
    /// </summary>
    /// <returns>複製された <see cref="TrackPlayEvent"/></returns>
    public TrackPlayEvent Clone()
    {
        return new TrackPlayEvent(PlayedAt, FilePath, Title, Artist, Album, PlayedDuration);
    }
}
