using System.Text.Json;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

public sealed class HardwareSpooferEntry
{
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required RegistryValueKind Kind { get; init; }
    public required string OriginalValue { get; init; }
    public string? CurrentValue { get; set; }
    public bool IsModified => CurrentValue is not null && CurrentValue != OriginalValue;
}

public static class HardwareSpooferService
{
    private static readonly string BackupPath = Path.Combine(
        ConfigManager.GetDataDir(), "hardware_spoofer_backup.json");

    public static bool IsAdmin
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static bool HasBackup => File.Exists(BackupPath);

    public static string ReadValue(string keyPath, string valueName, string defaultValue = "")
    {
        try
        {
            var hive = GetHive(keyPath, out var subKey);
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null) return defaultValue;
            var val = key.GetValue(valueName);
            if (val is null) return defaultValue;
            if (val is int i) return i.ToString();
            if (val is string[] arr) return string.Join("|", arr);
            if (val is byte[] bytes) return BitConverter.ToString(bytes).Replace("-", "");
            return val.ToString() ?? defaultValue;
        }
        catch { return defaultValue; }
    }

    public static int ReadDword(string keyPath, string valueName, int defaultValue = 0)
    {
        try
        {
            var hive = GetHive(keyPath, out var subKey);
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null) return defaultValue;
            var val = key.GetValue(valueName);
            if (val is int i) return i;
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    public static bool WriteValue(string keyPath, string valueName, string value, RegistryValueKind kind)
    {
        try
        {
            var hive = GetHive(keyPath, out var subKey);
            using var key = hive.OpenSubKey(subKey, writable: true);
            if (key is null) return false;
            if (kind == RegistryValueKind.DWord)
            {
                if (int.TryParse(value, out var dw))
                    key.SetValue(valueName, dw, RegistryValueKind.DWord);
                else return false;
            }
            else if (kind == RegistryValueKind.MultiString)
            {
                key.SetValue(valueName, new[] { value }, RegistryValueKind.MultiString);
            }
            else
            {
                key.SetValue(valueName, value, kind);
            }
            return true;
        }
        catch { return false; }
    }

    private static readonly string[] GpuNameKeywords =
        ["nvidia", "geforce", "gtx", "rtx", "amd", "radeon", "arc", "iris", "uhd graphics", "hd graphics"];

    private static readonly string[] GpuExcludeKeywords =
        ["usb", "controller", "host", "xhci", "ehci", "uhci", "chipset", "smbus", "audio", "sound"];

    private static bool ContainsKeyword(string? text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return keywords.Any(kw => lower.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGpuName(string? text) => ContainsKeyword(text, GpuNameKeywords);

    /// <summary>
    /// Enumerates Enum\PCI display adapter instances for NVIDIA / AMD / Intel vendors.
    /// This is the location Windows actually reads for device display names
    /// (Device Manager / Task Manager / WMI Win32_VideoController).
    /// </summary>
    public static List<string> FindGpuEnumKeyPaths()
    {
        var results = new List<string>();
        try
        {
            using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (enumKey is null) return results;

            foreach (var deviceName in enumKey.GetSubKeyNames())
            {
                var deviceUpper = deviceName.ToUpperInvariant();
                if (!deviceUpper.Contains("VEN_10DE") &&
                    !deviceUpper.Contains("VEN_1002") &&
                    !deviceUpper.Contains("VEN_8086"))
                    continue;

                var devicePath = $@"SYSTEM\CurrentControlSet\Enum\PCI\{deviceName}";
                using var deviceKey = Registry.LocalMachine.OpenSubKey(devicePath);
                if (deviceKey is null) continue;

                foreach (var instanceName in deviceKey.GetSubKeyNames())
                {
                    var instancePath = $@"{devicePath}\{instanceName}";
                    using var instanceKey = Registry.LocalMachine.OpenSubKey(instancePath);
                    if (instanceKey is null) continue;

                    var deviceDesc = instanceKey.GetValue("DeviceDesc") as string;
                    var friendlyName = instanceKey.GetValue("FriendlyName") as string;

                    if (ContainsKeyword(deviceDesc, GpuExcludeKeywords) ||
                        ContainsKeyword(friendlyName, GpuExcludeKeywords))
                        continue;

                    var isGpu = false;
                    var classGuid = instanceKey.GetValue("ClassGUID") as string;
                    if (classGuid is not null &&
                        classGuid.Trim().Trim('{', '}').Equals("4D36E968-E325-11CE-BFC1-08002BE10318", StringComparison.OrdinalIgnoreCase))
                        isGpu = true;
                    if (!isGpu)
                        isGpu = IsGpuName(deviceDesc) || IsGpuName(friendlyName);

                    if (isGpu)
                        results.Add(instancePath);
                }
            }
        }
        catch { }

        return results;
    }

    /// <summary>
    /// Finds the primary (discrete, non-integrated) GPU Enum\PCI instance key.
    /// </summary>
    public static string? FindPrimaryGpuEnumKey()
    {
        var gpuKeys = FindGpuEnumKeyPaths();
        if (gpuKeys.Count == 0) return null;

        foreach (var keyPath in gpuKeys)
        {
            if (IsIntegratedGpu(keyPath)) continue;
            return keyPath;
        }
        return gpuKeys[0];
    }

    private static bool IsIntegratedGpu(string instancePath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(instancePath);
            var location = key?.GetValue("LocationInformation") as string;
            if (string.IsNullOrEmpty(location)) return false;
            var lower = location.ToLowerInvariant();
            return lower.Contains("internal") || lower.Contains("on board") || lower.Contains("bus 0");
        }
        catch { return false; }
    }

    private static List<string> FindGpuClassKeys()
    {
        var results = new List<string>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return results;

            foreach (var subName in classKey.GetSubKeyNames())
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(subName, @"^00\d+")) continue;
                var subPath = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{subName}";
                using var subKey = Registry.LocalMachine.OpenSubKey(subPath);
                var desc = subKey?.GetValue("DriverDesc") as string;
                if (IsGpuName(desc)) results.Add(subPath);
            }
        }
        catch { }
        return results;
    }

    private static List<string> FindGpuVideoKeys()
    {
        var results = new List<string>();
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (videoKey is null) return results;

            foreach (var guidName in videoKey.GetSubKeyNames())
            {
                var guidPath = $@"SYSTEM\CurrentControlSet\Control\Video\{guidName}";
                using var guidKey = Registry.LocalMachine.OpenSubKey(guidPath);
                if (guidKey is null) continue;

                foreach (var subName in guidKey.GetSubKeyNames())
                {
                    var subPath = $@"{guidPath}\{subName}";
                    using var subKey = Registry.LocalMachine.OpenSubKey(subPath);
                    if (subKey is null) continue;
                    var text = string.Join(" ", new[]
                    {
                        subKey.GetValue("DriverDesc") as string,
                        subKey.GetValue("DeviceDesc") as string,
                        subKey.GetValue("Description") as string,
                        subKey.GetValue("FriendlyName") as string
                    });
                    if (IsGpuName(text)) results.Add(subPath);
                }
            }
        }
        catch { }
        return results;
    }

    /// <summary>
    /// Writes a GPU name to ALL relevant registry locations for maximum coverage,
    /// mirroring the NexBox approach:
    /// 1. Enum\PCI — where Windows actually caches the device display name
    ///    (DeviceDesc, FriendlyName) shown in Device Manager / Task Manager / WMI.
    /// 2. Control\Class — driver instance keys (DriverDesc, HardwareInformation.*).
    /// 3. Control\Video — legacy display keys (DriverDesc, DeviceDesc, Description, FriendlyName).
    /// </summary>
    public static bool WriteGpuName(string gpuName, string? providerName)
    {
        var anySuccess = false;

        var chipTypeName = gpuName.Contains("Family", StringComparison.OrdinalIgnoreCase)
            ? gpuName
            : gpuName + " Family";

        // 1. Enum\PCI — the key location for the displayed GPU name
        foreach (var keyPath in FindGpuEnumKeyPaths())
        {
            try
            {
                var hive = GetHive(keyPath, out var subKey);
                using var key = hive.OpenSubKey(subKey, writable: true);
                if (key is null) continue;

                key.SetValue("FriendlyName", gpuName, RegistryValueKind.String);

                var deviceDesc = key.GetValue("DeviceDesc") as string;
                if (!string.IsNullOrEmpty(deviceDesc))
                {
                    var parts = deviceDesc.Split(';', 2);
                    key.SetValue("DeviceDesc",
                        parts.Length > 1 ? $"{parts[0]};{gpuName}" : gpuName,
                        RegistryValueKind.String);
                }

                anySuccess = true;
            }
            catch { }
        }

        // 2. Control\Class — driver instance keys
        foreach (var keyPath in FindGpuClassKeys())
        {
            try
            {
                var hive = GetHive(keyPath, out var subKey);
                using var key = hive.OpenSubKey(subKey, writable: true);
                if (key is null) continue;

                key.SetValue("DriverDesc", gpuName, RegistryValueKind.String);
                key.SetValue("HardwareInformation.ChipType", new[] { chipTypeName }, RegistryValueKind.MultiString);
                key.SetValue("HardwareInformation.AdapterString", gpuName, RegistryValueKind.String);
                if (!string.IsNullOrEmpty(providerName))
                    key.SetValue("ProviderName", providerName, RegistryValueKind.String);

                anySuccess = true;
            }
            catch { }
        }

        // 3. Control\Video — legacy display keys
        foreach (var keyPath in FindGpuVideoKeys())
        {
            try
            {
                var hive = GetHive(keyPath, out var subKey);
                using var key = hive.OpenSubKey(subKey, writable: true);
                if (key is null) continue;

                key.SetValue("DriverDesc", gpuName, RegistryValueKind.String);
                key.SetValue("DeviceDesc", gpuName, RegistryValueKind.String);
                key.SetValue("Description", gpuName, RegistryValueKind.String);
                key.SetValue("FriendlyName", gpuName, RegistryValueKind.String);

                anySuccess = true;
            }
            catch { }
        }

        return anySuccess;
    }

    /// <summary>
    /// Reads the current GPU description, preferring the Enum\PCI display name
    /// (FriendlyName, then DeviceDesc after the hardware ID prefix).
    /// </summary>
    public static string ReadCurrentGpuDesc()
    {
        var primary = FindPrimaryGpuEnumKey();
        if (primary is not null)
        {
            var friendly = ReadValue(primary, "FriendlyName");
            if (!string.IsNullOrEmpty(friendly)) return StripDisplayPrefix(friendly);
            var desc = ReadValue(primary, "DeviceDesc");
            if (!string.IsNullOrEmpty(desc)) return StripDisplayPrefix(desc);
        }

        var videoKey = FindPrimaryGpuKey();
        if (videoKey is not null)
        {
            var val = ReadValue(videoKey, "DriverDesc");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        var classKey = FindPrimaryGpuClassKey();
        if (classKey is not null)
        {
            var val = ReadValue(classKey, "DriverDesc");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return "";
    }

    /// <summary>
    /// Strips the hardware-ID / localized-INF prefix from a display name
    /// (e.g. "@oem30.inf,%token%;Intel(R) Arc(TM) B390 GPU" -> "Intel(R) Arc(TM) B390 GPU").
    /// </summary>
    private static string StripDisplayPrefix(string value)
    {
        var idx = value.LastIndexOf(';');
        if (idx < 0) return value;
        var result = value[(idx + 1)..].Trim();
        return string.IsNullOrEmpty(result) ? value : result;
    }

    public static string ReadCurrentGpuProvider()
    {
        var videoKey = FindPrimaryGpuKey();
        if (videoKey is not null)
        {
            var val = ReadValue(videoKey, "ProviderName");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        var classKey = FindPrimaryGpuClassKey();
        if (classKey is not null)
        {
            var val = ReadValue(classKey, "ProviderName");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return "";
    }

    public static List<HardwareSpooferEntry> ReadAllCurrent()
    {
        var entries = new List<HardwareSpooferEntry>();

        // CPU
        var cpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = cpuKey, ValueName = "ProcessorNameString",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(cpuKey, "ProcessorNameString"),
            CurrentValue = ReadValue(cpuKey, "ProcessorNameString")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = cpuKey, ValueName = "VendorIdentifier",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(cpuKey, "VendorIdentifier"),
            CurrentValue = ReadValue(cpuKey, "VendorIdentifier")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = cpuKey, ValueName = "~MHz",
            Kind = RegistryValueKind.DWord,
            OriginalValue = ReadDword(cpuKey, "~MHz").ToString(),
            CurrentValue = ReadDword(cpuKey, "~MHz").ToString()
        });

        // GPU — just store the display values; actual write uses WriteGpuName
        var gpuDesc = ReadCurrentGpuDesc();
        var gpuProvider = ReadCurrentGpuProvider();
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = "__GPU__", ValueName = "DriverDesc",
            Kind = RegistryValueKind.String,
            OriginalValue = gpuDesc,
            CurrentValue = gpuDesc
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = "__GPU__", ValueName = "ProviderName",
            Kind = RegistryValueKind.String,
            OriginalValue = gpuProvider,
            CurrentValue = gpuProvider
        });

        // System info
        var sysKey = @"SYSTEM\CurrentControlSet\Control\SystemInformation";
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = sysKey, ValueName = "SystemProductName",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(sysKey, "SystemProductName"),
            CurrentValue = ReadValue(sysKey, "SystemProductName")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = sysKey, ValueName = "SystemManufacturer",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(sysKey, "SystemManufacturer"),
            CurrentValue = ReadValue(sysKey, "SystemManufacturer")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = sysKey, ValueName = "SystemFamily",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(sysKey, "SystemFamily"),
            CurrentValue = ReadValue(sysKey, "SystemFamily")
        });

        // BIOS
        var biosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BIOSVendor",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BIOSVendor"),
            CurrentValue = ReadValue(biosKey, "BIOSVendor")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BIOSVersion",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BIOSVersion"),
            CurrentValue = ReadValue(biosKey, "BIOSVersion")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BaseBoardManufacturer",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BaseBoardManufacturer"),
            CurrentValue = ReadValue(biosKey, "BaseBoardManufacturer")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BaseBoardProduct",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BaseBoardProduct"),
            CurrentValue = ReadValue(biosKey, "BaseBoardProduct")
        });

        return entries;
    }

    public static string? FindPrimaryGpuKey()
    {
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (videoKey is null) return null;

            foreach (var subKeyName in videoKey.GetSubKeyNames())
            {
                var subPath = $@"SYSTEM\CurrentControlSet\Control\Video\{subKeyName}\0000";
                using var subKey = Registry.LocalMachine.OpenSubKey(subPath, writable: false);
                if (subKey is null) continue;

                var desc = subKey.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(desc)) continue;

                if (IsVirtualGpu(desc)) continue;

                return subPath;
            }
        }
        catch { }

        return null;
    }

    public static string? FindPrimaryGpuClassKey()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (subKeyName.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                    subKeyName.Equals("Properties", StringComparison.OrdinalIgnoreCase))
                    continue;

                var subPath = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{subKeyName}";
                using var subKey = Registry.LocalMachine.OpenSubKey(subPath, writable: false);
                if (subKey is null) continue;

                var desc = subKey.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(desc)) continue;

                if (IsVirtualGpu(desc)) continue;

                return subPath;
            }
        }
        catch { }

        return null;
    }

    private static bool IsVirtualGpu(string desc)
    {
        return desc.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("RDPDD", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("VGA Save", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("IddDesk", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("GameViewer", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase);
    }

    public static void SaveBackup(List<HardwareSpooferEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(BackupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Also backup the raw GPU registry values for full restore
            var gpuBackup = new Dictionary<string, Dictionary<string, string>>();
            foreach (var keyPath in new[] { FindPrimaryGpuKey(), FindPrimaryGpuClassKey() })
            {
                if (keyPath is null) continue;
                try
                {
                    var hive = GetHive(keyPath, out var subKey);
                    using var key = hive.OpenSubKey(subKey, writable: false);
                    if (key is null) continue;
                    var vals = new Dictionary<string, string>();
                    foreach (var vn in key.GetValueNames())
                    {
                        if (vn.Equals("DriverDesc", StringComparison.OrdinalIgnoreCase) ||
                            vn.Equals("ProviderName", StringComparison.OrdinalIgnoreCase) ||
                            vn.Equals("HardwareInformation.ChipType", StringComparison.OrdinalIgnoreCase) ||
                            vn.Equals("HardwareInformation.AdapterString", StringComparison.OrdinalIgnoreCase))
                        {
                            vals[vn] = ReadValue(keyPath, vn);
                        }
                    }
                    gpuBackup[keyPath] = vals;
                }
                catch { }
            }

            var json = JsonSerializer.Serialize(new
            {
                Entries = entries.Select(e => new { e.KeyPath, e.ValueName, e.Kind, e.OriginalValue }).ToList(),
                GpuBackup = gpuBackup
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BackupPath, json);
        }
        catch { }
    }

    public static List<HardwareSpooferEntry>? LoadBackup()
    {
        try
        {
            if (!File.Exists(BackupPath)) return null;
            var json = File.ReadAllText(BackupPath);
            var doc = JsonDocument.Parse(json);

            var entriesArray = doc.RootElement.TryGetProperty("Entries", out var entriesEl)
                ? entriesEl
                : doc.RootElement;

            var items = JsonSerializer.Deserialize<List<BackupItem>>(entriesArray.GetRawText());
            if (items is null) return null;

            return items.Select(i => new HardwareSpooferEntry
            {
                KeyPath = i.KeyPath,
                ValueName = i.ValueName,
                Kind = i.Kind,
                OriginalValue = i.OriginalValue,
                CurrentValue = i.OriginalValue
            }).ToList();
        }
        catch { return null; }
    }

    public static int ApplyChanges(List<HardwareSpooferEntry> entries)
    {
        if (!HasBackup)
            SaveBackup(entries);

        var count = 0;

        // GPU — use the dedicated multi-path writer
        var gpuDesc = entries.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "DriverDesc");
        var gpuProvider = entries.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "ProviderName");
        if (gpuDesc?.IsModified == true)
        {
            if (WriteGpuName(gpuDesc.CurrentValue!, gpuProvider?.CurrentValue))
                count++;
        }

        // Other entries — standard single-key write
        foreach (var entry in entries)
        {
            if (entry.KeyPath == "__GPU__") continue; // already handled above
            if (entry.CurrentValue is null || entry.CurrentValue == entry.OriginalValue) continue;
            if (WriteValue(entry.KeyPath, entry.ValueName, entry.CurrentValue, entry.Kind))
                count++;
        }
        return count;
    }

    public static int RestoreAll()
    {
        var backup = LoadBackup();
        if (backup is null) return 0;

        var count = 0;

        // GPU restore — use the dedicated multi-path writer
        var gpuDesc = backup.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "DriverDesc");
        var gpuProvider = backup.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "ProviderName");
        if (gpuDesc is not null)
        {
            if (WriteGpuName(gpuDesc.OriginalValue, gpuProvider?.OriginalValue))
                count++;
        }

        // Other entries
        foreach (var entry in backup)
        {
            if (entry.KeyPath == "__GPU__") continue;
            if (WriteValue(entry.KeyPath, entry.ValueName, entry.OriginalValue, entry.Kind))
                count++;
        }

        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }

        return count;
    }

    public static void DeleteBackup()
    {
        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
    }

    private static RegistryKey GetHive(string keyPath, out string subKey)
    {
        subKey = keyPath;
        return Registry.LocalMachine;
    }

    private sealed class BackupItem
    {
        public required string KeyPath { get; init; }
        public required string ValueName { get; init; }
        public required RegistryValueKind Kind { get; init; }
        public required string OriginalValue { get; init; }
    }
}
