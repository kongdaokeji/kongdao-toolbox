using System.Text.Json;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 工具参数 JSON 与 M.E.AI 10.x 的 IDictionary&lt;string, object&gt; 表示互转。
/// 复杂值（数组/对象）保留为 JsonElement，由 AIFunction 参数绑定处理。
/// </summary>
internal static class AgentArgsJson
{
    public static Dictionary<string, object?> ParseToDictionary(string json)
    {
        var result = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = ConvertValue(prop.Value);
        }
        catch { }
        return result;
    }

    private static object? ConvertValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.Clone()
    };
}
