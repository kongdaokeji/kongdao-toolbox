using System.Diagnostics;

namespace TubaWinUi3.Services.MemoryManagement;

public enum MemoryCleanMode { Light, Standard, Deep }
public sealed record MemoryCleanResult(MemorySnapshot Before, MemorySnapshot Immediate, MemorySnapshot After, IReadOnlyList<string> Steps);

public static class MemoryCleaner
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static bool CanTrim(int pid, int foreground, bool critical, uint protection, string name) => pid > 4 && pid != Environment.ProcessId && pid != foreground && !critical && protection == 0xFFFFFFFE
        && !new[] { "csrss", "smss", "wininit", "winlogon", "lsass", "services", "dwm", "registry", "system", "MsMpEng", "msedgewebview2" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    internal static string Step(string name, Action action)
    {
        try { action(); return name + "：成功"; }
        catch (Exception ex) { return name + "：失败 — " + ex.Message; }
    }
    public static async Task<MemoryCleanResult> CleanAsync(MemoryCleanMode mode = MemoryCleanMode.Standard, CancellationToken token = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await Gate.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                var before = MemoryReader.Read(); var steps = new List<string>();
                void MemoryList(int command)
                {
                    using var privilege = new MemoryNative.PrivilegeScope("SeProfileSingleProcessPrivilege");
                    int status = MemoryNative.NtSetSystemInformation(80, ref command, sizeof(int));
                    if (status < 0) throw new InvalidOperationException($"权限不足或系统不支持，NTSTATUS 0x{status:X8}");
                }
                token.ThrowIfCancellationRequested();
                if (mode == MemoryCleanMode.Deep)
                {
                    steps.Add(Step("修改页写回请求", () => MemoryList(3)));
                    if (!token.IsCancellationRequested) steps.Add(Step("系统文件缓存工作集", () =>
                    {
                        using var privilege = new MemoryNative.PrivilegeScope("SeIncreaseQuotaPrivilege");
                        MemoryNative.Check(MemoryNative.SetSystemFileCacheSize(nuint.MaxValue, nuint.MaxValue, 0));
                    }));
                }
                if (mode != MemoryCleanMode.Light && !token.IsCancellationRequested)
                {
                    int trimmed = 0, skipped = 0, failed = 0;
                    var failures = new List<string>();
                    using var current = Process.GetCurrentProcess(); int session = current.SessionId;
                    foreach (var process in Process.GetProcesses())
                    {
                        using (process)
                        {
                            if (token.IsCancellationRequested) continue;
                            try
                            {
                                MemoryNative.GetWindowThreadProcessId(MemoryNative.GetForegroundWindow(), out int foreground);
                                int pid = process.Id; string name = process.ProcessName;
                                if (process.SessionId != session || pid <= 4 || pid == Environment.ProcessId || pid == foreground) { skipped++; continue; }
                                using var handle = MemoryNative.OpenProcess(0x1100, false, pid);
                                // Hold the same handle through protection checks and trimming; PID reuse cannot redirect the operation.
                                if (handle.IsInvalid || !MemoryNative.IsProcessCritical(handle, out var critical) || !MemoryNative.GetProcessInformation(handle, 7, out var protection, sizeof(uint))
                                    || !CanTrim(pid, foreground, critical, protection, name)) { skipped++; continue; }
                                MemoryNative.GetWindowThreadProcessId(MemoryNative.GetForegroundWindow(), out foreground);
                                if (pid == foreground) { skipped++; continue; }
                                MemoryNative.Check(MemoryNative.EmptyWorkingSet(handle)); trimmed++;
                            }
                            catch (Exception ex) { failed++; if (failures.Count < 5) failures.Add($"PID {process.Id}：{ex.Message}"); }
                        }
                    }
                    steps.Add($"后台进程工作集：成功 {trimmed}，跳过 {skipped}，失败 {failed}（不释放程序私有提交）" + (failures.Count > 0 ? "\n" + string.Join("\n", failures) : ""));
                }
                if (!token.IsCancellationRequested) steps.Add(Step(mode == MemoryCleanMode.Light ? "低优先级待机缓存" : "全部待机缓存", () => MemoryList(mode == MemoryCleanMode.Light ? 5 : 4)));
                else steps.Add("已取消后续操作；已完成操作不会撤销。");
                var immediate = MemoryReader.Read();
                await Task.Delay(3000); // Separate immediate results from cache refilling and normal allocation activity.
                return new MemoryCleanResult(before, immediate, MemoryReader.Read(), steps);
            });
        }
        finally { Gate.Release(); }
    }
}
