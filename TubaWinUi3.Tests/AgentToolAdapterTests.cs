using FieldCure.Ai.Providers.Models;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// ChatPanel 工具适配层测试：AgentTool → IAssistTool 的元数据映射、
/// 完全访问模式下的动态确认策略、技能触发对 web_search 的拦截。
/// 与 AgentToolLoopGuardTests 同集合串行：两者都读写 AgentToolLoopGuard 静态状态。
/// </summary>
[Collection("AgentToolRegistry")]
public class AgentToolAdapterTests
{
    private static AgentTool MakeTool(
        string name = "test_tool",
        bool requiresConfirmation = false,
        bool alwaysConfirm = false,
        bool isPlanTool = false)
        => new()
        {
            Name = name,
            DisplayName = "测试工具",
            Glyph = "\uE001",
            Function = AIFunctionFactory.Create((string arg) => "ok:" + arg, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = requiresConfirmation,
            AlwaysConfirm = alwaysConfirm,
            IsPlanTool = isPlanTool,
        };

    private static void WithFullAccess(bool fullAccess, Action body)
    {
        var prev = AgentToolContext.IsFullAccess;
        try
        {
            AgentToolContext.IsFullAccess = fullAccess;
            body();
        }
        finally
        {
            AgentToolContext.IsFullAccess = prev;
        }
    }

    [Fact]
    public void Metadata_MapsNameAndSchema()
    {
        var adapter = new AgentToolAdapter(MakeTool());

        Assert.Equal("test_tool", adapter.Name);
        Assert.Equal("测试工具", adapter.DisplayName);
        // JSON Schema 从 AIFunction 生成声明自动取（非空、含 properties）
        Assert.Contains("properties", adapter.ParameterSchema);
    }

    [Fact]
    public void RequiresConfirmation_DangerousNeedsApprovalInControlledMode()
    {
        var adapter = new AgentToolAdapter(MakeTool(requiresConfirmation: true));

        WithFullAccess(false, () => Assert.True(adapter.RequiresConfirmation));
    }

    [Fact]
    public void RequiresConfirmation_FullAccessSkipsDangerousAndPlan()
    {
        var dangerous = new AgentToolAdapter(MakeTool(requiresConfirmation: true));
        var plan = new AgentToolAdapter(MakeTool(isPlanTool: true));

        WithFullAccess(true, () =>
        {
            Assert.False(dangerous.RequiresConfirmation);
            Assert.False(plan.RequiresConfirmation);
        });
    }

    [Fact]
    public void RequiresConfirmation_AlwaysConfirmBypassesFullAccess()
    {
        var adapter = new AgentToolAdapter(MakeTool(alwaysConfirm: true));

        WithFullAccess(true, () => Assert.True(adapter.RequiresConfirmation));
    }

    [Fact]
    public void RequiresConfirmation_PlainToolNeverConfirms()
    {
        var adapter = new AgentToolAdapter(MakeTool());

        WithFullAccess(false, () => Assert.False(adapter.RequiresConfirmation));
        WithFullAccess(true, () => Assert.False(adapter.RequiresConfirmation));
    }

    [Fact]
    public async Task ExecuteAsync_InvokesUnderlyingFunction()
    {
        var adapter = new AgentToolAdapter(MakeTool());

        using var doc = System.Text.Json.JsonDocument.Parse("""{"arg":"hello"}""");
        var result = await adapter.ExecuteAsync(doc.RootElement);

        Assert.Equal("ok:hello", result);
    }

    [Fact]
    public async Task ExecuteAsync_WebSearchBlockedWhileSkillTriggerActive()
    {
        var adapter = new AgentToolAdapter(MakeTool(name: "web_search"));
        using var doc = System.Text.Json.JsonDocument.Parse("""{"query":"RTX 5090"}""");

        var prev = AgentToolContext.SkillTriggerActive;
        try
        {
            AgentToolContext.SkillTriggerActive = true;
            var result = await adapter.ExecuteAsync(doc.RootElement);

            Assert.Contains("web_search 已被禁用", result);
            Assert.Contains("浏览器", result);
        }
        finally
        {
            AgentToolContext.SkillTriggerActive = prev;
        }
    }

    [Fact]
    public async Task ExecuteAsync_WebSearchAllowedWhenTriggerInactive()
    {
        var adapter = new AgentToolAdapter(MakeTool(name: "web_search"));
        using var doc = System.Text.Json.JsonDocument.Parse("""{"arg":"RTX 5090"}""");

        var prev = AgentToolContext.SkillTriggerActive;
        try
        {
            AgentToolContext.SkillTriggerActive = false;
            var result = await adapter.ExecuteAsync(doc.RootElement);

            Assert.StartsWith("ok:", result);
        }
        finally
        {
            AgentToolContext.SkillTriggerActive = prev;
        }
    }
}