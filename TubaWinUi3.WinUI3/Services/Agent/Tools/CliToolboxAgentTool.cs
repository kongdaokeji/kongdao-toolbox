using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 工具箱 CLI 工具（依据《CLI工具使用文档.md》）：
/// get_cli_tool_usage 按需获取单个工具的完整用法（参数表/示例），
/// run_cli_tool 解析路径后执行（危险操作，需用户确认）。
/// </summary>
public static class CliToolboxAgentTool
{
    public static void Register()
    {
        Add("get_cli_tool_usage", "CLI 工具用法", "\uE7C1", false,
            (Func<string, string>)GetCliToolUsage);
        Add("run_cli_tool", "运行工具箱 CLI 工具", "\uE756", true,
            (Func<string, string, string, int?, CancellationToken, Task<string>>)RunCliToolAsync,
            "run_cli_tool", "AI 请求执行此工具箱 CLI 工具");
    }

    [Description("获取工具箱某个命令行工具的完整使用文档（绝对路径、参数表、示例、注意事项）。执行 run_cli_tool 之前必须先调用本工具，确认参数后再执行")]
    public static string GetCliToolUsage(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "错误：缺少 name 参数";
        var tool = CliToolboxCatalog.Default.Find(name);
        if (tool is null)
            return $"未找到 CLI 工具「{name}」。可用工具（名字 —— 简介）：\n"
                   + string.Join("\n", CliToolboxCatalog.Default.Index.Select(t => $"- {t.Name} —— {t.Description}"));
        var exePath = !string.IsNullOrWhiteSpace(tool.ExecutablePath)
            ? CliToolboxCatalog.Default.ResolveExePath(tool.ExecutablePath)
            : null;
        var header = exePath is null
            ? $"# {tool.Name}（{tool.Category}）\n"
            : $"# {tool.Name}（{tool.Category}）\n绝对路径：`{exePath}`\n\n";
        return header + tool.Detail;
    }

    [Description("运行工具箱内置的 CLI 工具（需用户确认后执行；支持超时，默认 60 秒；长时间工具如烤机请设置更大 timeout）。传入工具名与命令行参数（无参数时省略或传空），路径由系统自动解析")]
    public static async Task<string> RunCliToolAsync(string toolName, string? args, string reason, int? timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "错误：缺少 toolName 参数";
        var timeoutSec = Math.Clamp(timeout ?? 60, 5, 3600);
        args ??= "";

        var tool = CliToolboxCatalog.Default.Find(toolName);
        if (tool is null)
            return $"未找到 CLI 工具「{toolName}」。可用工具（名字 —— 简介）：\n"
                   + string.Join("\n", CliToolboxCatalog.Default.Index.Select(t => $"- {t.Name} —— {t.Description}"));

        if (string.IsNullOrWhiteSpace(tool.ExecutablePath))
            return $"工具「{tool.Name}」文档中未收录可执行文件路径，请先调用 get_cli_tool_usage 查看文档";

        var exePath = CliToolboxCatalog.Default.ResolveExePath(tool.ExecutablePath);
        if (!File.Exists(exePath))
            return $"工具「{tool.Name}」的可执行文件不存在：{exePath}\n请先调用 get_cli_tool_usage 核对文档路径";

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            var isScript = Path.GetExtension(exePath).ToLowerInvariant() is ".bat" or ".cmd" or ".ps1";
            var request = isScript
                ? new ScriptRunRequest
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{exePath}\" {args}",
                    OutputEncoding = Encoding.UTF8
                }
                : new ScriptRunRequest
                {
                    FileName = exePath,
                    Arguments = args,
                    OutputEncoding = Encoding.UTF8
                };

            var result = await ScriptRunnerService.RunAsync(request, ct: timeoutCts.Token);

            var sb = new StringBuilder();
            sb.AppendLine($"工具：{tool.Name}");
            sb.AppendLine($"命令：{exePath} {args}");
            if (!string.IsNullOrWhiteSpace(result.Output))
                sb.AppendLine(result.Output.Trim());
            if (!string.IsNullOrWhiteSpace(result.Error))
                sb.AppendLine($"[stderr] {result.Error.Trim()}");
            sb.AppendLine($"退出码：{result.ExitCode}");
            return sb.ToString();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "工具执行已取消";
        }
        catch (OperationCanceledException)
        {
            return $"工具执行超时（{timeoutSec} 秒），已强制终止";
        }
        catch (Exception ex)
        {
            return $"执行失败：{ex.Message}";
        }
    }

    private static void Add(string name, string displayName, string glyph, bool dangerous, Delegate method, string? confirmKind = null, string? defaultReason = null)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = dangerous,
            ConfirmKind = confirmKind,
            DefaultReason = defaultReason,
        });
    }
}
