using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class AgentMemoryTests
{
    [Fact]
    public void EstimateTokens_CjkCountsAsOnePerChar()
    {
        // 10 个 CJK 字符 → 约 10 token
        var cjk = "一二三四五六七八九十";
        Assert.True(AgentMemory.EstimateTokens(cjk) >= 10);
        Assert.True(AgentMemory.EstimateTokens(cjk) <= 15);
    }

    [Fact]
    public void EstimateTokens_AsciiRoughlyOneThird()
    {
        // 90 个 ASCII 字符 → 约 30 token
        var ascii = new string('a', 90);
        Assert.True(AgentMemory.EstimateTokens(ascii) >= 25);
        Assert.True(AgentMemory.EstimateTokens(ascii) <= 35);
    }

    [Fact]
    public void EstimateTokens_EmptyIsZero()
        => Assert.Equal(0, AgentMemory.EstimateTokens(""));

    [Fact]
    public void TruncateLongToolResults_TruncatesOversized()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("call1", new string('x', 5000))])
        };

        var changed = AgentMemory.TruncateLongToolResults(history);

        Assert.True(changed);
        var result = history[1].Contents.OfType<FunctionResultContent>().First().Result?.ToString() ?? "";
        Assert.True(result.Length <= AgentMemory.MaxToolResultChars + 60);
        Assert.Contains("已截断", result);
    }

    [Fact]
    public void TruncateLongToolResults_ShortResultsUntouched()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            new(ChatRole.Tool, [new FunctionResultContent("call1", "short")])
        };

        var changed = AgentMemory.TruncateLongToolResults(history);

        Assert.False(changed);
        Assert.Equal("short", history[1].Contents.OfType<FunctionResultContent>().First().Result);
    }

    [Fact]
    public async Task PrepareHistoryAsync_WithinBudget_NoSummarization()
    {
        var fake = new FakeChatClient();
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.User, "你好"),
            new(ChatRole.Assistant, "你好！有什么可以帮你？")
        };

        var result = await AgentMemory.PrepareHistoryAsync(fake, history, budgetTokens: 6000);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, fake.SummarizeCalls);
    }

    [Fact]
    public async Task PrepareHistoryAsync_SingleMessage_ReturnsCopyNotSameReference()
    {
        // 回归测试：历史上仅 1 条（如新会话只有 system）时，
        // 返回值必须是独立副本——调用方会 Clear 原列表，
        // 若返回同一引用会把 system 连带清空（曾导致模型收不到 system）。
        var fake = new FakeChatClient();
        var history = new List<ChatMessage> { new(ChatRole.System, "sys") };

        var result = await AgentMemory.PrepareHistoryAsync(fake, history, budgetTokens: 6000);

        Assert.NotSame(history, result);
        Assert.Single(result);
        Assert.Equal(ChatRole.System, result[0].Role);

        // 模拟调用方行为：Clear 原列表后 result 必须不受影响
        history.Clear();
        Assert.Single(result);
        Assert.Equal("sys", result[0].Text);
    }

    [Fact]
    public async Task PrepareHistoryAsync_OverBudget_SummarizesOldestRound()
    {
        var fake = new FakeChatClient();
        var history = new List<ChatMessage> { new(ChatRole.System, "sys") };

        // 构造 6 轮长对话（超出小预算）
        for (var i = 0; i < 6; i++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"第 {i} 轮用户问题" + new string('啊', 400)));
            history.Add(new ChatMessage(ChatRole.Assistant, $"第 {i} 轮助手回答" + new string('哦', 400)));
        }

        var result = await AgentMemory.PrepareHistoryAsync(fake, history, budgetTokens: 2000);

        Assert.True(fake.SummarizeCalls >= 1, "超预算时应触发滚动摘要");
        Assert.Contains("【历史摘要】", result.Select(m => m.Text).FirstOrDefault(t => t?.Contains("历史摘要") == true) ?? "");
        // 摘要后总 token 应显著下降
        var total = result.Sum(m => AgentMemory.EstimateTokens(m.Text ?? ""));
        Assert.True(total <= 2000 + 500, $"摘要后 token 应回到预算内，实际 {total}");
    }

    /// <summary>最小 IChatClient 桩：记录摘要调用次数。</summary>
    private sealed class FakeChatClient : IChatClient
    {
        public int SummarizeCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SummarizeCalls++;
            var reply = new ChatMessage(ChatRole.Assistant, "已压缩的对话要点摘要：用户询问系统问题，助手给出建议。");
            return Task.FromResult(new ChatResponse(reply));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
