using System.Reflection;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

// 与 AgentRuntimeUsageTests 共享集合：静态注册表会被反射清空，须串行
[Collection("AgentToolRegistry")]
public class AgentToolRegistryTests
{
    private static readonly FieldInfo ToolsField =
        typeof(AgentToolRegistry).GetField("_tools", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void ClearRegistry()
        => ((List<AgentTool>)ToolsField.GetValue(null)!).Clear();

    [Fact]
    public void RegisterDefaults_RegistersAllToolGroups()
    {
        ClearRegistry();
        AgentToolRegistry.RegisterDefaults();

        var names = AgentToolRegistry.Tools.Select(t => t.Name).ToList();

        // 系统工具
        Assert.Contains("get_hardware_info", names);
        Assert.Contains("get_system_info", names);
        Assert.Contains("list_programs", names);
        Assert.Contains("disk_usage", names);
        Assert.Contains("network_info", names);
        Assert.Contains("list_processes", names);
        Assert.Contains("list_startup", names);
        Assert.Contains("list_services", names);
        Assert.Contains("list_tools", names);
        Assert.Contains("read_reg", names);
        Assert.Contains("write_reg", names);
        Assert.Contains("launch_tool", names);
        // 文件工具
        Assert.Contains("list_dir", names);
        Assert.Contains("get_info", names);
        Assert.Contains("read_file", names);
        Assert.Contains("write_file", names);
        Assert.Contains("edit_file", names);
        Assert.Contains("find_files", names);
        Assert.Contains("delete_file", names);
        Assert.Contains("move_file", names);
        Assert.Contains("copy_file", names);
        // 命令 / 网络 / 记忆 / 计划
        Assert.Contains("run_command", names);
        Assert.Contains("run_powershell", names);
        Assert.Contains("web_search", names);
        Assert.Contains("fetch_page", names);
        Assert.Contains("download_file", names);
        Assert.Contains("read_memory", names);
        Assert.Contains("write_memory", names);
        Assert.Contains("clear_memory", names);
        Assert.Contains("create_plan", names);

        Assert.True(AgentToolRegistry.Tools.Count >= 30, $"工具总数应 >= 30，实际 {AgentToolRegistry.Tools.Count}");
    }

    [Fact]
    public void RegisterDefaults_NoDuplicateNames()
    {
        ClearRegistry();
        AgentToolRegistry.RegisterDefaults();

        var duplicates = AgentToolRegistry.Tools
            .GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Register_DuplicateName_Throws()
    {
        ClearRegistry();
        var tool = new AgentTool
        {
            Name = "dup_tool",
            DisplayName = "重复工具",
            Glyph = "\uE721",
            Function = AIFunctionFactory.Create((Func<string, string>)(s => s),
                new AIFunctionFactoryOptions { Name = "dup_tool" })
        };

        AgentToolRegistry.Register(tool);
        Assert.Throws<InvalidOperationException>(() => AgentToolRegistry.Register(tool));
    }

    [Fact]
    public void Find_ReturnsRegisteredTool()
    {
        ClearRegistry();
        AgentToolRegistry.RegisterDefaults();

        var tool = AgentToolRegistry.Find("web_search");
        Assert.NotNull(tool);
        Assert.Equal("web_search", tool!.Name);
        Assert.Equal("联网搜索", tool.DisplayName);
        Assert.False(tool.RequiresConfirmation);
    }

    [Fact]
    public void DangerousTools_RequireConfirmation()
    {
        ClearRegistry();
        AgentToolRegistry.RegisterDefaults();

        foreach (var name in new[] { "run_command", "write_reg", "write_file", "delete_file", "download_file", "launch_tool" })
        {
            var tool = AgentToolRegistry.Find(name);
            Assert.NotNull(tool);
            Assert.True(tool!.RequiresConfirmation, $"{name} 应为危险操作");
        }

        Assert.True(AgentToolRegistry.Find("create_plan")!.IsPlanTool);
    }
}
