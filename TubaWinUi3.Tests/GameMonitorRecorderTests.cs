using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「游戏监控」数据记录：指标声明解析、JSON/Markdown 导出与 2 小时上限相关逻辑。
/// 这些格式是用户拿到的成品文件，改动导出结构时应当先让本组测试通过。
/// </summary>
public class GameMonitorRecorderTests
{
    private static List<MonitorRecordMetric> Metrics(params string[] keys) =>
        GameMonitorRecorder.AllMetrics.Where(m => keys.Contains(m.Key)).ToList();

    private static MonitorRecordSample Sample(double seconds, params float[] values) =>
        new() { Seconds = seconds, Values = values };

    private static MonitorRecordMeta Meta() => new()
    {
        StartTime = new DateTime(2026, 9, 10, 21, 0, 0),
        EndTime = new DateTime(2026, 9, 10, 21, 0, 10),
        IntervalMs = 1000,
        TargetWindow = "game — 测试窗口",
        CpuName = "Test CPU",
        GpuName = "Test GPU",
        FpsProcess = "game",
        DurationSeconds = 10
    };

    [Fact]
    public void MetricKeys_AreUnique()
    {
        var keys = GameMonitorRecorder.AllMetrics.Select(m => m.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ParseSelection_NullMeansEverythingSelected()
    {
        Assert.Equal(GameMonitorRecorder.AllMetrics.Count, GameMonitorRecorder.ParseSelection(null).Count);
    }

    [Fact]
    public void ParseSelection_KeepsDeclarationOrderAndDropsUnknownKeys()
    {
        var list = GameMonitorRecorder.ParseSelection("gputemp,bogus,fps");
        Assert.Equal(new[] { "fps", "gputemp" }, list.Select(m => m.Key).ToArray());
    }

    [Fact]
    public void NeedsFps_OnlyTrueWhenFpsGroupSelected()
    {
        Assert.True(GameMonitorRecorder.NeedsFps(Metrics("fps")));
        Assert.True(GameMonitorRecorder.NeedsFps(Metrics("frametime")));
        Assert.False(GameMonitorRecorder.NeedsFps(Metrics("cputemp", "memload")));
    }

    [Fact]
    public void BuildJson_EmitsMetricHeaderAndRawSamples()
    {
        var metrics = Metrics("fps", "cputemp");
        var samples = new List<MonitorRecordSample> { Sample(0, 60, 55), Sample(1, 59, -1) };

        var json = GameMonitorRecorder.BuildJson(metrics, samples, Meta());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("sampleCount").GetInt32());
        Assert.Equal(2, root.GetProperty("metrics").GetArrayLength());
        Assert.Equal("fps", root.GetProperty("metrics")[0].GetProperty("key").GetString());
        Assert.Equal("°C", root.GetProperty("metrics")[1].GetProperty("unit").GetString());

        var rows = root.GetProperty("samples");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(0, rows[0][0].GetDouble());
        Assert.Equal(60, rows[0][1].GetDouble());
        // 不可用哨兵值原样保留，便于下游自行判断
        Assert.Equal(-1, rows[1][2].GetDouble());
        Assert.Equal("game — 测试窗口", root.GetProperty("target").GetProperty("window").GetString());
    }

    [Fact]
    public void BuildMarkdown_SkipsUnavailableValuesInStats()
    {
        var metrics = Metrics("fps");
        var samples = new List<MonitorRecordSample> { Sample(0, 60), Sample(1, -1), Sample(2, 30) };

        var md = GameMonitorRecorder.BuildMarkdown(metrics, samples, Meta());

        Assert.Contains("# 游戏监控记录", md);
        Assert.Contains("## 统计摘要", md);
        Assert.Contains("## 采样明细", md);
        // 有效样本 2 条：最小 30、平均 45、最大 60
        Assert.Contains("| 30.00 | 45.00 | 60.00 |", md);
        // 不可用样本在明细里显示为 --
        Assert.Contains("| -- |", md);
    }

    [Fact]
    public void BuildMarkdown_EscapesPipeInWindowTitle()
    {
        var meta = Meta();
        meta.TargetWindow = "weird|title";
        var md = GameMonitorRecorder.BuildMarkdown(Metrics("fps"), [Sample(0, 60)], meta);
        Assert.Contains(@"weird\|title", md);
    }

    [Fact]
    public void BuildMarkdown_MarksTruncatedRecording()
    {
        var meta = Meta();
        meta.Truncated = true;
        var md = GameMonitorRecorder.BuildMarkdown(Metrics("fps"), [Sample(0, 60)], meta);
        Assert.Contains($"{GameMonitorRecorder.MaxDurationMinutes} 分钟上限", md);
    }

    [Fact]
    public void DetailRowIndexes_CapsRowsForLongRecordings()
    {
        // 2 小时 @200ms ≈ 36000 条：明细必须被抽稀，但始终保留首尾
        const int total = 36000;
        var indexes = GameMonitorRecorder.DetailRowIndexes(total);
        Assert.True(indexes.Count <= GameMonitorRecorder.MaxDetailRows + 1, $"实际 {indexes.Count} 行");
        Assert.Equal(0, indexes[0]);
        Assert.Equal(total - 1, indexes[^1]);
        Assert.Equal(indexes, indexes.Distinct().ToList());
    }

    [Fact]
    public void DetailRowIndexes_KeepsEverySampleForShortRecordings()
    {
        Assert.Equal(new[] { 0, 1, 2 }, GameMonitorRecorder.DetailRowIndexes(3));
        Assert.Empty(GameMonitorRecorder.DetailRowIndexes(0));
    }

    [Fact]
    public void Percentile_InterpolatesAndHandlesEdgeCases()
    {
        var sorted = new List<float> { 10, 20, 30, 40 };
        Assert.Equal(10, GameMonitorRecorder.Percentile(sorted, 0));
        Assert.Equal(40, GameMonitorRecorder.Percentile(sorted, 100));
        Assert.Equal(25, GameMonitorRecorder.Percentile(sorted, 50));
        Assert.Equal(10, GameMonitorRecorder.Percentile(new List<float> { 10 }, 99));
        Assert.True(double.IsNaN(GameMonitorRecorder.Percentile([], 50)));
    }

    [Fact]
    public void MaxDuration_IsTwoHours()
    {
        Assert.Equal(120, GameMonitorRecorder.MaxDurationMinutes);
    }

    [Fact]
    public void AllMetrics_CoversBatteryGroup()
    {
        var keys = GameMonitorRecorder.AllMetrics.Select(m => m.Key).ToList();
        Assert.Contains("batpercent", keys);
        Assert.Contains("batpower", keys);
        Assert.Contains(GameMonitorRecorder.AllMetrics, m => m.Group == "电池");
    }

    [Fact]
    public void ParseOutputs_DefaultsToJsonAndMarkdown()
    {
        Assert.Equal(GameMonitorRecorder.DefaultOutputs, GameMonitorRecorder.ParseOutputs(null));
        Assert.True(GameMonitorRecorder.DefaultOutputs.HasFlag(MonitorRecordOutput.Json));
        Assert.True(GameMonitorRecorder.DefaultOutputs.HasFlag(MonitorRecordOutput.Markdown));
        Assert.False(GameMonitorRecorder.DefaultOutputs.HasFlag(MonitorRecordOutput.Csv));
    }

    [Fact]
    public void Outputs_RoundTripThroughSettings()
    {
        var all = MonitorRecordOutput.Json | MonitorRecordOutput.Markdown | MonitorRecordOutput.Csv;
        Assert.Equal(all, GameMonitorRecorder.ParseOutputs(GameMonitorRecorder.SerializeOutputs(all)));
        Assert.Equal(MonitorRecordOutput.Csv, GameMonitorRecorder.ParseOutputs("csv"));
        Assert.Equal(MonitorRecordOutput.None, GameMonitorRecorder.ParseOutputs(""));
        Assert.Equal(MonitorRecordOutput.None, GameMonitorRecorder.ParseOutputs("bogus"));
    }

    [Fact]
    public void BuildCsv_HeaderFollowsMetricOrderAndLeavesUnavailableBlank()
    {
        var metrics = Metrics("fps", "cputemp");
        var samples = new List<MonitorRecordSample> { Sample(0, 60, 55), Sample(1, 59, -1) };

        var csv = GameMonitorRecorder.BuildCsv(metrics, samples, Meta());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("# 空岛图吧工具箱 WinUI3 · 游戏监控", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("# CPU,"));
        var header = lines.First(l => l.StartsWith("时间 (s)"));
        Assert.Equal("时间 (s),FPS,CPU 温度 (°C)", header);
        // 表头之后是全部样本（不抽稀），不可用值留空
        Assert.Equal("0.0,60.00,55.00", lines[^2]);
        Assert.Equal("1.0,59.00,", lines[^1]);
    }

    [Fact]
    public void BuildCsv_QuotesFieldsWithCommas()
    {
        var meta = Meta();
        meta.TargetWindow = "a,b";
        var csv = GameMonitorRecorder.BuildCsv(Metrics("fps"), [Sample(0, 60)], meta);
        Assert.Contains("\"a,b\"", csv);
    }

    // --------------------------------------------------- 首尾无效段裁剪

    [Fact]
    public void TrimIdleEdges_DropsLeadingAndTrailingFpsZeroSamples()
    {
        // 游戏还没进前台时 FPS 恒为 0，停止录制后主循环还会残留几条 0
        var samples = new List<MonitorRecordSample>
        {
            Sample(0.0, 0, 10),
            Sample(0.2, 0, 11),
            Sample(0.4, 60, 12),
            Sample(0.6, 61, 13),
            Sample(0.8, 0, 14)
        };

        var trimmed = GameMonitorRecorder.TrimIdleEdges(Metrics("fps", "cpuload"), samples, out var head, out var tail);

        Assert.Equal(2, trimmed.Count);
        Assert.Equal(60, trimmed[0].Values[0]);
        Assert.Equal(61, trimmed[1].Values[0]);
        Assert.Equal(0.4, head, 3);
        Assert.Equal(0.2, tail, 2);
    }

    [Fact]
    public void TrimIdleEdges_KeepsEverythingWhenFpsNeverReports()
    {
        // 全程没抓到进程（例如只录桌面）→ 不能把整段记录删空
        var samples = new List<MonitorRecordSample> { Sample(0, 0), Sample(1, 0) };

        var trimmed = GameMonitorRecorder.TrimIdleEdges(Metrics("fps"), samples, out var head, out var tail);

        Assert.Equal(2, trimmed.Count);
        Assert.Equal(0, head);
        Assert.Equal(0, tail);
    }

    [Fact]
    public void TrimIdleEdges_KeepsMidRecordingZeroFrames()
    {
        // 记录中途切后台、加载卡顿造成的 0 帧属于真实数据，只削首尾
        var samples = new List<MonitorRecordSample> { Sample(0, 60), Sample(1, 0), Sample(2, 61) };

        var trimmed = GameMonitorRecorder.TrimIdleEdges(Metrics("fps"), samples, out _, out _);

        Assert.Equal(3, trimmed.Count);
    }

    [Fact]
    public void TrimIdleEdges_WithoutFpsMetricIsNoOp()
    {
        var samples = new List<MonitorRecordSample> { Sample(0, 0), Sample(1, 0) };

        var trimmed = GameMonitorRecorder.TrimIdleEdges(Metrics("cpuload"), samples, out _, out _);

        Assert.Equal(2, trimmed.Count);
    }

    [Fact]
    public void TrimIdleEdges_IsIdempotent()
    {
        var samples = new List<MonitorRecordSample> { Sample(0, 0), Sample(1, 60), Sample(2, 0) };

        var once = GameMonitorRecorder.TrimIdleEdges(Metrics("fps"), samples, out _, out _);
        var twice = GameMonitorRecorder.TrimIdleEdges(Metrics("fps"), once, out var head, out var tail);

        Assert.Single(twice);
        Assert.Equal(0, head);
        Assert.Equal(0, tail);
    }

    [Fact]
    public void TrimIdleEdges_EmptyInputIsSafe()
    {
        var trimmed = GameMonitorRecorder.TrimIdleEdges(Metrics("fps"), [], out var head, out var tail);
        Assert.Empty(trimmed);
        Assert.Equal(0, head);
        Assert.Equal(0, tail);
    }
}
