using System;
using System.Globalization;
using System.Windows;
using AudioEffector.Presentation.Converters;
using Xunit;

namespace AudioEffector.Tests.Presentation.Converters;

/// <summary>
/// <see cref="AlbumDisplayFallbackConverter"/> の文字列フォールバック解決動作を検証する単体テストクラス
/// </summary>
public sealed class AlbumDisplayFallbackConverterTests
{
    /// <summary>
    /// アルバム名が設定されている場合、アーティスト名より優先してアルバム名を返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_アルバム名とアーティスト名ともに設定時_アルバム名を返すこと()
    {
        // Arrange
        var sut = new AlbumDisplayFallbackConverter();
        object[] values = { "Hurry Up, We're Dreaming", "M83" };

        // Act
        var result = sut.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("Hurry Up, We're Dreaming", result);
    }

    /// <summary>
    /// アルバム名が空文字・空白・null・UnsetValue の場合、アーティスト名へフォールバックすることを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Convert_アルバム名が未設定または空白時_アーティスト名へフォールバックすること(string? album)
    {
        // Arrange
        var sut = new AlbumDisplayFallbackConverter();
        object[] values = { album!, "M83" };

        // Act
        var result = sut.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("M83", result);
    }

    /// <summary>
    /// すべての候補が未設定・空白・UnsetValue の場合、既定のフォールバック文字列を返すことを検証します。
    /// </summary>
    [Fact]
    public void Convert_全候補が未設定またはUnsetValue時_既定フォールバック文字列を返すこと()
    {
        // Arrange
        var sut = new AlbumDisplayFallbackConverter();
        object[] values = { DependencyProperty.UnsetValue, "   " };

        // Act
        var defaultResult = sut.Convert(values, typeof(string), null, CultureInfo.InvariantCulture);
        var customResult = sut.Convert(values, typeof(string), "Custom Fallback", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("Unknown Album", defaultResult);
        Assert.Equal("Custom Fallback", customResult);
    }

    /// <summary>
    /// ConvertBack を呼び出した場合、NotSupportedException がスローされることを検証します。
    /// </summary>
    [Fact]
    public void ConvertBack_呼び出し時_NotSupportedExceptionをスローすること()
    {
        // Arrange
        var sut = new AlbumDisplayFallbackConverter();

        // Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            sut.ConvertBack("Album", new[] { typeof(string) }, null!, CultureInfo.InvariantCulture));
    }
}
