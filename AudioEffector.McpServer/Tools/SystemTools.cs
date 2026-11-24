using System.ComponentModel;
using System.Globalization;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Repositories;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.DependencyInjection;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpAppOptions = AudioEffector.McpServer.Configuration.McpServerOptions;

namespace AudioEffector.McpServer.Tools;

/// <summary>
/// MCP サーバの接続疎通および稼働状態を確認するためのリファレンスツール群です。
/// </summary>
[McpServerToolType]
public sealed class SystemTools
{
    /// <summary>
    /// Phase 1（IPC 未構成時）の WPF 連携状態値。
    /// </summary>
    public const string WPF_IPC_STATUS_NOT_CONFIGURED = "not_configured";

    private readonly McpAppOptions _options;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly ITrackRepository _trackRepository;
    private readonly ILogger<SystemTools> _logger;
    private readonly string _serverVersion;

    /// <summary>
    /// <see cref="SystemTools"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ。</param>
    /// <param name="trackRepository">楽曲リポジトリ。</param>
    /// <param name="logger">診断ロガー。</param>
    public SystemTools(
        McpAppOptions options,
        IDataDirectoryProvider dataDirectoryProvider,
        ITrackRepository trackRepository,
        ILogger<SystemTools> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataDirectoryProvider);
        ArgumentNullException.ThrowIfNull(trackRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _dataDirectoryProvider = dataDirectoryProvider;
        _trackRepository = trackRepository;
        _logger = logger;
        _serverVersion = McpServiceCollectionExtensions.GetServerVersion(typeof(SystemTools).Assembly);
    }

    /// <summary>
    /// MCP サーバの稼働状態と接続疎通を確認します。
    /// </summary>
    /// <param name="message">エコー確認用の任意メッセージ（省略可）。</param>
    /// <returns>疎通結果を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "ping")]
    [Description("MCP サーバの稼働状態と接続疎通を確認します。")]
    public CallToolResult Ping(
        [Description("エコー確認用の任意メッセージ（省略可）")] string? message = null)
    {
        try
        {
            var response = new PingResponse(
                Status: "ok",
                ServerName: McpServiceCollectionExtensions.SERVER_NAME,
                Version: _serverVersion,
                Transport: GetTransportName(_options.Transport),
                Echo: message,
                TimestampUtc: DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    /// <summary>
    /// MCP サーバの構成情報・ライブラリ登録楽曲件数・WPF 本体との連携状態を取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>サーバ情報を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "get_server_info")]
    [Description("MCP サーバの構成情報・ライブラリ登録楽曲件数・WPF 本体との連携状態を取得します。")]
    public async Task<CallToolResult> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tracks = await _trackRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var response = new ServerInfoResponse(
                ServerName: McpServiceCollectionExtensions.SERVER_NAME,
                Version: _serverVersion,
                Transport: GetTransportName(_options.Transport),
                DataDirectory: _dataDirectoryProvider.DataDirectory,
                LibraryTrackCount: tracks.Count,
                WpfIpcStatus: WPF_IPC_STATUS_NOT_CONFIGURED);

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    private static string GetTransportName(McpTransportMode transport)
    {
        return transport switch
        {
            McpTransportMode.Stdio => "stdio",
            McpTransportMode.Http => "http",
            _ => transport.ToString().ToLowerInvariant(),
        };
    }
}
