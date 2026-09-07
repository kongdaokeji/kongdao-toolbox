using TubaWinUi3.Services.RogueCleaner;

namespace TubaWinUi3.Tests.RogueCleaner;

/// <summary>清理引擎批次清单、恢复中心数据读写与反馈脱敏测试。</summary>
public class RogueCleanerCleanerEngineTests
{
    private static DataStore TempStore()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TubaWinUi3-RogueCleaner-Tests", Guid.NewGuid().ToString("N"));
        var store = DataStore.CreateForExecutable(Path.Combine(dir, "app.exe"));
        store.Ensure();
        return store;
    }

    [Fact]
    public void CleanerEngine_ManifestRoundtrip_LoadBatches()
    {
        var store = TempStore();
        var engine = new CleanerEngine(store);
        var batch = new CleanupBatch
        {
            Id = "20260813-120000",
            CreatedAt = "2026-08-13 12:00:00",
            Path = Path.Combine(store.Backups, "20260813-120000"),
            Results =
            [
                new CleanupResult
                {
                    Id = 1,
                    Title = "测试清理项",
                    Vendor = "测试厂商",
                    Category = "开机启动",
                    ActionKind = "DeleteRegistryKey",
                    TechnicalLocation = @"HKCU\Software\Test\Key",
                    Status = "Done",
                    Message = "已处理。",
                    Target = new ActionTarget { Kind = "DeleteRegistryKey", Hive = "HKCU", View = "Registry64", SubKey = @"Software\Test\Key" }
                }
            ]
        };
        Directory.CreateDirectory(batch.Path);
        CleanerEngine.WriteJson(Path.Combine(batch.Path, "manifest.json"), batch);

        var loaded = engine.LoadBatches();
        Assert.Single(loaded);
        Assert.Equal(batch.Id, loaded[0].Id);
        Assert.Single(loaded[0].Results);
        Assert.Equal("已处理", ChineseDisplayText.CleanupStatus(loaded[0].Results[0].Status));
        Assert.Equal(@"HKCU\Software\Test\Key", loaded[0].Results[0].TechnicalLocation);
    }

    [Fact]
    public void CleanerEngine_FindOldBatchRecords_KeepsLatestAndRecent()
    {
        var store = TempStore();
        var engine = new CleanerEngine(store);
        var batches = new List<CleanupBatch>();
        for (int i = 0; i < 25; i++)
        {
            batches.Add(new CleanupBatch
            {
                Id = "2026010" + (i < 10 ? "0" + i : i.ToString()),
                CreatedAt = DateTime.Now.AddDays(-(60 - i)).ToString("yyyy-MM-dd HH:mm:ss"),
                Results = []
            });
        }
        var old = engine.FindOldBatchRecords(batches, DateTime.Now, keepLatest: 20, keepDays: 30);
        // 最近 20 批保留；另外 30 天内的保留 → 只有超过 30 天且不在最近 20 批内的才会被清理
        Assert.True(old.Count >= 1);
        Assert.All(old, b => Assert.True(b.CreatedAt.CompareTo(DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss")) < 0));
    }

    [Fact]
    public void FeedbackService_RunSelfTests_SanitizesSecrets()
    {
        var store = TempStore();
        var failures = FeedbackService.RunSelfTests(store);
        Assert.True(failures.Count == 0, "反馈脱敏自测失败：\n" + string.Join("\n", failures));
    }

    [Fact]
    public void FeedbackService_Sanitize_RemovesSecrets()
    {
        string value = "路径 C:\\Users\\Alice\\Documents\\a.exe，邮箱 alice@example.com，https://example.com/private，IP 192.168.1.9:8080，token=secret-token";
        string sanitized = FeedbackService.Sanitize(value);
        Assert.DoesNotContain("alice@example.com", sanitized);
        Assert.DoesNotContain("https://example.com", sanitized);
        Assert.DoesNotContain("192.168.1.9", sanitized);
        Assert.DoesNotContain("secret-token", sanitized);
        Assert.DoesNotContain("\\Users\\Alice", sanitized);
    }
}
