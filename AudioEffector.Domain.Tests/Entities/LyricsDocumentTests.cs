using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// LyricsDocument エンティティの現在行二分探索・同一タイムスタンプ範囲・間奏空行・offset 補正・シーク位置計算を検証するテストクラスです。
/// </summary>
public sealed class LyricsDocumentTests
{
    private static LyricsDocument CreateStandardSyncedDocument(TimeSpan offset = default)
    {
        var lines = new[]
        {
            new LyricLine(0, TimeSpan.FromSeconds(10), "1行目の歌詞"),
            new LyricLine(1, TimeSpan.FromSeconds(20), "2行目の歌詞"),
            new LyricLine(2, TimeSpan.FromSeconds(30), "3行目の歌詞"),
        };

        return new LyricsDocument(lines, isSynced: true, offset: offset, sourceType: LyricsSourceType.LrcFile);
    }

    /// <summary>
    /// 最初の歌詞行より前のイントロ区間および負の再生位置では -1 が返されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1000)]
    [InlineData(0)]
    [InlineData(9999)]
    public void FindActiveLineIndex_イントロ区間_マイナス1を返すこと(int positionMs)
    {
        // Arrange
        LyricsDocument sut = CreateStandardSyncedDocument();

        // Act
        int actual = sut.FindActiveLineIndex(TimeSpan.FromMilliseconds(positionMs));

        // Assert
        Assert.Equal(-1, actual);
    }

    /// <summary>
    /// 先頭行・中間行・末尾行・曲終盤の境界値に対して正しいアクティブ行インデックスを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(10000, 0)]
    [InlineData(15000, 0)]
    [InlineData(19999, 0)]
    [InlineData(20000, 1)]
    [InlineData(29999, 1)]
    [InlineData(30000, 2)]
    [InlineData(120000, 2)]
    public void FindActiveLineIndex_標準同期歌詞の境界値_期待される行インデックスを返すこと(int positionMs, int expectedIndex)
    {
        // Arrange
        LyricsDocument sut = CreateStandardSyncedDocument();

        // Act
        int actual = sut.FindActiveLineIndex(TimeSpan.FromMilliseconds(positionMs));

        // Assert
        Assert.Equal(expectedIndex, actual);
    }

    /// <summary>
    /// 同一タイムスタンプを持つ複数行（訳詞併記）が存在する場合、先頭行インデックスと同時ハイライト範囲を返すことを検証します。
    /// </summary>
    [Fact]
    public void FindActiveLineIndexおよびRange_同一タイムスタンプ複数行_グループ先頭インデックスと全範囲を返すこと()
    {
        // Arrange
        var lines = new[]
        {
            new LyricLine(0, TimeSpan.FromSeconds(5), "Intro line"),
            new LyricLine(1, TimeSpan.FromSeconds(15), "Hello world"),
            new LyricLine(2, TimeSpan.FromSeconds(15), "こんにちは世界"),
            new LyricLine(3, TimeSpan.FromSeconds(15), "Bonjour le monde"),
            new LyricLine(4, TimeSpan.FromSeconds(25), "Next line"),
        };
        var sut = new LyricsDocument(lines, isSynced: true);

        // Act
        int activeIndex = sut.FindActiveLineIndex(TimeSpan.FromSeconds(18));
        (int start, int end) = sut.FindActiveLineRange(TimeSpan.FromSeconds(18));

        // Assert
        Assert.Equal(1, activeIndex);
        Assert.Equal(1, start);
        Assert.Equal(3, end);
    }

    /// <summary>
    /// 間奏・歌唱休止用の空行に到達した場合、直前のハイライトを解除するため -1 および (-1, -1) を返し、後続行で再びアクティブになることを検証します。
    /// </summary>
    [Fact]
    public void FindActiveLineIndex_間奏空行到達時_マイナス1を返し後続行で復帰すること()
    {
        // Arrange
        var lines = new[]
        {
            new LyricLine(0, TimeSpan.FromSeconds(10), "Aメロ"),
            new LyricLine(1, TimeSpan.FromSeconds(20), string.Empty),
            new LyricLine(2, TimeSpan.FromSeconds(35), "Bメロ"),
        };
        var sut = new LyricsDocument(lines, isSynced: true);

        // Act
        int beforeBreak = sut.FindActiveLineIndex(TimeSpan.FromSeconds(15));
        int duringBreak = sut.FindActiveLineIndex(TimeSpan.FromSeconds(20));
        (int breakStart, int breakEnd) = sut.FindActiveLineRange(TimeSpan.FromSeconds(30));
        int afterBreak = sut.FindActiveLineIndex(TimeSpan.FromSeconds(35));

        // Assert
        Assert.Equal(0, beforeBreak);
        Assert.Equal(-1, duringBreak);
        Assert.Equal(-1, breakStart);
        Assert.Equal(-1, breakEnd);
        Assert.Equal(2, afterBreak);
    }

    /// <summary>
    /// 正の offset が指定された場合、LRC 標準仕様に従い表示タイミングが早まり、0 秒未満となる行は 0 秒にクランプされることを検証します。
    /// </summary>
    [Fact]
    public void FindActiveLineIndex_正のOffset補正_表示が早まり0秒未満は0秒クランプされること()
    {
        // Arrange
        var lines = new[]
        {
            new LyricLine(0, TimeSpan.FromMilliseconds(300), "冒頭行"),
            new LyricLine(1, TimeSpan.FromSeconds(10), "2行目"),
        };
        var sut = new LyricsDocument(lines, isSynced: true, offset: TimeSpan.FromMilliseconds(500));

        // Act
        TimeSpan? line0Effective = sut.GetEffectiveTimestamp(0);
        TimeSpan? line1Effective = sut.GetEffectiveTimestamp(1);
        int atZero = sut.FindActiveLineIndex(TimeSpan.Zero);
        int justBeforeLine1 = sut.FindActiveLineIndex(TimeSpan.FromMilliseconds(9499));
        int atLine1 = sut.FindActiveLineIndex(TimeSpan.FromMilliseconds(9500));

        // Assert
        Assert.Equal(TimeSpan.Zero, line0Effective);
        Assert.Equal(TimeSpan.FromMilliseconds(9500), line1Effective);
        Assert.Equal(0, atZero);
        Assert.Equal(0, justBeforeLine1);
        Assert.Equal(1, atLine1);
    }

    /// <summary>
    /// 負の offset が指定された場合、LRC 標準仕様に従い表示タイミングが遅れることを検証します。
    /// </summary>
    [Fact]
    public void FindActiveLineIndex_負のOffset補正_表示が遅れること()
    {
        // Arrange
        LyricsDocument sut = CreateStandardSyncedDocument(offset: TimeSpan.FromMilliseconds(-500));

        // Act
        int beforeAdjusted = sut.FindActiveLineIndex(TimeSpan.FromMilliseconds(10499));
        int atAdjusted = sut.FindActiveLineIndex(TimeSpan.FromMilliseconds(10500));

        // Assert
        Assert.Equal(-1, beforeAdjusted);
        Assert.Equal(0, atAdjusted);
    }

    /// <summary>
    /// 非同期（プレーン）歌詞および空ドキュメントでは、どの再生位置でも -1 を返すことを検証します。
    /// </summary>
    [Fact]
    public void FindActiveLineIndex_非同期歌詞および空ドキュメント_常にマイナス1を返すこと()
    {
        // Arrange
        var plainLines = new[]
        {
            new LyricLine(0, null, "タイムタグのない歌詞1"),
            new LyricLine(1, null, "タイムタグのない歌詞2"),
        };
        var sut = new LyricsDocument(plainLines, isSynced: false);

        // Act
        int plainIndex = sut.FindActiveLineIndex(TimeSpan.FromSeconds(10));
        int emptyIndex = LyricsDocument.Empty.FindActiveLineIndex(TimeSpan.FromSeconds(10));

        // Assert
        Assert.False(sut.IsSynced);
        Assert.Equal(-1, plainIndex);
        Assert.Equal(-1, emptyIndex);
    }

    /// <summary>
    /// GetSeekPosition が同期テキスト行では補正後再生位置を返し、間奏空行・非同期歌詞・範囲外では null を返すことを検証します。
    /// </summary>
    [Fact]
    public void GetSeekPosition_同期テキスト行と対象外行_適切なシーク位置またはnullを返すこと()
    {
        // Arrange
        var syncedLines = new[]
        {
            new LyricLine(0, TimeSpan.FromSeconds(10), "歌唱行"),
            new LyricLine(1, TimeSpan.FromSeconds(20), string.Empty),
        };
        var sut = new LyricsDocument(syncedLines, isSynced: true, offset: TimeSpan.FromMilliseconds(250));
        var plainDoc = new LyricsDocument([new LyricLine(0, null, "プレーン")], isSynced: false);

        // Act
        TimeSpan? seekPos = sut.GetSeekPosition(0);
        TimeSpan? instrumentalPos = sut.GetSeekPosition(1);
        TimeSpan? outOfRangePos = sut.GetSeekPosition(99);
        TimeSpan? plainPos = plainDoc.GetSeekPosition(0);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(9750), seekPos);
        Assert.Equal(0, sut.FindActiveLineIndex(seekPos!.Value));
        Assert.Null(instrumentalPos);
        Assert.Null(outOfRangePos);
        Assert.Null(plainPos);
    }

    /// <summary>
    /// 大量の歌詞行（1000行）に対しても二分探索で正確にアクティブ行を特定できることを検証します。
    /// </summary>
    [Fact]
    public void FindActiveLineIndex_大量の歌詞行_二分探索で正確な行インデックスを返すこと()
    {
        // Arrange
        var lines = new List<LyricLine>(1000);
        for (int i = 0; i < 1000; i++)
        {
            lines.Add(new LyricLine(i, TimeSpan.FromSeconds(i + 1), $"Line {i}"));
        }

        var sut = new LyricsDocument(lines, isSynced: true);

        // Act
        int actual = sut.FindActiveLineIndex(TimeSpan.FromSeconds(777.5));

        // Assert
        Assert.Equal(776, actual);
    }

    /// <summary>
    /// WithSource 呼び出し時および行インデックス不整合入力時に、不変性を保って正規化されることを検証します。
    /// </summary>
    [Fact]
    public void WithSource_ソース情報付与_既存データを保持しつつソース情報が更新されること()
    {
        // Arrange
        var lines = new[]
        {
            new LyricLine(10, TimeSpan.FromSeconds(5), "歌詞"),
        };
        var sut = new LyricsDocument(lines, isSynced: true, title: "  Song Title  ", artist: "Artist");

        // Act
        LyricsDocument updated = sut.WithSource(LyricsSourceType.LrcFile, @"C:\Music\song.lrc");

        // Assert
        Assert.Equal(0, sut.Lines[0].Index);
        Assert.Equal("Song Title", sut.Title);
        Assert.Equal(LyricsSourceType.None, sut.SourceType);
        Assert.Equal(LyricsSourceType.LrcFile, updated.SourceType);
        Assert.Equal(@"C:\Music\song.lrc", updated.SourcePath);
        Assert.Equal("Song Title", updated.Title);
    }
}
