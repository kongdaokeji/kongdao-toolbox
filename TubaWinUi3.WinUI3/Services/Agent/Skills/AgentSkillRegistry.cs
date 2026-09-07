using System.Text;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// Agent 技能注册表。启动时注册一次；重复 Id 抛异常。
/// 测试通过反射清理私有静态列表（与 AgentToolRegistryTests 同模式）。
/// </summary>
public static class AgentSkillRegistry
{
    private static readonly List<AgentSkill> _skills = [];

    public static IReadOnlyList<AgentSkill> All => _skills;

    public static void Register(AgentSkill skill)
    {
        if (string.IsNullOrWhiteSpace(skill.Id))
            throw new InvalidOperationException("Agent 技能 Id 不能为空");
        if (_skills.Any(s => s.Id == skill.Id))
            throw new InvalidOperationException($"Agent 技能 '{skill.Id}' 重复注册");
        _skills.Add(skill);
    }

    public static AgentSkill? Find(string id)
        => _skills.FirstOrDefault(s => s.Id == id);

    /// <summary>注册内置技能（启动时调用一次）。</summary>
    public static void RegisterDefaults()
    {
        PcBuildSkill.Register();
    }

    /// <summary>
    /// 技能触发检测（纯逻辑，可单测）：用户消息命中某个激活技能的触发关键词时，
    /// 返回要注入系统提示词末尾的强指令；未命中返回空串。
    /// </summary>
    public static string BuildTriggerFor(string userText, IEnumerable<string> activeSkillIds)
    {
        if (string.IsNullOrWhiteSpace(userText)) return "";
        var active = activeSkillIds.ToHashSet();

        foreach (var skill in _skills.Where(s => active.Contains(s.Id)))
        {
            if (skill.TriggerKeywords.Length == 0) continue;
            if (!skill.TriggerKeywords.Any(k => userText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;

            return $"【技能强制触发】用户消息命中了已加载技能「{skill.DisplayName}」的触发场景（命中关键词：{string.Join("/", skill.TriggerKeywords.Where(k => userText.Contains(k, StringComparison.OrdinalIgnoreCase)))}）。" +
                   "**本次回复必须完整执行该技能的要求**——完整技能要求见本条消息之后注入的「系统指令」（user 角色）条目，其要求为最高优先级。" +
                   "若技能要求用浏览器查询实时价格，则**必须**调用 browser_navigate / browser_get_page / browser_run_js 操作真实浏览器页面获取价格，" +
                   "**禁止**只用 web_search / fetch_page 代替（搜索返回的不是可购买的真实价格）。" +
                   "执行完技能要求后再按正常流程回复用户。";
        }

        return "";
    }

    /// <summary>
    /// 生成「已加载技能」系统提示词段落（索引，仅含名称与简介），仅包含激活的技能。
    /// 完整技能指令（SystemPromptFragment）按需加载：只在命中触发词时随「系统指令」注入，
    /// 避免每轮请求都携带完整片段浪费 token。未激活任何技能时返回空串。
    /// </summary>
    public static string BuildActiveSkillsContext(IEnumerable<AgentSkill> activeSkills)
    {
        var list = activeSkills.ToList();
        if (list.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("## 已加载技能");
        sb.AppendLine();
        sb.AppendLine("以下技能已激活：用户咨询对应场景时，按技能要求执行（完整技能要求会在触发时随「系统指令」注入，要求为最高优先级）：");
        sb.AppendLine();
        foreach (var skill in list)
            sb.AppendLine($"- {skill.DisplayName}：{skill.Description}");
        return sb.ToString().TrimEnd();
    }
}
