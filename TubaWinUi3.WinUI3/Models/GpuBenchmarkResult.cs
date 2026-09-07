using System;

namespace TubaWinUi3.Models;

public sealed class GpuBenchmarkResult
{
    public string GpuName { get; set; } = "";

    public int GpuIndex { get; set; }

    public int FurMarkScore { get; set; }

    public double AvgFps { get; set; }

    public double MinFps { get; set; }

    public double MaxFps { get; set; }

    public double RenderFps { get; set; }

    public int RenderScore { get; set; }

    public int TotalScore { get; set; }

    public string Grade { get; set; } = "";

    public TimeSpan Duration { get; set; }
}
