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

                string title = !string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                    ? tagFile.Tag.Title
                    : filePath.FileName;

                string artist = !string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer)
                    ? tagFile.Tag.FirstPerformer
                    : (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstAlbumArtist) ? tagFile.Tag.FirstAlbumArtist : "Unknown Artist");

                string album = !string.IsNullOrWhiteSpace(tagFile.Tag.Album)
                    ? tagFile.Tag.Album
                    : "Unknown Album";

                TimeSpan duration = tagFile.Properties.Duration;
                uint year = tagFile.Tag.Year;
                uint trackNumber = tagFile.Tag.Track;
                int bitrate = tagFile.Properties.AudioBitrate;
                int sampleRate = tagFile.Properties.AudioSampleRate;
                int bitsPerSample = tagFile.Properties.BitsPerSample;
                string genre = tagFile.Tag.FirstGenre ?? string.Empty;

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
