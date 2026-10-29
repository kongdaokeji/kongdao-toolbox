using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 记录查看页的数据来源：导出的 JSON / CSV 解析成统一模型 + 可视化载荷。
/// 写入端在 <see cref="GameMonitorRecorderTests"/>，这里是读回端。
/// </summary>
public class GameMonitorRecordReaderTests
{
    private static List<MonitorRecordMetric> Metrics(params string[] keys) =>
        GameMonitorRecorder.AllMetrics.Where(m => keys.Contains(m.Key)).ToList();

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

    private static List<MonitorRecordSample> SampleList() =>
    [
        new() { Seconds = 0, Values = [120, 65, 1.5f] },
        new() { Seconds = 1, Values = [118, 66, -1f] }
    ];

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gm-record-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ListRecordFiles_FiltersExtensionsAndSortsNewestFirst()
    {
        var dir = TempDir();
        try
        {
            var oldFile = Path.Combine(dir, "游戏监控记录_20260910_100000.json");
            var newFile = Path.Combine(dir, "游戏监控记录_20260910_110000.csv");
            var other = Path.Combine(dir, "readme.txt");
            File.WriteAllText(oldFile, "{}");
            File.WriteAllText(newFile, "x");
            File.WriteAllText(other, "x");
            File.SetLastWriteTimeUtc(oldFile, new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 9, 10, 11, 0, 0, DateTimeKind.Utc));

            var list = GameMonitorRecordReader.ListRecordFiles(dir);

            Assert.Equal(2, list.Count);
            Assert.Equal(newFile, list[0]);
            Assert.Equal(oldFile, list[1]);
            Assert.Empty(GameMonitorRecordReader.ListRecordFiles(Path.Combine(dir, "not-exist")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ParseJson_RoundTripsRecorderOutput()
    {
        var metrics = Metrics("fps", "cputemp", "netdown");
        var json = GameMonitorRecorder.BuildJson(metrics, SampleList(), Meta());

        var data = GameMonitorRecordReader.ParseJson(json);

        Assert.Equal(new[] { "fps", "cputemp", "netdown" }, data.Metrics.Select(m => m.Key).ToArray());
        Assert.Equal(2, data.Samples.Count);
        Assert.Equal(120, data.Samples[0].Values[0]);
        // 不可用哨兵值保留，由展示层决定怎么画
        Assert.Equal(-1, data.Samples[1].Values[2]);
        Assert.Equal("game — 测试窗口", data.Meta.TargetWindow);
        Assert.Equal("Test GPU", data.Meta.GpuName);
        Assert.Equal(new DateTime(2026, 9, 10, 21, 0, 0), data.Meta.StartTime);
        Assert.Equal(10, data.Meta.DurationSeconds);
        Assert.False(data.IsCsv);
    }

    [Fact]
    public void ParseCsv_RoundTripsRecorderOutputIncludingQuotedFields()
    {
        var metrics = Metrics("fps", "cputemp", "netdown");
        var meta = Meta();
        meta.TargetWindow = "a,b — 带逗号的窗口";
        var csv = GameMonitorRecorder.BuildCsv(metrics, SampleList(), meta);

        var data = GameMonitorRecordReader.ParseCsv(csv);

        Assert.True(data.IsCsv);
        Assert.Equal(new[] { "fps", "cputemp", "netdown" }, data.Metrics.Select(m => m.Key).ToArray());
        Assert.Equal("°C", data.Metrics[1].Unit);
        Assert.Equal("CPU 温度", data.Metrics[1].Label);
        Assert.Equal("a,b — 带逗号的窗口", data.Meta.TargetWindow);
        Assert.Equal(1000, data.Meta.IntervalMs);
        Assert.Equal(2, data.Samples.Count);
        Assert.Equal(120, data.Samples[0].Values[0]);
        Assert.Equal(65, data.Samples[0].Values[1]);
        // 空值 = 不可用
        Assert.Equal(-1, data.Samples[1].Values[2]);
        // 时长可由起止时间推算
        Assert.Equal(10, data.Meta.DurationSeconds);
    }

    [Fact]
    public void ParseCsv_FallsBackToLabelMatchingWithoutKeyRow()
    {
        var csv = string.Join('\n',
            "# 采样间隔,500 ms",
            "时间 (s),FPS,GPU 温度 (°C)",
            "0.0,60.00,70.00",
            "0.5,59.00,71.00");

        var data = GameMonitorRecordReader.ParseCsv(csv);

        Assert.Equal(new[] { "fps", "gputemp" }, data.Metrics.Select(m => m.Key).ToArray());
        Assert.Equal("GPU", data.Metrics[1].Group);
        Assert.Equal(500, data.Meta.IntervalMs);
        Assert.Equal(59, data.Samples[1].Values[0]);
    }

    [Fact]
    public void ParseJson_InvalidContentThrows()
    {
        Assert.ThrowsAny<JsonException>(() => GameMonitorRecordReader.ParseJson("not json"));
    }

    // ------------------------------------------------------- BuildView

    [Fact]
    public void BuildView_CarriesFullStatsAndDownsampledSeries()
    {
        var metrics = Metrics("fps", "cputemp");
        var samples = new List<MonitorRecordSample>();
        for (int i = 0; i < 9000; i++)
            samples.Add(new MonitorRecordSample { Seconds = i, Values = [60 + i % 30, 70] });

        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            FileName = "x.json",
            Meta = Meta(),
            Metrics = metrics,
            Samples = samples
        });

        Assert.Equal(9000, view.Data.Samples.Count);
        Assert.Equal(3, view.Step);
        Assert.True(view.Times.Count <= GameMonitorRecordReader.MaxChartPoints + 1);

        // 每条曲线的点数与时间轴严格对齐（LiveCharts 按下标绘制）
        Assert.Equal(view.Times.Count, view.Metrics[0].Values.Count);
        Assert.Equal(view.Times.Count, view.Metrics[1].Values.Count);

        // 时间轴由抽稀下标换算而来，首尾与原始样本对齐
        Assert.Equal(0d, view.Times[0]);
        Assert.Equal(8999d, view.Times[^1]);

        // 统计基于全量数据（60..89 循环，平均 74.5）
        var fps = view.Metrics[0];
        Assert.Equal(9000, fps.Count);
        Assert.True(fps.HasData);
        Assert.Equal("60.00", fps.Min);
        Assert.Equal("89.00", fps.Max);
        Assert.StartsWith("74.", fps.Avg);

        var temp = view.Metrics[1];
        Assert.Equal(9000, temp.Count);
        Assert.Equal("70.00", temp.Min);
        Assert.Equal("70.00", temp.Avg);
        Assert.Equal("70.00", temp.Max);
    }

    [Fact]
    public void BuildView_KeepsShortSeriesUnsampled()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("fps"),
            Samples = [new() { Seconds = 0, Values = [60] }, new() { Seconds = 1, Values = [61] }]
        });

        Assert.Equal(1, view.Step);
        Assert.Equal(new[] { 0d, 1d }, view.Times.ToArray());
    }

    [Fact]
    public void BuildView_MapsUnavailableValuesToNullGaps()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            // 用不带 FPS 的指标组，避免首尾无效段裁剪介入
            Metrics = Metrics("cpuload", "cputemp"),
            Samples =
            [
                new() { Seconds = 0, Values = [60, 70] },
                new() { Seconds = 1, Values = [61, -1f] },
                new() { Seconds = 2, Values = [-1f, 72] }
            ]
        });

        // 不可用（-1）哨兵值变成 null，图表上表现为断点
        Assert.Equal(new double?[] { 60, 61, null }, view.Metrics[0].Values.ToArray());
        Assert.Equal(new double?[] { 70, null, 72 }, view.Metrics[1].Values.ToArray());

        // 统计时剔除断点：CPU 温度只剩 70 / 72 两个有效值
        Assert.Equal(2, view.Metrics[0].Count);
        Assert.Equal(2, view.Metrics[1].Count);
        Assert.Equal("70.00", view.Metrics[1].Min);
        Assert.Equal("72.00", view.Metrics[1].Max);
        Assert.Equal("71.00", view.Metrics[1].Avg);
    }

    [Fact]
    public void BuildView_WorksOnParsedCsvEndToEnd()
    {
        var metrics = Metrics("fps", "cputemp", "netdown");
        var csv = GameMonitorRecorder.BuildCsv(metrics, SampleList(), Meta());

        var view = GameMonitorRecordReader.BuildView(GameMonitorRecordReader.ParseCsv(csv));

        Assert.Equal(2, view.Times.Count);
        Assert.Equal(3, view.Metrics.Count);
        Assert.Equal("fps", view.Metrics[0].Metric.Key);
        Assert.Equal("CPU 温度", view.Metrics[1].Metric.Label);
        Assert.Equal("°C", view.Metrics[1].Metric.Unit);
        Assert.Equal(120d, view.Metrics[0].Values[0] ?? double.NaN);
        // 第二条样本的网速是不可用哨兵 → 断点
        Assert.Null(view.Metrics[2].Values[1]);
    }

    [Fact]
    public void BuildView_TrimsLeadingAndTrailingFpsIdleSamples()
    {
        // 起始 2 秒游戏还没出画面（FPS=0），结束前 1 秒进程已退出
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("fps", "cpuload"),
            Samples =
            [
                new() { Seconds = 0, Values = [0, 10] },
                new() { Seconds = 1, Values = [0, 11] },
                new() { Seconds = 2, Values = [60, 12] },
                new() { Seconds = 3, Values = [62, 13] },
                new() { Seconds = 4, Values = [0, 14] }
            ]
        });

        Assert.Equal(new[] { 2d, 3d }, view.Times.ToArray());
        Assert.Equal(2, view.Data.Samples.Count);
        Assert.Equal(2, view.HeadTrimmedSeconds, 3);
        Assert.Equal(1, view.TailTrimmedSeconds, 3);

        // 统计也不再被这段 0 污染
        Assert.Equal(2, view.Metrics[0].Count);
        Assert.Equal("61.00", view.Metrics[0].Avg);
        Assert.Equal("60.00", view.Metrics[0].Min);
    }

    // --------------------------------------------------- 图表指标选择

    [Fact]
    public void PickDefaultMetricKey_PrefersFps()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("cputemp", "fps"),
            Samples = [new() { Seconds = 0, Values = [70, 60] }, new() { Seconds = 1, Values = [71, 61] }]
        });

        // 记录查看默认只画 FPS，与指标在文件里的先后无关
        Assert.Equal("fps", GameMonitorRecordReader.PickDefaultMetricKey(view.Metrics));
    }

    [Fact]
    public void PickDefaultMetricKey_FallsBackToFirstMetricWithData()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("cputemp"),
            Samples = [new() { Seconds = 0, Values = [70] }, new() { Seconds = 1, Values = [71] }]
        });

        // 没有 FPS 的旧记录：退回第一条有数据的指标
        Assert.Equal("cputemp", GameMonitorRecordReader.PickDefaultMetricKey(view.Metrics));
        Assert.Null(GameMonitorRecordReader.PickDefaultMetricKey([]));
    }

    [Fact]
    public void PickDefaultMetricKey_NullWhenNothingHasData()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("gputemp"),
            Samples = [new() { Seconds = 0, Values = [-1f] }]
        });

        Assert.False(view.Metrics[0].HasData);
        Assert.Null(GameMonitorRecordReader.PickDefaultMetricKey(view.Metrics));
    }

    [Fact]
    public void PickMetricKeys_TakesFirstMaxWithData()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("fps", "cputemp", "cpuload"),
            Samples = [new() { Seconds = 0, Values = [60, 70, 12] }, new() { Seconds = 1, Values = [61, 71, 13] }]
        });

        // 按指标定义顺序取前 N 条，超上限即截断；0 表示不画任何图表
        Assert.Equal(["fps", "cpuload", "cputemp"], GameMonitorRecordReader.PickMetricKeys(view.Metrics, 3));
        Assert.Equal(["fps", "cpuload"], GameMonitorRecordReader.PickMetricKeys(view.Metrics, 2));
        Assert.Empty(GameMonitorRecordReader.PickMetricKeys(view.Metrics, 0));
    }

    [Fact]
    public void PickMetricKeys_SkipsMetricsWithoutData()
    {
        var view = GameMonitorRecordReader.BuildView(new MonitorRecordData
        {
            Metrics = Metrics("fps", "cputemp"),
            Samples = [new() { Seconds = 0, Values = [60, -1f] }, new() { Seconds = 1, Values = [61, -1f] }]
        });

        Assert.Equal(["fps"], GameMonitorRecordReader.PickMetricKeys(view.Metrics, 2));
    }
}
