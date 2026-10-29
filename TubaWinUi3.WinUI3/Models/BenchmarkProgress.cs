namespace TubaWinUi3.Models;

public sealed class BenchmarkProgress
{
    public string Phase { get; init; } = "";

    public string SubPhase { get; init; } = "";

    public double Progress { get; init; }

    public string Detail { get; init; } = "";
}
