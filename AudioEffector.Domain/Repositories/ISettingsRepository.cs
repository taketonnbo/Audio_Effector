using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// アプリケーション設定値（キーと値のディクショナリ等）の永続化を担当するリポジトリインターフェース
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// 指定されたキーの設定文字列値を取得します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="defaultValue">デフォルト値</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値文字列</returns>
    Task<string?> GetValueAsync(string key, string? defaultValue = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたキーの設定文字列値を保存します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="value">設定値文字列</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SetValueAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// すべての設定キーと値のディクショナリを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>設定値ディクショナリ</returns>
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたキーの設定を削除します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
