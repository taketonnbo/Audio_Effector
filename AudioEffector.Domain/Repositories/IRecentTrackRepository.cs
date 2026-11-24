using System;
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

    /// <summary>ロック中に最新の履歴へ変更を適用し保存します</summary>
    /// <param name="update">追加、削除または全消去の処理</param>
    /// <returns>更新後の履歴一覧</returns>
    IReadOnlyList<RecentTrackEntry> Update(Action<List<RecentTrackEntry>> update);
}
