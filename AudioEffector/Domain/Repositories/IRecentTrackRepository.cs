using System.Collections.Generic;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// 最近再生した曲の履歴を永続化・読み込みするためのリポジトリインターフェース
/// </summary>
public interface IRecentTrackRepository
{
    /// <summary>
    /// 永続化されている再生履歴エントリ一覧を読み込みます
    /// </summary>
    /// <returns>再生履歴エントリ一覧（新しい順）</returns>
    IReadOnlyList<RecentTrackEntry> Load();

    /// <summary>
    /// 再生履歴エントリ一覧を永続化します
    /// </summary>
    /// <param name="entries">保存対象の履歴エントリ一覧</param>
    void Save(IReadOnlyList<RecentTrackEntry> entries);
}
