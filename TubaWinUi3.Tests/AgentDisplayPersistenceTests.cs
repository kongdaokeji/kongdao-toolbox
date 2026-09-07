using System.Text.Json;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class AgentDisplayPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    [Fact]
    public void StepSnapshot_RoundTrip_PreservesFields()
    {
        var snapshot = AgentStepSnapshot.From(new AgentStep
        {
            DisplayName = "联网搜索",
            Glyph = "\uE721",
            Summary = "搜索：RTX 5090 评测",
            Status = AgentStepStatus.Success,
            Result = "找到 5 条结果",
            Duration = TimeSpan.FromSeconds(3.2)
        });

        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        var back = JsonSerializer.Deserialize<AgentStepSnapshot>(json, JsonOpts);

        Assert.NotNull(back);
        Assert.Equal("联网搜索", back.DisplayName);
        Assert.Equal("搜索：RTX 5090 评测", back.Summary);
        // 状态以字符串持久化（AgentStepStatus.Success 而不是数字 2）
        Assert.Equal(AgentStepStatus.Success, back.Status);
        Assert.Contains("\"Status\":\"Success\"", json);
        Assert.Equal("找到 5 条结果", back.Result);
        Assert.Equal(3.2, back.DurationSeconds!.Value, precision: 2);
    }

    [Fact]
    public void StepSnapshot_ToAgentStep_RebuildsRowData()
    {
        var snapshot = new AgentStepSnapshot
        {
            DisplayName = "执行命令",
            Glyph = "\uE756",
            Summary = "命令：dir",
            Status = AgentStepStatus.Failed,
            Error = "命令失败：拒绝访问",
            DurationSeconds = 1.5
        };

        var step = snapshot.ToAgentStep();

        Assert.Equal("执行命令", step.DisplayName);
        Assert.Equal(AgentStepStatus.Failed, step.Status);
        Assert.Equal("命令失败：拒绝访问", step.Error);
        Assert.Equal(TimeSpan.FromSeconds(1.5), step.Duration);
        // 供 UI 行绑定使用的派生值
        Assert.Equal("失败", step.StatusText);
    }

    [Fact]
    public void DisplayItem_RoundTrip_PreservesOrderAndSteps()
    {
        var items = new List<ConversationDisplayItem>
        {
            new() { Type = "text", Role = "user", Content = "帮我安装 PCL2" },
            new() { Type = "text", Role = "assistant", Content = "好的，开始安装" },
            new()
            {
                Type = "steps",
                Steps =
                [
                    new AgentStepSnapshot { DisplayName = "联网搜索", Summary = "搜索：PCL2", Status = AgentStepStatus.Success },
                    new AgentStepSnapshot { DisplayName = "下载文件", Summary = "下载：PCL2", Status = AgentStepStatus.Success }
                ],
                SummaryText = "联网搜索×1 · 下载文件×1 · 2 步完成 · 耗时 8.3s",
                DurationSeconds = 8.3,
                PromptTokens = 1200,
                CompletionTokens = 400
            },
            new() { Type = "text", Role = "assistant", Content = "安装完成！" }
        };

        var json = JsonSerializer.Serialize(items, JsonOpts);
        var back = JsonSerializer.Deserialize<List<ConversationDisplayItem>>(json, JsonOpts);

        Assert.NotNull(back);
        Assert.Equal(4, back.Count);
        Assert.Equal("text", back[0].Type);
        Assert.Equal("帮我安装 PCL2", back[0].Content);
        Assert.Equal("steps", back[2].Type);
        Assert.Equal(2, back[2].Steps.Count);
        Assert.Equal("下载文件", back[2].Steps[1].DisplayName);
        Assert.Equal("联网搜索×1 · 下载文件×1 · 2 步完成 · 耗时 8.3s", back[2].SummaryText);
        Assert.Equal(8.3, back[2].DurationSeconds!.Value, precision: 1);
        Assert.Equal(1200, back[2].PromptTokens);
        Assert.Equal("安装完成！", back[3].Content);
    }

    [Fact]
    public void DisplayItem_EmptySteps_RoundTrips()
    {
        var item = new ConversationDisplayItem { Type = "steps", SummaryText = "1 步完成" };

        var json = JsonSerializer.Serialize(item, JsonOpts);
        var back = JsonSerializer.Deserialize<ConversationDisplayItem>(json, JsonOpts);

        Assert.NotNull(back);
        Assert.Equal("steps", back.Type);
        Assert.Empty(back.Steps);
    }

    [Fact]
    public void MetaItem_RoundTrip_PreservesTokens()
    {
        var item = new ConversationDisplayItem { Type = "meta", PromptTokens = 12345, CompletionTokens = 678 };

        var json = JsonSerializer.Serialize(item, JsonOpts);
        var back = JsonSerializer.Deserialize<ConversationDisplayItem>(json, JsonOpts);

        Assert.NotNull(back);
        Assert.Equal("meta", back.Type);
        Assert.Equal(12345, back.PromptTokens);
        Assert.Equal(678, back.CompletionTokens);
    }
}
