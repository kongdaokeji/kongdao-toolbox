using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

/// <summary>《CLI工具使用文档.md》解析与 Agent 工具注册测试。</summary>
[Collection("AgentToolRegistry")]
public class CliToolboxCatalogTests
{
    private static readonly string RepoDocPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CLI工具使用文档.md"));

    private static CliToolboxCatalog CreateCatalog()
        => new(RepoDocPath);

    [Fact]
    public void Index_ParsesAllCliTools()
    {
        var catalog = CreateCatalog();
        var tools = catalog.Index;

        // 实测后移除 CLI 不可用工具（GpuTest/UltraISO）：19 个
        Assert.Equal(19, tools.Count);
        Assert.All(tools, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.False(string.IsNullOrWhiteSpace(t.Category));
            Assert.False(string.IsNullOrWhiteSpace(t.Detail));
        });
    }

    [Fact]
    public void Index_OnlyContainsWhitelistedCategories()
    {
        var categories = CreateCatalog().Index.Select(t => t.Category).Distinct().ToList();

        Assert.Equal(5, categories.Count);
        Assert.Contains("处理器工具", categories);
        Assert.Contains("显卡工具", categories);
        Assert.Contains("硬盘工具", categories);
        Assert.Contains("综合检测", categories);
        Assert.Contains("其他工具", categories);
    }

    [Fact]
    public void Find_ByExactName_IsCaseInsensitive()
    {
        var catalog = CreateCatalog();

        var tool = catalog.Find("urwtest");
        Assert.NotNull(tool);
        Assert.Equal("urwtest", tool.Name);
        Assert.Equal("硬盘工具", tool.Category);
        Assert.Contains("urwtest_v18.exe", tool.Detail);
        Assert.Equal(@"硬盘工具\URWTEST\urwtest_v18.exe", tool.ExecutablePath);

        Assert.NotNull(catalog.Find("HWINFO"));
        Assert.NotNull(catalog.Find("hwinfo"));
    }

    [Fact]
    public void Find_MultipleNameTool_MatchesByAnyPart()
    {
        var catalog = CreateCatalog();

        Assert.NotNull(catalog.Find("Autoruns"));
        Assert.NotNull(catalog.Find("autorunsc"));
        Assert.NotNull(catalog.Find("nvidiaInspector"));
        Assert.NotNull(catalog.Find("nvidiaProfileInspector"));
    }

    [Fact]
    public void Find_UnknownTool_ReturnsNull()
    {
        Assert.Null(CreateCatalog().Find("不存在的工具xyz"));
        Assert.Null(CreateCatalog().Find(""));
    }

    [Fact]
    public void ResolveExePath_ExistingTool_FileExistsOnDisk()
    {
        var catalog = CreateCatalog();
        var tool = catalog.Find("urwtest");

        var full = catalog.ResolveExePath(tool!.ExecutablePath!);
        Assert.True(File.Exists(full), $"文档路径应存在：{full}");
    }

    [Fact]
    public void BuildIndexContext_ContainsOnlyNamesDescriptionsAndPaths()
    {
        var context = CreateCatalog().BuildIndexContext();

        Assert.Contains("工具箱命令行工具", context);
        Assert.Contains("urwtest —— U 盘/SSD 读写可靠性测试", context);
        Assert.Contains("Prime95 —— CPU 烤机", context);
        // AI 应知道工具在哪个目录：绝对 Tools 根 + 每项相对路径
        Assert.Contains("工具箱 Tools 目录", context);
        Assert.Contains(@"（相对路径：硬盘工具\URWTEST\urwtest_v18.exe）", context);
        // 索引不得泄漏详细用法（参数表/路径标签）
        Assert.DoesNotContain("**路径**", context);
        Assert.DoesNotContain("参数表", context);
    }

    [Fact]
    public void DefaultCatalog_ReadsBundledDocFromOutput()
    {
        // 主项目与测试项目都把文档链进输出目录 Metadata\ 下
        var catalog = CliToolboxCatalog.Default;
        Assert.NotEmpty(catalog.Index);
    }

    [Fact]
    public void GetCliToolUsage_ReturnsDetailForKnownTool()
    {
        var usage = CliToolboxAgentTool.GetCliToolUsage("urwtest");

        Assert.Contains("urwtest", usage);
        Assert.Contains("参数", usage);
        Assert.Contains("示例", usage);
        // 附带解析后的绝对路径，AI 可直接用于 run_command 等场景
        Assert.Contains("绝对路径：", usage);
        Assert.Contains("urwtest_v18.exe", usage);
    }

    [Fact]
    public void GetCliToolUsage_UnknownTool_ReturnsErrorWithAvailableList()
    {
        var usage = CliToolboxAgentTool.GetCliToolUsage("不存在的工具");

        Assert.Contains("未找到 CLI 工具", usage);
        Assert.Contains("urwtest", usage); // 附带可用列表
    }

    [Fact]
    public void RegisterDefaults_RegistersCliToolboxTools()
    {
        ClearRegistry();
        AgentToolRegistry.RegisterDefaults();

        var cliTool = AgentToolRegistry.Find("run_cli_tool");
        var usageTool = AgentToolRegistry.Find("get_cli_tool_usage");

        Assert.NotNull(cliTool);
        Assert.True(cliTool.RequiresConfirmation, "run_cli_tool 应需用户确认");
        Assert.Equal("run_cli_tool", cliTool.ConfirmKind);
        Assert.NotNull(cliTool.DefaultReason);

        Assert.NotNull(usageTool);
        Assert.False(usageTool.RequiresConfirmation, "get_cli_tool_usage 为只读工具");
    }

    private static void ClearRegistry()
    {
        var field = typeof(AgentToolRegistry).GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        ((List<AgentTool>)field!.GetValue(null)!).Clear();
    }
}
