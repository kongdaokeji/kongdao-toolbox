using System.Reflection;
using System.Text.Json;
using FieldCure.Ai.Providers.Models;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// 现有 Agent 工具 → ChatPanel <see cref="IAssistTool"/> 适配器：
/// - JSON Schema 直接取自 <see cref="AIFunction"/> 自动生成的声明（不再手写）;
/// - 危险操作确认策略与旧 AgentRuntime 完全一致：AlwaysConfirm 必须确认；
///   计划/危险工具在「完全访问」模式下豁免；IsFullAccess 实时读取，开关立即生效；
/// - web_search 在技能强制触发（配电脑查价）时按旧引擎同款文案拦截，强制改用浏览器工具。
/// </summary>
internal sealed class AgentToolAdapter : IAssistTool
{
    /// <summary>工具结果最大长度（字符）：超长结果截断保留开头，控制上下文体积
    /// （与思维链截断互补，双管齐下；旧引擎 AgentMemory 截 1200，这里放宽兼容大结果）。</summary>
    private const int MaxToolResultChars = AgentToolLoopGuard.MaxToolResultChars;

    /// <summary>任意工具开始执行（供 UI 联动，如 bot 头像 orbit 态）。</summary>
    public static event Action<string>? ToolExecutionStarted;
    /// <summary>任意工具执行结束（无论成败均触发）。</summary>
    public static event Action<string>? ToolExecutionFinished;

    private readonly AgentTool _tool;
    private readonly string _parameterSchema;

    public AgentToolAdapter(AgentTool tool)
    {
        _tool = tool;
        _parameterSchema = tool.Function.JsonSchema.GetRawText();
    }

    public string Name => _tool.Name;
    public string DisplayName => _tool.DisplayName;
    public string Description => _tool.Function.Description ?? "";
    public string ParameterSchema => _parameterSchema;

    /// <summary>
    /// 是否需要用户确认（ChatPanel 据此弹出 ToolApprovalPanel）。
    /// 动态计算：完全访问开关改动的瞬间即生效，无需重建工具列表。
    /// </summary>
    public bool RequiresConfirmation =>
        _tool.AlwaysConfirm || (!AgentToolContext.IsFullAccess && (_tool.IsPlanTool || _tool.RequiresConfirmation));

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        // 技能强制触发（「电脑选购」技能要求用浏览器查京东实时价格）→ 拦截 web_search，
        // 与 AgentRuntime 中的运行时拦截语义一致。
        // 拦截计数：同一会话内第二次起直接强硬终止，防弱模型反复尝试被禁工具空转烧轮次。
        if (_tool.Name == "web_search" && AgentToolContext.SkillTriggerActive)
        {
            if (AgentToolLoopGuard.RegisterWebSearchBlocked() >= 2)
            {
                return
                    "[web_search 已禁用] 本任务中 web_search 已连续两次被拦截，请勿再尝试。请改用浏览器工具：browser_navigate / browser_get_page / browser_run_js；或基于已有信息直接总结回答。";
            }

            return
                "web_search 已被禁用：当前任务触发了「电脑选购」技能，要求用浏览器查询京东实时价格（搜索返回的不是可购买的真实价格）。请改用浏览器工具：browser_navigate 打开 https://search.jd.com/Search?keyword=商品名（URL 编码），再用 browser_get_page / browser_run_js 提取价格。";
        }

        ToolExecutionStarted?.Invoke(_tool.Name);
        try
        {
            return await ExecuteCoreAsync(parameters, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 用户主动停止：原样上抛，不算工具失败
        }
        catch (Exception ex)
        {
            // 终态错误标记：工具异常转成结构化文案回传（而不是上抛中断会话）。
            // 区分可重试（参数/调用方式问题）与系统性失败（勿重试）——旧的统一
            // "请重试"式鼓励会让模型对同一失败操作反复调用空转烧轮次（死循环放大环节）。
            return FormatToolError(ex);
        }
        finally
        {
            ToolExecutionFinished?.Invoke(_tool.Name);
        }
    }

    private async Task<string> ExecuteCoreAsync(JsonElement parameters, CancellationToken ct)
    {
        var args = AgentArgsJson.ParseToDictionary(parameters.GetRawText());

        // 重复调用护栏：非空参数且签名完全相同的第二次调用直接拦截（不真正执行），
        // 打破模型"反复调用同一操作"的循环（死循环放大环节之一）；
        // 空参数（查询类，如实时温度）豁免——允许重复取最新值。
        var normalized = NormalizeArgs(parameters);
        if (normalized is not null && AgentToolLoopGuard.IsDuplicate(_tool.Name, normalized))
        {
            return "[重复调用已拦截] 相同参数的工具调用在本轮对话中已执行过，结果不会变化。请勿重复执行，直接基于已有结果继续完成用户的目标；确有必要时先向用户说明再进行下一步。";
        }

        var result = await _tool.Function.InvokeAsync(new AIFunctionArguments(args), ct);
        var text = result?.ToString();

        // 空结果标记：避免模型把"无内容返回"误判为执行失败而无限重试（死循环放大环节之一）
        if (string.IsNullOrWhiteSpace(text))
            return "（工具已执行，未返回内容）";

        // 工具结果长度上限：超长结果截断保留开头（与思维链截断互补，控制上下文体积——
        // 上下文越满模型越容易迷失、越绕越深）；防切断 surrogate pair（emoji 等）
        if (text.Length > MaxToolResultChars)
        {
            var cut = text[..MaxToolResultChars];
            if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
            return cut + $"\n…（结果过长，已截断 {text.Length - MaxToolResultChars} 字符）";
        }
        return text;
    }

    /// <summary>
    /// 工具异常 → 回传给模型的终态错误文案。
    /// 参数类错误（可调整后重试）与系统性错误（勿重复调用）分开表述，避免模型
    /// 对同一失败操作反复调用陷入空转。自动解包 AIFunction 的反射包装异常。
    /// </summary>
    private static string FormatToolError(Exception ex)
    {
        var inner = ex;
        while (inner is TargetInvocationException { InnerException: { } tie } && tie != inner)
            inner = tie;

        var message = string.IsNullOrWhiteSpace(inner.Message) ? inner.GetType().Name : inner.Message;
        return inner is ArgumentException or JsonException or FormatException
            ? $"[工具错误] 参数无效：{message}。请检查调用参数后重试，或换一种方式完成用户的目标。"
            : $"[工具错误] 执行失败：{message}。此问题属于系统/环境层面，重复调用不会改变结果，请勿重试；请改用其他工具或直接基于已有信息总结回答。";
    }

    /// <summary>
    /// 参数规范化：委托 <see cref="AgentToolLoopGuard.NormalizeArgs"/>（新/旧引擎共用实现）。
    /// 空对象/非对象返回 null，表示不做去重（查询类工具允许重复取最新值）。
    /// </summary>
    private static string? NormalizeArgs(JsonElement parameters)
        => AgentToolLoopGuard.NormalizeArgs(parameters.GetRawText());
}