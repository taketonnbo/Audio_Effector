using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using NLog;

namespace AudioEffector.Infrastructure.Lyrics;

/// <summary>
/// ローカルファイルシステムの外部 .lrc ファイルおよび音声ファイル埋め込み歌詞タグから歌詞を読み込む <see cref="ILyricsProvider"/> 実装です。
/// </summary>
public sealed class LocalLyricsProvider : ILyricsProvider
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly UTF8Encoding StrictUtf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding ShiftJisEncoding;

    private readonly ILrcParser _lrcParser;

    static LocalLyricsProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ShiftJisEncoding = Encoding.GetEncoding(932);
    }

    /// <summary>
    /// <see cref="LocalLyricsProvider"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="lrcParser">LRC テキストパーサー。</param>
    public LocalLyricsProvider(ILrcParser lrcParser)
    {
        _lrcParser = lrcParser ?? throw new ArgumentNullException(nameof(lrcParser));
    }

    /// <summary>
    /// 指定された楽曲の歌詞を優先順位（① 同階層・同名 .lrc ファイル → ② 音声ファイル埋め込み歌詞タグ → ③ なし）に従って非同期で読み込みます。
    /// </summary>
    /// <param name="track">歌詞を取得する対象楽曲。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>取得された <see cref="LyricsDocument"/>（取得できない場合は <see langword="null"/>）。</returns>
    public async Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (track is null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return null;
        }

        string audioFilePath = track.FilePath;

        // 優先順位 1: 同階層・同名の外部 .lrc ファイル
        LyricsDocument? externalLyrics = await TryLoadExternalLrcAsync(audioFilePath, cancellationToken).ConfigureAwait(false);
        if (externalLyrics is not null)
        {
            return externalLyrics;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 優先順位 2: 音声ファイル内の埋め込み歌詞タグ
        LyricsDocument? embeddedLyrics = await TryLoadEmbeddedLyricsAsync(audioFilePath, cancellationToken).ConfigureAwait(false);
        if (embeddedLyrics is not null)
        {
            return embeddedLyrics;
        }

        // 優先順位 3: 取得不可時は null を返却
        return null;
    }

    private async Task<LyricsDocument?> TryLoadExternalLrcAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        string lrcPath;
        try
        {
            lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
        }
        catch (ArgumentException ex)
        {
            Logger.Warn(ex, "外部 LRC パス解決に失敗しました: {AudioFilePath}", audioFilePath);
            return null;
        }

        if (string.IsNullOrWhiteSpace(lrcPath) || !File.Exists(lrcPath))
        {
            return null;
        }

        try
        {
            byte[] rawBytes = await File.ReadAllBytesAsync(lrcPath, cancellationToken).ConfigureAwait(false);
            if (rawBytes.Length == 0)
            {
                return null;
            }

            string content = DecodeTextWithFallback(rawBytes);
            LyricsDocument parsed = _lrcParser.Parse(content);

            // 空・メタデータのみで表示可能な歌詞行を含まない場合は埋め込み歌詞へフォールバックする
            if (!parsed.HasVisibleLyrics)
            {
                return null;
            }

            return parsed.WithSource(LyricsSourceType.LrcFile, lrcPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "外部 LRC ファイルの読み込みに失敗しました（埋め込み歌詞へフォールバックします）: {LrcPath}", lrcPath);
            return null;
        }
    }

    private Task<LyricsDocument?> TryLoadEmbeddedLyricsAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioFilePath))
        {
            return Task.FromResult<LyricsDocument?>(null);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using TagLib.File tagFile = TagLib.File.Create(audioFilePath);
                string? rawLyrics = ExtractEmbeddedLyricsText(tagFile.Tag);
                if (string.IsNullOrWhiteSpace(rawLyrics))
                {
                    return null;
                }

                LyricsDocument parsed = _lrcParser.Parse(rawLyrics);
                if (!parsed.HasVisibleLyrics)
                {
                    return null;
                }

                return parsed.WithSource(LyricsSourceType.EmbeddedTag, audioFilePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "埋め込み歌詞タグの読み取りに失敗しました: {AudioFilePath}", audioFilePath);
                return null;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// バイト配列を UTF-8（BOM 有無両対応・厳格デコード）優先で文字列へ変換し、デコード不可時は Shift-JIS（CP932）へフォールバックします。
    /// </summary>
    /// <param name="bytes">デコード対象の生バイト配列。</param>
    /// <returns>デコードされた文字列。</returns>
    internal static string DecodeTextWithFallback(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        int offset = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            offset = 3;
        }

        try
        {
            return StrictUtf8Encoding.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return ShiftJisEncoding.GetString(bytes);
        }
    }

    private static string? ExtractEmbeddedLyricsText(TagLib.Tag? tag)
    {
        if (tag is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(tag.Lyrics))
        {
            return tag.Lyrics;
        }

        return ExtractFormatSpecificLyrics(tag);
    }

    private static string? ExtractFormatSpecificLyrics(TagLib.Tag? tag)
    {
        if (tag is null)
        {
            return null;
        }

        if (tag is TagLib.CombinedTag combinedTag)
        {
            foreach (TagLib.Tag childTag in combinedTag.Tags)
            {
                string? extracted = ExtractFormatSpecificLyrics(childTag);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }

            return null;
        }

        if (tag is TagLib.Ogg.XiphComment xiphComment)
        {
            string? lyrics = xiphComment.GetField("LYRICS").FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                ?? xiphComment.GetField("UNSYNCEDLYRICS").FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (!string.IsNullOrWhiteSpace(lyrics))
            {
                return lyrics;
            }
        }

        if (tag is TagLib.Id3v2.Tag id3v2Tag)
        {
            TagLib.Id3v2.UnsynchronisedLyricsFrame? frame = id3v2Tag
                .GetFrames<TagLib.Id3v2.UnsynchronisedLyricsFrame>()
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Text));
            if (frame is not null)
            {
                return frame.Text;
            }
        }

        return null;
    }
}
