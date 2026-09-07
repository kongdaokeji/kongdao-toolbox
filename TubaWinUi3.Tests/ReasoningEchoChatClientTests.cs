using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Tests;

/// <summary>
/// 思考链（reasoning_content）回传与持久化测试：
/// DeepSeek 系思考模型要求曾返回过 reasoning_content 的 assistant 消息在后续请求中原样回传。
/// </summary>
public class ReasoningEchoChatClientTests
{
    [Fact]
    public void EchoReasoning_AddsReasoningContentToSdkMessage()
    {
        var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "最终回答");
        msg.Contents.Add(new TextReasoningContent("这是思考链"));

        var echoed = ReasoningEchoChatClient.EchoReasoning([msg]).Single();

        Assert.NotNull(echoed.RawRepresentation);
        var sdk = Assert.IsType<AssistantChatMessage>(echoed.RawRepresentation);
#pragma warning disable SCME0001
        var json = ModelReaderWriter.Write(sdk).ToString();
#pragma warning restore SCME0001
        Assert.Contains("\"reasoning_content\":\"这是思考链\"", json);
        Assert.Contains("\"content\":\"最终回答\"", json);
    }

    [Fact]
    public void EchoReasoning_PreservesToolCalls()
    {
        var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "调用工具");
        msg.Contents.Add(new TextReasoningContent("思考"));
        msg.Contents.Add(new FunctionCallContent("call-1", "web_search",
            new Dictionary<string, object?> { ["query"] = "显卡" }));

        var echoed = ReasoningEchoChatClient.EchoReasoning([msg]).Single();
        var sdk = Assert.IsType<AssistantChatMessage>(echoed.RawRepresentation);

        Assert.Single(sdk.ToolCalls);
        Assert.Equal("call-1", sdk.ToolCalls[0].Id);
        Assert.Equal("web_search", sdk.ToolCalls[0].FunctionName);
        var args = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(sdk.ToolCalls[0].FunctionArguments.ToString());
        Assert.Equal("显卡", args!["query"]);
#pragma warning disable SCME0001
        var json = ModelReaderWriter.Write(sdk).ToString();
#pragma warning restore SCME0001
        Assert.Contains("reasoning_content", json);
        Assert.Contains("tool_calls", json);
    }

    [Fact]
    public void EchoReasoning_PlainMessagesUnchanged()
    {
        var user = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "你好");
        var tool = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Tool, [new FunctionResultContent("call-1", "ok")]);

        var echoed = ReasoningEchoChatClient.EchoReasoning([user, tool]).ToList();

        Assert.Equal(2, echoed.Count);
        Assert.Null(echoed[0].RawRepresentation);
        Assert.Null(echoed[1].RawRepresentation);
    }

    /// <summary>思维链最大长度护栏：旧引擎回传同样截断超长 reasoning，字段保持非空。</summary>
    [Fact]
    public void EchoReasoning_OverlongReasoning_IsTruncated()
    {
        var longReasoning = new string('思', TubaChatProvider.MaxThinkingChars + 100);
        var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "回答");
        msg.Contents.Add(new TextReasoningContent(longReasoning));

        var echoed = ReasoningEchoChatClient.EchoReasoning([msg]).Single();
        var sdk = Assert.IsType<AssistantChatMessage>(echoed.RawRepresentation);
#pragma warning disable SCME0001
        var json = ModelReaderWriter.Write(sdk).ToString();
#pragma warning restore SCME0001

        Assert.Contains("[思维链过长，已截断]", json);
        Assert.True(json.Length < longReasoning.Length, "回传的 reasoning 必须被截短");
    }

    [Fact]
    public void Converter_ReasoningRoundTrips()
    {
        var msg = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "回答");
        msg.Contents.Add(new TextReasoningContent("思考过程"));

        // ChatMessage → AiChatMessage（持久化）
        var ai = AgentMessageConverter.ToAiMessages([msg]).Single();
        Assert.Equal("思考过程", ai.ReasoningContent);

        // AiChatMessage → ChatMessage（恢复）
        var restored = AgentMessageConverter.ToChatMessages([ai]).Single();
        var reasoning = Assert.Single(restored.Contents.OfType<TextReasoningContent>());
        Assert.Equal("思考过程", reasoning.Text);
    }

    [Fact]
    public void Converter_NoReasoning_StaysNull()
    {
        var ai = AiChatMessage.Assistant("普通回答");
        Assert.Null(ai.ReasoningContent);

        var restored = AgentMessageConverter.ToChatMessages([ai]).Single();
        Assert.DoesNotContain(restored.Contents, c => c is TextReasoningContent);
    }
}
