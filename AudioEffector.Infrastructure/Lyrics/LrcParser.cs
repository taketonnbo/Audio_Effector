using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Lyrics;

/// <summary>
/// LRC 形式のテキスト解析およびフォーマットを行う <see cref="ILrcParser"/> の標準実装です。
/// </summary>
public sealed partial class LrcParser : ILrcParser
{
    private const int MAX_MINUTES = 99999;

    /// <summary>
    /// LRC 形式またはプレーンテキスト形式の歌詞文字列を解析して <see cref="LyricsDocument"/> を構築します。
    /// </summary>
    /// <param name="content">解析対象の歌詞テキスト。</param>
    /// <returns>解析結果の <see cref="LyricsDocument"/>。</returns>
    public LyricsDocument Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return LyricsDocument.Empty;
        }

        string normalized = content.TrimStart('\uFEFF');
        string[] rawLines = normalized.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        string? title = null;
        string? artist = null;
        string? album = null;
        string? author = null;
        TimeSpan offset = TimeSpan.Zero;

        var syncedEntries = new List<(TimeSpan Timestamp, int Order, string Text)>();
        var plainCandidates = new List<string>();
        int orderCounter = 0;

        foreach (string rawLine in rawLines)
        {
            string trimmedLine = rawLine.Trim();
            if (TryExtractLeadingTimeTags(trimmedLine, out List<TimeSpan> timestamps, out string lyricText))
            {
                foreach (TimeSpan timestamp in timestamps)
                {
                    syncedEntries.Add((timestamp, orderCounter++, lyricText));
                }

                continue;
            }

            if (TryParseMetadataTag(trimmedLine, out string tagKey, out string tagValue))
            {
                ApplyMetadata(tagKey, tagValue, ref title, ref artist, ref album, ref author, ref offset);
                continue;
            }

            // 角括弧タグのみの不正行（例: [99:99.99] や未対応単独タグ）は安全にスキップする
            if (IsStandaloneBracketLine(trimmedLine))
            {
                continue;
            }

            plainCandidates.Add(trimmedLine);
        }

        if (syncedEntries.Count > 0)
        {
            // タイムスタンプ昇順・同一タイムスタンプは元記載順維持の安定ソート
            List<LyricLine> syncedLines = syncedEntries
                .OrderBy(entry => entry.Timestamp)
                .ThenBy(entry => entry.Order)
                .Select((entry, index) => new LyricLine(index, entry.Timestamp, entry.Text))
                .ToList();

            return new LyricsDocument(
                lines: syncedLines,
                isSynced: true,
                title: title,
                artist: artist,
                album: album,
                author: author,
                offset: offset);
        }

        // 有効なタイムタグがない場合はプレーン歌詞として構築（前後余分な空行は除去）
        List<LyricLine> plainLines = BuildPlainLines(plainCandidates);

        return new LyricsDocument(
            lines: plainLines,
            isSynced: false,
            title: title,
            artist: artist,
            album: album,
            author: author,
            offset: offset);
    }

    /// <summary>
    /// <see cref="LyricsDocument"/> を LRC 形式のテキスト文字列へフォーマットします。
    /// </summary>
    /// <param name="document">フォーマット対象の歌詞ドキュメント。</param>
    /// <returns>LRC 形式に整形された文字列。</returns>
    public string Format(LyricsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var outputLines = new List<string>(document.Lines.Count + 5);

        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            outputLines.Add($"[ti:{document.Title}]");
        }

        if (!string.IsNullOrWhiteSpace(document.Artist))
        {
            outputLines.Add($"[ar:{document.Artist}]");
        }

        if (!string.IsNullOrWhiteSpace(document.Album))
        {
            outputLines.Add($"[al:{document.Album}]");
        }

        if (!string.IsNullOrWhiteSpace(document.Author))
        {
            outputLines.Add($"[by:{document.Author}]");
        }

        if (document.Offset != TimeSpan.Zero)
        {
            long offsetMs = (long)Math.Round(document.Offset.TotalMilliseconds, MidpointRounding.AwayFromZero);
            if (offsetMs != 0)
            {
                outputLines.Add(string.Create(CultureInfo.InvariantCulture, $"[offset:{offsetMs}]"));
            }
        }

        foreach (LyricLine line in document.Lines)
        {
            if (line.Timestamp.HasValue)
            {
                string tag = FormatTimestampTag(line.Timestamp.Value);
                outputLines.Add(string.IsNullOrEmpty(line.Text) ? tag : $"{tag}{line.Text}");
            }
            else
            {
                outputLines.Add(line.Text);
            }
        }

        return string.Join(Environment.NewLine, outputLines);
    }

    private static bool TryExtractLeadingTimeTags(string line, out List<TimeSpan> timestamps, out string lyricText)
    {
        timestamps = new List<TimeSpan>();
        lyricText = string.Empty;

        if (string.IsNullOrEmpty(line) || line[0] != '[')
        {
            return false;
        }

        int currentIndex = 0;
        while (currentIndex < line.Length)
        {
            int nextIndex = currentIndex;
            while (nextIndex < line.Length && char.IsWhiteSpace(line[nextIndex]))
            {
                nextIndex++;
            }

            if (nextIndex >= line.Length || line[nextIndex] != '[')
            {
                break;
            }

            Match match = LeadingTimeTagRegex().Match(line, nextIndex);
            if (!match.Success || match.Index != nextIndex)
            {
                break;
            }

            if (!TryBuildTimeSpan(match, out TimeSpan parsedTime))
            {
                // 不正なタイムタグ（60秒以上等）が含まれる場合はタイムタグ抽出を打ち切る
                return false;
            }

            timestamps.Add(parsedTime);
            currentIndex = match.Index + match.Length;
        }

        if (timestamps.Count == 0)
        {
            return false;
        }

        lyricText = line[currentIndex..].Trim();
        return true;
    }

    private static bool TryBuildTimeSpan(Match match, out TimeSpan result)
    {
        result = TimeSpan.Zero;

        if (!int.TryParse(match.Groups["min"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || minutes < 0
            || minutes > MAX_MINUTES)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["sec"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || seconds < 0
            || seconds >= 60)
        {
            return false;
        }

        int milliseconds = 0;
        Group fracGroup = match.Groups["frac"];
        if (fracGroup.Success && fracGroup.Length > 0)
        {
            if (!int.TryParse(fracGroup.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int fracValue))
            {
                return false;
            }

            milliseconds = fracGroup.Length switch
            {
                1 => fracValue * 100,
                2 => fracValue * 10,
                3 => fracValue,
                _ => 0,
            };
        }

        result = TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds)
            + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private static bool TryParseMetadataTag(string trimmedLine, out string tagKey, out string tagValue)
    {
        tagKey = string.Empty;
        tagValue = string.Empty;

        if (string.IsNullOrEmpty(trimmedLine) || trimmedLine[0] != '[' || trimmedLine[^1] != ']')
        {
            return false;
        }

        Match match = MetadataTagRegex().Match(trimmedLine);
        if (!match.Success)
        {
            return false;
        }

        tagKey = match.Groups["key"].Value.Trim();
        tagValue = match.Groups["value"].Value.Trim();
        return true;
    }

    private static void ApplyMetadata(
        string tagKey,
        string tagValue,
        ref string? title,
        ref string? artist,
        ref string? album,
        ref string? author,
        ref TimeSpan offset)
    {
        if (string.Equals(tagKey, "ti", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(tagValue))
            {
                title = tagValue;
            }
        }
        else if (string.Equals(tagKey, "ar", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(tagValue))
            {
                artist = tagValue;
            }
        }
        else if (string.Equals(tagKey, "al", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(tagValue))
            {
                album = tagValue;
            }
        }
        else if (string.Equals(tagKey, "by", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(tagValue))
            {
                author = tagValue;
            }
        }
        else if (string.Equals(tagKey, "offset", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(tagValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int offsetMs))
            {
                offset = TimeSpan.FromMilliseconds(offsetMs);
            }
        }
    }

    private static bool IsStandaloneBracketLine(string trimmedLine)
    {
        if (string.IsNullOrEmpty(trimmedLine))
        {
            return false;
        }

        return StandaloneBracketLineRegex().IsMatch(trimmedLine);
    }

    private static List<LyricLine> BuildPlainLines(List<string> candidates)
    {
        int firstNonEmpty = candidates.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        if (firstNonEmpty < 0)
        {
            return new List<LyricLine>();
        }

        int lastNonEmpty = candidates.FindLastIndex(line => !string.IsNullOrWhiteSpace(line));
        var result = new List<LyricLine>(lastNonEmpty - firstNonEmpty + 1);

        for (int i = firstNonEmpty; i <= lastNonEmpty; i++)
        {
            result.Add(new LyricLine(result.Count, timestamp: null, text: candidates[i]));
        }

        return result;
    }

    private static string FormatTimestampTag(TimeSpan timestamp)
    {
        TimeSpan clamped = timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
        long totalMilliseconds = (long)Math.Round(clamped.TotalMilliseconds, MidpointRounding.AwayFromZero);
        long totalSeconds = totalMilliseconds / 1000;
        long totalMinutes = totalSeconds / 60;
        int seconds = (int)(totalSeconds % 60);
        int milliseconds = (int)(totalMilliseconds % 1000);

        if (milliseconds % 10 == 0)
        {
            int centiseconds = milliseconds / 10;
            return string.Create(CultureInfo.InvariantCulture, $"[{totalMinutes:00}:{seconds:00}.{centiseconds:00}]");
        }

        return string.Create(CultureInfo.InvariantCulture, $"[{totalMinutes:00}:{seconds:00}.{milliseconds:000}]");
    }

    [GeneratedRegex(@"\[(?<min>\d+):(?<sec>\d{1,2})(?:[.:](?<frac>\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTimeTagRegex();

    [GeneratedRegex(@"^\[(?<key>[a-zA-Z]+):(?<value>[^\]]*)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex MetadataTagRegex();

    [GeneratedRegex(@"^(?:\[[^\]]+\]\s*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneBracketLineRegex();
}
