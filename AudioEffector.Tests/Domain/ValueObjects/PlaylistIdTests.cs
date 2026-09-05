using System;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

public class PlaylistIdTests
{
    /// <summary>
    /// PlaylistId.Newを呼び出した際、Guid.Emptyではない一意のGUIDを持つインスタンスが生成されるかを検証します。
    /// </summary>
    [Fact]
    public void New_呼び出し時_空でないランダムGUIDで生成されること()
    {
        // Arrange & Act
        var sut1 = PlaylistId.New();
        var sut2 = PlaylistId.New();

        // Assert
        Assert.NotEqual(Guid.Empty, sut1.Value);
        Assert.NotEqual(Guid.Empty, sut2.Value);
        Assert.NotEqual(sut1, sut2);
    }

    /// <summary>
    /// 有効なGUIDを指定してPlaylistIdを生成した場合、その値が保持されるかを検証します。
    /// </summary>
    [Fact]
    public void From_有効なGUID_指定GUIDで生成されること()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var sut = PlaylistId.From(guid);

        // Assert
        Assert.Equal(guid, sut.Value);
        Assert.Equal(guid.ToString(), sut.ToString());
    }

    /// <summary>
    /// Guid.Emptyを指定して生成した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void From_空のGUID_ArgumentExceptionをスローすること()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new PlaylistId(Guid.Empty));
    }

    /// <summary>
    /// 有効なGUID文字列を指定して生成した場合、正しくパースされてインスタンスが生成されるかを検証します。
    /// </summary>
    [Fact]
    public void From_有効なGUID文字列_正しくパースして生成されること()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var guidStr = guid.ToString();

        // Act
        var sut = PlaylistId.From(guidStr);

        // Assert
        Assert.Equal(guid, sut.Value);
    }

    /// <summary>
    /// 不正な文字列を指定して生成した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("xyz")]
    public void From_不正なGUID文字列_ArgumentExceptionをスローすること(string invalidStr)
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => PlaylistId.From(invalidStr));
    }

    /// <summary>
    /// 同一のGUIDを持つPlaylistId同士が等価と判定されるかを検証します。
    /// </summary>
    [Fact]
    public void Equals_同一GUID同士_等価と判定されること()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var sut1 = PlaylistId.From(guid);
        var sut2 = PlaylistId.From(guid);

        // Act & Assert
        Assert.True(sut1.Equals(sut2));
        Assert.True(sut1 == sut2);
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
    }
}
