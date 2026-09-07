using Microsoft.Extensions.AI;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class AgentMessageConverterTests
{
    [Fact]
    public void RoundTrip_PreservesToolCallsAndResults()
    {
        var original = new List<AiChatMessage>
        {
            AiChatMessage.System("系统提示"),
            AiChatMessage.User("帮我查一下配置"),
            AiChatMessage.Assistant("我来查询", new List<AiToolCallItem>
            {
                new() { Id = "call_1", Name = "get_hardware_info", Arguments = "{}" }
            }),
            AiChatMessage.Tool("call_1", "CPU: i9-14900K", "get_hardware_info"),
            AiChatMessage.Assistant("查询完成：CPU 是 i9-14900K")
        };

        var chatMessages = AgentMessageConverter.ToChatMessages(original);
        var back = AgentMessageConverter.ToAiMessages(chatMessages);

        Assert.Equal(original.Count, back.Count);
        Assert.Equal("系统提示", back[0].Content);
        Assert.Equal("user", back[1].Role);
        Assert.Equal("assistant", back[2].Role);
        Assert.NotNull(back[2].ToolCalls);
        var call = Assert.Single(back[2].ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_hardware_info", call.Name);
        Assert.Equal("tool", back[3].Role);
        Assert.Equal("call_1", back[3].ToolCallId);
        Assert.Equal("CPU: i9-14900K", back[3].Content);
        Assert.Equal("查询完成：CPU 是 i9-14900K", back[4].Content);
    }

    [Fact]
    public void ToChatMessages_EmptyList_ReturnsEmpty()
        => Assert.Empty(AgentMessageConverter.ToChatMessages([]));

    [Fact]
    public void ToAiMessages_EmptyList_ReturnsEmpty()
        => Assert.Empty(AgentMessageConverter.ToAiMessages([]));

    [Fact]
    public void ToChatMessages_ParsesArgsJsonIntoDictionary()
    {
        var msg = AiChatMessage.Assistant("", new List<AiToolCallItem>
        {
            new() { Id = "call_2", Name = "web_search", Arguments = """{"query":"显卡评测","top":3}""" }
        });

        var chat = AgentMessageConverter.ToChatMessages([msg]);
        var fcc = Assert.Single(chat[0].Contents.OfType<FunctionCallContent>());

        Assert.Equal("call_2", fcc.CallId);
        Assert.Equal("web_search", fcc.Name);
        Assert.NotNull(fcc.Arguments);
        Assert.Equal("显卡评测", fcc.Arguments["query"]);
    }
}
