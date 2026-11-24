using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioEffector.McpServer.Protocol;

/// <summary>
/// MCP サーバの全ツールおよびリソースで共通利用する JSON シリアライズ設定を提供します。
/// </summary>
public static class McpJsonOptions
{
    /// <summary>
    /// MCP 応答 JSON の標準シリアライズ設定を取得します。
    /// <c>camelCase</c> 命名規則、<see langword="null"/> 値省略、列挙型の文字列変換、および日本語文字の非エスケープを適用します。
    /// </summary>
    public static JsonSerializerOptions Default { get; } = CreateDefault();

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
