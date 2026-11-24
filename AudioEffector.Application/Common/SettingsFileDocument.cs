using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Application.Common;

/// <summary>アプリ設定とキー別設定を独立して保持する保存形式</summary>
public sealed class SettingsFileDocument
{
    /// <summary>アプリケーション全体の設定</summary>
    public AppSettings Settings { get; set; } = new();

    /// <summary>プリセットなどのキー別設定</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>旧形式のアプリ設定または文字列辞書も読み込みます</summary>
    /// <param name="json">設定ファイルの内容</param>
    /// <returns>両方の設定を保持するドキュメント</returns>
    public static SettingsFileDocument Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("設定にはJSONオブジェクトが必要です。");
        }

        if (root.TryGetProperty(nameof(Settings), out var settings) && settings.ValueKind == JsonValueKind.Object)
        {
            var parsed = root.Deserialize<SettingsFileDocument>()!;
            if (parsed.Values == null) throw new JsonException("キー別設定を読み込めません。");
            return parsed;
        }

        if (root.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String))
        {
            return new SettingsFileDocument { Values = root.Deserialize<Dictionary<string, string>>()! };
        }

        return new SettingsFileDocument { Settings = root.Deserialize<AppSettings>()! };
    }
}
