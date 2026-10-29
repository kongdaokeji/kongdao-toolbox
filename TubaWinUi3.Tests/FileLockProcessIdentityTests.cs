using System.Diagnostics;
using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 结束进程前必须确认 PID 还是扫描时那一个：确认对话框（最多等 5 秒空闲 + 用户思考）期间
/// 目标可能已经退出、PID 可能被复用，只按 PID 杀会误杀无关进程。
/// 这里只测核对逻辑本身（进程名 / 映像路径 / 启动时间），不真的结束任何进程。
/// </summary>
public class FileLockProcessIdentityTests
{
    private static int CurrentPid => Environment.ProcessId;

    private static string CurrentName => Process.GetProcessById(CurrentPid).ProcessName;

    [Fact]
    public void SameProcess_MatchingIdentity_IsMatch()
    {
        ProcessIdentityCheck check = FileLockService.VerifyProcessIdentity(
            CurrentPid, CurrentName, FileLockService.QueryProcessPath(CurrentPid), null);

        Assert.Equal(ProcessIdentityCheck.Match, check);
    }

    [Fact]
    public void WrongProcessName_IsMismatch()
    {
        ProcessIdentityCheck check = FileLockService.VerifyProcessIdentity(
            CurrentPid, "definitely-not-" + CurrentName, "", null);

        Assert.Equal(ProcessIdentityCheck.Mismatch, check);
    }

    [Fact]
    public void WrongImagePath_IsMismatch()
    {
        Assert.False(string.IsNullOrEmpty(FileLockService.QueryProcessPath(CurrentPid)),
            "取不到本进程的 exe 路径，用例前提不成立");

        ProcessIdentityCheck check = FileLockService.VerifyProcessIdentity(
            CurrentPid, "", @"C:\definitely\not\this.exe", null);

        Assert.Equal(ProcessIdentityCheck.Mismatch, check);
    }

    [Fact]
    public void WrongStartTime_IsMismatch()
    {
        DateTime actual = Process.GetProcessById(CurrentPid).StartTime.ToUniversalTime();

        ProcessIdentityCheck check = FileLockService.VerifyProcessIdentity(
            CurrentPid, "", "", actual.AddHours(-1));

        Assert.Equal(ProcessIdentityCheck.Mismatch, check);
    }

    [Fact]
    public void ExitedProcess_IsExited()
    {
        ProcessIdentityCheck check = FileLockService.VerifyProcessIdentity(
            FindUnusedPid(), "whatever", "", null);

        Assert.Equal(ProcessIdentityCheck.Exited, check);
    }

    /// <summary>找一个当前肯定没人用的 PID：从现存最大 PID 往上按 4 递增（Windows 的 PID 是 4 对齐的）。</summary>
    private static int FindUnusedPid()
    {
        int candidate = Process.GetProcesses().Max(p => p.Id) + 4;
        while (Process.GetProcesses().Any(p => p.Id == candidate)) candidate += 4;
        return candidate;
    }
}
