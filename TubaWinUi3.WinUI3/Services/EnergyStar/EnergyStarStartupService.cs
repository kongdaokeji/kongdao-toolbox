// EnergyStar auto-start helper for the unpackaged (non-MSIX) build.
// Ported from EnergyStarX StartupService (https://github.com/JasonWei512/EnergyStarX)
// — only the admin scheduled-task path is kept, because TubaWinUi3 is unpackaged
// (WindowsPackageType=None) and its app already runs as admin (auto-elevates via
// App.OnLaunched). MSIX StartupTask correspondence therefore does not apply here.

using System.Diagnostics;
using System.Security.Principal;

namespace TubaWinUi3.Services;

/// <summary>Schedule EnergyStar to start (throttling) at Windows logon, as admin.</summary>
public static class EnergyStarStartupService
{
    public const string ScheduleTaskName = "TubaWinUi3EnergyStarStartupTask";
    public const string SilentArg = "--energystar-silent";

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

    /// <summary>Create (or replace) the admin scheduled task. Requires UAC (already admin).</summary>
    public static async Task<bool> CreateAdminScheduleTaskAsync()
    {
        var exePath = GetExecutablePath();
        if (string.IsNullOrEmpty(exePath)) return false;

        var xml = $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
            <RegistrationInfo>
                <Description>开机自启 TubaWinUi3 后台节能 (EcoQoS 效率模式)。</Description>
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
                <Priority>7</Priority>
            </Settings>
            <Actions Context="Author">
                <Exec>
                    <Command>{{exePath}}</Command>
                    <Arguments>{{SilentArg}}</Arguments>
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
                // Already admin (the window tool runs as admin); runas would fail silently otherwise.
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
            // UAC cancelled / already-admin elevation rejected.
            try { File.Delete(xmlPath); } catch { }
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }

        return await GetAdminScheduleTaskExistsAsync();
    }

    /// <summary>Delete the scheduled task. Requires admin.</summary>
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

    private static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }
}
