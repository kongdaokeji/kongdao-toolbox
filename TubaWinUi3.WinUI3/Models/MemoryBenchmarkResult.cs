using System;

namespace TubaWinUi3.Models;

public sealed class MemoryBenchmarkResult
{
    public float TotalCapacityGB { get; set; }

    public int CapacityScore { get; set; }

    public int TotalScore { get; set; }

    public string Grade { get; set; } = "";

    public TimeSpan Duration { get; set; }
}
