namespace TubaWinUi3.Tests;

/// <summary>
/// 共享静态 AgentToolRegistry 状态的测试集合：
/// 集合内类串行执行，避免 RegisterDefaults/Clear 与读取方
/// （如 AgentRuntime 测试中的 RunLoopAsync 枚举工具列表）并发竞争。
/// </summary>
[CollectionDefinition("AgentToolRegistry")]
public sealed class AgentToolRegistryCollection
{
}
