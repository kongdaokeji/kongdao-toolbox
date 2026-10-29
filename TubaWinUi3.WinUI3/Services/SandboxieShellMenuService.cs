using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>
/// Sandboxie-Plus「在沙箱中运行」资源管理器右键菜单：
/// 在 <c>HKCU\Software\Classes\Directory\Background\shell</c> 注册，
/// 文件夹背景（含桌面下的文件夹空白处）右键即可用沙盒中的资源管理器打开该文件夹。
/// 注册在 HKCU，便于随时开合，也便于「右键菜单管理」工具识别。
/// </summary>
public static class SandboxieShellMenuService
{
    private const string KeyName = "RunInSandboxTuba";
    private const string MenuText = "在沙箱中运行";

    private static string ShellKeyPath => $@"Software\Classes\Directory\Background\shell\{KeyName}";

    /// <summary>定位 Sandboxie-Plus 安装目录（含 SandboxiePlus.exe 的目录）。</summary>
    public static string? GetSandboxieDir()
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sandboxie-Plus"),
                     @"C:\Program Files\Sandboxie-Plus",
                     @"C:\Program Files (x86)\Sandboxie-Plus",
                 })
        {
            if (File.Exists(Path.Combine(dir, "SandboxiePlus.exe")))
                return dir;
        }

        return null;
    }

    public static string? GetSandboxiePlusExe() => GetSandboxieDir() is { } dir
        ? Path.Combine(dir, "SandboxiePlus.exe")
        : null;

    /// <summary>「在沙箱中运行」菜单项当前是否已注册。</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellKeyPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 注册右键菜单：命令为 <c>Start.exe /box:DefaultBox explorer.exe "%V"</c>，
    /// 即在默认沙盒中用资源管理器打开右键所在的文件夹。
    /// </summary>
    public static bool Enable()
    {
        var dir = GetSandboxieDir();
        if (dir is null) return false;

        var startExe = Path.Combine(dir, "Start.exe");
        var plusExe = Path.Combine(dir, "SandboxiePlus.exe");
        if (!File.Exists(startExe)) return false;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ShellKeyPath);
            key.SetValue(null, MenuText);
            key.SetValue("Icon", plusExe);
            using var command = key.CreateSubKey("command");
            command.SetValue(null, $"\"{startExe}\" /box:DefaultBox explorer.exe \"%V\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>移除右键菜单注册。</summary>
    public static void Disable()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ShellKeyPath, false);
        }
        catch { }
    }
}
