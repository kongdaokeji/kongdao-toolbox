namespace TubaWinUi3.Services.Agent;

public enum AgentStepStatus
{
    /// <summary>执行中。</summary>
    Running,
    /// <summary>等待用户确认（危险操作）。</summary>
    AwaitingConfirmation,
    /// <summary>执行成功。</summary>
    Success,
    /// <summary>执行失败。</summary>
    Failed,
    /// <summary>用户拒绝执行。</summary>
    Rejected,
    /// <summary>被取消。</summary>
    Cancelled
}

/// <summary>
/// Agent 步骤记录：一次工具调用的完整轨迹，是步骤链可视化（StepChainControl）的数据源。
/// </summary>
public sealed class AgentStep
{
    public string ToolName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? Detail { get; init; }
    public string? Reason { get; init; }
    public string? CallId { get; init; }
    public bool IsDangerous { get; init; }
    public AgentStepStatus Status { get; set; } = AgentStepStatus.Running;
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public TimeSpan? Duration { get; set; }

    public string StatusText => Status switch
    {
        AgentStepStatus.Running => "执行中…",
        AgentStepStatus.AwaitingConfirmation => "等待确认",
        AgentStepStatus.Success => "完成",
        AgentStepStatus.Failed => "失败",
        AgentStepStatus.Rejected => "已拒绝",
        AgentStepStatus.Cancelled => "已取消",
        _ => ""
    };
}

/// <summary>需要用户确认的危险操作请求（取代旧 [ACTION] 文本协议）。</summary>
public sealed class AgentConfirmationRequest
{
    public string CallId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>确认分类：run_command / write_reg / write_file / delete_file / move_file / copy_file / download_file / launch_tool / plan。</summary>
    public string Kind { get; init; } = "action";
    public IReadOnlyList<string>? PlanSteps { get; init; }
    public string? PlanGoal { get; init; }
    public AgentStep? Step { get; init; }

    internal PendingToolCall? Pending { get; init; }
}

/// <summary>用户对某个确认请求的决策。</summary>
public sealed class AgentConfirmationDecision
{
    public required AgentConfirmationRequest Request { get; init; }
    public required bool Confirmed { get; init; }
}

/// <summary>一轮完整步骤链的汇总（用于折叠摘要节点）。</summary>
public sealed class AgentStepGroupSummary
{
    public int Total { get; init; }
    public int Success { get; init; }
    public int Failed { get; init; }
    /// <summary>整链耗时。</summary>
    public TimeSpan? Duration { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    /// <summary>本组缓存命中 token（提供商/网关返回缓存统计时才有值）。</summary>
    public int CacheHitTokens { get; init; }
    public int CacheMissTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public IReadOnlyDictionary<string, int> ByTool { get; init; } = new Dictionary<string, int>();

    public string ToDisplayText()
    {
        var parts = new List<string>();

        // 折叠摘要：只展示做了什么（执行了命令、执行了搜索），不占版面
        if (ByTool.Count > 0)
        {
            var actions = ByTool
                .Select(kv => kv.Value > 1 ? $"{kv.Key}×{kv.Value}" : kv.Key)
                .ToList();
            parts.Add("执行了" + string.Join("、", actions));
        }

        var status = Failed > 0
            ? $"{Success} 成功 / {Failed} 失败"
            : Total switch { 1 => "1 步完成", _ => $"{Total} 步完成" };
        parts.Add(status);

        if (Duration is { } d && d.TotalSeconds >= 0.5)
            parts.Add($"耗时 {d.TotalSeconds:F1}s");

        if (TotalTokens > 0)
            parts.Add($"消耗 {FormatTokens(TotalTokens)}");

        if (CacheHitTokens > 0)
        {
            var total = CacheHitTokens + CacheMissTokens;
            var pct = total > 0 ? (int)Math.Round(CacheHitTokens * 100.0 / total) : 100;
            parts.Add($"缓存命中 {FormatTokens(CacheHitTokens)} ({pct}%)");
        }

        return string.Join(" · ", parts);
    }

    private static string FormatTokens(int tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:F1}k" : tokens.ToString();
}
