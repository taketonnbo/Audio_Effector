using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// ユーザープレイリストの永続化および取得アクセスを担当するリポジトリインターフェース
/// </summary>
public interface IPlaylistRepository
{
    /// <summary>
    /// 指定されたIDのプレイリストを非同期で取得します
    /// </summary>
    /// <param name="id">プレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プレイリストエンティティ（存在しない場合はnull）</returns>
    Task<UserPlaylist?> GetByIdAsync(PlaylistId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// すべてのプレイリストを非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プレイリストの読み取り専用コレクション</returns>
    Task<IReadOnlyList<UserPlaylist>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// プレイリストを保存または更新します
    /// </summary>
    /// <param name="playlist">保存対象のプレイリスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SaveAsync(UserPlaylist playlist, CancellationToken cancellationToken = default);

    /// <summary>
    /// すべてのプレイリストを指定された一覧で原子的に置き換えます
    /// </summary>
    /// <param name="playlists">保存対象のプレイリスト一覧</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task ReplaceAllAsync(IReadOnlyList<UserPlaylist> playlists, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたIDのプレイリストを削除します
    /// </summary>
    /// <param name="id">削除対象のプレイリストID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task DeleteAsync(PlaylistId id, CancellationToken cancellationToken = default);

    /// <summary>最新のプレイリストに変更を適用し、変更があれば保存します</summary>
    /// <param name="id">対象のプレイリストID</param>
    /// <param name="update">変更の有無を返す更新処理</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新後のプレイリスト（存在しない場合はnull）</returns>
    Task<UserPlaylist?> UpdateAsync(PlaylistId id, Func<UserPlaylist, bool> update, CancellationToken cancellationToken = default);
}
