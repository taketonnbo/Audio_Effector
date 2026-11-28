using System;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Lyrics;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Lyrics;

/// <summary>
/// LrcParser のタイムタグ解析・複数タグ展開・メタデータ抽出・不正行フォールバック・Format ラウンドトリップを検証するテストクラスです。
/// </summary>
public sealed class LrcParserTests
{
    /// <summary>
    /// 標準形式 [mm:ss.xx]、3桁ミリ秒 [mm:ss.xxx]、小数省略 [mm:ss]、コロン区切り [mm:ss:xx]、3桁分表記がすべて正確にパースされることを検証します。
    /// </summary>
    [Fact]
    public void Parse_各種対応タイムタグ形式_正確にタイムスタンプへ変換されること()
    {
        // Arrange
        var sut = new LrcParser();
        const string content = """
            [00:05.25]標準2桁センチ秒
            [00:10.125]3桁ミリ秒
            [00:15]小数省略
            [00:20:50]コロン区切り
            [00:25.5]1桁小数
            [105:12.30]3桁分表記
            """;

        // Act
        LyricsDocument actual = sut.Parse(content);

        // Assert
        Assert.True(actual.IsSynced);
        Assert.Equal(6, actual.Lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(5250), actual.Lines[0].Timestamp);
        Assert.Equal("標準2桁センチ秒", actual.Lines[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(10125), actual.Lines[1].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(15), actual.Lines[2].Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(20500), actual.Lines[3].Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(25500), actual.Lines[4].Timestamp);
        Assert.Equal(TimeSpan.FromMinutes(105) + TimeSpan.FromSeconds(12) + TimeSpan.FromMilliseconds(300), actual.Lines[5].Timestamp);
    }

    /// <summary>
    /// 1行に複数のタイムタグが並記されている場合、全タイムスタンプへ展開された上で時系列昇順に安定ソートされることを検証します。
    /// </summary>
    [Fact]
    public void Parse_複数タイムタグ行と同一タイムスタンプ行_展開および元記載順維持で昇順ソートされること()
    {
        // Arrange
        var sut = new LrcParser();
        const string content = """
            [01:05.50][00:12.00]サビの歌詞
            [00:30.00]Hello world
            [00:30.00]こんにちは世界
            [00:05.00]Aメロ
            """;

        // Act
        LyricsDocument actual = sut.Parse(content);

        // Assert
        Assert.True(actual.IsSynced);
        Assert.Equal(5, actual.Lines.Count);

        Assert.Equal((TimeSpan.FromSeconds(5), "Aメロ"), (actual.Lines[0].Timestamp, actual.Lines[0].Text));
        Assert.Equal((TimeSpan.FromSeconds(12), "サビの歌詞"), (actual.Lines[1].Timestamp, actual.Lines[1].Text));
        Assert.Equal((TimeSpan.FromSeconds(30), "Hello world"), (actual.Lines[2].Timestamp, actual.Lines[2].Text));
        Assert.Equal((TimeSpan.FromSeconds(30), "こんにちは世界"), (actual.Lines[3].Timestamp, actual.Lines[3].Text));
        Assert.Equal((TimeSpan.FromMilliseconds(65500), "サビの歌詞"), (actual.Lines[4].Timestamp, actual.Lines[4].Text));
    }

    /// <summary>
    /// メタデータタグ（ti, ar, al, by, offset）が抽出され、歌詞本文の行には含まれないことを検証します。
    /// </summary>
    [Fact]
    public void Parse_メタデータタグ含有LRC_各メタデータが抽出され本文から除外されること()
    {
        // Arrange
        var sut = new LrcParser();
        const string content = """
            [ti:夜明けのメロディ]
            [ar:テストアーティスト]
            [al:ファーストアルバム]
            [by:LyricsAuthor]
            [offset:-350]
            [re:AudioEffector]
            [00:01.00]最初のフレーズ
            """;

        // Act
        LyricsDocument actual = sut.Parse(content);

        // Assert
        Assert.True(actual.IsSynced);
        Assert.Equal("夜明けのメロディ", actual.Title);
        Assert.Equal("テストアーティスト", actual.Artist);
        Assert.Equal("ファーストアルバム", actual.Album);
        Assert.Equal("LyricsAuthor", actual.Author);
        Assert.Equal(TimeSpan.FromMilliseconds(-350), actual.Offset);
        Assert.Single(actual.Lines);
        Assert.Equal("最初のフレーズ", actual.Lines[0].Text);
    }

    /// <summary>
    /// 間奏用の空行が保持され、不正フォーマット行や不正秒数タイムタグは例外を投げずにスキップされることを検証します。
    /// </summary>
    [Fact]
    public void Parse_間奏空行と不正行混在_間奏行を保持しつつ不正行を安全にスキップすること()
    {
        // Arrange
        var sut = new LrcParser();
        const string content = """
            [offset:invalid_number]
            [00:05.00]1番の歌詞
            [00:75.00]秒数不正の行
            [broken_tag]壊れた行
            [00:20.00]
            [00:35.00]2番の歌詞
            """;

        // Act
        LyricsDocument actual = sut.Parse(content);

        // Assert
        Assert.True(actual.IsSynced);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
        Assert.Equal(3, actual.Lines.Count);
        Assert.Equal("1番の歌詞", actual.Lines[0].Text);
        Assert.True(actual.Lines[1].IsInstrumental);
        Assert.Equal(TimeSpan.FromSeconds(20), actual.Lines[1].Timestamp);
        Assert.Equal("2番の歌詞", actual.Lines[2].Text);
    }

    /// <summary>
    /// 有効なタイムタグが1つも含まれないテキストは非同期（プレーン）歌詞として上から順に保持されることを検証します。
    /// </summary>
    [Fact]
    public void Parse_タイムタグなしプレーンテキスト_IsSyncedがFalseで全文を保持すること()
    {
        // Arrange
        var sut = new LrcParser();
        const string content = "\uFEFF[ti:プレーン曲]\n\n1行目の歌詞\r\n2行目の歌詞\r3行目の歌詞\n";

        // Act
        LyricsDocument actual = sut.Parse(content);

        // Assert
        Assert.False(actual.IsSynced);
        Assert.True(actual.HasVisibleLyrics);
        Assert.Equal("プレーン曲", actual.Title);
        Assert.Equal(3, actual.Lines.Count);
        Assert.All(actual.Lines, l => Assert.Null(l.Timestamp));
        Assert.Equal("1行目の歌詞", actual.Lines[0].Text);
        Assert.Equal("2行目の歌詞", actual.Lines[1].Text);
        Assert.Equal("3行目の歌詞", actual.Lines[2].Text);
    }

    /// <summary>
    /// 空文字列またはメタデータタグのみの入力では表示可能な歌詞行が 0 行となることを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("[ti:Title Only]\n[ar:Artist Only]\n[offset:100]")]
    public void Parse_空またはメタデータのみ_表示可能な歌詞行が0行となること(string content)
    {
        // Arrange
        var sut = new LrcParser();

        // Act
        LyricsDocument actual = sut.Parse(content);

        // Assert
        Assert.False(actual.IsSynced);
        Assert.False(actual.HasVisibleLyrics);
        Assert.Empty(actual.Lines);
    }

    /// <summary>
    /// Format で出力した LRC 文字列を再度 Parse した際、メタデータ・タイムスタンプ（2桁/3桁ミリ秒/3桁分）・間奏空行・offset が忠実に復元されることを検証します。
    /// </summary>
    [Fact]
    public void Format_Parseとのラウンドトリップ_メタデータと全歌詞行が一致して復元されること()
    {
        // Arrange
        var sut = new LrcParser();
        var original = new LyricsDocument(
            lines:
            [
                new LyricLine(0, TimeSpan.FromMilliseconds(12340), "標準行"),
                new LyricLine(1, TimeSpan.FromMilliseconds(23456), "3桁ミリ秒行"),
                new LyricLine(2, TimeSpan.FromSeconds(40), string.Empty),
                new LyricLine(3, TimeSpan.FromMinutes(102) + TimeSpan.FromMilliseconds(5500), "長時間曲行"),
            ],
            isSynced: true,
            title: "RoundTrip Song",
            artist: "RoundTrip Artist",
            album: "RoundTrip Album",
            author: "AudioEffector",
            offset: TimeSpan.FromMilliseconds(250));

        // Act
        string formatted = sut.Format(original);
        LyricsDocument reparsed = sut.Parse(formatted);

        // Assert
        Assert.Contains("[ti:RoundTrip Song]", formatted, StringComparison.Ordinal);
        Assert.Contains("[offset:250]", formatted, StringComparison.Ordinal);
        Assert.Contains("[00:12.34]標準行", formatted, StringComparison.Ordinal);
        Assert.Contains("[00:23.456]3桁ミリ秒行", formatted, StringComparison.Ordinal);
        Assert.Contains("[102:05.50]長時間曲行", formatted, StringComparison.Ordinal);

        Assert.True(reparsed.IsSynced);
        Assert.Equal(original.Title, reparsed.Title);
        Assert.Equal(original.Artist, reparsed.Artist);
        Assert.Equal(original.Album, reparsed.Album);
        Assert.Equal(original.Author, reparsed.Author);
        Assert.Equal(original.Offset, reparsed.Offset);
        Assert.Equal(original.Lines.Count, reparsed.Lines.Count);
        for (int i = 0; i < original.Lines.Count; i++)
        {
            Assert.Equal(original.Lines[i].Timestamp, reparsed.Lines[i].Timestamp);
            Assert.Equal(original.Lines[i].Text, reparsed.Lines[i].Text);
        }
    }
}
