using System;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// お気に入り楽曲の永続化およびメタデータベース識別を行うためのエントリモデル
/// </summary>
public sealed class FavoriteTrackEntry
{
    private const string DEFAULT_ARTIST = "Unknown Artist";
    private const string DEFAULT_ALBUM = "Unknown Album";

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
    /// トラック番号（同一アルバム内の同名曲判別用）
    /// </summary>
    public uint TrackNumber { get; }

    /// <summary>
    /// 再生時間（秒・同一曲判別の補助）
    /// </summary>
    public double DurationSeconds { get; }

    /// <summary>
    /// 最後に確認されたファイルパス（タグ未設定曲やタグ編集直後のフォールバック照合用）
    /// </summary>
    public string LastKnownFilePath { get; }

    /// <summary>
    /// お気に入り登録日時
    /// </summary>
    public DateTime AddedAt { get; }

    /// <summary>
    /// メタデータのみによる識別に十分なタグ情報（タイトルおよび既知のアーティストまたはアルバム）を保持しているかどうか
    /// </summary>
    public bool HasSufficientMetadata =>
        !string.IsNullOrWhiteSpace(Title) &&
        (!IsDefaultOrEmptyArtist(Artist) || !IsDefaultOrEmptyAlbum(Album));

    /// <summary>
    /// FavoriteTrackEntryを初期化します
    /// </summary>
    /// <param name="title">楽曲タイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="trackNumber">トラック番号</param>
    /// <param name="durationSeconds">再生時間（秒）</param>
    /// <param name="lastKnownFilePath">最後に確認されたファイルパス</param>
    /// <param name="addedAt">お気に入り登録日時</param>
    public FavoriteTrackEntry(
        string title,
        string artist,
        string album,
        uint trackNumber,
        double durationSeconds,
        string lastKnownFilePath,
        DateTime addedAt)
    {
        Title = NormalizeText(title);
        Artist = NormalizeText(artist);
        Album = NormalizeText(album);
        TrackNumber = trackNumber;
        DurationSeconds = NormalizeDuration(durationSeconds);
        LastKnownFilePath = NormalizeText(lastKnownFilePath);
        AddedAt = addedAt == default ? DateTime.Now : addedAt;
    }

    /// <summary>
    /// Trackエンティティからお気に入りエントリを生成します
    /// </summary>
    /// <param name="track">変換元のトラック</param>
    /// <param name="addedAt">お気に入り登録日時（省略時は現在時刻）</param>
    /// <returns>生成されたお気に入りエントリ</returns>
    public static FavoriteTrackEntry FromTrack(Track track, DateTime? addedAt = null)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new FavoriteTrackEntry(
            title: track.Title,
            artist: track.Artist,
            album: track.Album,
            trackNumber: track.TrackNumber,
            durationSeconds: track.Duration.TotalSeconds,
            lastKnownFilePath: track.FilePath,
            addedAt: addedAt ?? DateTime.Now);
    }

    /// <summary>
    /// 旧形式（ファイルパス文字列のみ）からお気に入りエントリを生成します
    /// </summary>
    /// <param name="filePath">旧形式で保存されていたファイルパス</param>
    /// <param name="addedAt">登録日時（省略時は現在時刻）</param>
    /// <returns>生成されたお気に入りエントリ</returns>
    public static FavoriteTrackEntry FromLegacyFilePath(string filePath, DateTime? addedAt = null)
    {
        return new FavoriteTrackEntry(
            title: string.Empty,
            artist: string.Empty,
            album: string.Empty,
            trackNumber: 0,
            durationSeconds: 0,
            lastKnownFilePath: filePath,
            addedAt: addedAt ?? DateTime.Now);
    }

    /// <summary>
    /// 元の登録日時を維持したまま、最新のトラック情報で更新した新しいエントリを生成します
    /// </summary>
    /// <param name="track">最新情報を保持するトラック</param>
    /// <returns>最新情報で更新されたお気に入りエントリ</returns>
    public FavoriteTrackEntry WithUpdatedTrack(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        string updatedPath = string.IsNullOrWhiteSpace(track.FilePath)
            ? LastKnownFilePath
            : track.FilePath;

        return new FavoriteTrackEntry(
            title: string.IsNullOrWhiteSpace(track.Title) ? Title : track.Title,
            artist: string.IsNullOrWhiteSpace(track.Artist) ? Artist : track.Artist,
            album: string.IsNullOrWhiteSpace(track.Album) ? Album : track.Album,
            trackNumber: track.TrackNumber > 0 ? track.TrackNumber : TrackNumber,
            durationSeconds: track.Duration.TotalSeconds > 0 ? track.Duration.TotalSeconds : DurationSeconds,
            lastKnownFilePath: updatedPath,
            addedAt: AddedAt);
    }

    /// <summary>
    /// 文字列を前後トリムして正規化します
    /// </summary>
    /// <param name="value">対象文字列</param>
    /// <returns>正規化後の文字列</returns>
    public static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// 再生時間（秒）を非負・小数第3位丸めに正規化します
    /// </summary>
    /// <param name="durationSeconds">再生時間（秒）</param>
    /// <returns>正規化後の再生時間（秒）</returns>
    public static double NormalizeDuration(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return 0d;
        }

        return Math.Round(durationSeconds, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// アーティスト名が未設定またはデフォルト値かどうかを判定します
    /// </summary>
    /// <param name="artist">アーティスト名</param>
    /// <returns>未設定またはデフォルト値の場合はtrue</returns>
    public static bool IsDefaultOrEmptyArtist(string? artist)
    {
        string normalized = NormalizeText(artist);
        return string.IsNullOrEmpty(normalized) ||
               string.Equals(normalized, DEFAULT_ARTIST, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// アルバム名が未設定またはデフォルト値かどうかを判定します
    /// </summary>
    /// <param name="album">アルバム名</param>
    /// <returns>未設定またはデフォルト値の場合はtrue</returns>
    public static bool IsDefaultOrEmptyAlbum(string? album)
    {
        string normalized = NormalizeText(album);
        return string.IsNullOrEmpty(normalized) ||
               string.Equals(normalized, DEFAULT_ALBUM, StringComparison.OrdinalIgnoreCase);
    }
}
