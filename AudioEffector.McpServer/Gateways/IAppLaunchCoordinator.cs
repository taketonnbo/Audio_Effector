namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// 同一 Windows セッション内の複数 MCP サーバプロセス間で WPF 本体起動を調停するインターフェースです。
/// </summary>
public interface IAppLaunchCoordinator
{
    /// <summary>
    /// セッション内で排他的な起動調停ロックを取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>ロック解放用のハンドル。</returns>
    Task<IDisposable> AcquireLaunchLockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// セッション内で現在起動進行中（生存中）として記録されているプロセス情報を取得します。
    /// 記録が存在しないか、すでに終了済みの場合は <see langword="null"/> を返します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>生存中の起動プロセス情報。存在しない場合は <see langword="null"/>。</returns>
    Task<LaunchedProcessIdentity?> TryGetActiveLaunchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新規起動したプロセス情報をセッション共有状態へ記録します。
    /// </summary>
    /// <param name="processIdentity">起動したプロセスの識別情報。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>非同期タスク。</returns>
    Task RecordLaunchedProcessAsync(LaunchedProcessIdentity processIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 記録されている起動中プロセス情報をクリアします。
    /// </summary>
    /// <param name="expectedProcess">指定された場合、記録内容と一致するときのみクリアします。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>非同期タスク。</returns>
    Task ClearLaunchedProcessAsync(LaunchedProcessIdentity? expectedProcess = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された起動プロセスが現在も生存しているかを判定します。
    /// </summary>
    /// <param name="processIdentity">確認対象のプロセス識別情報。</param>
    /// <returns>同一プロセスが生存中であれば <see langword="true"/>。</returns>
    bool IsProcessAlive(LaunchedProcessIdentity processIdentity);
}

/// <summary>
/// 起動した WPF 本体プロセスを他 MCP プロセスから安全に識別するための情報です。
/// </summary>
/// <param name="ProcessId">OS プロセス ID。</param>
/// <param name="StartTimeUtcTicks">プロセス開始時刻（UTC Ticks、PID 再利用誤検知防止用）。</param>
public sealed record LaunchedProcessIdentity(int ProcessId, long StartTimeUtcTicks);
