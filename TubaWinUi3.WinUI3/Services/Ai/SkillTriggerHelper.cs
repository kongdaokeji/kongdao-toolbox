using System.Text;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// ChatPanel 宿主页（完整版 AiAgentPage / 快捷问询小弹窗）共用的技能触发辅助：
/// 把用户消息命中触发词的检测与系统提示词注入逻辑集中在此，避免两处实现走样。
/// </summary>
public static class SkillTriggerHelper
{
    /// <summary>
    /// 检测用户消息命中的技能触发词，返回注入系统提示词的「技能强制触发」指令与完整技能章节
    /// （强度对齐旧 AgentSession 的 system + user 双注入，弱模型也可靠）。
    /// 未命中返回 (null, null)。
    /// </summary>
    public static (string? Trigger, string? Fragments) BuildTriggerPayload(
        string userText, IEnumerable<string> activeSkillIds)
    {
        if (string.IsNullOrWhiteSpace(userText)) return (null, null);

        var matched = AgentSkillRegistry.All
            .Where(s => activeSkillIds.Contains(s.Id) && s.TriggerKeywords.Length > 0)
            .Where(s => s.TriggerKeywords.Any(k => userText.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matched.Count == 0) return (null, null);

        var hits = string.Join("/", matched
            .SelectMany(s => s.TriggerKeywords)
            .Where(k => userText.Contains(k, StringComparison.OrdinalIgnoreCase)));
        var trigger = $"【技能强制触发】用户消息命中了已加载技能「{string.Join("、", matched.Select(s => s.DisplayName))}」的触发场景（命中关键词：{hits}）。" +
                      "**本次回复必须完整执行该技能的要求**——完整技能要求见下方注入的「系统指令」章节，其要求为最高优先级。" +
                      "若技能要求用浏览器查询实时价格，则**必须**调用 browser_navigate / browser_get_page / browser_run_js 操作真实浏览器页面获取价格，" +
                      "**禁止**只用 web_search / fetch_page 代替（搜索返回的不是可购买的真实价格）。" +
                      "执行完技能要求后再按正常流程回复用户。";

        var sb = new StringBuilder();
        sb.AppendLine($"【系统指令】当前时间：{DateTime.Now:yyyy年M月d日 HH:mm}。以下已加载技能已触发，本次任务必须完整执行其要求（技能要求优先于其他默认策略）：");
        sb.AppendLine();
        foreach (var skill in matched)
        {
            sb.AppendLine($"—— 技能「{skill.DisplayName}」要求 ——");
            sb.AppendLine(skill.SystemPromptFragment.Trim());
            sb.AppendLine();
        }
        sb.AppendLine("（若技能要求用浏览器查询价格：web_search 已被系统禁用，必须使用 browser_* 浏览器工具；遇到登录拦截调用 browser_wait_for_login 等待用户登录）");
        return (trigger, sb.ToString());
    }

    /// <summary>
    /// 组装 ChatPanel.SystemPrompt：基础系统提示词 + 技能索引 + （可选）触发注入。
    /// </summary>
    public static string BuildSystemPrompt(
        IEnumerable<string> activeSkillIds, string? trigger = null, string? fragments = null)
    {
        var content = AgentSession.BuildSystemPromptContent(activeSkillIds);
        if (!string.IsNullOrWhiteSpace(trigger))
            content += "\n\n" + trigger;
        if (!string.IsNullOrWhiteSpace(fragments))
            content += "\n\n" + fragments;
        return content;
    }
}