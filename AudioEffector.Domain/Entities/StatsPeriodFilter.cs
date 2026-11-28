namespace AudioEffector.Domain.Entities;

/// <summary>
/// ライブラリ全体のリスニング統計集計に用いる期間フィルタ種別を表します
/// </summary>
public enum StatsPeriodFilter
{
    /// <summary>今週（月曜日 00:00 から日曜日の終了まで）</summary>
    ThisWeek,

    /// <summary>今月（当月 1 日 00:00 から月末まで）</summary>
    ThisMonth,

    /// <summary>今年（当年 1 月 1 日 00:00 から年末まで）</summary>
    ThisYear,

    /// <summary>全期間（期間制限なし）</summary>
    AllTime,
}
