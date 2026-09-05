using System;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

public class TimePositionTests
{
    /// <summary>
    /// 負の時間を指定して初期化した場合、TimeSpan.Zeroにクランプされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_負の再生時間_TimeSpanZeroにクランプされること()
    {
        // Arrange & Act
        var sut = new TimePosition(TimeSpan.FromSeconds(-10), TimeSpan.FromSeconds(-60));

        // Assert
        Assert.Equal(TimeSpan.Zero, sut.Current);
        Assert.Equal(TimeSpan.Zero, sut.Total);
    }

    /// <summary>
    /// 現在位置と総時間から、0.0〜1.0の進捗比率（ProgressRatio）が正しく計算されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(50, 100, 0.5)]
    [InlineData(100, 100, 1.0)]
    [InlineData(150, 100, 1.0)] // 総時間を超える場合は1.0にクランプ
    public void ProgressRatio_現在位置と総時間_0から1の進捗比率を返すこと(double currentSec, double totalSec, double expectedRatio)
    {
        // Arrange
        var sut = new TimePosition(TimeSpan.FromSeconds(currentSec), TimeSpan.FromSeconds(totalSec));

        // Act
        var actualRatio = sut.ProgressRatio;

        // Assert
        Assert.Equal(expectedRatio, actualRatio, precision: 3);
    }

    /// <summary>
    /// 総再生時間が0秒の場合、ゼロ除算を回避してProgressRatioが0.0を返すかを検証します。
    /// </summary>
    [Fact]
    public void ProgressRatio_総時間が0秒_0を返すこと()
    {
        // Arrange
        var sut = new TimePosition(TimeSpan.FromSeconds(10), TimeSpan.Zero);

        // Act
        var ratio = sut.ProgressRatio;

        // Assert
        Assert.Equal(0.0, ratio);
    }

    /// <summary>
    /// 1時間未満および1時間以上の場合に応じて、適切な時間フォーマット文字列（"mm:ss" または "h:mm:ss"）が生成されるかを検証します。
    /// </summary>
    [Fact]
    public void CurrentStringおよびTotalString_1時間未満と1時間以上_適切な時間フォーマットを返すこと()
    {
        // Arrange
        var shortTime = new TimePosition(TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(225));
        var longTime = new TimePosition(TimeSpan.FromSeconds(3665), TimeSpan.FromSeconds(7320));

        // Act & Assert
        Assert.Equal("01:05", shortTime.CurrentString);
        Assert.Equal("03:45", shortTime.TotalString);
        Assert.Equal("1:01:05", longTime.CurrentString);
        Assert.Equal("2:02:00", longTime.TotalString);
    }

    /// <summary>
    /// 残り再生時間がマイナス記号付き文字列（例: "-01:15"）として正しく生成されるかを検証します。
    /// </summary>
    [Fact]
    public void RemainingString_残り時間のフォーマット_マイナス記号付き文字列を返すこと()
    {
        // Arrange
        var sut = new TimePosition(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(175));

        // Act
        var remaining = sut.Remaining;
        var remainingStr = sut.RemainingString;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(75), remaining);
        Assert.Equal("-01:15", remainingStr);
    }

    /// <summary>
    /// WithCurrentを呼び出した場合、総時間を維持したまま新しい現在位置のTimePositionインスタンスが返されるかを検証します。
    /// </summary>
    [Fact]
    public void WithCurrent_現在位置の更新_新しいTimePositionインスタンスを返すこと()
    {
        // Arrange
        var sut = new TimePosition(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(200));

        // Act
        var updated = sut.WithCurrent(TimeSpan.FromSeconds(90));

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(90), updated.Current);
        Assert.Equal(TimeSpan.FromSeconds(200), updated.Total);
    }
}
