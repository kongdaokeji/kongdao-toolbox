using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

/// <summary>AgentRuntime token 用量兜底：API 不返回 usage 时本地估算。</summary>
[Collection("AgentToolRegistry")]
public class AgentRuntimeUsageTests
{
    [Fact]
    public async Task RunLoop_NoApiUsage_InvokesOnUsageWithLocalEstimate()
    {
        var fake = new FakeChatClient("你好，测试");
        AgentUsage? usage = null;
        var cb = new AgentRunCallbacks { OnUsage = u => usage = u };
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "你是图吧助手。"),
            new(ChatRole.User, "帮我看看电脑卡顿怎么办")
        };

        var done = await AgentRuntime.RunLoopAsync(history, cb, CancellationToken.None, clientOverride: fake);

        Assert.True(done);
        Assert.NotNull(usage);
        Assert.True(usage.PromptTokens > 0, "提示词估算应大于 0");
        Assert.True(usage.CompletionTokens > 0, "输出估算应大于 0");
    }

    [Fact]
    public async Task RunLoop_NoApiUsage_CompletionMatchesCjkHeuristic()
    {
        var fake = new FakeChatClient("你好，测试"); // 5 个 CJK 字符 → 5 token
        AgentUsage? usage = null;
        var cb = new AgentRunCallbacks { OnUsage = u => usage = u };
        var history = new List<ChatMessage> { new(ChatRole.System, "sys"), new(ChatRole.User, "hi") };

        await AgentRuntime.RunLoopAsync(history, cb, CancellationToken.None, clientOverride: fake);

        Assert.NotNull(usage);
        Assert.Equal(5, usage.CompletionTokens);
        Assert.Equal(AgentMemory.EstimateTokens("sys") + AgentMemory.EstimateTokens("hi"), usage.PromptTokens);
    }

    [Fact]
    public async Task RunLoop_WithToolResult_CountsResultInPrompt()
    {
        var fake = new FakeChatClient("已完成");
        AgentUsage? usage = null;
        var cb = new AgentRunCallbacks { OnUsage = u => usage = u };
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.User, "读一下文件"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", new string('数', 100))])
        };

        await AgentRuntime.RunLoopAsync(history, cb, CancellationToken.None, clientOverride: fake);

        Assert.NotNull(usage);
        // 100 个 CJK 字符的工具结果应计入提示词
        Assert.Equal(100, usage.PromptTokens - AgentMemory.EstimateTokens("sys") - AgentMemory.EstimateTokens("读一下文件"));
    }

    /// <summary>最小流式 IChatClient 桩：只返回纯文本，不携带 usage（模拟不返回用量字段的端点）。</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _reply;

        public FakeChatClient(string reply) => _reply = reply;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, _reply);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
