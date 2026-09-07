using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// AgentRuntime（旧引擎）工具循环护栏集成测试：
/// 重复调用拦截、空结果标记、无进展终止、web_search 技能拦截计数、确认执行登记。
/// 与 AgentRuntimeUsageTests / AgentToolRegistryTests 共享集合：静态注册表与
/// AgentToolLoopGuard 被反射清空/重置，须串行执行。
/// </summary>
[Collection("AgentToolRegistry")]
public class AgentRuntimeLoopGuardTests
{
    private static readonly FieldInfo ToolsField =
        typeof(AgentToolRegistry).GetField("_tools", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void ClearRegistry()
        => ((List<AgentTool>)ToolsField.GetValue(null)!).Clear();

    private static AgentTool RegisterFake(string name, Delegate func, bool requiresConfirmation = false)
    {
        ClearRegistry();
        var tool = new AgentTool
        {
            Name = name,
            DisplayName = name,
            Glyph = "\uE73E",
            Function = AIFunctionFactory.Create(func, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = requiresConfirmation,
        };
        AgentToolRegistry.Register(tool);
        return tool;
    }

    private static List<string> ToolResults(List<ChatMessage> history)
        => history
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Select(c => c.Result?.ToString() ?? "")
            .ToList();

    private static List<ChatMessage> NewHistory()
        => [new(ChatRole.System, "你是图吧助手。"), new(ChatRole.User, "测试指令")];

    private static ChatResponseUpdate ToolCall(string callId, string name, string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonValueToObject(p.Value));
        return new ChatResponseUpdate(ChatRole.Assistant,
            new AIContent[] { new FunctionCallContent(callId, name, args) });
    }

    private static object? JsonValueToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText()
    };

    private static ChatResponseUpdate Reasoning(string t)
        => new(ChatRole.Assistant, new AIContent[] { new TextReasoningContent(t) });

    private static ChatResponseUpdate Text(string t)
        => new(ChatRole.Assistant, t);

    /// <summary>核心：相同工具 + 相同参数的第二次调用被拦截，底层函数只执行一次。</summary>
    [Fact]
    public async Task RunLoop_DuplicateToolCall_SecondBlocked_ExecutesOnce()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        RegisterFake("fake_query", (Func<string, string>)(query => { calls++; return $"结果:{query}"; }));

        var client = new ScriptedChatClient(
            () => [ToolCall("c1", "fake_query", """{"query":"显卡价格"}""")],
            () => [ToolCall("c2", "fake_query", """{"query":"显卡价格"}""")],
            () => [Text("搞定")]);

        var history = NewHistory();
        var done = await AgentRuntime.RunLoopAsync(history, new AgentRunCallbacks(), CancellationToken.None, clientOverride: client);

        Assert.True(done);
        Assert.Equal(1, calls); // 第二次未真正执行
        var results = ToolResults(history);
        Assert.Contains(results, r => r.Contains("结果:显卡价格"));
        Assert.Contains(results, r => r.Contains("重复调用已拦截"));
    }

    /// <summary>空结果标记：工具返回空串时回填"未返回内容"，不让模型误判为失败而无限重试。</summary>
    [Fact]
    public async Task RunLoop_EmptyToolResult_Marked()
    {
        AgentToolLoopGuard.Reset();
        RegisterFake("fake_silent", (Func<string>)(() => ""));

        var client = new ScriptedChatClient(
            () => [ToolCall("c1", "fake_silent", "{}")],
            () => [Text("完成")]);

        var history = NewHistory();
        var done = await AgentRuntime.RunLoopAsync(history, new AgentRunCallbacks(), CancellationToken.None, clientOverride: client);

        Assert.True(done);
        Assert.Contains(ToolResults(history), r => r.Contains("未返回内容"));
    }

    /// <summary>无进展检测：连续纯工具轮达阈值后终止循环，不再发出后续请求。</summary>
    [Fact]
    public async Task RunLoop_NoProgressPureToolRounds_Terminates()
    {
        AgentToolLoopGuard.Reset();
        RegisterFake("fake_query", (Func<string, string>)(query => $"结果:{query}"));

        // 连续 6 轮纯工具调用（无用户可见文本）→ 第 7 轮开头终止；
        // 每轮参数不同（避免被去重拦截，确保每轮都是"真实执行"的纯工具轮）
        var rounds = Enumerable.Range(1, AgentToolLoopGuard.NoProgressThreshold)
            .Select(i => (Func<ChatResponseUpdate[]>)(() => [ToolCall($"c{i}", "fake_query", $"{{\"query\":\"q{i}\"}}")]))
            .Append(() => [Text("不应到达")])
            .ToArray();
        var client = new ScriptedChatClient(rounds);

        string? error = null;
        var cb = new AgentRunCallbacks { OnError = e => error = e };
        var done = await AgentRuntime.RunLoopAsync(NewHistory(), cb, CancellationToken.None, clientOverride: client);

        Assert.True(done);
        Assert.NotNull(error);
        Assert.Contains("循环", error);
        Assert.Equal(AgentToolLoopGuard.NoProgressThreshold, client.RequestCount); // 第 7 轮请求未发出
    }

    /// <summary>web_search 技能拦截计数：第二次起直接强硬终止，不再重复引导。</summary>
    [Fact]
    public async Task RunLoop_WebSearchSkillBlocked_SecondTime_HardStop()
    {
        AgentToolLoopGuard.Reset();
        RegisterFake("web_search", (Func<string, string>)(query => "结果"));

        var prev = AgentToolContext.SkillTriggerActive;
        try
        {
            AgentToolContext.SkillTriggerActive = true;

            var client = new ScriptedChatClient(
                () => [ToolCall("c1", "web_search", """{"query":"RTX 5090"}""")],
                () => [ToolCall("c2", "web_search", """{"query":"RTX 5090"}""")],
                () => [Text("好了")]);

            var history = NewHistory();
            var done = await AgentRuntime.RunLoopAsync(history, new AgentRunCallbacks(), CancellationToken.None, clientOverride: client);

            Assert.True(done);
            var results = ToolResults(history);
            Assert.Contains(results, r => r.Contains("web_search 已被禁用") && r.Contains("browser_navigate"));
            Assert.Contains(results, r => r.Contains("已连续两次被拦截")); // 第二次直接终止
        }
        finally
        {
            AgentToolContext.SkillTriggerActive = prev;
        }
    }

    /// <summary>
    /// 确认执行登记：用户确认的调用真正执行并登记签名，
    /// 后续轮次模型发起相同调用被拦截（不重复打扰用户确认、不重复执行）。
    /// </summary>
    [Fact]
    public async Task RunLoop_ConfirmedTool_Registered_LaterDuplicateBlocked()
    {
        AgentToolLoopGuard.Reset();
        var calls = 0;
        RegisterFake("fake_danger", (Func<string, string>)(query => { calls++; return $"已执行:{query}"; }), requiresConfirmation: true);

        var prev = AgentToolContext.IsFullAccess;
        try
        {
            AgentToolContext.IsFullAccess = false; // 确保危险工具走确认分支

            IReadOnlyList<AgentConfirmationRequest>? requests = null;
            var cb = new AgentRunCallbacks { OnConfirmationsRequested = r => requests = r };
            var history = NewHistory();

            // 轮1：需确认 → 暂停；用户确认后 ResumeLoop 执行（calls=1）并登记；
            // 轮2：模型又发起相同调用 → 被拦截；轮3：文本结束
            var client = new ScriptedChatClient(
                () => [ToolCall("c1", "fake_danger", """{"query":"删除旧文件"}""")],
                () => [ToolCall("c2", "fake_danger", """{"query":"删除旧文件"}""")],
                () => [Text("完成")]);

            var paused = await AgentRuntime.RunLoopAsync(history, cb, CancellationToken.None, clientOverride: client);
            Assert.False(paused); // 已暂停等待确认
            Assert.NotNull(requests);
            Assert.Single(requests);

            var decisions = new[] { new AgentConfirmationDecision { Request = requests![0], Confirmed = true } };
            var done = await AgentRuntime.ResumeLoopAsync(history, decisions, cb, CancellationToken.None, clientOverride: client);

            Assert.True(done);
            Assert.Equal(1, calls); // 确认执行 1 次，轮2 的相同调用被拦截
            var results = ToolResults(history);
            Assert.Contains(results, r => r.Contains("已执行:删除旧文件"));
            Assert.Contains(results, r => r.Contains("重复调用已拦截"));
        }
        finally
        {
            AgentToolContext.IsFullAccess = prev;
        }
    }

    // ---------- 思维链长度护栏 ----------

    /// <summary>超长思维链截断保留开头：8000 字符思考 → 回填历史时最多 6000。</summary>
    [Fact]
    public async Task RunLoop_OverlongReasoning_IsTruncated()
    {
        AgentToolLoopGuard.Reset();

        var client = new ScriptedChatClient(
            () => [Reasoning(new string('思', 8000)), Text("回答完毕")]);

        var history = NewHistory();
        var done = await AgentRuntime.RunLoopAsync(history, new AgentRunCallbacks(), CancellationToken.None, clientOverride: client);

        Assert.True(done);
        var assistant = history.First(m => m.Role == ChatRole.Assistant);
        var reasoning = assistant.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal(6000, reasoning!.Text?.Length); // 8000 → 截断到 6000 保留开头
    }

    // ---------- 历史预算压缩 ----------

    /// <summary>历史超预算时从最旧丢弃，保留首条 system，压缩后总长收敛到预算内。</summary>
    [Fact]
    public void TrimHistory_OverBudget_DropsOldestKeepingFirstSystem()
    {
        var history = new List<ChatMessage> { new(ChatRole.System, "sys") };
        for (var i = 0; i < 30; i++)
            history.Add(new ChatMessage(ChatRole.User, new string('长', 3000))); // 约 90K 字符
        var before = history.Count;

        AgentRuntime.TrimHistory(history);

        Assert.True(history.Count < before, "超限历史应被压缩");
        Assert.Equal(ChatRole.System, history[0].Role); // 首条 system 保留
        var total = history.Skip(1).Sum(m => (m.Text ?? "").Length);
        Assert.True(total <= 40000, $"压缩后总长应 <= 40000，实际 {total}");
    }

    /// <summary>预算内历史原样保留（正常会话无感知）。</summary>
    [Fact]
    public void TrimHistory_UnderBudget_Unchanged()
    {
        var history = new List<ChatMessage> { new(ChatRole.System, "sys"), new(ChatRole.User, "hi") };
        var before = history.Count;

        AgentRuntime.TrimHistory(history);

        Assert.Equal(before, history.Count);
    }

    /// <summary>最小脚本化流式 IChatClient：按轮返回预设响应（模拟模型行为）。</summary>
    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Func<ChatResponseUpdate[]>[] _rounds;
        private int _index;

        public ScriptedChatClient(params Func<ChatResponseUpdate[]>[] rounds) => _rounds = rounds;

        public int RequestCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "（非流式兜底）")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequestCount++;
            if (_index >= _rounds.Length)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "（脚本耗尽）");
                yield break;
            }
            foreach (var u in _rounds[_index++]()) yield return u;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
