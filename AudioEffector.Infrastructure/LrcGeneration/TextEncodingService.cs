using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.LrcGeneration;

/// <summary>
/// テキストファイルの文字コード厳密判定（UTF-8 / Shift-JIS / EUC-JP / UTF-16）、非 UTF-8 承諾変換読み込み、
/// および UTF-8（BOM なし）での安全な `.lrc` ファイル保存を提供する <see cref="ITextEncodingService"/> 実装です
/// </summary>
public sealed class TextEncodingService : ITextEncodingService
{
    private const int FILE_STREAM_BUFFER_SIZE = 4096;

    private static readonly UTF8Encoding StrictUtf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictShiftJis;
    private static readonly Encoding StrictEucJp;
    private static readonly Encoding StrictUtf16Le = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf16Be = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf32Le = new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
    private static readonly Encoding StrictUtf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);
    private static readonly Encoding FallbackShiftJis;

    static TextEncodingService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        StrictShiftJis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        StrictEucJp = Encoding.GetEncoding(51932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        FallbackShiftJis = Encoding.GetEncoding(932);
    }

    /// <summary>
    /// 指定されたテキストファイルの文字コードおよび行数を非同期で検査します
    /// </summary>
    /// <param name="textFilePath">検査対象のテキストファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検査結果 <see cref="TextEncodingInspectionResult"/></returns>
    public async Task<TextEncodingInspectionResult> InspectAsync(
        string textFilePath,
        CancellationToken cancellationToken = default)
    {
        ValidateTextFilePath(textFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] rawBytes = await ReadAllBytesSharedAsync(textFilePath, cancellationToken).ConfigureAwait(false);
        return InspectBytes(textFilePath, rawBytes);
    }

    /// <summary>
    /// 指定されたテキストファイルを UTF-8 文字列として非同期読み込みします。
    /// 非 UTF-8 ファイルの場合、<paramref name="allowEncodingConversion"/> が <see langword="true"/> のときのみ内部変換を許可し、元ファイル自体は変更しません。
    /// </summary>
    /// <param name="textFilePath">読み込み対象のテキストファイルパス</param>
    /// <param name="allowEncodingConversion">非 UTF-8 からの変換をユーザーが承諾済みかどうか</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>UTF-8 相当へデコードされたテキスト全文</returns>
    public async Task<string> ReadAsUtf8TextAsync(
        string textFilePath,
        bool allowEncodingConversion,
        CancellationToken cancellationToken = default)
    {
        ValidateTextFilePath(textFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] rawBytes = await ReadAllBytesSharedAsync(textFilePath, cancellationToken).ConfigureAwait(false);
        DetectedText detected = DetectAndDecode(rawBytes);

        if (!detected.IsUtf8 && !allowEncodingConversion)
        {
            throw new InvalidOperationException(
                $"入力テキストは {detected.EncodingName} です。UTF-8 への変換承諾が必要です。");
        }

        return detected.Text;
    }

    /// <summary>
    /// 指定されたパスへ LRC コンテンツを UTF-8（BOM なし）で安全に非同期保存します
    /// </summary>
    /// <param name="destinationPath">保存先のファイルパス</param>
    /// <param name="lrcContent">保存する LRC 文字列</param>
    /// <param name="overwrite">既存ファイルが存在する場合に上書きを許可するかどうか</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveLrcFileAsync(
        string destinationPath,
        string lrcContent,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("保存先ファイルパスが指定されていません。", nameof(destinationPath));
        }

        ArgumentNullException.ThrowIfNull(lrcContent);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(destinationPath);
        if (!overwrite && File.Exists(fullPath))
        {
            throw new IOException($"保存先ファイルが既に存在します（上書き未承諾）: {fullPath}");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{fullPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            byte[] utf8Bytes = StrictUtf8NoBom.GetBytes(lrcContent);
            await using (var fs = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: FILE_STREAM_BUFFER_SIZE,
                useAsync: true))
            {
                await fs.WriteAsync(utf8Bytes, cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!overwrite && File.Exists(fullPath))
            {
                throw new IOException($"保存先ファイルが既に存在します（上書き未承諾）: {fullPath}");
            }

            File.Move(tempPath, fullPath, overwrite: overwrite);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // 一時ファイル削除の失敗で元の例外を隠蔽しない
                }
            }
        }
    }

    /// <summary>
    /// バイト配列から文字コードと行数を検査して <see cref="TextEncodingInspectionResult"/> を構築します
    /// </summary>
    /// <param name="filePath">検査対象のファイルパス</param>
    /// <param name="rawBytes">対象ファイルの生バイト配列</param>
    /// <returns>検査結果</returns>
    internal static TextEncodingInspectionResult InspectBytes(string filePath, byte[] rawBytes)
    {
        DetectedText detected = DetectAndDecode(rawBytes);
        (int totalLines, int nonEmptyLines) = CountLines(detected.Text);

        return new TextEncodingInspectionResult(
            filePath: filePath,
            isUtf8: detected.IsUtf8,
            hasUtf8Bom: detected.HasUtf8Bom,
            detectedEncodingName: detected.EncodingName,
            totalLineCount: totalLines,
            nonEmptyLineCount: nonEmptyLines);
    }

    private static DetectedText DetectAndDecode(byte[] rawBytes)
    {
        if (rawBytes.Length == 0)
        {
            return new DetectedText(IsUtf8: true, HasUtf8Bom: false, EncodingName: "UTF-8", Text: string.Empty);
        }

        // 1. UTF-8 BOM
        if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
        {
            try
            {
                string text = StrictUtf8NoBom.GetString(rawBytes, 3, rawBytes.Length - 3);
                return new DetectedText(IsUtf8: true, HasUtf8Bom: true, EncodingName: "UTF-8 (BOM)", Text: text);
            }
            catch (DecoderFallbackException)
            {
                // BOM 類似バイト列でも厳密 UTF-8 でない場合は後続判定へ
            }
        }

        // 2. UTF-32 LE / BE BOM（UTF-16 LE の FF FE プレフィックスより先に判定）
        if (rawBytes.Length >= 4 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE && rawBytes[2] == 0x00 && rawBytes[3] == 0x00)
        {
            string text = StrictUtf32Le.GetString(rawBytes, 4, rawBytes.Length - 4);
            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "UTF-32 LE", Text: text);
        }

        if (rawBytes.Length >= 4 && rawBytes[0] == 0x00 && rawBytes[1] == 0x00 && rawBytes[2] == 0xFE && rawBytes[3] == 0xFF)
        {
            string text = StrictUtf32Be.GetString(rawBytes, 4, rawBytes.Length - 4);
            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "UTF-32 BE", Text: text);
        }

        // 3. UTF-16 LE / BE BOM
        if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
        {
            string text = StrictUtf16Le.GetString(rawBytes, 2, rawBytes.Length - 2);
            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "UTF-16 LE", Text: text);
        }

        if (rawBytes.Length >= 2 && rawBytes[0] == 0xFE && rawBytes[1] == 0xFF)
        {
            string text = StrictUtf16Be.GetString(rawBytes, 2, rawBytes.Length - 2);
            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "UTF-16 BE", Text: text);
        }

        // 3. UTF-8 (BOM なし・厳密検証)
        try
        {
            string utf8Text = StrictUtf8NoBom.GetString(rawBytes);
            return new DetectedText(IsUtf8: true, HasUtf8Bom: false, EncodingName: "UTF-8", Text: utf8Text);
        }
        catch (DecoderFallbackException)
        {
            // 非 UTF-8: 日本語レガシーエンコーディング候補を比較判定
        }

        // 4. Shift-JIS (CP932) vs EUC-JP (51932) 判定
        bool canDecodeSjis = TryDecodeStrict(StrictShiftJis, rawBytes, out string sjisText);
        bool canDecodeEuc = TryDecodeStrict(StrictEucJp, rawBytes, out string eucText);

        if (canDecodeSjis && !canDecodeEuc)
        {
            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "Shift-JIS (CP932)", Text: sjisText);
        }

        if (canDecodeEuc && !canDecodeSjis)
        {
            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "EUC-JP", Text: eucText);
        }

        if (canDecodeSjis && canDecodeEuc)
        {
            int sjisScore = ScoreJapaneseNaturalness(sjisText);
            int eucScore = ScoreJapaneseNaturalness(eucText);
            if (eucScore > sjisScore)
            {
                return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "EUC-JP", Text: eucText);
            }

            return new DetectedText(IsUtf8: false, HasUtf8Bom: false, EncodingName: "Shift-JIS (CP932)", Text: sjisText);
        }

        // 5. フォールバック（Shift-JIS 寛容デコード）
        return new DetectedText(
            IsUtf8: false,
            HasUtf8Bom: false,
            EncodingName: "Shift-JIS (CP932)",
            Text: FallbackShiftJis.GetString(rawBytes));
    }

    private static bool TryDecodeStrict(Encoding encoding, byte[] bytes, out string text)
    {
        try
        {
            text = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static int ScoreJapaneseNaturalness(string text)
    {
        int score = 0;
        foreach (char c in text)
        {
            if ((c >= '\u3040' && c <= '\u309F') || (c >= '\u30A0' && c <= '\u30FF'))
            {
                // ひらがな・カタカナは日本語歌詞の強力な指標
                score += 4;
            }
            else if (c >= '\u4E00' && c <= '\u9FFF')
            {
                // CJK 統合漢字
                score += 2;
            }
            else if (c >= 0x20 && c <= 0x7E)
            {
                score += 1;
            }
            else if (c == '\r' || c == '\n' || c == '\t')
            {
                score += 1;
            }
            else if (c >= '\uFF61' && c <= '\uFF9F')
            {
                // 半角カナは EUC-JP を Shift-JIS 誤読した際に出やすいため減点
                score -= 2;
            }
            else if (char.IsControl(c))
            {
                score -= 10;
            }
        }

        return score;
    }

    private static (int TotalLines, int NonEmptyLines) CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        string[] lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        int nonEmpty = 0;
        foreach (string line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                nonEmpty++;
            }
        }

        return (lines.Length, nonEmpty);
    }

    private static void ValidateTextFilePath(string textFilePath)
    {
        if (string.IsNullOrWhiteSpace(textFilePath))
        {
            throw new ArgumentException("テキストファイルパスが指定されていません。", nameof(textFilePath));
        }

        if (!File.Exists(textFilePath))
        {
            throw new FileNotFoundException("指定されたテキストファイルが見つかりません。", textFilePath);
        }
    }

    private static async Task<byte[]> ReadAllBytesSharedAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: FILE_STREAM_BUFFER_SIZE,
            useAsync: true);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    private readonly record struct DetectedText(bool IsUtf8, bool HasUtf8Bom, string EncodingName, string Text);
}
