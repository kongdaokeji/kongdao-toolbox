using System;

namespace TubaWinUi3.Models;

public sealed class CpuBenchmarkResult
{
    public double SingleCoreIterations { get; set; }

    public int SingleCoreScore { get; set; }

    public double MultiCoreIterations { get; set; }

    public int MultiCoreScore { get; set; }

    public InterCoreLatencyMatrix? LatencyMatrix { get; set; }

    public int LatencyScore { get; set; }

    public int TotalScore { get; set; }

    public string Grade { get; set; } = "";

    public TimeSpan Duration { get; set; }
}
