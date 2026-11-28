using System;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// 楽曲別の累計リスニング統計および日時毎の再生履歴（ListeningStats.json）を永続化・読み込みするためのリポジトリインターフェース
/// </summary>
public interface IListeningStatsRepository
{
    /// <summary>
    /// 永続化されているリスニング統計データ全体を読み込みます
    /// </summary>
    /// <returns>リスニング統計データのスナップショット</returns>
    ListeningStatsData Load();

    /// <summary>
    /// リスニング統計データ全体を永続化します
    /// </summary>
    /// <param name="data">保存対象のリスニング統計データ</param>
    void Save(ListeningStatsData data);

    /// <summary>
    /// ファイルロック取得中に最新のリスニング統計データへ変更を適用し、原子的に永続化します
    /// </summary>
    /// <param name="update">最新データへ適用する更新処理</param>
    /// <returns>更新後のリスニング統計データのスナップショット</returns>
    ListeningStatsData Update(Action<ListeningStatsData> update);
}
