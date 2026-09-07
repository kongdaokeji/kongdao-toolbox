using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class AgentStepTests
{
    [Fact]
    public void StatusText_MapsAllStatuses()
    {
        Assert.Equal("执行中…", new AgentStep { Status = AgentStepStatus.Running }.StatusText);
        Assert.Equal("等待确认", new AgentStep { Status = AgentStepStatus.AwaitingConfirmation }.StatusText);
        Assert.Equal("完成", new AgentStep { Status = AgentStepStatus.Success }.StatusText);
        Assert.Equal("失败", new AgentStep { Status = AgentStepStatus.Failed }.StatusText);
        Assert.Equal("已拒绝", new AgentStep { Status = AgentStepStatus.Rejected }.StatusText);
        Assert.Equal("已取消", new AgentStep { Status = AgentStepStatus.Cancelled }.StatusText);
    }

    [Fact]
    public void GroupSummary_ToDisplayText_CountsByTool()
    {
        var summary = new AgentStepGroupSummary
        {
            Total = 4,
            Success = 3,
            Failed = 1,
            ByTool = new Dictionary<string, int>
            {
                ["联网搜索"] = 2,
                ["执行命令"] = 2
            }
        };

        var text = summary.ToDisplayText();

        // 折叠摘要：只展示做了什么（执行了命令、执行了搜索），不占版面
        Assert.Contains("执行了联网搜索×2", text);
        Assert.Contains("执行命令×2", text);
        Assert.Contains("3 成功 / 1 失败", text);
    }

    [Fact]
    public void GroupSummary_SingleStep_Text()
    {
        var summary = new AgentStepGroupSummary
        {
            Total = 1,
            Success = 1,
            ByTool = new Dictionary<string, int> { ["读取文件"] = 1 }
        };

        Assert.Equal("执行了读取文件 · 1 步完成", summary.ToDisplayText());
    }

    [Fact]
    public void GroupSummary_Empty_Text()
    {
        var summary = new AgentStepGroupSummary { Total = 0, Success = 0 };
        Assert.Equal("0 步完成", summary.ToDisplayText());
    }

    [Fact]
    public void GroupSummary_IncludesDurationAndTokens()
    {
        var summary = new AgentStepGroupSummary
        {
            Total = 3,
            Success = 3,
            Duration = TimeSpan.FromSeconds(12.4),
            PromptTokens = 1500,
            CompletionTokens = 500,
            ByTool = new Dictionary<string, int> { ["联网搜索"] = 2, ["读取文件"] = 1 }
        };

        var text = summary.ToDisplayText();

        Assert.Contains("执行了联网搜索×2", text);
        Assert.Contains("读取文件", text);
        Assert.Contains("3 步完成", text);
        Assert.Contains("耗时 12.4s", text);
        Assert.Contains("消耗 2.0k", text);
    }

    [Fact]
    public void GroupSummary_ShortDurationAndZeroTokens_Omitted()
    {
        var summary = new AgentStepGroupSummary
        {
            Total = 1,
            Success = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            PromptTokens = 0,
            CompletionTokens = 0,
            ByTool = new Dictionary<string, int> { ["读取文件"] = 1 }
        };

        Assert.Equal("执行了读取文件 · 1 步完成", summary.ToDisplayText());
    }
}
