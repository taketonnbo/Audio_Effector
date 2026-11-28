using System;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Tests.Entities;

/// <summary>
/// <see cref="StatsDateRange"/> およびライブラリ全体統計ドメインモデルの境界値・期間判定を検証するテストクラス
/// </summary>
public sealed class LibraryStatsModelsTests
{
    /// <summary>
    /// ThisWeek 指定時に基準日時を含む月曜日 00:00 から翌週月曜日 00:00 未満の範囲が生成されることを検証します。
    /// </summary>
    [Fact]
    public void FromPeriodFilter_今週指定時_月曜開始から翌週月曜未満の範囲を生成すること()
    {
        // Arrange（2026/05/13 は水曜日）
        var referenceNow = new DateTime(2026, 5, 13, 15, 30, 0);

        // Act
        StatsDateRange sut = StatsDateRange.FromPeriodFilter(StatsPeriodFilter.ThisWeek, referenceNow);

        // Assert
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0), sut.StartInclusive);
        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0), sut.EndExclusive);
        Assert.True(sut.Contains(new DateTime(2026, 5, 11, 0, 0, 0)));
        Assert.True(sut.Contains(new DateTime(2026, 5, 17, 23, 59, 59)));
        Assert.False(sut.Contains(new DateTime(2026, 5, 10, 23, 59, 59)));
        Assert.False(sut.Contains(new DateTime(2026, 5, 18, 0, 0, 0)));
    }

    /// <summary>
    /// 基準日時が日曜日の場合でも ThisWeek が直前の月曜日を週開始として扱うことを検証します。
    /// </summary>
    [Fact]
    public void FromPeriodFilter_日曜日に今週指定時_直前の月曜開始の範囲を生成すること()
    {
        // Arrange（2026/05/17 は日曜日）
        var sunday = new DateTime(2026, 5, 17, 20, 0, 0);

        // Act
        StatsDateRange sut = StatsDateRange.FromPeriodFilter(StatsPeriodFilter.ThisWeek, sunday);

        // Assert
        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0), sut.StartInclusive);
        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0), sut.EndExclusive);
    }

    /// <summary>
    /// ThisMonth / ThisYear / AllTime の各フィルタで期待通りの半開区間が生成されることを検証します。
    /// </summary>
    [Fact]
    public void FromPeriodFilter_今月今年全期間指定時_各期間の半開区間を生成すること()
    {
        // Arrange
        var referenceNow = new DateTime(2026, 5, 13, 10, 0, 0);

        // Act
        StatsDateRange monthRange = StatsDateRange.FromPeriodFilter(StatsPeriodFilter.ThisMonth, referenceNow);
        StatsDateRange yearRange = StatsDateRange.FromPeriodFilter(StatsPeriodFilter.ThisYear, referenceNow);
        StatsDateRange allTimeRange = StatsDateRange.FromPeriodFilter(StatsPeriodFilter.AllTime, referenceNow);

        // Assert
        Assert.Equal(new DateTime(2026, 5, 1), monthRange.StartInclusive);
        Assert.Equal(new DateTime(2026, 6, 1), monthRange.EndExclusive);
        Assert.Equal(new DateTime(2026, 1, 1), yearRange.StartInclusive);
        Assert.Equal(new DateTime(2027, 1, 1), yearRange.EndExclusive);
        Assert.True(allTimeRange.IsUnbounded);
        Assert.True(allTimeRange.Contains(new DateTime(2000, 1, 1)));
    }

    /// <summary>
    /// 終了日時が開始日時より前の場合に例外がスローされることを検証します。
    /// </summary>
    [Fact]
    public void StatsDateRange_終了日時が開始日時より前の場合_ArgumentExceptionをスローすること()
    {
        // Arrange
        var start = new DateTime(2026, 5, 10);
        var end = new DateTime(2026, 5, 9);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => StatsDateRange.ForDateRange(start, end));
    }

    /// <summary>
    /// 特定年月・特定年ファクトリが将来の期間拡張向けに正確な半開区間を返すことを検証します。
    /// </summary>
    [Fact]
    public void ForSpecificMonthAndYear_任意の年月指定時_対象月および年の半開区間を返すこと()
    {
        // Arrange & Act
        StatsDateRange feb2024 = StatsDateRange.ForSpecificMonth(2024, 2);
        StatsDateRange year2025 = StatsDateRange.ForSpecificYear(2025);

        // Assert
        Assert.Equal(new DateTime(2024, 2, 1), feb2024.StartInclusive);
        Assert.Equal(new DateTime(2024, 3, 1), feb2024.EndExclusive);
        Assert.True(feb2024.Contains(new DateTime(2024, 2, 29, 23, 59, 59)));
        Assert.Equal(new DateTime(2025, 1, 1), year2025.StartInclusive);
        Assert.Equal(new DateTime(2026, 1, 1), year2025.EndExclusive);
    }
}
