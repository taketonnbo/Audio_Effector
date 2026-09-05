using AudioEffector.Domain.Services;
using Xunit;

namespace AudioEffector.Tests.Domain.Services;

public class SequentialPlaybackStrategyTests
{
    /// <summary>
    /// 通常の順次再生において、現在のインデックスの次の曲のインデックスが正しく返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(1, 5, 2)]
    [InlineData(3, 5, 4)]
    public void GetNextIndex_通常遷移_次の楽曲インデックスを返すこと(int currentIndex, int totalTracks, int expectedNext)
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var actualNext = sut.GetNextIndex(currentIndex, totalTracks);

        // Assert
        Assert.Equal(expectedNext, actualNext);
    }

    /// <summary>
    /// 末尾の楽曲を再生中の場合、次の曲が存在しないためnullが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetNextIndex_末尾楽曲_nullを返すこと()
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var next = sut.GetNextIndex(4, 5);

        // Assert
        Assert.Null(next);
    }

    /// <summary>
    /// 現在のインデックスが負の値（未選択状態）の場合、先頭曲（0）が返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetNextIndex_負のインデックス_先頭0を返すこと()
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var next = sut.GetNextIndex(-1, 5);

        // Assert
        Assert.Equal(0, next);
    }

    /// <summary>
    /// 楽曲総数が0以下の場合、nullが返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetNextIndex_楽曲総数0以下_nullを返すこと(int totalTracks)
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var next = sut.GetNextIndex(0, totalTracks);

        // Assert
        Assert.Null(next);
    }

    /// <summary>
    /// 通常の順次再生において、前の曲のインデックスが正しく返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(4, 5, 3)]
    [InlineData(2, 5, 1)]
    [InlineData(1, 5, 0)]
    public void GetPreviousIndex_通常遷移_前の楽曲インデックスを返すこと(int currentIndex, int totalTracks, int expectedPrev)
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var actualPrev = sut.GetPreviousIndex(currentIndex, totalTracks);

        // Assert
        Assert.Equal(expectedPrev, actualPrev);
    }

    /// <summary>
    /// 先頭曲（インデックス0以下）を再生中の場合、前の曲が存在しないためnullが返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetPreviousIndex_先頭楽曲_nullを返すこと(int currentIndex)
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var prev = sut.GetPreviousIndex(currentIndex, 5);

        // Assert
        Assert.Null(prev);
    }

    /// <summary>
    /// 楽曲総数が0以下の場合、前の曲取得でnullが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetPreviousIndex_楽曲総数0以下_nullを返すこと()
    {
        // Arrange
        var sut = new SequentialPlaybackStrategy();

        // Act
        var prev = sut.GetPreviousIndex(0, 0);

        // Assert
        Assert.Null(prev);
    }
}
