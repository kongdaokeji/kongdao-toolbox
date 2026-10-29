using TubaWinUi3.Services.MemoryManagement;
namespace TubaWinUi3.Tests;
public class MemoryManagementTests
{
    [Fact] public void StrongCleanupSkipsSelfForegroundCriticalAndProtectedProcesses()
    {
        Assert.False(MemoryCleaner.CanTrim(Environment.ProcessId, -1, false, 0xFFFFFFFE, "toolbox"));
        Assert.False(MemoryCleaner.CanTrim(100, 100, false, 0xFFFFFFFE, "game"));
        Assert.False(MemoryCleaner.CanTrim(100, -1, true, 0xFFFFFFFE, "app"));
        Assert.False(MemoryCleaner.CanTrim(100, -1, false, 3, "protected"));
        Assert.False(MemoryCleaner.CanTrim(100, -1, false, 0xFFFFFFFE, "lsass"));
        Assert.True(MemoryCleaner.CanTrim(100, -1, false, 0xFFFFFFFE, "background-app"));
    }
    [Fact] public void FailedStepDoesNotPreventLaterSteps()
    {
        Assert.Contains("失败", MemoryCleaner.Step("cache", () => throw new UnauthorizedAccessException("denied")));
        Assert.Contains("成功", MemoryCleaner.Step("standby", () => { }));
    }
    [Fact] public void LogPlotMakesTinyCategoriesVisibleWithoutChangingTheirActualShares()
    {
        const ulong tiny = 4096, total = 64UL * 1024 * 1024 * 1024;
        double smallBar = MemoryPlotScale.Value(tiny, total, false, true, 1048576)!.Value;
        double largeBar = MemoryPlotScale.Value(total, total, false, true, 1048576)!.Value;
        Assert.True(smallBar / largeBar > 0.3);
        Assert.Equal("<0.001%", MemoryText.Percentage(tiny, total));
        Assert.Equal(4096d / 1048576, MemoryPlotScale.Value(tiny, total, false, false, 1048576));
        Assert.Null(MemoryPlotScale.Value(null, total, false, false, 1));
        Assert.Null(MemoryPlotScale.Value(tiny, 0, true, false, 1));
    }
    [Fact] public void MigrationPreservesOtherDrivesAndKeepsSystemWhenRequested()
    {
        var state = new PageFileState(new(false, new PageFileEntry[] { new("C:\\pagefile.sys", PageFileMode.Custom, 1024, 8192), new("E:\\pagefile.sys", PageFileMode.Custom, 2048, 4096) }), Array.Empty<PageFileUsage>());
        var draft = PageFileMigrationPlanner.Build(state, "C:\\pagefile.sys", "D:\\", "C:\\", true, PageFileMode.SystemManaged, 0, 0);
        Assert.False(draft.Automatic); Assert.Equal(3, draft.Entries.Count);
        Assert.Equal(PageFileMode.SystemManaged, draft.Entries.Single(e => e.Path.StartsWith("C:")).Mode);
        Assert.Equal(4096u, draft.Entries.Single(e => e.Path.StartsWith("E:")).MaximumMiB);
    }
    [Fact] public void MigrationCanRemoveSourceAndHandlesAutomaticRuntimeOnlyFiles()
    {
        var state = new PageFileState(new(true, Array.Empty<PageFileEntry>()), new PageFileUsage[] { new("C:\\pagefile.sys", 4096, 2048, 3000) });
        var draft = PageFileMigrationPlanner.Build(state, "C:\\pagefile.sys", "D:\\", "C:\\", false, PageFileMode.Custom, 2048, 8192);
        Assert.Single(draft.Entries); Assert.Equal("D:\\pagefile.sys", draft.Entries[0].Path); Assert.Equal(8192u, draft.Entries[0].MaximumMiB);
        Assert.Throws<ArgumentException>(() => PageFileMigrationPlanner.Build(state, "C:\\pagefile.sys", "C:\\", "C:\\", true, PageFileMode.SystemManaged, 0, 0));
    }
    [Fact] public void SmallSharesAndZeroValuesStayVisible()
    {
        Assert.Equal("<0.001%", MemoryText.Percentage(4096, 64UL * 1024 * 1024 * 1024));
        Assert.Equal("0%", MemoryText.Percentage(0, 1024)); Assert.Equal("—", MemoryText.Percentage(null, 1024));
        Assert.Equal("4 KiB", MemoryText.Bytes(4096));
    }
    [Fact] public void ReadOnlyNativeSampleStillWorks()
    {
        var sample = MemoryReader.Read(); Assert.True(sample.Total > 0); Assert.True(sample.Available <= sample.Total);
    }
    private sealed class Store : IPageFileStore
    {
        internal PageFileConfiguration Value = Original;
        internal bool FailWrite, FailRecovery, Mismatch;
        internal int Writes;
        public PageFileConfiguration Read() => Value;
        public void Write(PageFileConfiguration value)
        {
            Writes++;
            if (Writes == 2 && FailRecovery) throw new UnauthorizedAccessException("Denied");
            Value = value;
            if (Writes == 1 && FailWrite) throw new UnauthorizedAccessException("Denied");
            if (Writes == 1 && Mismatch) Value = Original;
        }
    }
    private static PageFileConfiguration Original => new(false, [new("C:\\pagefile.sys", PageFileMode.Custom, 1024, 4096)]);
    private static PageFileConfiguration Draft => new(false, [new("C:\\pagefile.sys", PageFileMode.Custom, 2048, 8192)]);
    [Fact] public void ExternalChangeAndBackupFailurePreventAllWrites()
    {
        var store = new Store { Value = Draft };
        Assert.Throws<InvalidOperationException>(() => PageFileTransaction.Apply(store, Original, Draft, _ => { })); Assert.Equal(0, store.Writes);
        store.Value = Original;
        Assert.Throws<IOException>(() => PageFileTransaction.Apply(store, Original, Draft, _ => throw new IOException("disk full"))); Assert.Equal(0, store.Writes);
    }
    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public void PartialFailureOrReadbackMismatchRestoresOriginal(bool failure, bool mismatch)
    {
        var store = new Store { FailWrite = failure, Mismatch = mismatch };
        var error = Assert.Throws<InvalidOperationException>(() => PageFileTransaction.Apply(store, Original, Draft, _ => { }));
        Assert.Contains("restored", error.Message); Assert.Equal(2, store.Writes);
        Assert.Equal(MemoryPolicy.Fingerprint(Original), MemoryPolicy.Fingerprint(store.Read()));
    }
    [Fact] public void FailedRecoveryIsReported()
    {
        var store = new Store { FailWrite = true, FailRecovery = true };
        var error = Assert.Throws<InvalidOperationException>(() => PageFileTransaction.Apply(store, Original, Draft, _ => { }));
        Assert.Contains("Recovery incomplete", error.Message); Assert.Contains("C:\\pagefile.sys", error.Message); Assert.Contains("8192", error.Message);
    }
    [Fact] public void SuccessfulTransactionBacksUpBeforeWriting()
    {
        var store = new Store(); bool backedUp = false;
        PageFileTransaction.Apply(store, Original, Draft, value => { Assert.Equal(0, store.Writes); backedUp = true; });
        Assert.True(backedUp); Assert.Equal(MemoryPolicy.Fingerprint(Draft), MemoryPolicy.Fingerprint(store.Value));
    }
    [Fact] public void NoneEntriesDoNotProduceReadbackMismatch()
    {
        Assert.Equal(MemoryPolicy.Fingerprint(new(false, [])), MemoryPolicy.Fingerprint(new(false, [new("C:\\pagefile.sys", PageFileMode.None, 0, 0)])));
        Assert.Equal(MemoryPolicy.Fingerprint(new(true, [])), MemoryPolicy.Fingerprint(new(true, Original.Entries)));
    }
}
