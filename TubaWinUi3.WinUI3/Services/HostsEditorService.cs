namespace TubaWinUi3.Services;

public sealed class HostsEntry
{
    public bool Enabled { get; set; } = true;
    public bool IsComment { get; init; }
    public string Address { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Comment { get; set; } = "";
    public int LineNumber { get; init; }
    public string RawLine { get; init; } = "";
}

public static class HostsEditorService
{
    public static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts");

    public static bool IsAdmin => System.Security.Principal.WindowsIdentity.GetCurrent().Owner
        ?.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid) ?? false;

    public static List<HostsEntry> Load()
    {
        var entries = new List<HostsEntry>();
        if (!File.Exists(HostsPath)) return entries;

        var lines = File.ReadAllLines(HostsPath);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                entries.Add(new HostsEntry { LineNumber = i + 1, RawLine = line, IsComment = true, Enabled = false });
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var uncommented = trimmed[1..].TrimStart();
                var parts = SplitHostsLine(uncommented);
                if (parts is not null && LooksLikeIpAddress(parts.Value.Addr))
                {
                    entries.Add(new HostsEntry
                    {
                        Enabled = false,
                        IsComment = false,
                        Address = parts.Value.Addr,
                        Hostname = parts.Value.Host,
                        Comment = parts.Value.Comment,
                        LineNumber = i + 1,
                        RawLine = line
                    });
                }
                else
                {
                    entries.Add(new HostsEntry
                    {
                        Enabled = false,
                        IsComment = true,
                        Comment = trimmed,
                        LineNumber = i + 1,
                        RawLine = line
                    });
                }
                continue;
            }

            var hostParts = SplitHostsLine(trimmed);
            if (hostParts is not null && LooksLikeIpAddress(hostParts.Value.Addr))
            {
                entries.Add(new HostsEntry
                {
                    Enabled = true,
                    IsComment = false,
                    Address = hostParts.Value.Addr,
                    Hostname = hostParts.Value.Host,
                    Comment = hostParts.Value.Comment,
                    LineNumber = i + 1,
                    RawLine = line
                });
            }
            else
            {
                entries.Add(new HostsEntry
                {
                    Enabled = false,
                    IsComment = true,
                    Comment = trimmed,
                    LineNumber = i + 1,
                    RawLine = line
                });
            }
        }

        return entries;
    }

    public static void Save(List<HostsEntry> entries)
    {
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.IsComment)
            {
                if (string.IsNullOrEmpty(entry.Comment) || entry.Comment == "#")
                    lines.Add("");
                else
                    lines.Add(entry.Comment);
                continue;
            }

            if (string.IsNullOrEmpty(entry.Address) && string.IsNullOrEmpty(entry.Hostname))
            {
                lines.Add("");
                continue;
            }

            var l = entry.Enabled ? "" : "# ";
            l += $"{entry.Address}\t{entry.Hostname}";
            if (!string.IsNullOrEmpty(entry.Comment))
                l += $"\t# {entry.Comment}";
            lines.Add(l);
        }

        var backupPath = HostsPath + ".tuba_bak";
        if (File.Exists(HostsPath))
            File.Copy(HostsPath, backupPath, true);

        File.WriteAllLines(HostsPath, lines);
    }

    public static void Backup()
    {
        if (!File.Exists(HostsPath)) return;
        var backupPath = HostsPath + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}";
        File.Copy(HostsPath, backupPath, true);
    }

    public static void FlushDns()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ipconfig",
            Arguments = "/flushdns",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit(5000);
    }

    private static bool LooksLikeIpAddress(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (token.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return System.Net.IPAddress.TryParse(token, out _);
    }

    private static (string Addr, string Host, string Comment)? SplitHostsLine(string line)
    {
        var commentIdx = line.IndexOf('#');
        var mainPart = commentIdx >= 0 ? line[..commentIdx].Trim() : line.Trim();
        var comment = commentIdx >= 0 ? line[(commentIdx + 1)..].Trim() : "";

        var tokens = mainPart.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return null;

        return (tokens[0], tokens[1], comment);
    }
}