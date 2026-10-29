using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「文件占用查看」的端到端冒烟：真的持有某个文件的句柄，再用句柄枚举把它找出来。
/// 这条链路（CreateFileW → NtQueryObject 取内核名 → 句柄表枚举 → ObjectTypeIndex 过滤 →
/// DuplicateHandle → 匹配 → 设备名换盘符）是纯匹配单测覆盖不到的，只有在真实系统上跑才有意义。
/// </summary>
public class FileLockScanIntegrationTests
{
    /// <summary>打开时允许删除共享，这样 finally 里的清理不会被自己持有的句柄挡住。</summary>
    private const FileShare HoldShare = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>自己持有的文件句柄必须能被扫出来（否则工具的核心承诺就是假的）。</summary>
    [Fact]
    public void Scan_ForHeldFile_FindsOwningProcess()
    {
        string path = Path.Combine(Path.GetTempPath(), $"filelock-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "lock probe");

        try
        {
            using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, HoldShare))
            {
                var result = FileLockService.Scan(path, CancellationToken.None);

                Assert.True(result.Succeeded, $"扫描失败：{result.Error} / {result.ErrorDetail}");
                Assert.True(result.ScannedHandles > 0, "没有枚举到任何句柄，句柄表查询可能失效");
                Assert.True(result.Entries.Any(e => e.ProcessId == Environment.ProcessId),
                    $"未找到持有该文件的自身进程。scanned={result.ScannedHandles} guarded={result.GuardedHandles} "
                    + $"skipped={result.SkippedProcesses} truncated={result.Truncated} entries={result.Entries.Count}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// C1 回归：以「读写」方式持有的文件必须被报出来。
    ///
    /// 读写打开会被内核记成 GrantedAccess = 0x0012019F，而这个值恰好就是
    /// FILE_GENERIC_READ | FILE_GENERIC_WRITE 的展开——它曾经被当成「命名管道高危访问」
    /// 在 DuplicateHandle 之前整类跳过，于是 Excel / Word / .NET FileStream(ReadWrite)
    /// 这类最常见的占用方式被静默漏报（只增加 GuardedHandles 计数，用户看不出漏了谁）。
    /// 命名管道由 GetFileType == FILE_TYPE_DISK 过滤，不依赖 GrantedAccess。
    /// </summary>
    [Fact]
    public void Scan_ForFileHeldReadWrite_FindsOwningProcess()
    {
        string path = Path.Combine(Path.GetTempPath(), $"filelock-rw-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "lock probe");

        try
        {
            using (var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, HoldShare))
            {
                var result = FileLockService.Scan(path, CancellationToken.None);

                Assert.True(result.Succeeded, $"扫描失败：{result.Error} / {result.ErrorDetail}");
                Assert.True(result.Entries.Any(e => e.ProcessId == Environment.ProcessId),
                    $"以读写方式持有的文件没有被报出（C1 回归）。scanned={result.ScannedHandles} "
                    + $"guarded={result.GuardedHandles} skipped={result.SkippedProcesses} "
                    + $"truncated={result.Truncated} entries={result.Entries.Count}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>扫目录时要能报出具体是哪个子文件被占，且路径已还原成盘符形式（可读）。</summary>
    [Fact]
    public void Scan_ForDirectory_ReportsChildFileWithReadablePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"filelock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "held.txt");
        File.WriteAllText(file, "x");

        try
        {
            using (var hold = new FileStream(file, FileMode.Open, FileAccess.Read, HoldShare))
            {
                var result = FileLockService.Scan(dir, CancellationToken.None);

                Assert.True(result.Succeeded, $"扫描失败：{result.Error} / {result.ErrorDetail}");

            var me = result.Entries.FirstOrDefault(e => e.ProcessId == Environment.ProcessId);
            Assert.NotNull(me);
            Assert.Contains(me!.LockedPaths, p => p.EndsWith("held.txt", StringComparison.OrdinalIgnoreCase));

            // 进程名与 exe 路径都必须拿到——结果行靠它们回答「到底是谁占的」，
            // 取不到就只剩 PID，用户根本认不出是哪个程序。
            Assert.False(string.IsNullOrWhiteSpace(me.ProcessName), "进程名为空");
            Assert.False(string.IsNullOrWhiteSpace(me.ProcessPath), "进程 exe 路径为空");
            Assert.EndsWith(".exe", me.ProcessPath, StringComparison.OrdinalIgnoreCase);

            // 显示路径应已是盘符形式，而不是 \Device\HarddiskVolumeN\...
            Assert.Contains(me.LockedPaths, p => p.Contains(':'));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Scan_MissingPath_ReportsNotFound()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"filelock-missing-{Guid.NewGuid():N}");

        var result = FileLockService.Scan(missing, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(FileLockScanError.NotFound, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Scan_EmptyPath_ReportsEmptyPath(string path)
    {
        var result = FileLockService.Scan(path, CancellationToken.None);

        Assert.Equal(FileLockScanError.EmptyPath, result.Error);
    }

    /// <summary>
    /// 已取消的令牌必须让扫描以取消结束，而不是返回一份看似完整的结果。
    /// 先跑一次未取消的基准扫描，确认这个路径本身可扫——否则「提前返回」会让断言假通过/假失败。
    /// </summary>
    [Fact]
    public void Scan_CancelledToken_ThrowsOperationCanceled()
    {
        string path = Path.GetTempPath();
        var baseline = FileLockService.Scan(path, CancellationToken.None);
        Assert.True(baseline.Succeeded, $"基准扫描失败：{baseline.Error} / {baseline.ErrorDetail}");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => FileLockService.Scan(path, cts.Token));
    }
}
