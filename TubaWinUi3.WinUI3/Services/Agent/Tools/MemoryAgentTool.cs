using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 会话记忆工具：读写当前会话的持久化笔记（agent memory）。
/// 记忆内容会注入系统提示词，帮助模型跨轮次记住用户偏好与任务进度。
/// </summary>
public static class MemoryAgentTool
{
    public static void Register()
    {
        Add("read_memory", "读取记忆", (Func<string>)ReadMemory);
        Add("write_memory", "更新记忆", (Func<string, string>)WriteMemory);
        Add("clear_memory", "清空记忆", (Func<string>)ClearMemory);
    }

    /// <summary>解析当前会话记忆：优先取运行中的 AgentSession，ChatPanel 模式回退到页面桥接的记忆文件。</summary>
    private static ConversationMemory? ResolveMemory()
        => AgentToolContext.Current?.Memory ?? AgentToolContext.ActiveMemory;

    [Description("读取会话记忆（用户偏好、任务进度等持久化笔记）")]
    public static string ReadMemory()
    {
        var notes = ResolveMemory()?.Read();
        return string.IsNullOrWhiteSpace(notes)
            ? "（暂无会话记忆）"
            : notes;
    }

    [Description("写入/覆盖会话记忆笔记（记录用户偏好、任务进度、重要结论，供后续对话参考）")]
    public static string WriteMemory(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "错误：content 不能为空";
        if (ResolveMemory() is { } memory)
        {
            memory.Write(content);
            AgentToolContext.MemoryModified?.Invoke();
            return $"已保存会话记忆（{content.Length} 字符）";
        }
        return "错误：当前无会话记忆";
    }

    [Description("清空会话记忆笔记")]
    public static string ClearMemory()
    {
        if (ResolveMemory() is { } memory)
        {
            memory.Clear();
            AgentToolContext.MemoryModified?.Invoke();
        }
        return "已清空会话记忆";
    }

    private static void Add(string name, string displayName, Delegate method)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = "\uE8F1",
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
        });
    }
}

/// <summary>
/// 任务规划工具：模型先输出分步计划，用户批准后逐条执行。
/// 运行时拦截（IsPlanTool），确认卡片展示 goal + steps。
/// </summary>
public static class PlanAgentTool
{
    public static void Register()
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = "create_plan",
            DisplayName = "制定计划",
            Glyph = "\uE9D5",
            Function = AIFunctionFactory.Create((Func<string, string[], string>)CreatePlan, new AIFunctionFactoryOptions { Name = "create_plan" }),
            IsPlanTool = true,
            DefaultReason = "多步任务先制定计划，用户确认后执行",
        });
    }

    [Description("多步任务规划：先输出分步计划（goal + steps），用户确认后按计划执行")]
    public static string CreatePlan(string goal, string[] steps)
    {
        // 运行时拦截：正常模式等待用户确认；完全访问模式直接执行到此
        return "（计划已记录，请按计划逐步执行）";
    }
}
