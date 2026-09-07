using Microsoft.Extensions.AI;
using TubaWinUi3.Services;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// Agent 工具：包装可被模型调用的 <see cref="AIFunction"/> 与展示/确认元数据。
/// 工具函数由 <see cref="AIFunctionFactory"/> 从 C# 方法自动生成 JSON Schema，
/// 不再手写参数定义。
/// </summary>
public sealed class AgentTool
{
    /// <summary>函数名（snake_case，模型调用时使用）。</summary>
    public required string Name { get; init; }

    /// <summary>中文显示名（界面展示）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>Segoe Fluent 图标字形。</summary>
    public required string Glyph { get; init; }

    /// <summary>可调用的函数。</summary>
    public required AIFunction Function { get; init; }

    /// <summary>危险操作：调用后暂停循环，弹出确认卡片，用户确认后才真正执行。</summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>
    /// 始终暂停等用户确认（即使完全访问模式也跳过确认直接执行）。
    /// 用于必须用户亲手参与的操作（如等待用户在浏览器中完成登录）。
    /// </summary>
    public bool AlwaysConfirm { get; init; }

    /// <summary>计划工具（create_plan）：确认卡片展示分步计划。</summary>
    public bool IsPlanTool { get; init; }

    /// <summary>确认卡片分类（run_command / write_file / ...），用于图标与文案。</summary>
    public string? ConfirmKind { get; init; }

    /// <summary>确认卡片缺省理由。</summary>
    public string? DefaultReason { get; init; }
}

/// <summary>
/// Agent 工具注册表。启动时注册一次；重复 ID 抛异常。
/// 测试通过反射清理私有静态列表（与 BuiltinToolRegistryTests 同模式）。
/// </summary>
public static class AgentToolRegistry
{
    private static readonly List<AgentTool> _tools = [];

    public static IReadOnlyList<AgentTool> Tools => _tools;

    public static void Register(AgentTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new InvalidOperationException("Agent 工具名称不能为空");
        if (_tools.Any(t => t.Name == tool.Name))
            throw new InvalidOperationException($"Agent 工具 '{tool.Name}' 重复注册");
        _tools.Add(tool);
    }

    public static AgentTool? Find(string name)
        => _tools.FirstOrDefault(t => t.Name == name);

    public static void RegisterDefaults()
    {
        SystemAgentTool.Register();
        FileAgentTool.Register();
        CommandAgentTool.Register();
        WebAgentTool.Register();
        BrowserAgentTool.Register();
        MemoryAgentTool.Register();
        PlanAgentTool.Register();
        CliToolboxAgentTool.Register();
    }
}

/// <summary>
/// 工具执行上下文：通过 AsyncLocal 传递给被调用的工具函数
/// （记忆工具需要访问当前会话）。
/// </summary>
public static class AgentToolContext
{
    private static readonly AsyncLocal<AgentSession?> s_current = new();

    public static AgentSession? Current
    {
        get => s_current.Value;
        internal set => s_current.Value = value;
    }

    /// <summary>
    /// 完全访问模式：AI 可直接执行命令、修改注册表等危险操作，跳过全部确认。
    /// 由设置持久化（AiFullAccessMode），UI 开关切换。
    /// </summary>
    public static bool IsFullAccess
    {
        get => string.Equals(AppSettings.Get("AiFullAccessMode"), "1", StringComparison.OrdinalIgnoreCase);
        set => AppSettings.Set("AiFullAccessMode", value ? "1" : "0");
    }

    /// <summary>
    /// 技能强制触发激活中（ChatPanel 模式）：由页面在发送消息时按触发词设定，
    /// 工具适配层据此拦截 web_search（旧 AgentSession 引擎仍走 Current?.IsSkillTriggerActive）。
    /// </summary>
    public static bool SkillTriggerActive { get; set; }

    /// <summary>
    /// 当前会话记忆文件（ChatPanel 模式下记忆工具读写目标；旧引擎经 AgentSession.Memory 读取）。
    /// </summary>
    public static ConversationMemory? ActiveMemory { get; set; }

    /// <summary>会话记忆被记忆工具修改后触发（页面据此刷新 ChatPanel.MemoryText，让后续轮次读到新记忆）。</summary>
    public static Action? MemoryModified { get; set; }
}

/// <summary>等待用户确认的工具调用快照（运行时内部使用）。</summary>
internal sealed class PendingToolCall
{
    public required string CallId { get; init; }
    public required AgentTool Tool { get; init; }
    public required string Args { get; init; }
    public required AgentStep Step { get; init; }
}
