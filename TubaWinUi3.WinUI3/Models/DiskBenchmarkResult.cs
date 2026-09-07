using System;

namespace TubaWinUi3.Models;

public sealed class DiskBenchmarkResult
{
    public double SeqReadMBs { get; set; }

    public int SeqReadScore { get; set; }

    public double SeqWriteMBs { get; set; }

    public int SeqWriteScore { get; set; }

    public double Random4KReadIops { get; set; }

    public int Random4KReadScore { get; set; }

    public double Random4KWriteIops { get; set; }

    public int Random4KWriteScore { get; set; }

    public float Temperature { get; set; }

    public int TempScore { get; set; }

    public int TotalScore { get; set; }

    public string Grade { get; set; } = "";

    public TimeSpan Duration { get; set; }
}
