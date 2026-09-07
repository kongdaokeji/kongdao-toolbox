using System.Diagnostics;
using System.Security.Principal;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主动拦截后端开机自启（管理员计划任务）。
/// 后端需要管理员权限（写入 HKLM 屏蔽右键菜单），因此不能用普通用户 Run 键，
/// 走「登录时以最高权限运行」的计划任务，直接拉起 NativeAOT 后端（--config 已隐藏控制台窗口）。
/// </summary>
public static class ActiveInterceptStartupService
{
    public const string ScheduleTaskName = "TubaWinUi3ActiveInterceptBackend";

    public enum StartupType { None, Admin }

    public static async Task<bool> GetAdminScheduleTaskExistsAsync()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/query /tn \"{ScheduleTaskName}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        try
        {
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<StartupType> GetStartupTypeAsync()
    {
        return await GetAdminScheduleTaskExistsAsync() ? StartupType.Admin : StartupType.None;
    }

    /// <summary>创建（或覆盖）管理员计划任务：登录时以最高权限启动主动拦截后端。需管理员。</summary>
    public static async Task<bool> CreateAdminScheduleTaskAsync()
    {
        var exePath = ActiveInterceptService.BackEndExePath;
        if (!File.Exists(exePath)) return false;

        // 先把后端配置写好，计划任务启动时后端直接读取（不依赖主程序进程）。
        ActiveInterceptService.EnsureConfigWritten();

        var xml = $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
            <RegistrationInfo>
                <Description>登录时启动图吧工具箱主动拦截后端（流氓软件拦截器），需管理员权限以屏蔽第三方右键菜单。</Description>
                <URI>\{{ScheduleTaskName}}</URI>
            </RegistrationInfo>
            <Triggers>
                <LogonTrigger>
                    <Enabled>true</Enabled>
                    <UserId>{{WindowsIdentity.GetCurrent().Name}}</UserId>
                </LogonTrigger>
            </Triggers>
            <Principals>
                <Principal id="Author">
                    <LogonType>InteractiveToken</LogonType>
                    <RunLevel>HighestAvailable</RunLevel>
                </Principal>
            </Principals>
            <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                    <StopOnIdleEnd>false</StopOnIdleEnd>
                    <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
            </Settings>
            <Actions Context="Author">
                <Exec>
                    <Command>{{exePath}}</Command>
                    <Arguments>"--config" "{{ActiveInterceptService.ConfigPath}}"</Arguments>
                </Exec>
            </Actions>
            </Task>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
        await File.WriteAllTextAsync(xmlPath, xml);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/create /tn \"{ScheduleTaskName}\" /XML \"{xmlPath}\" /f",
                UseShellExecute = true,
                // 主程序本身以管理员运行，runas 兜底 UAC 授权。
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        try
        {
            process.Start();
            await process.WaitForExitAsync();
        }
        catch (Exception)
        {
            // UAC 被取消 / 非管理员环境拒绝提权。
            try { File.Delete(xmlPath); } catch { }
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }

        return await GetAdminScheduleTaskExistsAsync();
    }

    /// <summary>删除计划任务。需管理员。</summary>
    public static async Task<bool> DeleteAdminScheduleTaskAsync()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/delete /tn \"{ScheduleTaskName}\" /f",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        try
        {
            process.Start();
            await process.WaitForExitAsync();
        }
        catch (Exception)
        {
            return false;
        }
        return !await GetAdminScheduleTaskExistsAsync();
    }

    public static async Task<bool> SetStartupEnabledAsync(bool enabled)
    {
        if (enabled)
        {
            return await CreateAdminScheduleTaskAsync();
        }
        return await DeleteAdminScheduleTaskAsync();
    }
}
