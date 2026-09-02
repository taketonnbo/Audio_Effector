using System;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// 楽曲（音楽トラック）を表すドメインエンティティ
/// </summary>
public class Track : IEquatable<Track>
{
    /// <summary>
    /// 一意のトラックID
    /// </summary>
    public TrackId Id { get; }

    /// <summary>
    /// 音声ファイルのパス
    /// </summary>
    public AudioPath FilePath { get; }

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title { get; private set; }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist { get; private set; }

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album { get; private set; }

    /// <summary>
    /// 再生時間
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// リリース年
    /// </summary>
    public uint Year { get; }

    /// <summary>
    /// トラック番号
    /// </summary>
    public uint TrackNumber { get; }

    /// <summary>
    /// ビットレート（kbps）
    /// </summary>
    public int Bitrate { get; }

    /// <summary>
    /// サンプリング周波数（Hz）
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// 量子化ビット数（bit）
    /// </summary>
    public int BitsPerSample { get; }

    /// <summary>
    /// 音声フォーマット名（例: "FLAC", "MP3"）
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// ジャンル名
    /// </summary>
    public string Genre { get; }

    /// <summary>
    /// お気に入り登録状態
    /// </summary>
    public bool IsFavorite { get; private set; }

    /// <summary>
    /// 可逆圧縮音源かどうか
    /// </summary>
    public bool IsLossless { get; }

    /// <summary>
    /// ハイレゾ音源かどうか（96kHz以上または24bit以上）
    /// </summary>
    public bool IsHiRes { get; }

    /// <summary>
    /// 音質情報のフォーマット済み文字列（例: "24bit/96.0kHz FLAC" または "320kbps/44.1kHz MP3"）
    /// </summary>
    public string QualityInfo
    {
        get
        {
            if (BitsPerSample > 0)
            {
                return $"{BitsPerSample}bit/{SampleRate / 1000.0:F1}kHz {Format}";
            }

            if (Bitrate > 0)
            {
                return $"{Bitrate}kbps/{SampleRate / 1000.0:F1}kHz {Format}";
            }

            return $"{SampleRate / 1000.0:F1}kHz {Format}";
        }
    }

    /// <summary>
    /// 音質ラベル（"Hi-Res" または "Lossless"、それ以外は空文字）
    /// </summary>
    public string QualityLabel => IsHiRes ? "Hi-Res" : (IsLossless ? "Lossless" : string.Empty);

    /// <summary>
    /// トラックエンティティを初期化します
    /// </summary>
    /// <param name="id">トラックID</param>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="title">楽曲タイトル</param>
    /// <param name="artist">アーティスト名</param>
    /// <param name="album">アルバム名</param>
    /// <param name="duration">再生時間</param>
    /// <param name="year">リリース年</param>
    /// <param name="trackNumber">トラック番号</param>
    /// <param name="bitrate">ビットレート</param>
    /// <param name="sampleRate">サンプリング周波数</param>
    /// <param name="bitsPerSample">量子化ビット数</param>
    /// <param name="format">フォーマット</param>
    /// <param name="genre">ジャンル</param>
    /// <param name="isFavorite">お気に入り状態</param>
    /// <param name="isLossless">可逆圧縮フラグ</param>
    /// <param name="isHiRes">ハイレゾフラグ</param>
    public Track(
        TrackId id,
        AudioPath filePath,
        string title,
        string artist,
        string album,
        TimeSpan duration,
        uint year = 0,
        uint trackNumber = 0,
        int bitrate = 0,
        int sampleRate = 44100,
        int bitsPerSample = 16,
        string format = "MP3",
        string genre = "",
        bool isFavorite = false,
        bool isLossless = false,
        bool isHiRes = false)
    {
        Id = id;
        FilePath = filePath;
        Title = string.IsNullOrWhiteSpace(title) ? filePath.FileName : title.Trim();
        Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim();
        Album = string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album.Trim();
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        Year = year;
        TrackNumber = trackNumber;
        Bitrate = bitrate;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Format = string.IsNullOrWhiteSpace(format) ? "Unknown" : format.Trim();
        Genre = genre?.Trim() ?? string.Empty;
        IsFavorite = isFavorite;
        IsLossless = isLossless;
        IsHiRes = isHiRes;
    }

    /// <summary>
    /// お気に入り登録状態を更新します
    /// </summary>
    /// <param name="isFavorite">新しいお気に入り状態</param>
    public void SetFavorite(bool isFavorite)
    {
        IsFavorite = isFavorite;
    }

    /// <summary>
    /// タイトルおよびアーティスト情報を更新します
    /// </summary>
    /// <param name="title">新しいタイトル</param>
    /// <param name="artist">新しいアーティスト名</param>
    /// <param name="album">新しいアルバム名</param>
    public void UpdateMetadata(string title, string artist, string album)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = title.Trim();
        if (!string.IsNullOrWhiteSpace(artist)) Artist = artist.Trim();
        if (!string.IsNullOrWhiteSpace(album)) Album = album.Trim();
    }

    /// <summary>
    /// 同一性判定（TrackIdで比較）
    /// </summary>
    /// <param name="other">比較対象のTrack</param>
    /// <returns>同一の場合はtrue、それ以外はfalse</returns>
    public bool Equals(Track? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id.Equals(other.Id);
    }

    /// <summary>
    /// オブジェクト等価性判定
    /// </summary>
    /// <param name="obj">比較対象オブジェクト</param>
    /// <returns>等価な場合はtrue</returns>
    public override bool Equals(object? obj) => Equals(obj as Track);

    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>楽曲情報の文字列</returns>
    public override string ToString() => $"{Artist} - {Title} ({QualityInfo})";
}
