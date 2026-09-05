using System;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

public class AudioPathTests
{
    /// <summary>
    /// 有効な音声ファイルパスを指定してAudioPathを生成した場合、Value・Extension・FileNameが正しく設定されるかを検証します。
    /// </summary>
    [Fact]
    public void Create_有効なパス文字列_プロパティが正しく設定されること()
    {
        // Arrange
        var path = @"C:\Music\Album\song.flac";

        // Act
        var sut = AudioPath.Create(path);

        // Assert
        Assert.Equal(@"C:\Music\Album\song.flac", sut.Value);
        Assert.Equal(".flac", sut.Extension);
        Assert.Equal("song.flac", sut.FileName);
    }

    /// <summary>
    /// 前後に余計な空白を含むパスを指定した場合、トリムされたパスでAudioPathが生成されるかを検証します。
    /// </summary>
    [Fact]
    public void Create_前後に空白を含むパス_トリムされて生成されること()
    {
        // Arrange
        var path = @"   C:\Music\song.mp3   ";

        // Act
        var sut = AudioPath.Create(path);

        // Assert
        Assert.Equal(@"C:\Music\song.mp3", sut.Value);
        Assert.Equal(".mp3", sut.Extension);
        Assert.Equal("song.mp3", sut.FileName);
    }

    /// <summary>
    /// 空文字または空白文字列を指定した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_空文字または空白文字列_ArgumentExceptionをスローすること(string emptyPath)
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => AudioPath.Create(emptyPath));
    }

    /// <summary>
    /// Null文字列を指定した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void Create_Null文字列_ArgumentExceptionをスローすること()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => AudioPath.Create(null!));
    }

    /// <summary>
    /// サポート対象の主要な音声拡張子（大文字小文字問わず）を指定した場合、IsSupportedAudioFormatがTrueを返すかを検証します。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Music\test.mp3")]
    [InlineData(@"C:\Music\test.MP3")]
    [InlineData(@"C:\Music\test.flac")]
    [InlineData(@"C:\Music\test.FLAC")]
    [InlineData(@"C:\Music\test.wav")]
    [InlineData(@"C:\Music\test.m4a")]
    [InlineData(@"C:\Music\test.aac")]
    [InlineData(@"C:\Music\test.wma")]
    [InlineData(@"C:\Music\test.ogg")]
    [InlineData(@"C:\Music\test.alac")]
    [InlineData(@"C:\Music\test.aiff")]
    public void IsSupportedAudioFormat_対応拡張子_Trueを返すこと(string filePath)
    {
        // Arrange
        var sut = AudioPath.Create(filePath);

        // Act
        var isSupported = sut.IsSupportedAudioFormat();

        // Assert
        Assert.True(isSupported);
    }

    /// <summary>
    /// 音声以外の拡張子や非対応拡張子を指定した場合、IsSupportedAudioFormatがFalseを返すかを検証します。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Documents\test.txt")]
    [InlineData(@"C:\Programs\test.exe")]
    [InlineData(@"C:\Videos\test.mp4")]
    [InlineData(@"C:\Music\no_extension")]
    public void IsSupportedAudioFormat_非対応拡張子_Falseを返すこと(string filePath)
    {
        // Arrange
        var sut = AudioPath.Create(filePath);

        // Act
        var isSupported = sut.IsSupportedAudioFormat();

        // Assert
        Assert.False(isSupported);
    }

    /// <summary>
    /// string型とAudioPath型の相互暗黙型変換が正しく機能するかを検証します。
    /// </summary>
    [Fact]
    public void ImplicitOperator_文字列との相互暗黙型変換_正しく変換されること()
    {
        // Arrange
        var pathStr = @"C:\Music\sample.wav";

        // Act
        AudioPath sut = pathStr;
        string convertedStr = sut;

        // Assert
        Assert.Equal(pathStr, sut.Value);
        Assert.Equal(pathStr, convertedStr);
    }

    /// <summary>
    /// 同一のパス文字列を持つAudioPath同士が等価と判定されるかを検証します。
    /// </summary>
    [Fact]
    public void Equals_同一パスのAudioPath_等価と判定されること()
    {
        // Arrange
        var sut1 = AudioPath.Create(@"C:\Music\sample.flac");
        var sut2 = AudioPath.Create(@"C:\Music\sample.flac");

        // Act & Assert
        Assert.True(sut1.Equals(sut2));
        Assert.True(sut1 == sut2);
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
        Assert.Equal(sut1.ToString(), sut2.ToString());
    }
}
