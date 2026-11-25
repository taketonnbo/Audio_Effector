using System.Diagnostics.CodeAnalysis;

namespace AudioEffector.McpServer.Dtos;

/// <summary>
/// <c>launch_app</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="Launched">今回新規にプロセスを起動した場合は <see langword="true"/>。</param>
/// <param name="AlreadyRunning">すでに起動済みだった場合は <see langword="true"/>。</param>
/// <param name="Mode">実際に適用された起動モード（<c>"mini_player"</c> / <c>"normal"</c> / <c>"existing"</c>）。</param>
/// <param name="IpcReady">IPC 接続可能状態か否か。</param>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "カテゴリ単位の DTO 集約ファイル規約（MCPサーバ詳細設計書 5.1 準拠）")]
public sealed record LaunchAppResponse(
    bool Launched,
    bool AlreadyRunning,
    string Mode,
    bool IpcReady);
