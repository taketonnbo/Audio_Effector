using System;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// リスニング統計の集計対象期間（開始日時含む・終了日時含まない半開区間）を表す不変モデル
/// 既定の期間フィルタだけでなく、将来の特定週・月・年単位ランキング向けの任意期間指定にも対応します
/// </summary>
public sealed record StatsDateRange
{
    /// <summary>
    /// 期間制限なし（全期間）を表す共有インスタンス
    /// </summary>
    public static StatsDateRange AllTime { get; } = new(null, null);

    /// <summary>
    /// 集計期間の開始日時（含む）。未指定（null）の場合は下限なしを表します
    /// </summary>
    public DateTime? StartInclusive { get; }

    /// <summary>
    /// 集計期間の終了日時（含まない）。未指定（null）の場合は上限なしを表します
    /// </summary>
    public DateTime? EndExclusive { get; }

    /// <summary>
    /// 開始日時・終了日時の両方が未指定（全期間）かどうかを取得します
    /// </summary>
    public bool IsUnbounded => !StartInclusive.HasValue && !EndExclusive.HasValue;

    /// <summary>
    /// <see cref="StatsDateRange"/> を初期化します
    /// </summary>
    /// <param name="startInclusive">開始日時（含む、null は下限なし）</param>
    /// <param name="endExclusive">終了日時（含まない、null は上限なし）</param>
    public StatsDateRange(DateTime? startInclusive, DateTime? endExclusive)
    {
        if (startInclusive.HasValue && endExclusive.HasValue && endExclusive.Value < startInclusive.Value)
        {
            throw new ArgumentException("EndExclusive must be greater than or equal to StartInclusive.", nameof(endExclusive));
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    /// <summary>
    /// 指定された日時がこの期間範囲内に含まれるかどうかを判定します
    /// </summary>
    /// <param name="timestamp">判定対象の日時</param>
    /// <returns>範囲内に含まれる場合は true</returns>
    public bool Contains(DateTime timestamp)
    {
        if (StartInclusive.HasValue && timestamp < StartInclusive.Value)
        {
            return false;
        }

        if (EndExclusive.HasValue && timestamp >= EndExclusive.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 期間フィルタ種別と基準日時から集計期間を生成します
    /// </summary>
    /// <param name="filter">期間フィルタ種別</param>
    /// <param name="referenceNow">基準となる現在日時（ローカル時刻）</param>
    /// <returns>算出された <see cref="StatsDateRange"/></returns>
    public static StatsDateRange FromPeriodFilter(StatsPeriodFilter filter, DateTime referenceNow)
    {
        return filter switch
        {
            StatsPeriodFilter.ThisWeek => ForWeekContaining(referenceNow),
            StatsPeriodFilter.ThisMonth => ForSpecificMonth(referenceNow.Year, referenceNow.Month),
            StatsPeriodFilter.ThisYear => ForSpecificYear(referenceNow.Year),
            StatsPeriodFilter.AllTime => AllTime,
            _ => AllTime,
        };
    }

    /// <summary>
    /// 指定日時を含む週（月曜日 00:00〜翌週月曜日 00:00）の集計期間を生成します
    /// </summary>
    /// <param name="dateInWeek">対象週内の任意の日時</param>
    /// <returns>対象週を表す <see cref="StatsDateRange"/></returns>
    public static StatsDateRange ForWeekContaining(DateTime dateInWeek)
    {
        int daysSinceMonday = ((int)dateInWeek.DayOfWeek + 6) % 7;
        DateTime weekStart = dateInWeek.Date.AddDays(-daysSinceMonday);
        return new StatsDateRange(weekStart, weekStart.AddDays(7));
    }

    /// <summary>
    /// 指定された年月の集計期間を生成します
    /// </summary>
    /// <param name="year">西暦年（1〜9999）</param>
    /// <param name="month">月（1〜12）</param>
    /// <returns>指定年月を表す <see cref="StatsDateRange"/></returns>
    public static StatsDateRange ForSpecificMonth(int year, int month)
    {
        var monthStart = new DateTime(year, month, 1);
        return new StatsDateRange(monthStart, monthStart.AddMonths(1));
    }

    /// <summary>
    /// 指定された年の集計期間を生成します
    /// </summary>
    /// <param name="year">西暦年（1〜9999）</param>
    /// <returns>指定年を表す <see cref="StatsDateRange"/></returns>
    public static StatsDateRange ForSpecificYear(int year)
    {
        var yearStart = new DateTime(year, 1, 1);
        return new StatsDateRange(yearStart, yearStart.AddYears(1));
    }

    /// <summary>
    /// 任意の開始日時（含む）と終了日時（含まない）から集計期間を生成します
    /// </summary>
    /// <param name="startInclusive">開始日時（含む）</param>
    /// <param name="endExclusive">終了日時（含まない）</param>
    /// <returns>生成された <see cref="StatsDateRange"/></returns>
    public static StatsDateRange ForDateRange(DateTime startInclusive, DateTime endExclusive)
    {
        return new StatsDateRange(startInclusive, endExclusive);
    }
}
