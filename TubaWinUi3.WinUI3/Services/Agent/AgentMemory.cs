using System.Text;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 会话记忆：上下文预算管理（token 估算 → 结果截断 → LLM 滚动摘要）
/// 与持久化会话笔记。
/// </summary>
public static class AgentMemory
{
    public const int DefaultHistoryBudgetTokens = 6000;
    public const int MaxToolResultChars = 1200;

    /// <summary>
    /// 启发式 token 估算：CJK 字符按 1 token，其他字符按 3 字符 1 token，换行计 1。
    /// 仅用于预算裁剪，无需精确。
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var cjk = 0;
        var other = 0;
        foreach (var ch in text)
        {
            if (ch >= 0x2E80) cjk++;
            else other++;
        }
        return cjk + (int)Math.Ceiling(other / 3.0) + text.Count(c => c == '\n');
    }

    private static int EstimateTokens(IEnumerable<ChatMessage> messages)
        => messages.Sum(m => EstimateTokens(m.Text ?? ""));

    /// <summary>把过长的工具结果截断（纯本地，不消耗 LLM 调用）。</summary>
    public static bool TruncateLongToolResults(List<ChatMessage> history)
    {
        var changed = false;
        foreach (var m in history)
        {
            if (m.Role != ChatRole.Tool) continue;
            for (var i = 0; i < m.Contents.Count; i++)
            {
                if (m.Contents[i] is not FunctionResultContent frc || frc.Result is not string s) continue;
                if (s.Length <= MaxToolResultChars) continue;
                m.Contents[i] = new FunctionResultContent(
                    callId: frc.CallId,
                    result: s[..MaxToolResultChars] + $"\n…（结果过长已截断，原文 {s.Length} 字符）");
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// 发送前准备历史：本地截断过长结果；仍超预算时对最旧消息块做 LLM 滚动摘要，
    /// 以系统消息形式替换（保证对话协议完整）。
    /// </summary>
    public static async Task<List<ChatMessage>> PrepareHistoryAsync(
        IChatClient client,
        List<ChatMessage> history,
        int budgetTokens = DefaultHistoryBudgetTokens,
        CancellationToken ct = default)
    {
        if (history.Count <= 1)
        {
            // 必须返回副本：调用方随后会 Clear 原列表，
            // 若返回同一引用会导致 system 消息被连带清空（历史只剩 user）。
            return history.ToList();
        }

        var system = history[0];
        var rest = history.Skip(1).ToList();

        TruncateLongToolResults(rest);

        var guard = 0;
        while (rest.Count > 1 && EstimateTokens(rest) > budgetTokens && guard++ < 8)
        {
            // 取最旧的一轮（assistant + 其 tool 结果）作为摘要块
            var block = TakeOldestRound(rest, out var consumed);
            if (block.Count == 0) break;

            string summary;
            try
            {
                summary = await SummarizeAsync(client, block, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                break; // 摘要失败（如网络问题）则放弃压缩，避免无限循环
            }

            rest.RemoveRange(0, consumed);
            rest.Insert(0, new ChatMessage(ChatRole.System,
                "【历史摘要】\n" + summary + "\n\n（以下为继续对话所需的最近上下文）"));
        }

        var result = new List<ChatMessage> { system };
        result.AddRange(rest);
        return result;
    }

    /// <summary>取出最旧的一轮消息（从第一个带工具调用的 assistant 消息到其 tool 结果），返回消费数量。</summary>
    private static List<ChatMessage> TakeOldestRound(List<ChatMessage> rest, out int consumed)
    {
        var block = new List<ChatMessage>();
        consumed = 0;
        var takeTo = -1;

        for (var i = 0; i < rest.Count; i++)
        {
            var m = rest[i];
            block.Add(m);
            if (m.Role == ChatRole.Assistant && m.Contents.OfType<FunctionCallContent>().Any())
            {
                var callCount = m.Contents.OfType<FunctionCallContent>().Count();
                // 找到该 assistant 消息之后的所有 tool 结果
                var toolSeen = 0;
                var j = i + 1;
                for (; j < rest.Count; j++)
                {
                    if (rest[j].Role == ChatRole.Tool)
                    {
                        toolSeen++;
                        block.Add(rest[j]);
                        if (toolSeen >= callCount) break;
                    }
                    else break;
                }
                takeTo = Math.Max(i, j);
                break;
            }
        }

        if (takeTo < 0)
        {
            // 没有工具调用轮次：取最旧两条
            consumed = Math.Min(2, rest.Count);
            return rest.Take(consumed).ToList();
        }

        consumed = takeTo + 1;
        return block;
    }

    private static async Task<string> SummarizeAsync(IChatClient client, List<ChatMessage> block, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var m in block)
        {
            var role = m.Role == ChatRole.User ? "用户" : m.Role == ChatRole.Assistant ? "助手" : m.Role == ChatRole.Tool ? "工具结果" : "系统";
            sb.AppendLine($"[{role}] {m.Text}");
            foreach (var frc in m.Contents.OfType<FunctionResultContent>())
                sb.AppendLine($"[工具结果] {frc.Result}");
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是对话压缩器。请把下面的对话压缩为中文要点摘要，保留：用户目标、已执行的关键操作与结果、重要结论与待办。不要保留寒暄。直接输出摘要正文。"),
            new(ChatRole.User, sb.ToString())
        };

        var response = await client.GetResponseAsync(messages, new ChatOptions
        {
            Temperature = 0.2f,
            MaxOutputTokens = 512
        }, ct);

        return response.Text?.Trim() ?? "（摘要失败）";
    }
}

/// <summary>持久化会话笔记（agent memory 文件）。</summary>
public sealed class ConversationMemory
{
    private readonly string _path;

    public ConversationMemory(string path) => _path = path;

    public string Read()
    {
        try { return File.Exists(_path) ? File.ReadAllText(_path) : ""; }
        catch { return ""; }
    }

    public void Write(string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, content);
        }
        catch { }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { }
    }
}
