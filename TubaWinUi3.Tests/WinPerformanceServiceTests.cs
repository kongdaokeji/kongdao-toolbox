using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class WinPerformanceServiceTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(500.0, 400)]
    [InlineData(1000.0, 282)]
    [InlineData(2000.0, 200)]
    [InlineData(4000.0, 141)]
    [InlineData(8000.0, 100)]
    [InlineData(15000.0, 81)]
    [InlineData(26000.0, 51)]
    [InlineData(45000.0, 0)]
    [InlineData(60000.0, 0)]
    public void ComputeWinScore_EdgeCases(double avgMs, int expected)
    {
        Assert.Equal(expected, WinPerformanceService.ComputeWinScore(avgMs));
    }

    [Fact]
    public void ComputeWinScore_FasterThanBaseline_Exceeds100()
    {
        // 无上限：快于基准（8000ms）时得分超过 100，但平方根曲线避免分数爆炸
        Assert.True(WinPerformanceService.ComputeWinScore(4000) > 100);
        Assert.True(WinPerformanceService.ComputeWinScore(2000) > WinPerformanceService.ComputeWinScore(4000));
        // 1184ms（用户实测）约 260 分，落在 S 档而非几百上千分
        Assert.InRange(WinPerformanceService.ComputeWinScore(1184), 200, 399);
    }

    [Fact]
    public void ComputeWinScore_MonotonicNonIncreasing()
    {
        // 耗时越长，得分越低（不增）
        double prev = int.MaxValue;
        for (int ms = 100; ms <= 90000; ms += 500)
        {
            int score = WinPerformanceService.ComputeWinScore(ms);
            Assert.True(score <= prev, $"score at {ms}ms = {score} should not exceed {prev}");
            prev = score;
        }
    }

    [Fact]
    public void FinalizeResult_DropsSlowestRun_AndAveragesRest()
    {
        var result = new WinPerformanceResult
        {
            RunCount = 5,
            DroppedRunCount = 1,
            Runs =
            {
                // 第 3 项最慢（10000ms）应被去掉
                new WinPerformanceRunResult { ListLoadMs = 1000 },
                new WinPerformanceRunResult { ListLoadMs = 1000 },
                new WinPerformanceRunResult { ListLoadMs = 10000 },
                new WinPerformanceRunResult { ListLoadMs = 1000 },
                new WinPerformanceRunResult { ListLoadMs = 1000 }
            }
        };

        WinPerformanceService.FinalizeResult(result);

        // 去掉最慢的一轮后，剩余 4 轮平均 = 1000ms -> 282 分（快于基准，S 档 ≥130）
        Assert.Equal(1000.0, result.BestAvgMs, 1);
        Assert.Equal(282, result.FinalScore);
        Assert.Equal("S", result.Grade);
    }

    [Fact]
    public void FinalizeResult_EmptyRuns_GivesZero()
    {
        var result = new WinPerformanceResult { RunCount = 5, DroppedRunCount = 1 };
        WinPerformanceService.FinalizeResult(result);
        Assert.Equal(0, result.FinalScore);
        Assert.Equal(0.0, result.BestAvgMs, 1);
    }

    [Fact]
    public void FinalizeResult_DropCountClamped_ToRunCountMinusOne()
    {
        var result = new WinPerformanceResult
        {
            RunCount = 2,
            DroppedRunCount = 99, // 非法值应被钳制
            Runs =
            {
                new WinPerformanceRunResult { ListLoadMs = 500 },
                new WinPerformanceRunResult { ListLoadMs = 3000 }
            }
        };

        WinPerformanceService.FinalizeResult(result);

        // 2 轮最多只能去掉 1 轮 -> 去掉较慢的 3000ms，保留 500ms -> 400 分（S 档）
        Assert.Equal(500.0, result.BestAvgMs, 1);
        Assert.Equal(400, result.FinalScore);
        Assert.Equal("S", result.Grade);
    }

    [Fact]
    public void TotalMs_SumsAllSubTests()
    {
        var run = new WinPerformanceRunResult
        {
            ListLoadMs = 100,
            ImageListMs = 200,
            TabSwitchMs = 300,
            ScrollMs = 400,
            TreeExpandMs = 500,
            SortFilterMs = 600,
            TextRenderMs = 700
        };
        Assert.Equal(2800.0, run.TotalMs, 1);
    }

    [Fact]
    public void FinalizeResult_ComputesPerSubtestAverages_OnKeptRuns()
    {
        var result = new WinPerformanceResult
        {
            RunCount = 3,
            DroppedRunCount = 1,
            Runs =
            {
                new WinPerformanceRunResult { ListLoadMs = 100, ImageListMs = 200, TabSwitchMs = 300, ScrollMs = 400, TreeExpandMs = 500, SortFilterMs = 600, TextRenderMs = 700 }, // total 2800
                new WinPerformanceRunResult { ListLoadMs = 100, ImageListMs = 200, TabSwitchMs = 300, ScrollMs = 400, TreeExpandMs = 500, SortFilterMs = 600, TextRenderMs = 700 }, // total 2800
                new WinPerformanceRunResult { ListLoadMs = 1000, ImageListMs = 2000, TabSwitchMs = 3000, ScrollMs = 4000, TreeExpandMs = 5000, SortFilterMs = 6000, TextRenderMs = 7000 } // total 28000 -> 丢弃
            }
        };

        WinPerformanceService.FinalizeResult(result);

        // 保留两轮 2800ms
        Assert.Equal(2800.0, result.BestAvgMs, 1);
        Assert.Equal(100.0, result.AvgListLoadMs, 1);
        Assert.Equal(200.0, result.AvgImageListMs, 1);
        Assert.Equal(700.0, result.AvgTextRenderMs, 1);
    }

    [Fact]
    public void GenerateSortData_IsDeterministic()
    {
        var a = WinPerformanceService.GenerateSortData(100);
        var b = WinPerformanceService.GenerateSortData(100);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Key, b[i].Key);
            Assert.Equal(a[i].Value, b[i].Value);
        }
    }

    [Theory]
    [InlineData(500, "S")]
    [InlineData(300, "S")]
    [InlineData(130, "S")]
    [InlineData(129, "A+")]
    [InlineData(100, "A+")]
    [InlineData(99, "A")]
    [InlineData(75, "A")]
    [InlineData(74, "B+")]
    [InlineData(55, "B+")]
    [InlineData(54, "B")]
    [InlineData(40, "B")]
    [InlineData(39, "C")]
    [InlineData(20, "C")]
    [InlineData(19, "D")]
    [InlineData(10, "D")]
    [InlineData(9, "E")]
    [InlineData(0, "E")]
    public void ComputeGrade_TierThresholds(int score, string expected)
    {
        Assert.Equal(expected, PerformanceBenchmarkService.ComputeGrade(score));
    }
}
