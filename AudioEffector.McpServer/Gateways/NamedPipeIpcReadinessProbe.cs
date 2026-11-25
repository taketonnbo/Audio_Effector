using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AudioEffector.Infrastructure.Ipc;

namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// Named Pipe IPC を用いて Audio Effector（WPF 本体）の待受準備完了を確認するプローブ実装です。
/// </summary>
public sealed class NamedPipeIpcReadinessProbe : IIpcReadinessProbe
{
    /// <summary>
    /// 1 回の IPC 疎通確認に適用する既定の接続・応答タイムアウト時間。
    /// </summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromMilliseconds(300);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly string PingRequestJson = $$"""{"command":"{{AppIpcServer.COMMAND_PING}}"}""";

    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    /// <summary>
    /// 既定のユーザー専用パイプ名で <see cref="NamedPipeIpcReadinessProbe"/> の新しいインスタンスを初期化します。
    /// </summary>
    public NamedPipeIpcReadinessProbe()
        : this(IpcPipeNameResolver.GetDefaultPipeName(), DefaultConnectTimeout)
    {
    }

    /// <summary>
    /// パイプ名とタイムアウトを指定して <see cref="NamedPipeIpcReadinessProbe"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="pipeName">接続先の Named Pipe 名。</param>
    /// <param name="connectTimeout">接続および応答待機タイムアウト時間。</param>
    public NamedPipeIpcReadinessProbe(string pipeName, TimeSpan? connectTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        _pipeName = pipeName;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
    }

    /// <inheritdoc />
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(client, Utf8NoBom, bufferSize: 512, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 512, leaveOpen: true);

            await writer.WriteLineAsync(PingRequestJson.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            string? responseLine = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return false;
            }

            using JsonDocument doc = JsonDocument.Parse(responseLine);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("ok", out JsonElement okElement)
                && okElement.ValueKind == JsonValueKind.True;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // プローブ自身の短タイムアウトは「未準備」として扱う
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
