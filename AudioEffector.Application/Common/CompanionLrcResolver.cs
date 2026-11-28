using System;
using System.IO;
using System.Text;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.Common;

/// <summary>
/// 機器転送時に音声ファイルと同階層・同名の `.lrc` ファイルを探索し、生成元種別と設定に基づいて同梱可否を判定する共通ヘルパーです
/// </summary>
public static class CompanionLrcResolver
{
    private const int MAX_HEADER_CHARS = 65536;

    /// <summary>
    /// 転送元音声ファイルに対応する `.lrc` ファイルが存在し、かつ現在の <see cref="LrcTransferOptions"/> で転送対象と判定される場合にそのパスを返します
    /// </summary>
    /// <param name="sourceAudioFilePath">転送元の音声ファイルパス</param>
    /// <param name="options">種別別 `.lrc` 転送オプション</param>
    /// <param name="lrcParser">`.lrc` 生成元種別判別に使用するパーサー</param>
    /// <param name="lrcSourceFilePath">転送対象と判定された `.lrc` ファイルのフルパス（非対象時は空文字）</param>
    /// <param name="onWarning">読み込みや判定で例外が発生した際のログ通知コールバック（任意）</param>
    /// <returns>同梱転送すべき `.lrc` が解決できた場合は <see langword="true"/></returns>
    public static bool TryResolveCompanionLrcPath(
        string? sourceAudioFilePath,
        LrcTransferOptions? options,
        ILrcParser? lrcParser,
        out string lrcSourceFilePath,
        Action<string, Exception>? onWarning = null)
    {
        lrcSourceFilePath = string.Empty;

        if (options is null || !options.IsAnyEnabled || lrcParser is null || string.IsNullOrWhiteSpace(sourceAudioFilePath))
        {
            return false;
        }

        try
        {
            string candidatePath = Path.ChangeExtension(sourceAudioFilePath, ".lrc");
            if (string.IsNullOrWhiteSpace(candidatePath)
                || string.Equals(candidatePath, sourceAudioFilePath, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(candidatePath))
            {
                return false;
            }

            string headerText = ReadHeadTextSafely(candidatePath);
            LrcOriginType originType = lrcParser.ClassifyOrigin(headerText);
            if (!options.ShouldTransfer(originType))
            {
                return false;
            }

            lrcSourceFilePath = candidatePath;
            return true;
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"同梱 LRC ファイルの判定に失敗しました: {sourceAudioFilePath}", ex);
            return false;
        }
    }

    private static string ReadHeadTextSafely(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        char[] buffer = new char[MAX_HEADER_CHARS];
        int totalRead = 0;
        while (totalRead < MAX_HEADER_CHARS)
        {
            int read = reader.Read(buffer, totalRead, MAX_HEADER_CHARS - totalRead);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
        }

        return new string(buffer, 0, totalRead);
    }
}
