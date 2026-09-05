using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Domain.Services;
using Xunit;

namespace AudioEffector.Tests.Domain.Services;

public class ShufflePlaybackStrategyTests
{
    /// <summary>
    /// 同一のシードを持つRandomを渡した場合、常に再現可能なシャッフル順序が生成されるかを検証します。
    /// </summary>
    [Fact]
    public void Reshuffle_固定シードRandom_再現可能なシャッフル順序を生成すること()
    {
        // Arrange
        var random1 = new Random(42);
        var random2 = new Random(42);
        var sut1 = new ShufflePlaybackStrategy(random1);
        var sut2 = new ShufflePlaybackStrategy(random2);

        // Act
        var list1 = new List<int?>();
        var list2 = new List<int?>();
        for (int i = 0; i < 5; i++)
        {
            list1.Add(sut1.GetNextIndex(0, 5));
            list2.Add(sut2.GetNextIndex(0, 5));
        }

        // Assert
        Assert.Equal(list1, list2);
    }

    /// <summary>
    /// シャッフル実行後、0〜totalTracks-1の全インデックスが重複も欠落もなく網羅されているかを検証します。
    /// </summary>
    [Fact]
    public void Reshuffle_全インデックス網羅_重複や欠落なく全曲が含まれること()
    {
        // Arrange
        var sut = new ShufflePlaybackStrategy(new Random(100));
        int totalTracks = 10;

        // Act
        var played = new List<int>();
        for (int i = 0; i < totalTracks; i++)
        {
            var next = sut.GetNextIndex(0, totalTracks);
            Assert.NotNull(next);
            played.Add(next.Value);
        }

        // Assert
        Assert.Equal(totalTracks, played.Count);
        Assert.Equal(totalTracks, played.Distinct().Count());
        Assert.True(Enumerable.Range(0, totalTracks).All(idx => played.Contains(idx)));
    }

    /// <summary>
    /// 現在再生中の曲インデックスを指定してReshuffleした後、GetNextIndexからGetPreviousIndexで戻ると現在曲インデックスが返されるかを検証します。
    /// </summary>
    [Fact]
    public void Reshuffle_現在曲指定時_現在曲が先頭に配置されること()
    {
        // Arrange
        var sut = new ShufflePlaybackStrategy(new Random(123));
        int currentTrackIndex = 7;
        int totalTracks = 10;

        // Act
        sut.Reshuffle(totalTracks, currentTrackIndex);
        var next = sut.GetNextIndex(currentTrackIndex, totalTracks);
        var prev = sut.GetPreviousIndex(next!.Value, totalTracks);

        // Assert
        Assert.Equal(currentTrackIndex, prev);
    }

    /// <summary>
    /// 全曲を一巡再生した後、自動的に再シャッフルが行われて継続してインデックスが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetNextIndex_全曲一巡_自動的に再シャッフルされて次の一巡へ進むこと()
    {
        // Arrange
        var sut = new ShufflePlaybackStrategy(new Random(99));
        int totalTracks = 5;

        // Act
        var firstPass = new List<int>();
        for (int i = 0; i < totalTracks; i++)
        {
            firstPass.Add(sut.GetNextIndex(0, totalTracks)!.Value);
        }

        // 一巡後の次曲
        var secondPassFirst = sut.GetNextIndex(0, totalTracks);

        // Assert
        Assert.Equal(5, firstPass.Count);
        Assert.NotNull(secondPassFirst);
        Assert.InRange(secondPassFirst.Value, 0, totalTracks - 1);
    }

    /// <summary>
    /// 曲を複数回進めた後にGetPreviousIndexを呼び出した際、履歴順に直前の曲インデックスが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetPreviousIndex_履歴が存在する場合_直前の曲インデックスを返すこと()
    {
        // Arrange
        var sut = new ShufflePlaybackStrategy(new Random(50));
        var first = sut.GetNextIndex(0, 5)!.Value;
        var second = sut.GetNextIndex(first, 5)!.Value;

        // Act
        var prev = sut.GetPreviousIndex(second, 5);

        // Assert
        Assert.Equal(first, prev);
    }

    /// <summary>
    /// 履歴の先頭（最初の曲）より前には戻れないため、nullが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetPreviousIndex_履歴の先頭以前_nullを返すこと()
    {
        // Arrange
        var sut = new ShufflePlaybackStrategy(new Random(50));
        _ = sut.GetNextIndex(0, 5);
        _ = sut.GetPreviousIndex(0, 5);

        // Act
        var prev = sut.GetPreviousIndex(0, 5);

        // Assert
        Assert.Null(prev);
    }

    /// <summary>
    /// 楽曲総数が0以下の場合、nullが返されるかを検証します。
    /// </summary>
    [Fact]
    public void GetNextIndex_曲数0以下_nullを返すこと()
    {
        // Arrange
        var sut = new ShufflePlaybackStrategy();

        // Act
        var next = sut.GetNextIndex(0, 0);

        // Assert
        Assert.Null(next);
    }
}
