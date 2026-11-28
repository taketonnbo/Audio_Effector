using System;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Domain.Tests.ValueObjects;

/// <summary>
/// LyricLine 値オブジェクトの不変条件・クランプ・間奏/シーク可否判定を検証するテストクラスです。
/// </summary>
public sealed class LyricLineTests
{
    /// <summary>
    /// 負の行インデックスを指定した場合、ArgumentOutOfRangeException がスローされることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_負の行インデックス_ArgumentOutOfRangeExceptionをスローすること()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricLine(-1, TimeSpan.FromSeconds(1), "歌詞"));
    }

    /// <summary>
    /// 負のタイムスタンプを指定した場合、TimeSpan.Zero へクランプされることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_負のタイムスタンプ_TimeSpanZeroにクランプされること()
    {
        // Arrange & Act
        var sut = new LyricLine(0, TimeSpan.FromSeconds(-5), "先頭歌詞");

        // Assert
        Assert.Equal(TimeSpan.Zero, sut.Timestamp);
        Assert.True(sut.HasTimestamp);
        Assert.True(sut.IsSeekable);
    }

    /// <summary>
    /// タイムスタンプありかつテキストが空または空白のみの場合、間奏行と判定されシーク対象外となることを検証します。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsInstrumentalおよびIsSeekable_タイムスタンプありでテキストが空または空白_間奏行かつシーク不可となること(string? text)
    {
        // Arrange & Act
        var sut = new LyricLine(2, TimeSpan.FromSeconds(30), text);

        // Assert
        Assert.True(sut.HasTimestamp);
        Assert.True(sut.IsInstrumental);
        Assert.False(sut.IsSeekable);
        Assert.Equal(text ?? string.Empty, sut.Text);
    }

    /// <summary>
    /// タイムスタンプが null の非同期行は、テキストが存在してもシーク対象外であることを検証します。
    /// </summary>
    [Fact]
    public void IsSeekable_タイムスタンプなし非同期行_Falseを返すこと()
    {
        // Arrange & Act
        var sut = new LyricLine(0, null, "プレーン歌詞の1行目");

        // Assert
        Assert.False(sut.HasTimestamp);
        Assert.False(sut.IsInstrumental);
        Assert.False(sut.IsSeekable);
    }

    /// <summary>
    /// WithIndex を呼び出した場合、タイムスタンプとテキストを保持したまま新しいインデックスのインスタンスが返されることを検証します。
    /// </summary>
    [Fact]
    public void WithIndex_新しいインデックス指定_インデックスのみ更新された新インスタンスを返すこと()
    {
        // Arrange
        var sut = new LyricLine(0, TimeSpan.FromSeconds(12.5), "サビの歌詞");

        // Act
        LyricLine actual = sut.WithIndex(5);

        // Assert
        Assert.Equal(0, sut.Index);
        Assert.Equal(5, actual.Index);
        Assert.Equal(sut.Timestamp, actual.Timestamp);
        Assert.Equal(sut.Text, actual.Text);
    }
}
