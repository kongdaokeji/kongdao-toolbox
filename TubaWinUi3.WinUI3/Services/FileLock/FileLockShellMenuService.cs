using Microsoft.Win32;
using TubaWinUi3.ShellIntegration;

namespace TubaWinUi3.Services;

/// <summary>
/// 「文件占用查看」资源管理器右键菜单注册服务。
/// - 便携/安装版：在 HKCU\Software\Classes\{*|Directory}\shell 写经典菜单
///   （Win11 上位于「显示更多选项」），命令 = 本程序 --file-lock "%1"；
/// - MSIX 打包版（Win11）：菜单条目由包清单静态声明，处理程序（TubaWinUi3.ShellExtension）
///   在 GetState 里按标记文件返回 ECS_ENABLED/ECS_HIDDEN —— 本服务只负责写/删标记文件；
///   标记文件同时携带菜单标题/悬停提示（随界面语言变化重写）。
/// 开关的「真实状态」以注册表 / 标记文件为准（跨语言、跨提权会话共享），不依赖 AppSettings。
/// </summary>
internal static class FileLockShellMenuService
{
    internal static FileLockShellMenuContract.MenuMode Mode =>
        FileLockShellMenuContract.ResolveMode(RuntimeHelper.IsPackagedContext, Environment.OSVersion.Version.Build);

    /// <summary>当前系统/模式下是否支持右键菜单集成（MSIX + Windows 10 不支持）。</summary>
    internal static bool IsSupported => Mode != FileLockShellMenuContract.MenuMode.Unsupported;

    /// <summary>
    /// 处理程序读取标记文件的候选数据根（顺序与 ShellExtensionPaths.DataRoots 对应）：
    /// %LocalAppData% 共享真实路径 + 当前数据根（打包=LocalState / 便携=同 %LocalAppData%）。
    /// </summary>
    private static IReadOnlyList<string> DataRoots()
    {
        var roots = new List<string>();
        var shared = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(shared)) roots.Add(shared);

        var dataRoot = RuntimeHelper.GetLocalAppDataRoot();
        if (!string.IsNullOrWhiteSpace(dataRoot) &&
            !roots.Contains(dataRoot, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(dataRoot);
        }

        return roots;
    }

    /// <summary>右键菜单当前是否已开启（真实状态，非 UI 猜测）。</summary>
    internal static bool IsEnabled() => Mode switch
    {
        FileLockShellMenuContract.MenuMode.ModernMenu => FileLockShellMenuContract.MarkerExists(DataRoots()),
        FileLockShellMenuContract.MenuMode.ClassicRegistry => ClassicRegistryKeyExists(),
        _ => false,
    };

    /// <summary>开启右键菜单；返回失败原因（null = 成功）。</summary>
    internal static string? Enable(string title, string tooltip)
    {
        try
        {
            switch (Mode)
            {
                case FileLockShellMenuContract.MenuMode.ModernMenu:
                    return WriteMarkers(title, tooltip);
                case FileLockShellMenuContract.MenuMode.ClassicRegistry:
                    return EnableClassicRegistry(title) ? null : "写入注册表失败（HKCU 不可写）。";
                default:
                    return "当前系统不支持（MSIX 版的新版右键菜单需要 Windows 11）。";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>关闭右键菜单；返回失败原因（null = 成功）。</summary>
    internal static string? Disable()
    {
        try
        {
            switch (Mode)
            {
                case FileLockShellMenuContract.MenuMode.ModernMenu:
                    foreach (var root in DataRoots()) FileLockShellMenuContract.DeleteMarker(root);
                    return null;
                case FileLockShellMenuContract.MenuMode.ClassicRegistry:
                    foreach (var keyPath in FileLockShellMenuContract.ClassicRegistryKeyPaths())
                        Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
                    return null;
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>把标记文件写到所有候选数据根；全部写失败才算失败（处理程序任一处能读到即可）。</summary>
    private static string? WriteMarkers(string title, string tooltip)
    {
        var roots = DataRoots();
        if (roots.Count == 0) return "无法定位数据目录。";

        var failures = 0;
        string? lastError = null;
        foreach (var root in roots)
        {
            try
            {
                FileLockShellMenuContract.WriteMarker(root, title, tooltip);
            }
            catch (Exception ex)
            {
                failures++;
                lastError = ex.Message;
            }
        }

        return failures < roots.Count ? null : (lastError ?? "写入标记文件失败。");
    }

    private static bool ClassicRegistryKeyExists()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FileLockShellMenuContract.ClassicRegistryKeyPaths()[0]);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool EnableClassicRegistry(string title)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) return false;

        // 菜单图标用内置工具的彩色矢量图标（与卡片/桌面快捷方式同源）；生成失败只影响显示，不阻断注册。
        string? iconPath = null;
        try
        {
            var tool = BuiltinToolRegistry.GetById("file-locksmith");
            if (tool is not null) iconPath = WindowsSearchIndexService.EnsureBuiltinIcon(tool);
        }
        catch
        {
        }

        var command = FileLockShellMenuContract.BuildClassicCommand(exe);
        foreach (var keyPath in FileLockShellMenuContract.ClassicRegistryKeyPaths())
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key is null) return false;
            key.SetValue("MUIVerb", title);
            if (!string.IsNullOrEmpty(iconPath)) key.SetValue("Icon", iconPath);
            using var commandKey = key.CreateSubKey("command");
            if (commandKey is null) return false;
            commandKey.SetValue(null, command);
        }

        return true;
    }
}
