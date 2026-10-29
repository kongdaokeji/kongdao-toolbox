using System;

namespace TubaWinUi3.Models;

public sealed class BrowserBenchmarkResult
{
    public int JsScore { get; set; }

    public string JsDetail { get; set; } = "";

    public int DomScore { get; set; }

    public string DomDetail { get; set; } = "";

    public int CardScore { get; set; }

    public string CardDetail { get; set; } = "";

    public int CssScore { get; set; }

    public string CssDetail { get; set; } = "";

    public int LayoutScore { get; set; }

    public string LayoutDetail { get; set; } = "";

    public int EventScore { get; set; }

    public string EventDetail { get; set; } = "";

    public int TotalScore { get; set; }

    public string Grade { get; set; } = "";

    public TimeSpan Duration { get; set; }
}
