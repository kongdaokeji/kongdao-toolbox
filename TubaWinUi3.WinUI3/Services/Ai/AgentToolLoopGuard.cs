using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// Agent 工具循环护栏（会话级）：
/// - 重复调用检测：同一会话中「工具 + 参数」完全相同的第二次调用直接拦截、不真正执行，
///   阻止模型对同一操作反复调用（反复 launch_tool 同一工具 / web_search 同一关键词）空转烧轮次。
/// - 无进展检测：连续 N 轮纯工具调用（无用户可见文本）视为疑似死循环，
///   触发后由 Provider 在请求中注入"立即总结停止"的系统指令（见 TubaChatProvider.StreamCoreAsync）；
///   旧引擎 AgentRuntime 则直接终止循环。
/// - web_search 技能拦截计数：被拦第二次起直接强硬终止，防弱模型反复尝试同一被禁工具。
/// - 空参数调用（查询类，如实时温度）豁免去重——允许重复取最新值。
/// - 会话边界：新对话（AiAgentPage.ResetToNewChat）时调用 <see cref="Reset"/> 清空全部登记。
/// 线程安全：AI 工具调用与流式请求可能并发，所有状态访问加锁。
/// </summary>
internal static class AgentToolLoopGuard
{
    /// <summary>连续纯工具轮阈值：达到该轮数仍未产出用户可见文本，注入终止指令。</summary>
    internal const int NoProgressThreshold = 6;

    /// <summary>工具结果最大长度（字符）：超长结果截断保留开头，控制上下文体积
    /// （新引擎 AgentToolAdapter 与旧引擎 AgentRuntime 共用同一上限）。</summary>
    internal const int MaxToolResultChars = 6000;

    private static readonly object Sync = new();
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);
    private static int _consecutiveToolRounds;
    private static int _webSearchBlocked;

    /// <summary>
    /// 登记一次工具调用；返回 true 表示重复（应拦截），false 表示首次（可正常执行）。
    /// 签名 = toolName + '\u0000' + 规范化参数（递归按键排序序列化）。
    /// </summary>
    public static bool IsDuplicate(string toolName, string normalizedArgs)
    {
        lock (Sync)
            return !Seen.Add(toolName + "\u0000" + normalizedArgs);
    }

    /// <summary>
    /// 一轮流式请求结束后的进展上报：
    /// 模型产出过用户可见文本（TextDelta）→ 有进展，计数清零；
    /// 纯工具轮（无文本但有工具调用）→ 无进展，计数递增。
    /// </summary>
    public static void ReportRound(bool hadUserText, bool hadToolCalls)
    {
        lock (Sync)
            _consecutiveToolRounds = !hadUserText && hadToolCalls ? _consecutiveToolRounds + 1 : 0;
    }

    /// <summary>当前连续无进展（纯工具）轮数。</summary>
    public static int ConsecutiveToolRounds
    {
        get { lock (Sync) return _consecutiveToolRounds; }
    }

    /// <summary>是否应注入终止指令（连续纯工具轮达到阈值，疑似死循环）。</summary>
    public static bool ShouldInjectStopDirective => ConsecutiveToolRounds >= NoProgressThreshold;

    /// <summary>登记一次 web_search 技能拦截，返回包含本次在内的累计拦截次数。</summary>
    public static int RegisterWebSearchBlocked()
    {
        lock (Sync)
            return ++_webSearchBlocked;
    }

    /// <summary>清空全部登记（新对话/会话切换时调用）。</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            Seen.Clear();
            _consecutiveToolRounds = 0;
            _webSearchBlocked = 0;
        }
    }

    /// <summary>
    /// 参数规范化：对象递归按键排序序列化（{ "b":1,"a":2 } 与 { "a":2,"b":1 } 视为相同签名）。
    /// 空对象/非对象/非法 JSON 返回 null，表示不做去重（查询类工具允许重复取最新值）。
    /// 新引擎 AgentToolAdapter 与旧引擎 AgentRuntime 共用同一实现，保证两引擎签名一致。
    /// </summary>
    public static string? NormalizeArgs(string? jsonArgs)
    {
        if (string.IsNullOrWhiteSpace(jsonArgs)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonArgs);
            return NormalizeArgsCore(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeArgsCore(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.EnumerateObject().Any())
            return null;

        var sb = new StringBuilder();
        AppendNormalized(el, sb);
        return sb.ToString();
    }

    private static void AppendNormalized(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                sb.Append('{');
                var first = true;
                foreach (var prop in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(prop.Name)).Append(':');
                    AppendNormalized(prop.Value, sb);
                }
                sb.Append('}');
                break;
            }
            case JsonValueKind.Array:
            {
                sb.Append('[');
                var first = true;
                foreach (var item in el.EnumerateArray())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendNormalized(item, sb);
                }
                sb.Append(']');
                break;
            }
            default:
                sb.Append(el.GetRawText()); // 字符串/数字/布尔/null 原样（含引号，保真）
                break;
        }
    }
}
