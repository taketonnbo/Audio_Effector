using System.ComponentModel;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AudioEffector.McpServer.Tools;

/// <summary>
/// Audio Effector デスクトップアプリ本体の起動制御を提供する MCP ツール群です。
/// </summary>
[McpServerToolType]
public sealed class AppTools
{
    /// <summary>
    /// 既定の起動モード（ミニプレイヤー起動）。
    /// </summary>
    public const string DEFAULT_MODE = WpfAppLauncher.MODE_MINI_PLAYER;

    private readonly IAppLauncher _appLauncher;
    private readonly ILogger<AppTools> _logger;

    /// <summary>
    /// <see cref="AppTools"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="appLauncher">Audio Effector 本体起動ゲートウェイ。</param>
    /// <param name="logger">診断ロガー。</param>
    public AppTools(IAppLauncher appLauncher, ILogger<AppTools> logger)
    {
        ArgumentNullException.ThrowIfNull(appLauncher);
        ArgumentNullException.ThrowIfNull(logger);

        _appLauncher = appLauncher;
        _logger = logger;
    }

    /// <summary>
    /// Audio Effector デスクトップアプリ本体を指定された起動モード（通常起動またはミニプレイヤー起動）で起動します。
    /// すでに起動済みの場合は多重起動せず、現在のウィンドウ状態を維持したまま正常応答を返します。
    /// </summary>
    /// <param name="mode">起動モード（<c>"mini_player"</c> または <c>"normal"</c>、既定値: <c>"mini_player"</c>）。</param>
    /// <param name="waitForReady">起動後、WPF 本体の準備完了まで待機するかどうか（既定値: <see langword="true"/>）。</param>
    /// <param name="cancellationToken">キャンセル要求トークン。</param>
    /// <returns>起動状態を含む統一 JSON 応答。</returns>
    [McpServerTool(Name = "launch_app")]
    [Description("Audio Effector デスクトップアプリ本体を起動します（通常起動またはミニプレイヤー起動）。すでに起動済みの場合は現在のウィンドウ状態を維持したまま正常応答を返します。")]
    public async Task<CallToolResult> LaunchAppAsync(
        [Description("起動モード（\"mini_player\" または \"normal\"、既定値: \"mini_player\"）")] string mode = DEFAULT_MODE,
        [Description("起動後、WPF 本体の準備完了まで待機するかどうか（既定値: true）")] bool waitForReady = true,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseLaunchMode(mode, out AppLaunchMode launchMode))
        {
            return McpResponseFormatter.Error(
                McpErrorCodes.INVALID_PARAMETER,
                $"mode には '{WpfAppLauncher.MODE_MINI_PLAYER}' または '{WpfAppLauncher.MODE_NORMAL}' を指定してください。",
                new
                {
                    parameter = "mode",
                    allowedValues = new[] { WpfAppLauncher.MODE_MINI_PLAYER, WpfAppLauncher.MODE_NORMAL },
                });
        }

        try
        {
            AppLaunchResult launchResult = await _appLauncher
                .LaunchAsync(launchMode, waitForReady, cancellationToken)
                .ConfigureAwait(false);

            var response = new LaunchAppResponse(
                Launched: launchResult.Launched,
                AlreadyRunning: launchResult.AlreadyRunning,
                Mode: launchResult.Mode,
                IpcReady: launchResult.IpcReady);

            return McpResponseFormatter.Success(response);
        }
        catch (Exception ex)
        {
            return McpResponseFormatter.FromException(ex, _logger);
        }
    }

    private static bool TryParseLaunchMode(string? mode, out AppLaunchMode launchMode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            launchMode = default;
            return false;
        }

        string normalized = mode.Trim();
        if (string.Equals(normalized, WpfAppLauncher.MODE_MINI_PLAYER, StringComparison.OrdinalIgnoreCase))
        {
            launchMode = AppLaunchMode.MiniPlayer;
            return true;
        }

        if (string.Equals(normalized, WpfAppLauncher.MODE_NORMAL, StringComparison.OrdinalIgnoreCase))
        {
            launchMode = AppLaunchMode.Normal;
            return true;
        }

        launchMode = default;
        return false;
    }
}
