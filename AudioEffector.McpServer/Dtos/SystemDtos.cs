using System.Diagnostics.CodeAnalysis;

namespace AudioEffector.McpServer.Dtos;

/// <summary>
/// <c>ping</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="Status">稼働状態（正常時は <c>"ok"</c>）。</param>
/// <param name="ServerName">MCP サーバ名。</param>
/// <param name="Version">MCP サーバのアセンブリバージョン。</param>
/// <param name="Transport">現在稼働中のトランスポート種別（<c>"stdio"</c> / <c>"http"</c>）。</param>
/// <param name="Echo">クライアントから指定されたエコーメッセージ（未指定時は <see langword="null"/>）。</param>
/// <param name="TimestampUtc">応答生成時の UTC タイムスタンプ（ISO 8601 形式）。</param>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "カテゴリ単位の DTO 集約ファイル規約（MCPサーバ詳細設計書 5.1 準拠）")]
public sealed record PingResponse(
    string Status,
    string ServerName,
    string Version,
    string Transport,
    string? Echo,
    string TimestampUtc);

/// <summary>
/// <c>get_server_info</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="ServerName">MCP サーバ名。</param>
/// <param name="Version">MCP サーバのアセンブリバージョン。</param>
/// <param name="Transport">現在稼働中のトランスポート種別（<c>"stdio"</c> / <c>"http"</c>）。</param>
/// <param name="DataDirectory">永続化 JSON ファイル群の参照先データディレクトリパス。</param>
/// <param name="LibraryTrackCount">ライブラリに登録されている楽曲件数。</param>
/// <param name="WpfIpcStatus">Audio Effector WPF 本体との IPC 連携状態（Phase 1 では <c>"not_configured"</c>）。</param>
public sealed record ServerInfoResponse(
    string ServerName,
    string Version,
    string Transport,
    string DataDirectory,
    int LibraryTrackCount,
    string WpfIpcStatus);
