using System;
using System.Threading;
using System.Threading.Tasks;

namespace AudioEffector.Application.Common;

/// <summary>
/// プロセス間ファイルロック（.lock）・原子的書き込み・更新メタデータ監視を抽象化するインターフェース
/// </summary>
public interface IJsonFileLockHelper
{
    /// <summary>
    /// 指定ファイルに対応するロックファイル（.lock）を排他取得します（非同期）
    /// </summary>
    /// <param name="filePath">保護対象のデータファイルパス</param>
    /// <param name="timeout">待機タイムアウト時間（未指定時は既定値を使用）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>解放時にロックを解除するハンドル</returns>
    Task<IDisposable> AcquireLockAsync(
        string filePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定ファイルに対応するロックファイル（.lock）を排他取得します（同期）
    /// </summary>
    /// <param name="filePath">保護対象のデータファイルパス</param>
    /// <param name="timeout">待機タイムアウト時間（未指定時は既定値を使用）</param>
    /// <returns>解放時にロックを解除するハンドル</returns>
    IDisposable AcquireLock(string filePath, TimeSpan? timeout = null);

    /// <summary>
    /// 一意な一時ファイル経由で対象ファイルへ原子的に文字列を書き込みます（非同期）
    /// </summary>
    /// <param name="filePath">書き込み先ファイルパス</param>
    /// <param name="content">書き込むテキスト内容</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task WriteAllTextAtomicAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一意な一時ファイル経由で対象ファイルへ原子的に文字列を書き込みます（同期）
    /// </summary>
    /// <param name="filePath">書き込み先ファイルパス</param>
    /// <param name="content">書き込むテキスト内容</param>
    void WriteAllTextAtomic(string filePath, string content);

    /// <summary>
    /// 指定ファイルの現在メタデータ（存在有無・最終更新日時・サイズ）を取得します
    /// </summary>
    /// <param name="filePath">対象ファイルパス</param>
    /// <returns>ファイルメタデータのスナップショット</returns>
    FileMetadataSnapshot GetFileMetadata(string filePath);
}
