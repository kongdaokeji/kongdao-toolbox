using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Win32;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 系统信息工具：迁移自 AiAssistantService 的只读工具实现
/// （硬件/系统/软件/磁盘/网络/进程/启动项/服务/注册表/工具箱列表）。
/// </summary>
public static class SystemAgentTool
{
    public static void Register()
    {
        Add("get_hardware_info", "硬件信息", "\uE950", false, (Func<CancellationToken, Task<string>>)GetHardwareInfoAsync);
        Add("get_system_info", "系统信息", "\uE770", false, (Func<string>)GetSystemInfo);
        Add("list_programs", "软件列表", "\uE71D", false, (Func<string>)ListPrograms);
        Add("disk_usage", "磁盘使用", "\uEDA2", false, (Func<string>)DiskUsage);
        Add("network_info", "网络信息", "\uE968", false, (Func<string>)NetworkInfo);
        Add("list_processes", "进程列表", "\uE821", false, (Func<string>)ListProcesses);
        Add("list_startup", "启动项", "\uE7E8", false, (Func<string>)ListStartup);
        Add("list_services", "服务列表", "\uE9D9", false, (Func<string?, string>)ListServices);
        Add("list_tools", "工具箱软件", "\uE790", false, (Func<string?, string>)ListTools);
        Add("read_reg", "读取注册表", "\uE8B7", false, (Func<string, string?, string>)ReadReg);
        Add("write_reg", "修改注册表", "\uE70F", true, (Func<string, string, string, string?, string, string>)WriteReg, "write_reg");
        Add("launch_tool", "启动工具", "\uE768", true, (Func<string, string, string>)LaunchTool, "launch_tool");
    }

    [Description("获取本机硬件信息（CPU、GPU、内存、主板等）")]
    public static async Task<string> GetHardwareInfoAsync(CancellationToken ct)
    {
        try
        {
            var sections = await HardwareInfoService.LoadAsync(forceRefresh: false);
            var sb = new StringBuilder();
            foreach (var section in sections)
            {
                sb.AppendLine($"### {section.Title}");
                foreach (var item in section.Items)
                {
                    sb.AppendLine($"- {item.Label}：{item.Value}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            return "获取硬件信息已取消";
        }
        catch (Exception ex)
        {
            return $"获取硬件信息失败：{ex.Message}";
        }
    }

    [Description("获取系统基本信息（OS、用户名、磁盘使用等）")]
    public static string GetSystemInfo()
        => AiAssistantService.BuildSystemInfoContext();

    [Description("获取已安装软件列表")]
    public static string ListPrograms()
    {
        var sb = new StringBuilder();
        sb.AppendLine("已安装软件列表：");
        sb.AppendLine();

        try
        {
            var regPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var regPath in regPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key is null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var name = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (seen.Contains(name)) continue;
                    seen.Add(name);
                    sb.AppendLine(FormatProgramLine(name, subKey));
                }
            }

            using var userKey = Registry.CurrentUser.OpenSubKey(regPaths[0]);
            if (userKey is not null)
            {
                foreach (var subKeyName in userKey.GetSubKeyNames())
                {
                    using var subKey = userKey.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var name = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (seen.Contains(name)) continue;
                    seen.Add(name);
                    sb.AppendLine(FormatProgramLine(name, subKey));
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string FormatProgramLine(string name, RegistryKey subKey)
    {
        var version = subKey.GetValue("DisplayVersion") as string;
        var publisher = subKey.GetValue("Publisher") as string;
        var line = $"- {name}";
        if (!string.IsNullOrEmpty(version)) line += $" (v{version})";
        if (!string.IsNullOrEmpty(publisher)) line += $" [{publisher}]";
        return line;
    }

    [Description("获取磁盘使用概况")]
    public static string DiskUsage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("磁盘使用概况：");

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var used = drive.TotalSize - drive.AvailableFreeSpace;
                var pct = (double)used / drive.TotalSize * 100;
                sb.AppendLine($"  {drive.RootDirectory.FullName} 总共 {AgentToolHelpers.FormatSize(drive.TotalSize)}，已用 {AgentToolHelpers.FormatSize(used)} ({pct:F1}%)，可用 {AgentToolHelpers.FormatSize(drive.AvailableFreeSpace)}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("获取网络信息（网卡、IP等）")]
    public static string NetworkInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("网络信息：");

        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                sb.AppendLine($"- {ni.Name} ({ni.NetworkInterfaceType})");
                sb.AppendLine($"  状态：{ni.OperationalStatus}");
                sb.AppendLine($"  速度：{ni.Speed / 1_000_000} Mbps");
                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    sb.AppendLine($"  IP：{addr.Address}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("获取进程列表（按内存排序前 50）")]
    public static string ListProcesses()
    {
        var sb = new StringBuilder();
        sb.AppendLine("运行中进程（按内存排序前 50）：");
        sb.AppendLine();

        try
        {
            var procs = Process.GetProcesses()
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                .Take(50);

            foreach (var p in procs)
            {
                try
                {
                    sb.AppendLine($"- {p.ProcessName} (PID: {p.Id}) 内存: {AgentToolHelpers.FormatSize(p.WorkingSet64)}");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("获取启动项列表")]
    public static string ListStartup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("启动项列表：");
        sb.AppendLine();

        try
        {
            var regPaths = new[]
            {
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            };

            foreach (var (hive, path) in regPaths)
            {
                using var key = hive.OpenSubKey(path);
                if (key is null) continue;

                sb.AppendLine($"[{hive.Name}\\{path}]");
                foreach (var name in key.GetValueNames())
                {
                    var val = key.GetValue(name) as string ?? "";
                    sb.AppendLine($"  {name} = {val}");
                }
                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("获取服务列表（可用关键词筛选）")]
    public static string ListServices(string? filter)
    {
        var sb = new StringBuilder();
        sb.AppendLine("系统服务列表：");
        sb.AppendLine();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = "query state= all",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc is null) return "无法获取服务列表";

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            var lines = output.Split('\n');
            var serviceName = "";
            var displayName = "";
            var state = "";
            var count = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                    serviceName = trimmed.Substring("SERVICE_NAME:".Length).Trim();
                else if (trimmed.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
                    displayName = trimmed.Substring("DISPLAY_NAME:".Length).Trim();
                else if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                        state = "运行中";
                    else if (trimmed.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                        state = "已停止";
                    else
                        state = trimmed;
                }
                else if (string.IsNullOrEmpty(trimmed) && !string.IsNullOrEmpty(serviceName))
                {
                    if (string.IsNullOrWhiteSpace(filter) ||
                        serviceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        displayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- {displayName} ({serviceName}) — {state}");
                        count++;
                        if (count >= 80)
                        {
                            sb.AppendLine("... (超过 80 项，已截断)");
                            break;
                        }
                    }
                    serviceName = "";
                    displayName = "";
                    state = "";
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("获取工具箱软件列表（可按分类过滤）")]
    public static string ListTools(string? category)
    {
        var sb = new StringBuilder();

        try
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                var tools = ToolCatalog.GetTools(category);
                sb.AppendLine($"分类 '{category}' 下的工具：");
                foreach (var tool in tools)
                {
                    var desc = string.IsNullOrWhiteSpace(tool.Description) ? "" : $" — {tool.Description}";
                    sb.AppendLine($"- {tool.Name}{desc}");
                }
            }
            else
            {
                var categories = ToolCatalog.GetCategories();
                foreach (var cat in categories)
                {
                    var tools = ToolCatalog.GetTools(cat);
                    if (tools.Count == 0) continue;
                    sb.AppendLine($"### {cat}");
                    foreach (var tool in tools)
                    {
                        var desc = string.IsNullOrWhiteSpace(tool.Description) ? "" : $" — {tool.Description}";
                        sb.AppendLine($"- {tool.Name}{desc}");
                    }
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("读取注册表值（不填 value 则列出键下所有值与子键）")]
    public static string ReadReg(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return "错误：缺少 key 参数";

        var sb = new StringBuilder();

        try
        {
            var (hive, subPath) = ParseRegKey(key);
            using var regKey = hive.OpenSubKey(subPath);
            if (regKey is null)
            {
                sb.AppendLine($"注册表键 '{key}' 不存在");
                return sb.ToString();
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                var val = regKey.GetValue(value);
                if (val is null)
                {
                    sb.AppendLine($"值 '{value}' 不存在");
                }
                else
                {
                    sb.AppendLine($"{value} = {FormatRegValue(val)} (类型: {regKey.GetValueKind(value)})");
                }
            }
            else
            {
                sb.AppendLine($"注册表键：{key}");
                sb.AppendLine("值列表：");
                foreach (var name in regKey.GetValueNames())
                {
                    var val = regKey.GetValue(name);
                    sb.AppendLine($"  {(string.IsNullOrEmpty(name) ? "(默认)" : name)} = {FormatRegValue(val ?? "")}");
                }
                sb.AppendLine("子键：");
                foreach (var sub in regKey.GetSubKeyNames())
                {
                    sb.AppendLine($"  {sub}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    [Description("修改注册表值（需用户确认后执行；type 可选 REG_SZ/REG_DWORD/REG_QWORD/REG_EXPAND_SZ/REG_BINARY）")]
    public static string WriteReg(string key, string value, string data, string? type, string reason)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return "错误：缺少 key 或 value 参数";

        try
        {
            var (hive, subPath) = ParseRegKey(key);
            using var regKey = hive.CreateSubKey(subPath, true);

            if (string.Equals(type, "REG_DWORD", StringComparison.OrdinalIgnoreCase))
            {
                regKey.SetValue(value, int.Parse(data), RegistryValueKind.DWord);
            }
            else if (string.Equals(type, "REG_QWORD", StringComparison.OrdinalIgnoreCase))
            {
                regKey.SetValue(value, long.Parse(data), RegistryValueKind.QWord);
            }
            else if (string.Equals(type, "REG_EXPAND_SZ", StringComparison.OrdinalIgnoreCase))
            {
                regKey.SetValue(value, data, RegistryValueKind.ExpandString);
            }
            else if (string.Equals(type, "REG_BINARY", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Convert.FromHexString(data.Replace(" ", ""));
                regKey.SetValue(value, bytes, RegistryValueKind.Binary);
            }
            else
            {
                regKey.SetValue(value, data, RegistryValueKind.String);
            }

            return $"成功：已设置 {key}\\{value} = {data}";
        }
        catch (Exception ex)
        {
            return $"修改失败：{ex.Message}";
        }
    }

    [Description("启动工具箱中的软件（需用户确认后执行）。toolName 必须与 list_tools 返回的名称完全一致，不确定时先调用 list_tools 查询")]
    public static string LaunchTool(string toolName, string reason)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "错误：缺少 toolName 参数";
        return AiAssistantService.TryLaunchTool(toolName, out var message)
            ? message
            : $"无法启动：{message}";
    }

    // ---------- 注册辅助 ----------

    private static void Add(string name, string displayName, string glyph, bool dangerous, Delegate method, string? confirmKind = null)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = dangerous,
            ConfirmKind = confirmKind,
        });
    }

    private static (RegistryKey hive, string subPath) ParseRegKey(string keyPath)
    {
        var parts = keyPath.Split(['\\'], 2);
        var hiveName = parts[0].ToUpperInvariant();
        var subPath = parts.Length > 1 ? parts[1] : "";

        var hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_USERS" or "HKU" => Registry.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"未知的注册表根键：{hiveName}")
        };

        return (hive, subPath);
    }

    private static string FormatRegValue(object val)
    {
        return val switch
        {
            byte[] bytes => Convert.ToHexString(bytes),
            string[] sa => string.Join("; ", sa),
            _ => val.ToString() ?? ""
        };
    }
}
