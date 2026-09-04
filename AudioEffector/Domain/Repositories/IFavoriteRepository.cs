using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// お気に入り楽曲IDコレクションの永続化を担当するリポジトリインターフェース
/// </summary>
public interface IFavoriteRepository
{
    /// <summary>
    /// お気に入りに登録されているすべてのトラックIDを非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>お気に入りトラックIDの読み取り専用セット</returns>
    Task<IReadOnlySet<TrackId>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたトラックIDをお気に入りに追加します
    /// </summary>
    /// <param name="trackId">追加対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task AddAsync(TrackId trackId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたトラックIDをお気に入りから削除します
    /// </summary>
    /// <param name="trackId">削除対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task RemoveAsync(TrackId trackId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたトラックIDがお気に入りに登録されているかを確認します
    /// </summary>
    /// <param name="trackId">確認対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>登録されている場合はtrue、それ以外はfalse</returns>
    Task<bool> ContainsAsync(TrackId trackId, CancellationToken cancellationToken = default);
}
