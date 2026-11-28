using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// 楽曲に対応する歌詞データ全体（歌詞行コレクション・同期フラグ・メタデータ・タイミング補正）を表す不変エンティティです
/// </summary>
public sealed class LyricsDocument
{
    /// <summary>
    /// 空の歌詞ドキュメントインスタンスを取得します
    /// </summary>
    public static LyricsDocument Empty { get; } = new(
        Array.Empty<LyricLine>(),
        isSynced: false,
        sourceType: LyricsSourceType.None);

    private readonly LyricLine[] _lines;

    /// <summary>
    /// 歌詞行の不変コレクションを取得します
    /// </summary>
    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>
    /// タイムタグによる再生位置同期に対応した歌詞かどうかを取得します
    /// </summary>
    public bool IsSynced { get; }

    /// <summary>
    /// メタデータタグ（<c>[ti:]</c>）から取得した楽曲タイトルを取得します
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// メタデータタグ（<c>[ar:]</c>）から取得したアーティスト名を取得します
    /// </summary>
    public string? Artist { get; }

    /// <summary>
    /// メタデータタグ（<c>[al:]</c>）から取得したアルバム名を取得します
    /// </summary>
    public string? Album { get; }

    /// <summary>
    /// メタデータタグ（<c>[by:]</c>）から取得した LRC 作成者名を取得します
    /// </summary>
    public string? Author { get; }

    /// <summary>
    /// メタデータタグ（<c>[offset:]</c>）に基づく全体タイミング補正値を取得します（正の値で表示を早め、負の値で遅らせる）
    /// </summary>
    public TimeSpan Offset { get; }

    /// <summary>
    /// 歌詞データの取得元ソース種別を取得します
    /// </summary>
    public LyricsSourceType SourceType { get; }

    /// <summary>
    /// 歌詞データの取得元ファイルパス（.lrc パスまたは音声ファイルパス）を取得します
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// 歌詞行を 1 行以上保持しているかどうかを取得します
    /// </summary>
    public bool HasLines => _lines.Length > 0;

    /// <summary>
    /// 表示可能なテキストを含む歌詞行を 1 行以上保持しているかどうかを取得します
    /// </summary>
    public bool HasVisibleLyrics { get; }

    /// <summary>
    /// <see cref="LyricsDocument"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="lines">歌詞行コレクション（同期歌詞で未ソートの場合はタイムスタンプ昇順に安定ソートされます）</param>
    /// <param name="isSynced">同期歌詞フラグ</param>
    /// <param name="title">曲名メタデータ（任意）</param>
    /// <param name="artist">アーティスト名メタデータ（任意）</param>
    /// <param name="album">アルバム名メタデータ（任意）</param>
    /// <param name="author">LRC 作成者メタデータ（任意）</param>
    /// <param name="offset">全体タイミング補正値（既定: <see cref="TimeSpan.Zero"/>）</param>
    /// <param name="sourceType">歌詞ソース種別（既定: <see cref="LyricsSourceType.None"/>）</param>
    /// <param name="sourcePath">読み込み元ファイルパス（任意）</param>
    public LyricsDocument(
        IEnumerable<LyricLine>? lines,
        bool isSynced,
        string? title = null,
        string? artist = null,
        string? album = null,
        string? author = null,
        TimeSpan offset = default,
        LyricsSourceType sourceType = LyricsSourceType.None,
        string? sourcePath = null)
    {
        LyricLine[] copiedLines = lines is null
            ? Array.Empty<LyricLine>()
            : lines.Where(l => l is not null).ToArray();

        bool effectiveSynced = isSynced && copiedLines.Length > 0 && copiedLines.All(l => l.HasTimestamp);

        // 同期歌詞の二分探索前提（タイムスタンプ昇順）を不変条件として保証する（既ソート入力は O(N) チェックのみで通過）
        if (effectiveSynced && !IsTimestampSorted(copiedLines))
        {
            copiedLines = copiedLines
                .Select((line, order) => (Line: line, Order: order))
                .OrderBy(entry => entry.Line.Timestamp!.Value)
                .ThenBy(entry => entry.Order)
                .Select(entry => entry.Line)
                .ToArray();
        }

        // 行インデックスを 0..Count-1 へ整合させて不変性を保証
        for (int i = 0; i < copiedLines.Length; i++)
        {
            if (copiedLines[i].Index != i)
            {
                copiedLines[i] = copiedLines[i].WithIndex(i);
            }
        }

        _lines = copiedLines;
        Lines = new ReadOnlyCollection<LyricLine>(_lines);
        IsSynced = effectiveSynced;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Artist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();
        Album = string.IsNullOrWhiteSpace(album) ? null : album.Trim();
        Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
        Offset = offset;
        SourceType = sourceType;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath.Trim();
        HasVisibleLyrics = _lines.Any(l => !l.IsInstrumental);
    }

    /// <summary>
    /// ソース種別およびソースパスを付与した新しい <see cref="LyricsDocument"/> インスタンスを返します
    /// </summary>
    /// <param name="sourceType">歌詞ソース種別</param>
    /// <param name="sourcePath">読み込み元ファイルパス（任意）</param>
    /// <returns>ソース情報が更新された <see cref="LyricsDocument"/></returns>
    public LyricsDocument WithSource(LyricsSourceType sourceType, string? sourcePath = null)
    {
        return new LyricsDocument(
            _lines,
            IsSynced,
            Title,
            Artist,
            Album,
            Author,
            Offset,
            sourceType,
            sourcePath);
    }

    /// <summary>
    /// 指定された歌詞行の <see cref="Offset"/> 補正後タイムスタンプを計算します（0 秒未満は <see cref="TimeSpan.Zero"/> にクランプ）
    /// </summary>
    /// <param name="lineIndex">対象の行インデックス</param>
    /// <returns>補正後の有効タイムスタンプ（範囲外または非同期行の場合は <see langword="null"/>）</returns>
    public TimeSpan? GetEffectiveTimestamp(int lineIndex)
    {
        if ((uint)lineIndex >= (uint)_lines.Length)
        {
            return null;
        }

        return GetEffectiveTimestamp(_lines[lineIndex]);
    }

    /// <summary>
    /// 指定された <see cref="LyricLine"/> の <see cref="Offset"/> 補正後タイムスタンプを計算します
    /// </summary>
    /// <param name="line">対象の歌詞行</param>
    /// <returns>補正後の有効タイムスタンプ（タイムスタンプ未設定時は <see langword="null"/>）</returns>
    public TimeSpan? GetEffectiveTimestamp(LyricLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!line.Timestamp.HasValue)
        {
            return null;
        }

        return ComputeEffectiveTimestamp(line.Timestamp.Value);
    }

    /// <summary>
    /// 指定された歌詞行をクリックした際のシーク先再生位置を返します（同期歌詞かつテキストを含む行のみ対象）
    /// </summary>
    /// <param name="lineIndex">対象の行インデックス</param>
    /// <returns>シーク先の再生位置（シーク対象外の場合は <see langword="null"/>）</returns>
    public TimeSpan? GetSeekPosition(int lineIndex)
    {
        if (!IsSynced || (uint)lineIndex >= (uint)_lines.Length)
        {
            return null;
        }

        LyricLine line = _lines[lineIndex];
        if (!line.IsSeekable)
        {
            return null;
        }

        return ComputeEffectiveTimestamp(line.Timestamp!.Value);
    }

    /// <summary>
    /// 指定された再生位置に対応するアクティブ行のインデックスを O(log N) の二分探索で特定します
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>非同期歌詞・空ドキュメント・先頭行より前のイントロ区間では <c>-1</c> を返します</description></item>
    /// <item><description>同一の有効タイムスタンプを持つ複数行（訳詞併記など）が存在する場合は、間奏空行を除いた先頭インデックスを返します</description></item>
    /// <item><description>間奏・歌唱休止用の空行のみに到達した場合は、直前歌詞のハイライトを解除するため <c>-1</c> を返します</description></item>
    /// </list>
    /// </remarks>
    /// <param name="position">現在の楽曲再生位置</param>
    /// <returns>アクティブ行（グループの場合は先頭行）のインデックス。該当行がない場合は <c>-1</c></returns>
    public int FindActiveLineIndex(TimeSpan position)
    {
        (int startIndex, _) = FindActiveLineRange(position);
        return startIndex;
    }

    /// <summary>
    /// 指定された再生位置に対応するアクティブ行の範囲（同一有効タイムスタンプの複数行グループを含む）を O(log N) で特定します
    /// </summary>
    /// <param name="position">現在の楽曲再生位置</param>
    /// <returns>アクティブな行範囲 <c>(StartIndex, EndIndex)</c>（両端を含み、間奏空行を除外。該当なし時は <c>(-1, -1)</c>）</returns>
    public (int StartIndex, int EndIndex) FindActiveLineRange(TimeSpan position)
    {
        if (!IsSynced || _lines.Length == 0)
        {
            return (-1, -1);
        }

        // 有効タイムスタンプ <= position を満たす最大インデックス（upper）を二分探索
        int low = 0;
        int high = _lines.Length - 1;
        int upper = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            TimeSpan effective = ComputeEffectiveTimestamp(_lines[mid].Timestamp!.Value);

            if (effective <= position)
            {
                upper = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (upper < 0)
        {
            return (-1, -1);
        }

        // 同一有効タイムスタンプを持つグループの先頭インデックス（start）を二分探索
        TimeSpan targetEffective = ComputeEffectiveTimestamp(_lines[upper].Timestamp!.Value);
        low = 0;
        high = upper;
        int start = upper;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            TimeSpan effective = ComputeEffectiveTimestamp(_lines[mid].Timestamp!.Value);

            if (effective == targetEffective)
            {
                start = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        // 同一有効タイムスタンプ範囲の両端から間奏空行を除外する（正の offset による 0 秒クランプ衝突時も含む）
        while (start <= upper && _lines[start].IsInstrumental)
        {
            start++;
        }

        while (upper >= start && _lines[upper].IsInstrumental)
        {
            upper--;
        }

        if (start > upper)
        {
            return (-1, -1);
        }

        return (start, upper);
    }

    private static bool IsTimestampSorted(LyricLine[] lines)
    {
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i - 1].Timestamp!.Value > lines[i].Timestamp!.Value)
            {
                return false;
            }
        }

        return true;
    }

    private TimeSpan ComputeEffectiveTimestamp(TimeSpan rawTimestamp)
    {
        // LRC 標準仕様: 正の offset で表示を早め（raw - offset）、負の offset で遅らせる。0 秒未満は 0 秒にクランプ。
        TimeSpan adjusted = rawTimestamp - Offset;
        return adjusted < TimeSpan.Zero ? TimeSpan.Zero : adjusted;
    }
}
