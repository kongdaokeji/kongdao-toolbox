using System.Globalization;

namespace TubaWinUi3.Services.MemoryManagement;

public sealed record MemorySnapshot(DateTimeOffset Time, ulong Installed, ulong Total, ulong Available,
    ulong Commit, ulong CommitLimit, ulong CommitPeak, ulong Cache, ulong PagedPool, ulong NonpagedPool,
    ulong? Free, ulong? Zeroed, ulong? Modified, ulong[]? StandbyByPriority, string? ListError)
{
    public ulong Used => Total > Available ? Total - Available : 0;
    public ulong HardwareReserved => Installed > Total ? Installed - Total : 0;
    public ulong? Standby => StandbyByPriority?.Aggregate(0UL, (a, b) => a + b);
    public double AvailablePercent => Total == 0 ? 100 : Available * 100d / Total;
}
public enum PageFileMode { SystemManaged, Custom, None }
public sealed record PageFileEntry(string Path, PageFileMode Mode, uint InitialMiB, uint MaximumMiB);
public sealed record PageFileConfiguration(bool Automatic, IReadOnlyList<PageFileEntry> Entries);
public sealed record PageFileUsage(string Path, uint AllocatedMiB, uint UsedMiB, uint PeakMiB);
public sealed record PageFileState(PageFileConfiguration Configuration, IReadOnlyList<PageFileUsage> Usage);

public static class MemoryText
{
    public static string Bytes(ulong? value)
    {
        if (value is null) return "—";
        double v = value.Value; string[] units = ["B", "KiB", "MiB", "GiB", "TiB"]; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString("0.##", CultureInfo.CurrentCulture) + " " + units[u];
    }
    public static string Signed(long v) => (v < 0 ? "−" : "+") + Bytes((ulong)Math.Abs((decimal)v));
    public static string Percentage(ulong? value, ulong denominator) => value is null || denominator == 0 ? "—" : value == 0 ? "0%" : value * 100d / denominator < 0.001 ? "<0.001%" : $"{value * 100d / denominator:0.###}%";
    public static long Difference(ulong after, ulong before) => after >= before ? (long)Math.Min(after - before, (ulong)long.MaxValue) : -(long)Math.Min(before - after, (ulong)long.MaxValue);
}
internal static class MemoryPolicy
{
    internal static string Fingerprint(PageFileConfiguration c) => c.Automatic ? "Automatic" : "Manual|" + string.Join("|", c.Entries.Where(e => e.Mode != PageFileMode.None).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => $"{e.Path.ToUpperInvariant()}:{e.Mode}:{e.InitialMiB}:{e.MaximumMiB}"));
}

internal static class MemoryPlotScale
{
    internal static double? Value(ulong? bytes, ulong total, bool percent, bool logarithmic, double unit)
    {
        if (bytes is null) return null;
        if (logarithmic) return Math.Log10(1d + bytes.Value);
        if (percent) return total == 0 ? null : bytes.Value * 100d / total;
        return bytes.Value / unit;
    }
}
