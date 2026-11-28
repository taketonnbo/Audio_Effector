using System;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Tests.Services;

public sealed class LyricsForcedAlignerTests
{
    /// <summary>
    /// 歌詞行と音声認識セグメントが 1 対 1 で対応する場合に各行へ正確なタイムスタンプとメタデータが付与されることを検証します。
    /// </summary>
    [Fact]
    public void Align_一致する認識セグメント列_各歌詞行へ対応タイムスタンプとメタデータを付与する()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        string[] lyrics = ["夜空を見上げて", "光を探した", "明日へ歩き出す"];
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(10.2), TimeSpan.FromSeconds(14.0), "夜空を見上げて"),
            new(TimeSpan.FromSeconds(15.5), TimeSpan.FromSeconds(19.0), "光を探した"),
            new(TimeSpan.FromSeconds(22.1), TimeSpan.FromSeconds(26.0), "明日へ歩き出す"),
        ];

        // Act
        LyricsDocument doc = sut.Align(
            lyrics,
            segments,
            TimeSpan.FromSeconds(30),
            title: "Starlight",
            artist: "TestArtist",
            album: "TestAlbum");

        // Assert
        Assert.True(doc.IsSynced);
        Assert.Equal("Starlight", doc.Title);
        Assert.Equal("TestArtist", doc.Artist);
        Assert.Equal("TestAlbum", doc.Album);
        Assert.Equal(3, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10.2), doc.Lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(15.5), doc.Lines[1].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(22.1), doc.Lines[2].Timestamp);
    }

    /// <summary>
    /// 空行や前後空白を含む歌詞テキストでも空行が安全に除外され有効な歌詞行のみが同期されることを検証します。
    /// </summary>
    [Fact]
    public void Align_空行と前後空白を含む入力_空行を除外しトリミング済み歌詞行を返す()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        string[] lyrics = ["", "  最初のフレーズ  ", "   ", "次のフレーズ"];
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(8.0), "最初のフレーズ"),
            new(TimeSpan.FromSeconds(12.0), TimeSpan.FromSeconds(15.0), "次のフレーズ"),
        ];

        // Act
        LyricsDocument doc = sut.Align(lyrics, segments);

        // Assert
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal("最初のフレーズ", doc.Lines[0].Text);
        Assert.Equal("次のフレーズ", doc.Lines[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(5.0), doc.Lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(12.0), doc.Lines[1].Timestamp);
    }

    /// <summary>
    /// 同一テキストの繰り返しサビが含まれる場合に単調増加制約によって 1 回目と 2 回目の出現時刻が正しく区別されることを検証します。
    /// </summary>
    [Fact]
    public void Align_繰り返しサビを含む歌詞_単調順序でそれぞれの出現時刻へ割り当てる()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        string[] lyrics = ["Aメロ一番", "輝くサビのフレーズ", "Bメロ二番", "輝くサビのフレーズ"];
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), "Aメロ一番"),
            new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(28), "輝くサビのフレーズ"),
            new(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(48), "Bメロ二番"),
            new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(68), "輝くサビのフレーズ"),
        ];

        // Act
        LyricsDocument doc = sut.Align(lyrics, segments);

        // Assert
        Assert.Equal(4, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[1].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(60), doc.Lines[3].Timestamp);
    }

    /// <summary>
    /// 歌詞行数が認識セグメント数より多い長短不一致ケースでも未一致行がアンカー間で補間され単調非減少になることを検証します。
    /// </summary>
    [Fact]
    public void Align_認識セグメント数が歌詞行より少ない場合_アンカー間を補間し全行へ単調タイムスタンプを付与する()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        string[] lyrics = ["冒頭の一行目", "認識漏れした二行目", "認識漏れした三行目", "終盤の四行目"];
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), "冒頭の一行目"),
            new(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(45), "終盤の四行目"),
        ];

        // Act
        LyricsDocument doc = sut.Align(lyrics, segments, TimeSpan.FromSeconds(50));

        // Assert
        Assert.Equal(4, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), doc.Lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(40), doc.Lines[3].Timestamp);
        Assert.True(doc.Lines[1].Timestamp > doc.Lines[0].Timestamp);
        Assert.True(doc.Lines[2].Timestamp > doc.Lines[1].Timestamp);
        Assert.True(doc.Lines[3].Timestamp >= doc.Lines[2].Timestamp);
    }

    /// <summary>
    /// 歌詞行が空または認識セグメントが空の場合に空の LyricsDocument を返すことを検証します。
    /// </summary>
    [Fact]
    public void Align_空入力またはセグメント0件_空のLyricsDocumentを返す()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        AlignmentSegment[] segments = [new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Hello")];

        // Act
        LyricsDocument emptyLyricsDoc = sut.Align(Array.Empty<string>(), segments);
        LyricsDocument emptySegmentsDoc = sut.Align(["Hello"], Array.Empty<AlignmentSegment>());

        // Assert
        Assert.False(emptyLyricsDoc.HasLines);
        Assert.False(emptySegmentsDoc.HasLines);
    }

    /// <summary>
    /// 音声直接文字起こしモード用の BuildDocumentFromSegments が空テキストを除外しタイムスタンプ昇順の同期ドキュメントを構築することを検証します。
    /// </summary>
    [Fact]
    public void BuildDocumentFromSegments_未ソートおよび空テキスト混在セグメント_昇順の同期ドキュメントを構築する()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(24), "二番目の発話"),
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(16), "   "),
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), "最初の発話"),
        ];

        // Act
        LyricsDocument doc = sut.BuildDocumentFromSegments(segments, title: "VoiceMemo");

        // Assert
        Assert.True(doc.IsSynced);
        Assert.Equal("VoiceMemo", doc.Title);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal("最初の発話", doc.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(5), doc.Lines[0].Timestamp);
        Assert.Equal("二番目の発話", doc.Lines[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(20), doc.Lines[1].Timestamp);
    }

    /// <summary>
    /// テキスト類似度計算が全角半角・大文字小文字・句読点の違いを吸収して高いスコアを返すことを検証します。
    /// </summary>
    [Fact]
    public void ComputeTextSimilarity_全角半角と句読点差がある同一語句_1を返す()
    {
        // Arrange
        string left = "Hello, World!　ＨＥＬＬＯ";
        string right = "hello world hello";

        // Act
        double score = LyricsForcedAligner.ComputeTextSimilarity(left, right);

        // Assert
        Assert.Equal(1.0, score, precision: 6);
    }

    /// <summary>
    /// シフトおよび背景ノイズセグメント混在条件下でも閾値未満のダミーマッチに負けず正解アンカーが選ばれることを検証します。
    /// </summary>
    [Fact]
    public void Align_背景ノイズとシフトを含むセグメント列_ダミーマッチを排除し正解アンカーへ割り当てる()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        string[] lyrics = ["夜空を見上げて", "光を探した", "明日へ歩き出す", "夢を追いかけた"];
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "イントロノイズ雑音"),
            new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(16), "夜空を見上げて"),
            new(TimeSpan.FromSeconds(22), TimeSpan.FromSeconds(26), "光を探した"),
            new(TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(36), "明日へ歩き出す"),
        ];

        // Act
        LyricsDocument doc = sut.Align(lyrics, segments, TimeSpan.FromSeconds(45));

        // Assert
        Assert.Equal(4, doc.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(12), doc.Lines[0].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(22), doc.Lines[1].Timestamp);
        Assert.Equal(TimeSpan.FromSeconds(32), doc.Lines[2].Timestamp);
        Assert.True(doc.Lines[3].Timestamp > doc.Lines[2].Timestamp);
    }

    /// <summary>
    /// 先頭アンカーが先頭セグメントと一致した場合に先頭の未認識歌詞行がアンカーと同一時刻へ張り付かず 0 秒から補間されることを検証します。
    /// </summary>
    [Fact]
    public void Align_先頭セグメントが2行目以降と一致する場合_先頭未認識行が0秒からアンカーまで補間される()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        string[] lyrics = ["未認識の導入一行目", "未認識の導入二行目", "鮮明に認識されたサビ"];
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(36), "鮮明に認識されたサビ"),
        ];

        // Act
        LyricsDocument doc = sut.Align(lyrics, segments, TimeSpan.FromSeconds(40));

        // Assert
        Assert.Equal(3, doc.Lines.Count);
        Assert.Equal(TimeSpan.Zero, doc.Lines[0].Timestamp);
        Assert.True(doc.Lines[1].Timestamp > TimeSpan.Zero && doc.Lines[1].Timestamp < TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), doc.Lines[2].Timestamp);
    }

    /// <summary>
    /// Align は AutoGeneratedTextAlignment、BuildDocumentFromSegments は AutoGeneratedAudioTranscription の OriginType をそれぞれ付与することを検証します。
    /// </summary>
    [Fact]
    public void AlignおよびBuildDocumentFromSegments_生成方式に応じたLrcOriginTypeが付与されること()
    {
        // Arrange
        var sut = new LyricsForcedAligner();
        AlignmentSegment[] segments =
        [
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), "夜空の星"),
        ];

        // Act
        LyricsDocument alignedDoc = sut.Align(["夜空の星"], segments, TimeSpan.FromSeconds(10));
        LyricsDocument transcribedDoc = sut.BuildDocumentFromSegments(segments);

        // Assert
        Assert.Equal(LrcOriginType.AutoGeneratedTextAlignment, alignedDoc.OriginType);
        Assert.Equal(LrcOriginType.AutoGeneratedAudioTranscription, transcribedDoc.OriginType);
    }
}
