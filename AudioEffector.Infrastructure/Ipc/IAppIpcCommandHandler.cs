namespace AudioEffector.Infrastructure.Ipc;

/// <summary>
/// <see cref="AppIpcServer"/> が受信した Named Pipe IPC コマンドを処理するハンドラインターフェースです。
/// </summary>
public interface IAppIpcCommandHandler
{
    /// <summary>
    /// 指定された IPC リクエストを非同期で処理し、応答を返します。
    /// </summary>
    /// <param name="request">受信した IPC リクエスト。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>処理結果を表す <see cref="AppIpcResponse"/>。</returns>
    Task<AppIpcResponse> HandleAsync(AppIpcRequest request, CancellationToken cancellationToken = default);
}
