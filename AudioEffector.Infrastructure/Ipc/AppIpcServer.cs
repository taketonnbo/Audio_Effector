using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NLog;

namespace AudioEffector.Infrastructure.Ipc;

/// <summary>
/// Audio Effector（WPF 本体）側で MCP サーバからの Named Pipe IPC 接続および疎通確認を受け付ける軽量サーバです。
/// </summary>
public sealed class AppIpcServer : IDisposable
{
    /// <summary>
    /// 疎通確認（ping）コマンド名。
    /// </summary>
    public const string COMMAND_PING = "ping";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 既定のユーザー専用パイプ名で <see cref="AppIpcServer"/> の新しいインスタンスを初期化します。
    /// </summary>
    public AppIpcServer()
        : this(IpcPipeNameResolver.GetDefaultPipeName())
    {
    }

    /// <summary>
    /// 指定されたパイプ名で <see cref="AppIpcServer"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="pipeName">待受に使用する Named Pipe 名。</param>
    public AppIpcServer(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    /// <summary>
    /// 待受に使用している Named Pipe 名を取得します。
    /// </summary>
    public string PipeName => _pipeName;

    /// <summary>
    /// バックグラウンドでの Named Pipe 待受を開始します。
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        Logger.Info(CultureInfo.InvariantCulture, "Named Pipe IPC サーバの待受を開始しました（PipeName: {0}）。", _pipeName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        if (_listenTask is not null)
        {
            try
            {
                _listenTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // キャンセル終了時の集約例外は無視する
            }
            catch (OperationCanceledException)
            {
                // キャンセル終了は無視する
            }
        }

        _cts.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
#pragma warning disable CA2000 // 接続成功時は HandleClientAsync へ所有権を移譲し、例外時は catch で Dispose する
                serverStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#pragma warning restore CA2000

                await serverStream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                NamedPipeServerStream connectedStream = serverStream;
                serverStream = null;
                _ = Task.Run(() => HandleClientAsync(connectedStream, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                serverStream?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                serverStream?.Dispose();
                Logger.Warn(ex, "Named Pipe IPC サーバの待受中にエラーが発生しました。");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeoutCts.CancelAfter(ClientReadTimeout);

                using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                using var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

                string? requestLine = await reader.ReadLineAsync(readTimeoutCts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                string responseJson = ProcessRequestLine(requestLine);
                await writer.WriteLineAsync(responseJson.AsMemory(), readTimeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // クライアントタイムアウトまたはサーバ停止時は静かに終了する
            }
            catch (IOException)
            {
                // クライアント側切断時は静かに終了する
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Named Pipe IPC クライアント処理中に予期しないエラーが発生しました。");
            }
        }
    }

    private static string ProcessRequestLine(string requestLine)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(requestLine);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("command", out JsonElement commandElement)
                && string.Equals(commandElement.GetString(), COMMAND_PING, StringComparison.OrdinalIgnoreCase))
            {
                return """{"ok":true}""";
            }

            return """{"ok":false,"error":"unsupported_command"}""";
        }
        catch (JsonException)
        {
            return """{"ok":false,"error":"invalid_json"}""";
        }
    }
}
