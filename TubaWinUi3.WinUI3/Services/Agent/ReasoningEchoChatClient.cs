using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 思考链回传装饰器：DeepSeek 系思考模型（deepseek-v4-flash-free 等）要求
/// 对话中曾返回过 reasoning_content 的 assistant 消息在后续请求中**原样回传**，
/// 否则网关报 400（"The `reasoning_content` in the thinking mode must be passed back"）。
///
/// M.E.AI 的 OpenAI 适配器响应侧会把 reasoning_content 转为
/// <see cref="TextReasoningContent"/> 放进消息 Contents，但请求侧会丢弃它。
/// 这里在发送前把带 reasoning 的 assistant 消息重建为 OpenAI SDK 消息，
/// 通过 AssistantChatMessage.Patch 注入 reasoning_content 字段，并赋给
/// <see cref="Microsoft.Extensions.AI.ChatMessage.RawRepresentation"/> 让适配器原样透传。
/// </summary>
public sealed class ReasoningEchoChatClient : DelegatingChatClient
{
    public ReasoningEchoChatClient(IChatClient inner) : base(inner) { }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(EchoReasoning(messages).ToList(), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(EchoReasoning(messages).ToList(), options, cancellationToken);

    /// <summary>把带 reasoning 的 assistant 消息转换为带 reasoning_content 的 SDK 消息（测试可直接调用）。</summary>
    internal static IEnumerable<Microsoft.Extensions.AI.ChatMessage> EchoReasoning(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        foreach (var m in messages)
        {
            if (m.Role == ChatRole.Assistant)
            {
                var reasoning = m.Contents.OfType<TextReasoningContent>().Select(c => c.Text).ToList();
                if (reasoning.Count > 0)
                {
                    var parts = new List<ChatMessageContentPart>();
                    var toolCalls = new List<ChatToolCall>();
                    foreach (var c in m.Contents)
                    {
                        switch (c)
                        {
                            case TextContent t when !string.IsNullOrEmpty(t.Text):
                                parts.Add(ChatMessageContentPart.CreateTextPart(t.Text));
                                break;
                            case FunctionCallContent fc:
                                toolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                                    fc.CallId,
                                    fc.Name,
                                    new(JsonSerializer.SerializeToUtf8Bytes(
                                        fc.Arguments ?? new Dictionary<string, object?>()))));
                                break;
                        }
                    }

                    if (parts.Count == 0)
                        parts.Add(ChatMessageContentPart.CreateTextPart(m.Text ?? ""));

                    var sdk = new AssistantChatMessage(parts);
                    foreach (var tc in toolCalls)
                        sdk.ToolCalls.Add(tc);

#pragma warning disable SCME0001 // JsonPatch 为评估 API，但这是 SDK 唯一支持扩展字段（reasoning_content）的途径
                    // 思维链最大长度护栏：与 TubaChatProvider 同一上限，超长 reasoning 截断
                    // 再回传（防思考文本无限膨胀撑爆上下文导致工具循环死循环）；字段保持非空
                    sdk.Patch.Set("$.reasoning_content"u8, TubaChatProvider.TruncateThinking(string.Concat(reasoning))!);
#pragma warning restore SCME0001

                    m.RawRepresentation = sdk;
                }
            }

            yield return m;
        }
    }
}
