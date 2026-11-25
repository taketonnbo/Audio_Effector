using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.Configuration;

/// <summary>
/// MCP サーバが使用する通信トランスポート種別を表します。
/// </summary>
public enum McpTransportMode
{
    /// <summary>
    /// 標準入出力（stdio）を用いた JSON-RPC 2.0 通信（Phase 1 標準）。
    /// </summary>
    Stdio,

    /// <summary>
    /// Streamable HTTP を用いた通信（Phase 3 拡張予定）。
    /// </summary>
    Http,
}

/// <summary>
/// MCP サーバの起動オプションを保持する設定モデルです。
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// 環境変数プレフィックス（トランスポート指定）。
    /// </summary>
    public const string ENV_TRANSPORT = "AUDIOEFFECTOR_MCP__TRANSPORT";

    /// <summary>
    /// 環境変数名（HTTP バインドホスト指定）。
    /// </summary>
    public const string ENV_HOST = "AUDIOEFFECTOR_MCP__HOST";

    /// <summary>
    /// 環境変数名（HTTP 待受ポート指定）。
    /// </summary>
    public const string ENV_PORT = "AUDIOEFFECTOR_MCP__PORT";

    /// <summary>
    /// 環境変数名（JSON データディレクトリ指定）。
    /// </summary>
    public const string ENV_DATA_DIRECTORY = "AUDIOEFFECTOR_MCP__DATADIRECTORY";

    /// <summary>
    /// 環境変数名（最小ログレベル指定）。
    /// </summary>
    public const string ENV_MIN_LOG_LEVEL = "AUDIOEFFECTOR_MCP__MINLOGLEVEL";

    /// <summary>
    /// 環境変数名（Audio Effector WPF 本体実行ファイルパス指定）。
    /// </summary>
    public const string ENV_WPF_APP_PATH = "AUDIOEFFECTOR_MCP__WPFAPPPATH";

    /// <summary>
    /// 既定の HTTP バインドアドレス。
    /// </summary>
    public const string DEFAULT_HOST = "127.0.0.1";

    /// <summary>
    /// 既定の HTTP 待受ポート番号。
    /// </summary>
    public const int DEFAULT_PORT = 51890;

    /// <summary>
    /// 使用する通信トランスポートを取得または設定します（既定値: <see cref="McpTransportMode.Stdio"/>）。
    /// </summary>
    public McpTransportMode Transport { get; init; } = McpTransportMode.Stdio;

    /// <summary>
    /// HTTP モード時のバインドアドレスを取得または設定します（既定値: <c>127.0.0.1</c>）。
    /// </summary>
    public string Host { get; init; } = DEFAULT_HOST;

    /// <summary>
    /// HTTP モード時の待受ポート番号を取得または設定します（既定値: <c>51890</c>）。
    /// </summary>
    public int Port { get; init; } = DEFAULT_PORT;

    /// <summary>
    /// JSON データディレクトリの明示パスを取得または設定します（未指定時は <see langword="null"/>）。
    /// </summary>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// <c>stderr</c> へ出力する最小ログレベルを取得または設定します（既定値: <see cref="LogLevel.Information"/>）。
    /// </summary>
    public LogLevel MinLogLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// ヘッドレス再生を許可するかどうかを取得または設定します（Phase 2 以降の拡張用、既定値: <see langword="false"/>）。
    /// </summary>
    public bool AllowHeadlessPlayback { get; init; }

    /// <summary>
    /// Audio Effector（WPF 本体）実行ファイルの明示パスを取得または設定します（未指定時は <see langword="null"/>）。
    /// </summary>
    public string? WpfAppPath { get; init; }

    /// <summary>
    /// CLI 引数と環境変数から <see cref="McpServerOptions"/> を解析します。
    /// CLI 引数が環境変数より優先されます。
    /// </summary>
    /// <param name="args">コマンドライン引数配列。</param>
    /// <param name="options">解析に成功した場合に生成されたオプション。</param>
    /// <param name="errorMessage">解析に失敗した場合のエラーメッセージ。</param>
    /// <param name="getEnvironmentVariable">環境変数取得デリゲート（未指定時は <see cref="Environment.GetEnvironmentVariable(string)"/>）。</param>
    /// <returns>解析に成功した場合は <see langword="true"/>、不正な引数または値がある場合は <see langword="false"/>。</returns>
    public static bool TryParse(
        string[]? args,
        out McpServerOptions? options,
        out string? errorMessage,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        options = null;
        errorMessage = null;

        var envGetter = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        string? transportRaw = envGetter(ENV_TRANSPORT);
        string? hostRaw = envGetter(ENV_HOST);
        string? portRaw = envGetter(ENV_PORT);
        string? dataDirectoryRaw = envGetter(ENV_DATA_DIRECTORY);
        string? minLogLevelRaw = envGetter(ENV_MIN_LOG_LEVEL);
        string? wpfAppPathRaw = envGetter(ENV_WPF_APP_PATH);

        if (args is { Length: > 0 })
        {
            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i];
                if (string.IsNullOrWhiteSpace(current))
                {
                    continue;
                }

                string key;
                string? value;

                int equalsIndex = current.IndexOf('=', StringComparison.Ordinal);
                if (equalsIndex >= 0)
                {
                    key = current[..equalsIndex];
                    value = current[(equalsIndex + 1)..];
                }
                else
                {
                    key = current;
                    if (!IsKnownOption(key))
                    {
                        errorMessage = $"不明なオプションが指定されました: {key}";
                        return false;
                    }

                    if (i + 1 >= args.Length || IsKnownOptionToken(args[i + 1]))
                    {
                        errorMessage = $"オプション '{key}' に値が指定されていません。";
                        return false;
                    }

                    value = args[++i];
                }

                switch (key.ToLowerInvariant())
                {
                    case "--transport":
                        transportRaw = value;
                        break;
                    case "--host":
                        hostRaw = value;
                        break;
                    case "--port":
                        portRaw = value;
                        break;
                    case "--data-dir":
                        dataDirectoryRaw = value;
                        break;
                    case "--log-level":
                        minLogLevelRaw = value;
                        break;
                    case "--wpf-app-path":
                        wpfAppPathRaw = value;
                        break;
                    default:
                        errorMessage = $"不明なオプションが指定されました: {key}";
                        return false;
                }
            }
        }

        // Transport の解析
        McpTransportMode transport = McpTransportMode.Stdio;
        if (!string.IsNullOrWhiteSpace(transportRaw))
        {
            if (string.Equals(transportRaw.Trim(), "stdio", StringComparison.OrdinalIgnoreCase))
            {
                transport = McpTransportMode.Stdio;
            }
            else if (string.Equals(transportRaw.Trim(), "http", StringComparison.OrdinalIgnoreCase))
            {
                transport = McpTransportMode.Http;
            }
            else
            {
                errorMessage = $"--transport の値が不正です: '{transportRaw}'。'stdio' または 'http' を指定してください。";
                return false;
            }
        }

        // Host の解析
        string host = string.IsNullOrWhiteSpace(hostRaw) ? DEFAULT_HOST : hostRaw.Trim();

        // Port の解析
        int port = DEFAULT_PORT;
        if (!string.IsNullOrWhiteSpace(portRaw))
        {
            if (!int.TryParse(portRaw.Trim(), out port) || port < 1 || port > 65535)
            {
                errorMessage = $"--port の値が不正です: '{portRaw}'。1〜65535 の整数を指定してください。";
                return false;
            }
        }

        // DataDirectory の解析
        string? dataDirectory = string.IsNullOrWhiteSpace(dataDirectoryRaw) ? null : dataDirectoryRaw.Trim();

        // MinLogLevel の解析
        LogLevel minLogLevel = LogLevel.Information;
        if (!string.IsNullOrWhiteSpace(minLogLevelRaw))
        {
            if (!Enum.TryParse<LogLevel>(minLogLevelRaw.Trim(), ignoreCase: true, out minLogLevel)
                || !Enum.IsDefined(minLogLevel))
            {
                errorMessage = $"--log-level の値が不正です: '{minLogLevelRaw}'。Trace/Debug/Information/Warning/Error/Critical/None のいずれかを指定してください。";
                return false;
            }
        }

        // WpfAppPath の解析
        string? wpfAppPath = string.IsNullOrWhiteSpace(wpfAppPathRaw) ? null : wpfAppPathRaw.Trim();

        options = new McpServerOptions
        {
            Transport = transport,
            Host = host,
            Port = port,
            DataDirectory = dataDirectory,
            MinLogLevel = minLogLevel,
            WpfAppPath = wpfAppPath,
        };

        return true;
    }

    private static bool IsKnownOption(string key)
    {
        return key.ToLowerInvariant() is "--transport" or "--host" or "--port" or "--data-dir" or "--log-level" or "--wpf-app-path";
    }

    private static bool IsKnownOptionToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
        string candidateKey = equalsIndex >= 0 ? token[..equalsIndex] : token;
        return IsKnownOption(candidateKey);
    }
}
