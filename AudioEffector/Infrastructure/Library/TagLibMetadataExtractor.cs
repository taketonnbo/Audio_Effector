using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Library;

/// <summary>
/// TagLibSharpを利用して音声ファイルのID3/FLACメタデータ解析およびカバーアート抽出を行うクラス
/// </summary>
public class TagLibMetadataExtractor
{
    /// <summary>
    /// TagLibのタグ情報から表示・識別用の基本メタデータ（タイトル・アーティスト・アルバム・ジャンル）を統一ルールで抽出します
    /// </summary>
    /// <param name="tag">TagLibのタグオブジェクト</param>
    /// <param name="filePath">音声ファイルパス（タイトル未設定時のフォールバックに使用）</param>
    /// <returns>抽出された基本メタデータのタプル</returns>
    public static (string Title, string Artist, string Album, string Genre) ExtractDisplayTags(TagLib.Tag? tag, string filePath)
    {
        string fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fallbackTitle))
        {
            fallbackTitle = Path.GetFileName(filePath);
        }

        string title = !string.IsNullOrWhiteSpace(tag?.Title)
            ? tag.Title.Trim()
            : fallbackTitle;

        string artist = !string.IsNullOrWhiteSpace(tag?.FirstPerformer)
            ? tag.FirstPerformer.Trim()
            : (!string.IsNullOrWhiteSpace(tag?.FirstAlbumArtist) ? tag.FirstAlbumArtist.Trim() : "Unknown Artist");

        string album = !string.IsNullOrWhiteSpace(tag?.Album)
            ? tag.Album.Trim()
            : "Unknown Album";

        string genre = !string.IsNullOrWhiteSpace(tag?.FirstGenre)
            ? tag.FirstGenre.Trim()
            : string.Empty;

        return (title, artist, album, genre);
    }

    /// <summary>
    /// 音声ファイルからメタデータを非同期で抽出し、Trackエンティティを生成します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成されたTrackエンティティ（ファイルが存在しない・読み込めない場合はnull）</returns>
    public Task<Track?> ExtractMetadataAsync(AudioPath filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath.Value))
            {
                return null;
            }

            try
            {
                using var tagFile = TagLib.File.Create(filePath.Value);

                var (title, artist, album, genre) = ExtractDisplayTags(tagFile.Tag, filePath.Value);

                TimeSpan duration = tagFile.Properties.Duration;
                uint year = tagFile.Tag.Year;
                uint trackNumber = tagFile.Tag.Track;
                int bitrate = tagFile.Properties.AudioBitrate;
                int sampleRate = tagFile.Properties.AudioSampleRate;
                int bitsPerSample = tagFile.Properties.BitsPerSample;

                // フォーマット判定
                string ext = filePath.Extension.ToUpperInvariant().TrimStart('.');
                string format = string.IsNullOrWhiteSpace(ext) ? "AUDIO" : ext;

                // 可逆圧縮およびハイレゾ判定
                bool isLossless = format is "FLAC" or "WAV" or "ALAC" or "AIFF";
                bool isHiRes = sampleRate >= 96000 || bitsPerSample >= 24;

                var track = new Track(
                    id: TrackId.New(),
                    filePath: filePath,
                    title: title,
                    artist: artist,
                    album: album,
                    duration: duration,
                    year: year,
                    trackNumber: trackNumber,
                    bitrate: bitrate,
                    sampleRate: sampleRate,
                    bitsPerSample: bitsPerSample,
                    format: format,
                    genre: genre,
                    isFavorite: false,
                    isLossless: isLossless,
                    isHiRes: isHiRes);

                return track;
            }
            catch
            {
                // メタデータ読み込み失敗時はファイル名ベースの最小限のTrackを生成
                return new Track(
                    id: TrackId.New(),
                    filePath: filePath,
                    title: filePath.FileName,
                    artist: "Unknown Artist",
                    album: "Unknown Album",
                    duration: TimeSpan.Zero);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 音声ファイルに埋め込まれたカバーアート画像のバイト配列を非同期で抽出します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>画像のバイト配列（画像が存在しない・読み込めない場合はnull）</returns>
    public Task<byte[]?> ExtractAlbumArtBytesAsync(AudioPath filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath.Value))
            {
                return null;
            }

            try
            {
                using var tagFile = TagLib.File.Create(filePath.Value);
                if (tagFile.Tag.Pictures.Length > 0)
                {
                    var picture = tagFile.Tag.Pictures[0];
                    return picture.Data.Data;
                }
            }
            catch
            {
                // 画像取得失敗時はnull
            }

            return null;
        }, cancellationToken);
    }
}
