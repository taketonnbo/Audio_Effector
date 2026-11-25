namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// Audio Effector（WPF 本体）の起動および準備完了待機を抽象化するゲートウェイインターフェースです。
/// </summary>
public interface IAppLauncher
{
    /// <summary>
    /// Audio Effector（WPF 本体）を指定された起動モードで起動します。
    /// すでに起動済みの場合は新規プロセスを起動せず、現在のウィンドウ状態を維持したまま結果を返します。
    /// </summary>
    /// <param name="mode">起動モード。</param>
    /// <param name="waitForReady">起動後に WPF 本体の準備完了まで待機するかどうか。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>起動結果。</returns>
    Task<AppLaunchResult> LaunchAsync(
        AppLaunchMode mode,
        bool waitForReady,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Audio Effector（WPF 本体）の起動モードを表します。
/// </summary>
public enum AppLaunchMode
{
    /// <summary>
    /// ミニプレイヤー状態で起動します（<c>--mini-player</c>）。
    /// </summary>
    MiniPlayer,

    /// <summary>
    /// 通常のメインウィンドウ状態で起動します。
    /// </summary>
    Normal,
}

/// <summary>
/// Audio Effector（WPF 本体）の起動処理結果を表します。
/// </summary>
/// <param name="Launched">今回新規にプロセスを起動した場合は <see langword="true"/>。</param>
/// <param name="AlreadyRunning">すでに起動済みだった場合は <see langword="true"/>。</param>
/// <param name="Mode">実際に適用された起動モード（<c>"mini_player"</c> / <c>"normal"</c> / <c>"existing"</c>）。</param>
/// <param name="IpcReady">IPC 接続可能状態が確認できた場合は <see langword="true"/>。</param>
public sealed record AppLaunchResult(
    bool Launched,
    bool AlreadyRunning,
    string Mode,
    bool IpcReady);
