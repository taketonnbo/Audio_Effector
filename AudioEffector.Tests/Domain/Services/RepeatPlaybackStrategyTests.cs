using AudioEffector.Domain.Services;
using Xunit;

namespace AudioEffector.Tests.Domain.Services;

public class RepeatPlaybackStrategyTests
{
    /// <summary>
    /// 全曲リピート（RepeatAll）モードにおいて、末尾曲の次の曲として先頭インデックス0が返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetNextIndex_RepeatAllモード末尾楽曲_先頭インデックス0を返すこと()
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.All);

        // Act
        var next = sut.GetNextIndex(4, 5);

        // Assert
        Assert.Equal(0, next);
    }

    /// <summary>
    /// 全曲リピートモードにおいて、途中曲の場合は通常通り次のインデックスが返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(2, 5, 3)]
    public void GetNextIndex_RepeatAllモード通常遷移_次のインデックスを返すこと(int currentIndex, int totalTracks, int expectedNext)
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.All);

        // Act
        var next = sut.GetNextIndex(currentIndex, totalTracks);

        // Assert
        Assert.Equal(expectedNext, next);
    }

    /// <summary>
    /// 全曲リピートモードにおいて、先頭曲（0）の前の曲として末尾インデックス（total-1）が返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetPreviousIndex_RepeatAllモード先頭楽曲_末尾インデックスを返すこと()
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.All);

        // Act
        var prev = sut.GetPreviousIndex(0, 5);

        // Assert
        Assert.Equal(4, prev);
    }

    /// <summary>
    /// 単曲リピート（RepeatOne）モードにおいて、次の曲取得時に現在のインデックスがそのまま返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    public void GetNextIndex_RepeatOneモード_現在のインデックスをそのまま返すこと(int currentIndex, int totalTracks)
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.One);

        // Act
        var next = sut.GetNextIndex(currentIndex, totalTracks);

        // Assert
        Assert.Equal(currentIndex, next);
    }

    /// <summary>
    /// 単曲リピートモードにおいて、前の曲取得時に現在のインデックスがそのまま返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(2, 5)]
    public void GetPreviousIndex_RepeatOneモード_現在のインデックスをそのまま返すこと(int currentIndex, int totalTracks)
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.One);

        // Act
        var prev = sut.GetPreviousIndex(currentIndex, totalTracks);

        // Assert
        Assert.Equal(currentIndex, prev);
    }

    /// <summary>
    /// 楽曲総数が0以下の場合、次の曲取得でnullが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetNextIndex_曲数0以下_nullを返すこと()
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.All);

        // Act
        var next = sut.GetNextIndex(0, 0);

        // Assert
        Assert.Null(next);
    }

    /// <summary>
    /// 楽曲総数が0以下の場合、前の曲取得でnullが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetPreviousIndex_曲数0以下_nullを返すこと()
    {
        // Arrange
        var sut = new RepeatPlaybackStrategy(RepeatMode.All);

        // Act
        var prev = sut.GetPreviousIndex(0, 0);

        // Assert
        Assert.Null(prev);
    }
}
