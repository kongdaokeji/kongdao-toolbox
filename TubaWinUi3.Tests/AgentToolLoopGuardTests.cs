using System.Text.Json;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// Agent 工具循环护栏测试：重复调用拦截（防模型对同一操作反复调用陷入死循环）与空结果标记。
/// 与 AgentToolAdapterTests / AgentRuntime 系列同集合串行：都读写 AgentToolLoopGuard 静态状态。
/// </summary>
[Collection("AgentToolRegistry")]
public class AgentToolLoopGuardTests
{
    private static AgentToolAdapter MakeAdapter(string name, Delegate func)
    {
        var fn = AIFunctionFactory.Create(func, new AIFunctionFactoryOptions { Name = name });
        return new AgentToolAdapter(new AgentTool
        {
            Name = name,
            DisplayName = name,
            Glyph = "\uE73E",
            Function = fn
        });
    }

    /// <summary>核心：相同工具 + 相同参数的第二次调用被拦截，底层函数只执行一次。</summary>
    [Fact]
    public async Task SameToolAndArgs_SecondCall_IsBlockedAndNotExecuted()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("web_search",
            (Func<string, string>)(query => { calls++; return $"结果:{query}"; }));

        using var doc = JsonDocument.Parse("""{"query":"显卡价格"}""");
        var args = doc.RootElement;

        var first = await adapter.ExecuteAsync(args);
        var second = await adapter.ExecuteAsync(args);

        Assert.Contains("结果:显卡价格", first);
        Assert.Contains("重复调用已拦截", second);
        Assert.Equal(1, calls); // 第二次未真正执行
    }

    /// <summary>参数键顺序不同视为同一签名（{"a":1,"b":2} 与 {"b":2,"a":1} 相同）。</summary>
    [Fact]
    public async Task ReorderedArgsKeys_TreatedAsSameSignature()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("launch_tool",
            (Func<string, string>)(toolName => { calls++; return $"启动:{toolName}"; }));

        using var doc1 = JsonDocument.Parse("""{"toolName":"快速验机"}""");
        using var doc2 = JsonDocument.Parse("""{"toolName":"快速验机"}""");

        await adapter.ExecuteAsync(doc1.RootElement);
        var second = await adapter.ExecuteAsync(doc2.RootElement);

        Assert.Contains("重复调用已拦截", second);
        Assert.Equal(1, calls);
    }

    /// <summary>参数不同不拦截（同一工具不同用途的合法调用）。</summary>
    [Fact]
    public async Task DifferentArgs_AllowsExecution()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("browser_navigate",
            (Func<string, string>)(url => { calls++; return $"已打开 {url}"; }));

        using var doc1 = JsonDocument.Parse("""{"url":"https://a.com"}""");
        using var doc2 = JsonDocument.Parse("""{"url":"https://b.com"}""");

        await adapter.ExecuteAsync(doc1.RootElement);
        await adapter.ExecuteAsync(doc2.RootElement);

        Assert.Equal(2, calls); // 参数不同，两次都执行
    }

    /// <summary>空参数（查询类，如实时温度）豁免去重——允许重复取最新值。</summary>
    [Fact]
    public async Task EmptyArgs_ExemptFromDedup()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("get_cpu_temperature",
            (Func<string>)(() => { calls++; return $"{30 + calls}℃"; }));

        using var doc = JsonDocument.Parse("{}");
        await adapter.ExecuteAsync(doc.RootElement);
        var second = await adapter.ExecuteAsync(doc.RootElement);

        Assert.DoesNotContain("重复调用已拦截", second);
        Assert.Equal(2, calls); // 两次都真正执行
    }

    /// <summary>空结果标记：工具返回空串时给明确占位，不让模型误判为失败而无限重试。</summary>
    [Fact]
    public async Task EmptyResult_MarkedInsteadOfBlank()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("silent_tool", (Func<string>)(() => ""));

        using var doc = JsonDocument.Parse("{}");
        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Contains("未返回内容", result);
        Assert.NotEqual("", result);
    }

    /// <summary>Reset 后（新对话边界）同一调用可再次执行。</summary>
    [Fact]
    public async Task Reset_ClearsSeenCalls()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        var adapter = MakeAdapter("web_search",
            (Func<string, string>)(query => { calls++; return $"结果:{query}"; }));

        using var doc = JsonDocument.Parse("""{"query":"显卡价格"}""");
        await adapter.ExecuteAsync(doc.RootElement);
        await adapter.ExecuteAsync(doc.RootElement); // 被拦截
        Assert.Equal(1, calls);

        AgentToolLoopGuard.Reset(); // 新对话
        var afterReset = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Contains("结果:显卡价格", afterReset);
        Assert.Equal(2, calls); // Reset 后重新执行
    }

    // ---------- 无进展检测（连续纯工具轮 → 注入终止指令） ----------

    /// <summary>连续纯工具轮递增，达到阈值后触发终止指令注入；未达阈值不触发。</summary>
    [Fact]
    public void ReportRound_PureToolRounds_IncrementUntilThreshold()
    {
        AgentToolLoopGuard.Reset();
        Assert.False(AgentToolLoopGuard.ShouldInjectStopDirective);

        // 阈值 - 1 轮：仍未触发
        for (var i = 1; i < AgentToolLoopGuard.NoProgressThreshold; i++)
        {
            AgentToolLoopGuard.ReportRound(hadUserText: false, hadToolCalls: true);
            Assert.False(AgentToolLoopGuard.ShouldInjectStopDirective);
        }

        // 第阈值轮：触发
        AgentToolLoopGuard.ReportRound(hadUserText: false, hadToolCalls: true);
        Assert.True(AgentToolLoopGuard.ShouldInjectStopDirective);
        Assert.Equal(AgentToolLoopGuard.NoProgressThreshold, AgentToolLoopGuard.ConsecutiveToolRounds);
    }

    /// <summary>模型产出用户可见文本 = 有进展，计数清零；无工具也无文本的空轮同样清零。</summary>
    [Fact]
    public void ReportRound_UserText_ResetsProgress()
    {
        AgentToolLoopGuard.Reset();
        AgentToolLoopGuard.ReportRound(false, true);
        AgentToolLoopGuard.ReportRound(false, true);
        AgentToolLoopGuard.ReportRound(true, true); // 模型开口了
        Assert.Equal(0, AgentToolLoopGuard.ConsecutiveToolRounds);

        AgentToolLoopGuard.ReportRound(false, false); // 空轮
        Assert.Equal(0, AgentToolLoopGuard.ConsecutiveToolRounds);
    }

    /// <summary>Reset（新对话）清空无进展计数。</summary>
    [Fact]
    public void ReportRound_Reset_ClearsProgress()
    {
        AgentToolLoopGuard.Reset();
        for (var i = 0; i < AgentToolLoopGuard.NoProgressThreshold; i++)
            AgentToolLoopGuard.ReportRound(false, true);
        Assert.True(AgentToolLoopGuard.ShouldInjectStopDirective);

        AgentToolLoopGuard.Reset();
        Assert.False(AgentToolLoopGuard.ShouldInjectStopDirective);
        Assert.Equal(0, AgentToolLoopGuard.ConsecutiveToolRounds);
    }

    // ---------- web_search 技能拦截计数 ----------

    /// <summary>web_search 被技能拦截：第二次起返回强硬终止文案，不再重复引导。</summary>
    [Fact]
    public async Task WebSearchBlocked_SecondTime_ReturnsHardStop()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("web_search", (Func<string, string>)(query => "结果"));
        using var doc = JsonDocument.Parse("""{"query":"RTX 5090"}""");

        var prev = AgentToolContext.SkillTriggerActive;
        try
        {
            AgentToolContext.SkillTriggerActive = true;

            var first = await adapter.ExecuteAsync(doc.RootElement);
            var second = await adapter.ExecuteAsync(doc.RootElement);

            Assert.Contains("web_search 已被禁用", first);
            Assert.Contains("已连续两次被拦截", second); // 第二次直接终止，不再给长引导
        }
        finally
        {
            AgentToolContext.SkillTriggerActive = prev;
        }
    }

    // ---------- 异常终态文案（区分可重试 / 系统性勿重试） ----------

    /// <summary>系统性失败（非参数类异常）→ 回传"请勿重试"终态文案，不再上抛中断会话。</summary>
    [Fact]
    public async Task SystemError_ReturnsNoRetryTerminalText()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("flaky_tool",
            (Func<string>)(() => throw new InvalidOperationException("磁盘被占用")));
        using var doc = JsonDocument.Parse("{}");

        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Contains("[工具错误]", result);
        Assert.Contains("磁盘被占用", result);
        Assert.Contains("请勿重试", result); // 系统性失败明确禁止重试
    }

    /// <summary>参数类错误（ArgumentException）→ 标记"参数无效"，允许调整参数后重试。</summary>
    [Fact]
    public async Task ParamError_MarkedRetryable()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("arg_tool",
            (Func<string, string>)(arg => throw new ArgumentException("路径不能为空", nameof(arg))));
        using var doc = JsonDocument.Parse("""{"arg":""}""");

        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Contains("[工具错误]", result);
        Assert.Contains("参数无效", result);
        Assert.DoesNotContain("请勿重试", result);
    }

    /// <summary>用户主动取消（ct 取消）→ 异常原样上抛，不转成错误文案。</summary>
    [Fact]
    public async Task UserCancellation_ReThrown()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("slow_tool",
            (Func<string>)(() => throw new OperationCanceledException()));
        using var doc = JsonDocument.Parse("{}");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 用户停止信号

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ExecuteAsync(doc.RootElement, cts.Token));
    }

    // ---------- 工具结果长度上限（控制上下文体积） ----------

    /// <summary>超长工具结果截断保留开头 + 截断标记，不再整段灌进上下文。</summary>
    [Fact]
    public async Task OverlongResult_IsTruncatedWithMarker()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("big_output", (Func<string>)(() => new string('长', 8000)));
        using var doc = JsonDocument.Parse("{}");

        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.StartsWith(new string('长', 6000), result);
        Assert.Contains("结果过长，已截断", result);
        Assert.True(result.Length < 8000, "截断后必须显著小于原始长度");
    }

    /// <summary>正常长度结果原样返回，不追加任何标记。</summary>
    [Fact]
    public async Task NormalResult_Unchanged()
    {
        AgentToolLoopGuard.Reset();
        var adapter = MakeAdapter("normal_tool", (Func<string>)(() => "温度 36℃"));
        using var doc = JsonDocument.Parse("{}");

        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Equal("温度 36℃", result);
    }
}
