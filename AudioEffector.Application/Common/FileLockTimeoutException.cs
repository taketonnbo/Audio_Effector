using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// プロセス間ファイルロック（.lock）の取得が制限時間内に完了しなかった場合に送出される例外
/// </summary>
public sealed class FileLockTimeoutException : TimeoutException
{
    /// <summary>
    /// ロック対象ファイルパスとタイムアウト時間を指定して例外を初期化します
    /// </summary>
    /// <param name="filePath">ロック対象ファイルパス</param>
    /// <param name="timeout">設定されていた待機タイムアウト時間</param>
    /// <param name="innerException">直前に発生した内部例外</param>
    public FileLockTimeoutException(string filePath, TimeSpan timeout, Exception? innerException = null)
        : base($"ファイルロックの取得がタイムアウトしました（対象: '{filePath}', タイムアウト: {timeout.TotalMilliseconds:F0}ms）。", innerException)
    {
        FilePath = filePath;
        Timeout = timeout;
    }

    /// <summary>
    /// ロック取得に失敗した対象ファイルパスを取得します
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 設定されていた待機タイムアウト時間を取得します
    /// </summary>
    public TimeSpan Timeout { get; }
}
