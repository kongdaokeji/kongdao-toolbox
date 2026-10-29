namespace TubaWinUi3.Models;

public sealed class InterCoreLatencyMatrix
{
    public int CoreCount { get; set; }

    public double[,] Latencies { get; set; } = new double[0, 0];

    public double AverageNs { get; set; }

    public double MinNs { get; set; }

    public double MaxNs { get; set; }
}
