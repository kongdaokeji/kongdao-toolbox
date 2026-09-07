namespace TubaWinUi3.Services.Agent;

/// <summary>
/// Agent 技能：面向特定场景的能力包 = 注入系统提示词的指导片段 + 相关工具的调用约定。
/// 技能默认随会话加载，可在对话页头部「技能」菜单开关；
/// 提示词在每次发送时自动重建，开关在下一条消息生效。
/// </summary>
public sealed class AgentSkill
{
    /// <summary>技能 Id（snake_case）。</summary>
    public required string Id { get; init; }

    /// <summary>中文显示名（界面展示）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>Segoe Fluent 图标字形。</summary>
    public required string Glyph { get; init; }

    /// <summary>一句话简介（界面展示）。</summary>
    public required string Description { get; init; }

    /// <summary>
    /// 技能完整指导片段（触发条件 + 流程 + 工具调用约定）。按需加载：
    /// 命中触发词时才随「系统指令」注入，不常驻系统提示词（索引仅含 DisplayName + Description）。
    /// </summary>
    public required string SystemPromptFragment { get; init; }

    /// <summary>
    /// 触发关键词（子串匹配）：用户消息命中任一关键词时，系统在发送前自动把强指令注入系统提示词末尾，
    /// 不依赖模型自觉阅读技能章节（对弱模型也可靠）。
    /// </summary>
    public string[] TriggerKeywords { get; init; } = [];
}
