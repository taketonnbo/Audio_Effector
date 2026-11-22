using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// お気に入り楽曲エントリコレクションの永続化を担当するリポジトリインターフェース
/// </summary>
public interface IFavoriteRepository
{
    /// <summary>
    /// お気に入りに登録されているすべての楽曲エントリを非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>お気に入り楽曲エントリの読み取り専用リスト</returns>
    Task<IReadOnlyList<FavoriteTrackEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたお気に入りエントリを追加または更新します（重複登録は抑止されます）
    /// </summary>
    /// <param name="entry">追加または更新対象のお気に入りエントリ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task AddOrUpdateAsync(FavoriteTrackEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたトラックに一致するお気に入りエントリを削除します
    /// </summary>
    /// <param name="track">削除対象のトラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task RemoveAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>
    /// お気に入りエントリ一覧全体を上書き保存します
    /// </summary>
    /// <param name="entries">保存するお気に入りエントリ一覧</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SaveAllAsync(IReadOnlyList<FavoriteTrackEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたトラックがお気に入りに登録されているかを確認します
    /// </summary>
    /// <param name="track">確認対象のトラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>登録されている場合はtrue、それ以外はfalse</returns>
    Task<bool> ContainsAsync(Track track, CancellationToken cancellationToken = default);
}
