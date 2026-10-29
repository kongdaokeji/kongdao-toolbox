using System.IO;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class StorageUsageTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TubaStorageTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static StorageItem Item(StorageGroupKind kind, params string[] paths) => new()
    {
        Id = "test",
        Name = "测试条目",
        Description = "测试用",
        Kind = kind,
        Paths = paths
    };

    [Fact]
    public void MeasurePath_SumsNestedFiles()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[200]);
            File.WriteAllBytes(Path.Combine(root, "sub", "deep", "c.bin"), new byte[300]);

            var (size, files) = StorageUsageService.MeasurePath(root);

            Assert.Equal(600, size);
            Assert.Equal(3, files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MeasurePath_SkipsExcludedSubPath()
    {
        var root = CreateTempDir();
        try
        {
            var tools = Path.Combine(root, "Tools");
            var data = Path.Combine(root, "Data");
            Directory.CreateDirectory(tools);
            Directory.CreateDirectory(data);
            File.WriteAllBytes(Path.Combine(root, "app.exe"), new byte[50]);
            File.WriteAllBytes(Path.Combine(tools, "tool.exe"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(data, "settings.json"), new byte[500]);

            var (size, files) = StorageUsageService.MeasurePath(root, [tools, data]);

            Assert.Equal(50, size);
            Assert.Equal(1, files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MeasurePath_ExclusionIgnoresCaseAndTrailingSeparator()
    {
        var root = CreateTempDir();
        try
        {
            var tools = Path.Combine(root, "Tools");
            Directory.CreateDirectory(tools);
            File.WriteAllBytes(Path.Combine(root, "app.exe"), new byte[50]);
            File.WriteAllBytes(Path.Combine(tools, "tool.exe"), new byte[1000]);

            var (size, files) = StorageUsageService.MeasurePath(root, [tools.ToUpperInvariant() + "\\"]);

            Assert.Equal(50, size);
            Assert.Equal(1, files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MeasurePath_MissingPathReturnsZero()
    {
        var missing = Path.Combine(Path.GetTempPath(), "TubaStorageMissing_" + Guid.NewGuid().ToString("N"));

        var (size, files) = StorageUsageService.MeasurePath(missing);

        Assert.Equal(0, size);
        Assert.Equal(0, files);
    }

    [Fact]
    public void MeasurePath_SingleFileCountsAsOneFile()
    {
        var root = CreateTempDir();
        try
        {
            var file = Path.Combine(root, "single.bin");
            File.WriteAllBytes(file, new byte[321]);

            var (size, files) = StorageUsageService.MeasurePath(file);

            Assert.Equal(321, size);
            Assert.Equal(1, files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MeasureItem_SumsEveryPathAndAppliesExclusions()
    {
        var root = CreateTempDir();
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            var skipped = Path.Combine(root, "skipped");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(skipped);
            File.WriteAllBytes(Path.Combine(first, "a.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(second, "b.bin"), new byte[20]);
            File.WriteAllBytes(Path.Combine(skipped, "c.bin"), new byte[9999]);

            var item = new StorageItem
            {
                Id = "multi",
                Name = "多条路径",
                Description = "测试用",
                Kind = StorageGroupKind.Cache,
                Paths = new[] { first, second },
                ExcludedPaths = new[] { skipped }
            };

            var (size, files) = StorageUsageService.MeasureItem(item);

            Assert.Equal(30, size);
            Assert.Equal(2, files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Delete_RemovesDirectoryAndReportsFreedBytes()
    {
        var root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[400]);
            File.WriteAllBytes(Path.Combine(root, "nested", "b.bin"), new byte[600]);

            var outcome = StorageUsageService.Delete(new[] { Item(StorageGroupKind.Cache, root) });

            Assert.False(Directory.Exists(root));
            Assert.Equal(1000, outcome.FreedBytes);
            Assert.Equal(1, outcome.DeletedCount);
            Assert.Equal(0, outcome.SkippedCount);
            Assert.Empty(outcome.FailedPaths);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Delete_RemovesFileEntries()
    {
        var root = CreateTempDir();
        try
        {
            var file = Path.Combine(root, "log.txt");
            File.WriteAllBytes(file, new byte[128]);

            var outcome = StorageUsageService.Delete(new[] { Item(StorageGroupKind.Log, file) });

            Assert.False(File.Exists(file));
            Assert.Equal(128, outcome.FreedBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Delete_IgnoresItemsWithoutPaths()
    {
        var outcome = StorageUsageService.Delete(new[] { Item(StorageGroupKind.Program) });

        Assert.Equal(0, outcome.FreedBytes);
        Assert.Equal(0, outcome.DeletedCount);
        Assert.Empty(outcome.FailedPaths);
    }

    [Fact]
    public async Task ScanAsync_PopulatesEveryItemAndReportsProgress()
    {
        var root = CreateTempDir();
        var other = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[256]);
            File.WriteAllBytes(Path.Combine(other, "b.bin"), new byte[512]);

            var items = new[]
            {
                Item(StorageGroupKind.Cache, root),
                Item(StorageGroupKind.Log, other)
            };
            var progress = new SyncProgress();

            await StorageUsageService.ScanAsync(items, progress);

            Assert.Equal(256, items[0].SizeBytes);
            Assert.Equal(1, items[0].FileCount);
            Assert.Equal(512, items[1].SizeBytes);
            Assert.Equal(2, progress.Reports.Count);
            Assert.Equal(items.Length, progress.Reports[^1].Completed);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(other, true);
        }
    }

    private sealed class SyncProgress : IProgress<StorageUsageService.ScanProgress>
    {
        public List<StorageUsageService.ScanProgress> Reports { get; } = [];

        public void Report(StorageUsageService.ScanProgress value)
        {
            lock (Reports) Reports.Add(value);
        }
    }

    [Theory]
    [InlineData(StorageGroupKind.Program)]
    [InlineData(StorageGroupKind.Cache)]
    [InlineData(StorageGroupKind.Component)]
    [InlineData(StorageGroupKind.Download)]
    [InlineData(StorageGroupKind.Log)]
    [InlineData(StorageGroupKind.Temp)]
    [InlineData(StorageGroupKind.UserData)]
    public void EveryGroupKindHasLabelHintAndColor(StorageGroupKind kind)
    {
        Assert.False(string.IsNullOrWhiteSpace(StorageUsageService.GroupLabel(kind)));
        Assert.False(string.IsNullOrWhiteSpace(StorageUsageService.GroupHint(kind)));
        Assert.Matches("^#[0-9A-Fa-f]{6}$", StorageUsageService.GroupColor(kind));
    }

    [Fact]
    public void CreateCatalog_HasUniqueIdsAndFilledText()
    {
        var items = StorageUsageService.CreateCatalog();

        Assert.NotEmpty(items);
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var item in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.Equal(item.Paths.Count > 0, item.CanDelete);
        }
    }

    [Fact]
    public void CreateCatalog_NeverPreselectsDestructiveGroups()
    {
        var items = StorageUsageService.CreateCatalog();

        foreach (var item in items.Where(i =>
                     i.Kind is StorageGroupKind.UserData or StorageGroupKind.Download or StorageGroupKind.Program))
        {
            Assert.False(item.DefaultChecked);
        }
    }

    [Fact]
    public void CreateCatalog_ProgramItemExcludesTheToolsRoot()
    {
        var items = StorageUsageService.CreateCatalog();
        var program = items.Single(i => i.Id == "program");

        // Tools 目录单独成条，程序本体测量时必须排除，否则会被重复计数
        Assert.NotEmpty(program.ExcludedPaths);
    }

    [Fact]
    public void DisplayPath_SummarizesMultipleLocations()
    {
        var first = Path.Combine(Path.GetTempPath(), "first");
        var second = Path.Combine(Path.GetTempPath(), "second");

        Assert.Equal(string.Empty, Item(StorageGroupKind.Cache).DisplayPath);
        Assert.Equal(first, Item(StorageGroupKind.Cache, first).DisplayPath);
        Assert.Contains("等 2 处", Item(StorageGroupKind.Cache, first, second).DisplayPath);
    }
}
