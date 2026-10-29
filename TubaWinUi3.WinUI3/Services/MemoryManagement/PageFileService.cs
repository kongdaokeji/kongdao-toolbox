using System.Management;
using System.Text.Json;

namespace TubaWinUi3.Services.MemoryManagement;

internal interface IPageFileStore
{
    PageFileConfiguration Read();
    void Write(PageFileConfiguration value);
}
internal static class PageFileTransaction
{
    internal static void Apply(IPageFileStore store, PageFileConfiguration expected, PageFileConfiguration draft, Action<PageFileConfiguration> backup)
    {
        if (MemoryPolicy.Fingerprint(store.Read()) != MemoryPolicy.Fingerprint(expected)) throw new InvalidOperationException("Configuration changed externally. Refresh before applying.");
        backup(expected); // Failure to persist the backup prevents all writes.
        try
        {
            store.Write(draft);
            if (MemoryPolicy.Fingerprint(store.Read()) != MemoryPolicy.Fingerprint(draft)) throw new InvalidOperationException("Configuration readback does not match.");
        }
        catch (Exception error)
        {
            string recovery;
            try
            {
                store.Write(expected);
                recovery = RecoveryReport(expected, store.Read());
            }
            catch (Exception ex)
            {
                try { recovery = RecoveryReport(expected, store.Read()) + "\n" + ex.Message; }
                catch (Exception readError) { recovery = "Recovery incomplete; current state could not be read: " + ex.Message + "; " + readError.Message; }
            }
            throw new InvalidOperationException(error.Message + "\n" + recovery, error);
        }
    }
    private static string RecoveryReport(PageFileConfiguration expected, PageFileConfiguration actual)
    {
        if (MemoryPolicy.Fingerprint(expected) == MemoryPolicy.Fingerprint(actual)) return "Original configuration restored.";
        var missing = new List<string>();
        if (expected.Automatic != actual.Automatic) missing.Add($"Automatic: expected {expected.Automatic}, actual {actual.Automatic}");
        var paths = expected.Entries.Concat(actual.Entries).Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var before = expected.Entries.FirstOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && e.Mode != PageFileMode.None);
            var after = actual.Entries.FirstOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && e.Mode != PageFileMode.None);
            if (before?.Mode == after?.Mode && before?.InitialMiB == after?.InitialMiB && before?.MaximumMiB == after?.MaximumMiB) continue;
            missing.Add($"{path}: expected {before?.Mode} {before?.InitialMiB}/{before?.MaximumMiB} MiB; actual {after?.Mode} {after?.InitialMiB}/{after?.MaximumMiB} MiB");
        }
        return "Recovery incomplete:\n" + string.Join("\n", missing);
    }
}

public static class PageFileService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string BackupPath => Path.Combine(ConfigManager.GetDataDir(), "MemoryManagement", "pagefile-backup.json");
    public static async Task<PageFileState> ReadAsync() => await Serialized(() => new PageFileState(new WmiStore().Read(), ReadUsage()));
    private static async Task<T> Serialized<T>(Func<T> operation)
    {
        await Gate.WaitAsync();
        try { return await Task.Run(operation); }
        finally { Gate.Release(); }
    }
    public static async Task ApplyAsync(PageFileConfiguration expected, PageFileConfiguration draft)
    {
        await Serialized(() =>
        {
            Validate(draft);
            PageFileTransaction.Apply(new WmiStore(), expected, draft, original =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
                File.WriteAllText(BackupPath + ".tmp", JsonSerializer.Serialize(original));
                File.Move(BackupPath + ".tmp", BackupPath, true);
            });
            return true;
        });
    }
    public static PageFileConfiguration LoadBackup() => JsonSerializer.Deserialize<PageFileConfiguration>(File.ReadAllText(BackupPath)) ?? throw new InvalidOperationException("Invalid backup.");
    public static void Validate(PageFileConfiguration draft)
    {
        if (draft.Automatic) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in draft.Entries)
        {
            string root = Path.GetPathRoot(e.Path) ?? "";
            if (root.Length != 3 || !string.Equals(e.Path, Path.Combine(root, "pagefile.sys"), StringComparison.OrdinalIgnoreCase) || !seen.Add(root)) throw new ArgumentException("Each fixed drive can have one pagefile.sys.");
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) throw new ArgumentException("Only available fixed drives are supported.");
            ValidateSize(e, (ulong)drive.AvailableFreeSpace);
        }
    }
    internal static void ValidateSize(PageFileEntry e, ulong freeBytes)
    {
        if (e.Mode != PageFileMode.Custom) return;
        if (e.InitialMiB < 16 || e.InitialMiB > e.MaximumMiB || e.MaximumMiB > uint.MaxValue / 1024) throw new ArgumentException("Custom size must be 16–4194303 MiB, initial ≤ maximum.");
        // Validate against current free space, conservatively avoiding promised future reclamation.
        if ((ulong)e.MaximumMiB * 1048576 > freeBytes) throw new ArgumentException("Maximum size exceeds currently free disk space. Reduce it or free space first.");
    }
    private static ManagementScope Scope()
    {
        var scope = new ManagementScope(@"\\.\root\cimv2", new ConnectionOptions { EnablePrivileges = true, Timeout = TimeSpan.FromSeconds(30) });
        scope.Connect(); return scope;
    }
    private static ManagementObjectCollection Query(string query)
    {
        using var search = new ManagementObjectSearcher(Scope(), new ObjectQuery(query), new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(30) });
        return search.Get();
    }
    private static IReadOnlyList<PageFileUsage> ReadUsage()
    {
        var result = new List<PageFileUsage>();
        using var items = Query("SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage");
        foreach (ManagementObject m in items) using (m) result.Add(new((string)m["Name"], Convert.ToUInt32(m["AllocatedBaseSize"]), Convert.ToUInt32(m["CurrentUsage"]), Convert.ToUInt32(m["PeakUsage"])));
        return result;
    }
    private sealed class WmiStore : IPageFileStore
    {
        public PageFileConfiguration Read()
        {
            bool automatic = false;
            using (var systems = Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                foreach (ManagementObject m in systems) using (m) automatic = Convert.ToBoolean(m["AutomaticManagedPagefile"]);
            var entries = new List<PageFileEntry>();
            using var items = Query("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting");
            foreach (ManagementObject m in items) using (m)
            {
                uint initial = Convert.ToUInt32(m["InitialSize"]), maximum = Convert.ToUInt32(m["MaximumSize"]);
                entries.Add(new((string)m["Name"], initial == 0 && maximum == 0 ? PageFileMode.SystemManaged : PageFileMode.Custom, initial, maximum));
            }
            return new(automatic, entries);
        }
        public void Write(PageFileConfiguration value)
        {
            if (value.Automatic)
            {
                using var systems = Query("SELECT * FROM Win32_ComputerSystem");
                foreach (ManagementObject m in systems) using (m) { m["AutomaticManagedPagefile"] = true; m.Put(new PutOptions { Timeout = TimeSpan.FromSeconds(30) }); }
                return;
            }
            using (var systems = Query("SELECT * FROM Win32_ComputerSystem"))
                foreach (ManagementObject m in systems) using (m) { m["AutomaticManagedPagefile"] = false; m.Put(new PutOptions { Timeout = TimeSpan.FromSeconds(30) }); }
            var existing = Read().Entries;
            foreach (var e in value.Entries.Where(e => e.Mode != PageFileMode.None))
            {
                using var type = new ManagementClass(Scope(), new ManagementPath("Win32_PageFileSetting"), null);
                using var m = existing.Any(old => old.Path.Equals(e.Path, StringComparison.OrdinalIgnoreCase))
                    ? new ManagementObject(Scope(), new ManagementPath("Win32_PageFileSetting.Name=\"" + e.Path.Replace("\\", "\\\\") + "\""), null)
                    : type.CreateInstance() ?? throw new InvalidOperationException("Cannot create pagefile setting.");
                if (existing.Any(old => old.Path.Equals(e.Path, StringComparison.OrdinalIgnoreCase))) m.Get();
                m["Name"] = e.Path; m["InitialSize"] = e.Mode == PageFileMode.SystemManaged ? 0u : e.InitialMiB;
                m["MaximumSize"] = e.Mode == PageFileMode.SystemManaged ? 0u : e.MaximumMiB;
                m.Put(new PutOptions { Timeout = TimeSpan.FromSeconds(30) });
            }
            // Create/update replacements before removing obsolete settings, including during rollback.
            using (var items = Query("SELECT * FROM Win32_PageFileSetting"))
                foreach (ManagementObject m in items) using (m)
                    if (!value.Entries.Any(e => e.Mode != PageFileMode.None && e.Path.Equals((string)m["Name"], StringComparison.OrdinalIgnoreCase))) m.Delete(new DeleteOptions { Timeout = TimeSpan.FromSeconds(30) });
            using (var systems = Query("SELECT * FROM Win32_ComputerSystem"))
                foreach (ManagementObject m in systems) using (m) { m["AutomaticManagedPagefile"] = value.Automatic; m.Put(new PutOptions { Timeout = TimeSpan.FromSeconds(30) }); }
        }
    }
}
