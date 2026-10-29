using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 扫描结果一旦交给外部就不该再变：被放弃的分片线程可能仍在 AddHit，
/// 而页面在扫描返回后会直接枚举 LockedPaths（渲染结果行、资源管理器定位都读它）。
/// 共享内部那个活 List 会与后台写入撞出「集合已被修改」异常。
/// </summary>
public class FileLockResultSnapshotTests
{
    [Fact]
    public void EntrySnapshot_IsDetachedFromSourceList()
    {
        var source = new List<string> { @"C:\temp\a.txt", @"C:\temp\b.txt" };

        var entry = FileLockService.SnapshotEntry(1234, "notepad", @"C:\Windows\notepad.exe", source, null);

        // 模拟被放弃的分片线程在扫描返回后继续追加命中
        source.Add(@"C:\temp\late.txt");

        Assert.Equal(2, entry.LockedPaths.Count);
        Assert.Equal(new[] { @"C:\temp\a.txt", @"C:\temp\b.txt" }, entry.LockedPaths);
    }
}
