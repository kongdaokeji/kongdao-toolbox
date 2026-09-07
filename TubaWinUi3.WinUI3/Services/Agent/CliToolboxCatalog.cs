using System.Text;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 工具箱 CLI 工具目录：解析仓库根目录的《CLI工具使用文档.md》。
/// 索引只含工具名 + 简介（默认上下文），完整用法（参数表/示例）按需获取。
/// 文档路径可注入（测试用），默认在输出目录 Metadata\CLI工具使用文档.md。
/// </summary>
public sealed class CliToolboxCatalog
{
    /// <summary>文档中收录 CLI 工具的章节（白名单，其余章节不解析）。</summary>
    private static readonly string[] CategoryWhitelist =
    [
        "处理器工具", "显卡工具", "硬盘工具", "综合检测", "其他工具"
    ];

    private static readonly Lazy<CliToolboxCatalog> s_default = new(
        () => new CliToolboxCatalog(Path.Combine(AppContext.BaseDirectory, "Metadata", "CLI工具使用文档.md")));

    private readonly string _docPath;
    private List<CliTool>? _tools;
    private readonly object _lock = new();

    /// <summary>默认实例：读取输出目录中捆绑的文档。</summary>
    public static CliToolboxCatalog Default => s_default.Value;

    public CliToolboxCatalog(string docPath) => _docPath = docPath;

    /// <summary>全部 CLI 工具（解析失败/文档缺失时为空列表）。</summary>
    public IReadOnlyList<CliTool> Index
    {
        get
        {
            EnsureParsed();
            return _tools ?? [];
        }
    }

    /// <summary>按名字查找（不区分大小写，支持"Autoruns / autorunsc"这类双名）。</summary>
    public CliTool? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Index.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 生成系统提示词索引：只含「分类 / 工具名 —— 简介 + 相对路径」，
    /// 并给出 Tools 目录绝对路径（AI 据此知道工具在哪个目录），不含详细用法。
    /// </summary>
    public string BuildIndexContext()
    {
        if (Index.Count == 0)
            return "## 工具箱命令行工具\n（CLI 工具文档缺失，暂无可用命令行工具）";

        var sb = new StringBuilder();
        sb.AppendLine("## 工具箱命令行工具（以下工具均可通过 run_cli_tool 执行）");
        sb.AppendLine();
        sb.AppendLine($"工具箱 Tools 目录（绝对路径）：`{ToolCatalog.ToolsRoot}`（以下工具相对路径均以它为基准）");
        sb.AppendLine();
        foreach (var group in Index.GroupBy(t => t.Category))
        {
            sb.AppendLine($"### {group.Key}");
            foreach (var tool in group)
            {
                var rel = string.IsNullOrWhiteSpace(tool.ExecutablePath) ? "" : $"（相对路径：{tool.ExecutablePath}）";
                sb.AppendLine($"- {tool.Name} —— {tool.Description}{rel}");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>把文档中的相对路径解析为 Tools 根目录下的绝对路径。</summary>
    public string ResolveExePath(string relativePath)
        => Path.Combine(ToolCatalog.ToolsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void EnsureParsed()
    {
        if (_tools is not null) return;
        lock (_lock)
        {
            if (_tools is not null) return;
            _tools = Parse();
        }
    }

    private List<CliTool> Parse()
    {
        string[] lines;
        try
        {
            if (!File.Exists(_docPath)) return [];
            lines = File.ReadAllLines(_docPath);
        }
        catch
        {
            return [];
        }

        var tools = new List<CliTool>();
        string? category = null;
        CliTool? current = null;
        var detail = new List<string>();

        void CloseCurrent()
        {
            if (current is null) return;
            current.Detail = string.Join("\n", detail).Trim();
            tools.Add(current);
            current = null;
            detail.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseCurrent();
                category = CategoryWhitelist.Contains(line[3..].Trim()) ? line[3..].Trim() : null;
            }
            else if (category is not null && line.StartsWith("### ", StringComparison.Ordinal))
            {
                CloseCurrent();
                var header = line[4..].Trim();
                var idx = header.IndexOf(" —— ", StringComparison.Ordinal);
                if (idx <= 0) continue; // 名字或简介缺失，跳过
                current = new CliTool
                {
                    Name = header[..idx].Trim(),
                    Description = header[(idx + 4)..].Trim(),
                    Category = category
                };
            }
            else if (current is not null)
            {
                detail.Add(line);
            }
        }
        CloseCurrent();

        foreach (var tool in tools)
            tool.ExecutablePath = ExtractExecutablePath(tool.Detail);

        return tools;
    }

    /// <summary>从详情里提取 `**路径**：` 行的第一个反引号段（相对 Tools 根）。</summary>
    private static string? ExtractExecutablePath(string detail)
    {
        var m = Regex.Match(detail, @"\*\*路径\*\*：`([^`]+)`");
        if (!m.Success) return null;
        return m.Groups[1].Value.Trim();
    }
}

/// <summary>一个 CLI 工具条目（索引 = 名字 + 简介；Detail = 完整用法 markdown）。</summary>
public sealed class CliTool
{
    /// <summary>工具名（文档 ### 标题，可能含双名如 "Autoruns / autorunsc"）。</summary>
    public required string Name { get; init; }

    /// <summary>所属分类（处理器工具 / 显卡工具 / …）。</summary>
    public required string Category { get; init; }

    /// <summary>简介（### 标题 "——" 后的部分）。</summary>
    public required string Description { get; init; }

    /// <summary>完整用法 markdown（路径/参数表/示例/注意事项）。</summary>
    public string Detail { get; internal set; } = "";

    /// <summary>相对 Tools 根的 exe 路径（文档 **路径** 行第一个反引号段）。</summary>
    public string? ExecutablePath { get; internal set; }
}
