namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// Audio Effector（WPF 本体）の IPC 接続待受が準備完了しているかを判定するプローブインターフェースです。
/// </summary>
public interface IIpcReadinessProbe
{
    /// <summary>
    /// Audio Effector 本体の IPC 待受へ接続・疎通確認できるかどうかを非同期に判定します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>IPC 接続可能状態であれば <see langword="true"/>、未準備または接続不能であれば <see langword="false"/>。</returns>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
