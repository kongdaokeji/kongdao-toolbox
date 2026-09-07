using FieldCure.Ai.Providers.Models;
using OpenAI.Chat;
using TubaWinUi3.Services.Ai;
using Xunit;

using FcChatMessage = FieldCure.Ai.Providers.Models.ChatMessage;

namespace TubaWinUi3.Tests;

/// <summary>
/// ChatPanel Provider 适配层测试：FieldCure 消息树 → OpenAI SDK 请求消息的转换，
/// 重点是思考链（reasoning_content）回传与系统提示词合并规则。
/// </summary>
public class TubaChatProviderTests
{
    private static AiRequest MakeRequest(params FcChatMessage[] messages)
        => new() { Messages = messages, SystemPrompt = "你是图吧助手" };

    [Fact]
    public void BuildMessages_PrependsSystemPrompt()
    {
        var request = MakeRequest(new FcChatMessage(ChatRole.User, "你好"));

        var result = TubaChatProvider.BuildMessages(request);

        Assert.Equal(2, result.Count);
        Assert.IsType<SystemChatMessage>(result[0]);
        Assert.Equal("你是图吧助手", result[0].Content[0].Text);
        Assert.IsType<UserChatMessage>(result[1]);
    }

    [Fact]
    public void BuildMessages_MergesIntoExistingSystemMessage()
    {
        // 恢复的会话树内首条是 system → 与请求级系统提示词合并，避免网关拒绝多个 system
        var first = new FcChatMessage(ChatRole.System, "树内已有指令");
        var request = MakeRequest(first, new FcChatMessage(ChatRole.User, "hi"));

        var result = TubaChatProvider.BuildMessages(request);

        Assert.Single(result, m => m is SystemChatMessage);
        var system = Assert.IsType<SystemChatMessage>(result[0]);
        Assert.Contains("树内已有指令", system.Content[0].Text);
        Assert.Contains("你是图吧助手", system.Content[0].Text);
    }

    [Fact]
    public void ToOpenAiMessage_MapsRoles()
    {
        Assert.IsType<UserChatMessage>(TubaChatProvider.ToOpenAiMessage(new FcChatMessage(ChatRole.User, "u")));
        Assert.IsType<SystemChatMessage>(TubaChatProvider.ToOpenAiMessage(new FcChatMessage(ChatRole.System, "s")));
        var tool = Assert.IsType<ToolChatMessage>(TubaChatProvider.ToOpenAiMessage(
            new FcChatMessage(ChatRole.Tool, "结果") { ToolCallId = "call_1" }));
        Assert.Equal("call_1", tool.ToolCallId);
        Assert.IsType<AssistantChatMessage>(TubaChatProvider.ToOpenAiMessage(new FcChatMessage(ChatRole.Assistant, "a")));
    }

    [Fact]
    public void ToAssistantMessage_CarriesToolCalls()
    {
        var msg = new FcChatMessage(ChatRole.Assistant, "我来查一下")
        {
            ToolCalls =
            [
                new ToolCall { Id = "call_1", FunctionName = "get_info", Arguments = """{"path":"C:\\"}""" },
            ]
        };

        var result = Assert.IsType<AssistantChatMessage>(TubaChatProvider.ToAssistantMessage(msg));

        Assert.Equal("我来查一下", result.Content[0].Text);
        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_info", call.FunctionName);
        Assert.Contains("C:", call.FunctionArguments.ToString());
    }

    [Fact]
    public void ToAssistantMessage_EchoesReasoningContentPatch()
    {
        var msg = new FcChatMessage(ChatRole.Assistant, "思考后的回答")
        {
            ThinkingContent = "我的思考过程"
        };

        var result = Assert.IsType<AssistantChatMessage>(TubaChatProvider.ToAssistantMessage(msg));

#pragma warning disable SCME0001 // JsonPatch 为评估 API，测试验证 reasoning_content 补丁注入
        Assert.True(result.Patch.TryGetValue("$.reasoning_content"u8, out string? reasoning));
#pragma warning restore SCME0001
        Assert.Equal("我的思考过程", reasoning);
    }

    /// <summary>
    /// 回归：多轮工具调用循环（如"打开浏览器查第五人格更新"）中，纯工具调用轮次的
    /// assistant 消息 Content 为空，历史回传时不得抛
    /// "Value cannot be an empty collection. (Parameter 'contentParts')"。
    /// </summary>
    [Fact]
    public void BuildMessages_ToolOnlyRound_WithEmptyAssistantContent_DoesNotThrow()
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "看看第五人格官方更新了什么"),
            // 第 1 轮：模型只发起工具调用，无文本 → Content 为空
            new(ChatRole.Assistant, "")
            {
                ToolCalls =
                [
                    new ToolCall { Id = "call_browser", FunctionName = "browser_navigate", Arguments = """{"url":"https://www.identityv.com/"}""" },
                ]
            },
            // 工具执行结果回填
            new(ChatRole.Tool, "页面已打开，标题：Identity V")
            {
                ToolCallId = "call_browser",
            },
        };

        var request = new AiRequest
        {
            Messages = history,
            SystemPrompt = "你是图吧助手",
        };
        var result = TubaChatProvider.BuildMessages(request);

        // 系统提示词 + 3 条历史
        Assert.Equal(4, result.Count);
        var toolRound = Assert.IsType<AssistantChatMessage>(result[2]);
        var call = Assert.Single(toolRound.ToolCalls);
        Assert.Equal("call_browser", call.Id);
        Assert.Equal("browser_navigate", call.FunctionName);
        var toolResult = Assert.IsType<ToolChatMessage>(result[3]);
        Assert.Equal("call_browser", toolResult.ToolCallId);
    }

    /// <summary>回归：纯工具调用轮次（无文本、无 reasoning）也能单独转换。</summary>
    [Fact]
    public void ToAssistantMessage_EmptyContentWithToolCalls_DoesNotThrow()
    {
        var msg = new FcChatMessage(ChatRole.Assistant, "")
        {
            ToolCalls =
            [
                new ToolCall { Id = "call_1", FunctionName = "get_info", Arguments = """{"path":"C:\\"}""" },
            ]
        };

        var result = Assert.IsType<AssistantChatMessage>(TubaChatProvider.ToAssistantMessage(msg));

        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_info", call.FunctionName);
    }

    /// <summary>
    /// 回归：DeepSeek 思考模式要求 reasoning_content 原样回传（否则网关 400）。
    /// ChatPanel 把思考累积在根气泡上，工具轮次的包装消息不带 ThinkingContent ——
    /// 协议重建时必须用根气泡的思考补齐包装消息，且根气泡本体（渲染结构）不再发送。
    /// </summary>
    [Fact]
    public void BuildMessages_ToolWrapperInheritsReasoningFromRootBubble()
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "看看第五人格官方更新了什么"),
            // 根气泡：思考累积在此（渲染结构，协议重建时被剥离，不直接发送）
            new(ChatRole.Assistant, "") { ThinkingContent = "用户想查游戏更新，需要用浏览器打开官网" },
            // 工具轮次包装消息：协议消息 = 内容 + ToolCalls + 思考回传
            new(ChatRole.Assistant, "")
            {
                ToolCalls =
                [
                    new ToolCall { Id = "call_browser", FunctionName = "browser_navigate", Arguments = """{"url":"https://www.identityv.com/"}""" },
                ]
            },
            new(ChatRole.Tool, "页面已打开，标题：Identity V") { ToolCallId = "call_browser" },
        };

        var request = new AiRequest { Messages = history, SystemPrompt = "你是图吧助手" };
        var result = TubaChatProvider.BuildMessages(request);

        // 重建后：system + user + 包装消息 + 工具结果（根气泡不再出现）
        Assert.Equal(4, result.Count);
        var wrapper = Assert.IsType<AssistantChatMessage>(result[2]);
        Assert.Contains("browser_navigate", Assert.Single(wrapper.ToolCalls).FunctionName);
#pragma warning disable SCME0001 // JsonPatch 为评估 API，测试验证 reasoning_content 补丁注入
        Assert.True(wrapper.Patch.TryGetValue("$.reasoning_content"u8, out string? wrapperThinking));
#pragma warning restore SCME0001
        Assert.Contains("浏览器", wrapperThinking);
    }

    /// <summary>
    /// 协议重建：根气泡的累积文本（r1文本+r2文本+最终文本）按包装消息逐段剥离；
    /// 纯文本结束轮剩余为独立协议消息，且带思考回传。
    /// </summary>
    [Fact]
    public void BuildMessages_RebuildsPerRoundProtocolHistory()
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "帮我查第五人格更新"),
            // 根气泡：累积三段的完整文本（渲染结构；真实流式中各轮文本精确拼接、无分隔符）
            new(ChatRole.Assistant, "打开官网翻页【更新总结】新增角色与活动")
            {
                ThinkingContent = "第一轮思考，第二轮思考，最终思考"
            },
            // 第 1 轮工具轮次
            new(ChatRole.Assistant, "打开官网")
            {
                ToolCalls = [new ToolCall { Id = "c1", FunctionName = "browser_navigate", Arguments = """{"url":"https://www.identityv.com/"}""" }]
            },
            new(ChatRole.Tool, "OK") { ToolCallId = "c1" },
            // 第 2 轮工具轮次
            new(ChatRole.Assistant, "翻页")
            {
                ToolCalls = [new ToolCall { Id = "c2", FunctionName = "browser_get_page", Arguments = "{}" }]
            },
            new(ChatRole.Tool, "页面内容") { ToolCallId = "c2" },
        };

        var request = new AiRequest { Messages = history, SystemPrompt = "你是图吧助手" };
        var result = TubaChatProvider.BuildMessages(request);

        // 期望：system + user + 轮1 + 结果1 + 轮2 + 结果2 + 最终文本 = 7 条
        Assert.Equal(7, result.Count);
        Assert.IsType<AssistantChatMessage>(result[2]);
        Assert.IsType<ToolChatMessage>(result[3]);
        var finalRound = Assert.IsType<AssistantChatMessage>(result[6]);
        Assert.Equal("【更新总结】新增角色与活动", finalRound.Content[0].Text);
#pragma warning disable SCME0001 // JsonPatch 为评估 API，测试验证 reasoning_content 补丁注入
        Assert.True(finalRound.Patch.TryGetValue("$.reasoning_content"u8, out string? finalThinking));
#pragma warning restore SCME0001
        Assert.Contains("最终思考", finalThinking);
    }

    /// <summary>协议重建：纯文本轮次（无工具调用）的根气泡在下一用户消息前成为独立协议消息。</summary>
    [Fact]
    public void BuildMessages_TextOnlyRootBecomesProtocolMessage()
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "你好"),
            new(ChatRole.Assistant, "你好！有什么可以帮你")
            {
                ThinkingContent = "用户打招呼"
            },
            new(ChatRole.User, "帮我查一下"),
        };

        var request = new AiRequest { Messages = history, SystemPrompt = "你是图吧助手" };
        var result = TubaChatProvider.BuildMessages(request);

        Assert.Equal(4, result.Count); // system + user + assistant + user
        var assistantTurn = Assert.IsType<AssistantChatMessage>(result[2]);
        Assert.Equal("你好！有什么可以帮你", assistantTurn.Content[0].Text);
#pragma warning disable SCME0001 // JsonPatch 为评估 API，测试验证 reasoning_content 补丁注入
        Assert.True(assistantTurn.Patch.TryGetValue("$.reasoning_content"u8, out string? _));
#pragma warning restore SCME0001
    }

    /// <summary>完全没有思考链的历史（非思考模型）不得凭空发明 reasoning_content。</summary>
    [Fact]
    public void BuildMessages_NoThinkingAnywhere_DoesNotInventReasoning()
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "查一下"),
            new(ChatRole.Assistant, "好的")
            {
                ToolCalls =
                [
                    new ToolCall { Id = "call_1", FunctionName = "get_info", Arguments = """{"path":"C:\\"}""" },
                ]
            },
            new(ChatRole.Tool, "结果") { ToolCallId = "call_1" },
        };

        var request = new AiRequest { Messages = history, SystemPrompt = "你是图吧助手" };
        var result = TubaChatProvider.BuildMessages(request);

        // [0]=system, [1]=user, [2]=含工具调用的 assistant
        var assistant = Assert.IsType<AssistantChatMessage>(result[2]);
#pragma warning disable SCME0001 // JsonPatch 为评估 API，测试验证 reasoning_content 补丁注入
        Assert.False(assistant.Patch.TryGetValue("$.reasoning_content"u8, out string? _));
#pragma warning restore SCME0001
    }

    /// <summary>
    /// 序列化冒烟：JsonPatch 注入的 reasoning_content 必须真的出现在 SDK 序列化后的
    /// 请求 JSON 里（与 tool_calls 同一条 assistant 消息），否则网关收不到回传。
    /// </summary>
    [Fact]
    public void Serialization_ReasoningPatchAppearsInWireJson()
    {
        var wrapper = TubaChatProvider.ToAssistantMessage(
            new FcChatMessage(ChatRole.Assistant, "")
            {
                ToolCalls =
                [
                    new ToolCall { Id = "call_1", FunctionName = "browser_navigate", Arguments = """{"url":"https://www.identityv.com/"}""" },
                ]
            },
            fallbackThinking: "用户想查游戏更新，需要用浏览器打开官网");

        var json = System.ClientModel.Primitives.ModelReaderWriter.Write(wrapper).ToString();

        Assert.Contains("reasoning_content", json);
        Assert.Contains("用户想查游戏更新", json);
        Assert.Contains("tool_calls", json);
        Assert.Contains("browser_navigate", json);
    }

    /// <summary>序列化冒烟：根气泡（思考 + 文本）同样把 reasoning_content 带上。</summary>
    [Fact]
    public void Serialization_RootBubbleReasoningInWireJson()
    {
        var root = TubaChatProvider.ToAssistantMessage(
            new FcChatMessage(ChatRole.Assistant, "正在打开官网")
            {
                ThinkingContent = "用户想查游戏更新",
            });

        var json = System.ClientModel.Primitives.ModelReaderWriter.Write(root).ToString();

        Assert.Contains("reasoning_content", json);
        Assert.Contains("用户想查游戏更新", json);
        Assert.Contains("正在打开官网", json);
    }

    /// <summary>
    /// 提取冒烟：用 DeepSeek 真实流式 chunk 格式（delta.reasoning_content）反序列化
    /// StreamingChatCompletionUpdate，验证 JsonPatch 提取路径与 Provider 完全一致。
    /// </summary>
    [Fact]
    public void StreamExtraction_DeepSeekReasoningChunk_IsExtracted()
    {
        var chunk = """
            {"id":"chatcmpl-abc","object":"chat.completion.chunk","created":1785200000,
             "model":"deepseek-v4-flash",
             "choices":[{"index":0,"delta":{"reasoning_content":"先用浏览器打开官网","content":""},"finish_reason":null}]}
            """;
        var update = System.ClientModel.Primitives.ModelReaderWriter.Read<StreamingChatCompletionUpdate>(
            BinaryData.FromString(chunk));

        // 与 TubaChatProvider.StreamCoreAsync 完全相同的提取代码
#pragma warning disable SCME0001 // JsonPatch 为评估 API
        var extracted = update.Patch.TryGetValue("$.choices[0].delta.reasoning_content"u8, out string? reasoning);
#pragma warning restore SCME0001

        Assert.True(extracted, "未能从 DeepSeek 流式 chunk 中提取 reasoning_content");
        Assert.Equal("先用浏览器打开官网", reasoning);
    }

    /// <summary>提取冒烟：工具调用 chunk（仅 tool_calls）配合思考 content。</summary>
    [Fact]
    public void StreamExtraction_ToolCallChunkWithReasoning_IsExtracted()
    {
        var chunk = """
            {"id":"chatcmpl-abc","object":"chat.completion.chunk","created":1785200000,
             "model":"deepseek-v4-flash",
             "choices":[{"index":0,"delta":{"reasoning_content":"需打开官网页面","content":"",
               "tool_calls":[{"index":0,"id":"call_1","function":{"name":"browser_navigate","arguments":""}}]},"finish_reason":null}]}
            """;
        var update = System.ClientModel.Primitives.ModelReaderWriter.Read<StreamingChatCompletionUpdate>(
            BinaryData.FromString(chunk));

#pragma warning disable SCME0001 // JsonPatch 为评估 API
        var extracted = update.Patch.TryGetValue("$.choices[0].delta.reasoning_content"u8, out string? reasoning);
#pragma warning restore SCME0001

        Assert.True(extracted, "工具调用 chunk 的 reasoning_content 必须可提取");
        Assert.Equal("需打开官网页面", reasoning);
        Assert.Single(update.ToolCallUpdates);
    }

    // ---------- 思维链最大长度护栏（防"思维链死循环"：思考文本无限膨胀撑爆上下文） ----------

    /// <summary>思维链最大长度护栏：超长 thinking 回传前被截断，保留截断标记且字段非空（满足网关回传要求）。</summary>
    [Fact]
    public void ToAssistantMessage_OverlongThinking_IsTruncatedWithMarker()
    {
        var longThinking = new string('思', TubaChatProvider.MaxThinkingChars + 500);
        var msg = new FcChatMessage(ChatRole.Assistant, "回答") { ThinkingContent = longThinking };

        var result = Assert.IsType<AssistantChatMessage>(TubaChatProvider.ToAssistantMessage(msg));
#pragma warning disable SCME0001 // JsonPatch 为评估 API，测试验证 reasoning_content 补丁注入
        Assert.True(result.Patch.TryGetValue("$.reasoning_content"u8, out string? reasoning));
#pragma warning restore SCME0001

        Assert.StartsWith(new string('思', TubaChatProvider.MaxThinkingChars), reasoning);
        Assert.EndsWith("[思维链过长，已截断]", reasoning);
        Assert.True(reasoning.Length <= TubaChatProvider.MaxThinkingChars + 32, "截断后仅允许追加标记余量");
    }

    /// <summary>思维链最大长度护栏：恰好等于上限时原样返回，不截断、不追加标记。</summary>
    [Fact]
    public void TruncateThinking_AtLimit_Unchanged()
    {
        var thinking = new string('a', TubaChatProvider.MaxThinkingChars);

        Assert.Equal(thinking, TubaChatProvider.TruncateThinking(thinking));
    }

    /// <summary>思维链最大长度护栏：null/空串原样返回，不发明 reasoning_content。</summary>
    [Fact]
    public void TruncateThinking_NullOrEmpty_Unchanged()
    {
        Assert.Null(TubaChatProvider.TruncateThinking(null));
        Assert.Equal("", TubaChatProvider.TruncateThinking(""));
    }

    /// <summary>思维链最大长度护栏：截断点落在 emoji 代理对中间时回退一位，不产生孤立代理字符。</summary>
    [Fact]
    public void TruncateThinking_CutAtSurrogatePair_NoLoneSurrogate()
    {
        // 截断点正好落在 😀（U+1F600，surrogate pair D83D DE00）中间
        var thinking = new string('a', TubaChatProvider.MaxThinkingChars - 1) + "😀" + new string('b', 200);
        var result = TubaChatProvider.TruncateThinking(thinking)!;

        Assert.StartsWith(new string('a', TubaChatProvider.MaxThinkingChars - 1), result);
        Assert.EndsWith("[思维链过长，已截断]", result);
        Assert.DoesNotContain("\uD83D", result); // 无孤立高代理字符
    }

    // ---------- 历史预算压缩（防 30 轮工具循环回传撑爆上下文） ----------

    /// <summary>构造多轮工具循环历史：每轮思考 5000 + 工具结果 4000 字符。</summary>
    private static AiRequest MakeLoopRequest(int rounds)
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "查显卡价格"),
            // 根气泡：思考累积（无工具调用的 assistant；Content 为空故不产生独立协议消息）
            new(ChatRole.Assistant, "") { ThinkingContent = new string('思', 5000) },
        };
        for (var i = 0; i < rounds; i++)
        {
            history.Add(new(ChatRole.Assistant, "")
            {
                ToolCalls =
                [
                    new ToolCall { Id = $"c{i}", FunctionName = "web_search", Arguments = """{"query":"显卡价格"}""" },
                ]
            });
            history.Add(new(ChatRole.Tool, new string('果', 4000)) { ToolCallId = $"c{i}" });
        }
        return new AiRequest { Messages = history, SystemPrompt = "你是图吧助手" };
    }

    /// <summary>30 轮大历史超过预算：压缩生效，system 与最新 user 保留，总量回落到预算内。</summary>
    [Fact]
    public void BuildMessages_OverlongHistory_TrimsOldestMessages()
    {
        var request = MakeLoopRequest(rounds: 30);

        var result = TubaChatProvider.BuildMessages(request);

        // 保留集：system + user + 最近的工具轮次
        Assert.IsType<SystemChatMessage>(result[0]);
        Assert.Contains(result, m => m is UserChatMessage);
        Assert.True(result.Count < 40, $"30 轮历史应被压缩，实际 {result.Count} 条");

        var total = result.Where(m => m is not SystemChatMessage).Sum(TubaChatProvider.RoughLength);
        Assert.True(total <= TubaChatProvider.HistoryBudgetChars, $"压缩后总量须在预算内，实际 {total}");
    }

    /// <summary>短历史不触发压缩：消息原样保留（顺序与条数不变）。</summary>
    [Fact]
    public void BuildMessages_ShortHistory_Unchanged()
    {
        var history = new List<FcChatMessage>
        {
            new(ChatRole.User, "查一下"),
            new(ChatRole.Assistant, "好的") { ToolCalls = [new ToolCall { Id = "c1", FunctionName = "get_info", Arguments = "{}" }] },
            new(ChatRole.Tool, "结果") { ToolCallId = "c1" },
        };
        var request = new AiRequest { Messages = history, SystemPrompt = "你是图吧助手" };

        var result = TubaChatProvider.BuildMessages(request);

        Assert.Equal(4, result.Count); // system + user + assistant + tool，全部保留
    }

    /// <summary>TrimHistory 直接验证：超预算时删最旧，system 与新 user 始终保留且顺序正确。</summary>
    [Fact]
    public void TrimHistory_PreservesSystemAndLatestUser()
    {
        var msgs = new List<OpenAI.Chat.ChatMessage> { OpenAI.Chat.ChatMessage.CreateSystemMessage("sys") };
        for (var i = 0; i < 30; i++)
        {
            msgs.Add(OpenAI.Chat.ChatMessage.CreateAssistantMessage(new string('a', 2000)));
            msgs.Add(OpenAI.Chat.ChatMessage.CreateToolMessage($"c{i}", new string('b', 2000)));
        }
        msgs.Add(OpenAI.Chat.ChatMessage.CreateUserMessage("当前任务"));

        var trimmed = TubaChatProvider.TrimHistory(msgs);

        Assert.IsType<SystemChatMessage>(trimmed[0]); // system 在最前
        var last = Assert.IsType<UserChatMessage>(trimmed[^1]); // 最新 user 在最后
        Assert.Equal("当前任务", last.Content[0].Text);
        Assert.True(trimmed.Count < msgs.Count, "超预算必须丢弃最旧消息");
    }
}