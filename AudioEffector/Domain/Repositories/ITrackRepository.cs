using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// 楽曲（トラック）の永続化および検索アクセスを担当するリポジトリインターフェース
/// </summary>
public interface ITrackRepository
{
    /// <summary>
    /// 指定されたIDのトラックを非同期で取得します
    /// </summary>
    /// <param name="id">トラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トラックエンティティ（存在しない場合はnull）</returns>
    Task<Track?> GetByIdAsync(TrackId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたファイルパスのトラックを非同期で取得します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トラックエンティティ（存在しない場合はnull）</returns>
    Task<Track?> GetByPathAsync(AudioPath filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登録されているすべてのトラックを非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>全トラックの読み取り専用コレクション</returns>
    Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード（タイトル・アーティスト・アルバム）でトラックを検索します
    /// </summary>
    /// <param name="keyword">検索キーワード</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>一致したトラックのコレクション</returns>
    Task<IReadOnlyList<Track>> SearchAsync(string keyword, CancellationToken cancellationToken = default);

    /// <summary>
    /// トラック情報を保存または更新します
    /// </summary>
    /// <param name="track">保存対象のトラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SaveAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数のトラック情報を一括で保存または更新します
    /// </summary>
    /// <param name="tracks">保存対象のトラックコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SaveRangeAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたIDのトラックを削除します
    /// </summary>
    /// <param name="id">削除対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task DeleteAsync(TrackId id, CancellationToken cancellationToken = default);
}
