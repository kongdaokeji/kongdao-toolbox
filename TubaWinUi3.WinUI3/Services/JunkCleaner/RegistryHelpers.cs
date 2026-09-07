/* Ported from builtbybel/FluentCleaner (MIT) FluentCleaner.Core/Services/RegistryHelpers.cs */

using Microsoft.Win32;

namespace FluentCleaner.Services;

// Maps hive abbreviations (and full names) to their registry root key.
// Shared by DetectionService and CleaningService so neither owns the mapping.
public static class RegistryHelpers
{
    public static RegistryKey? OpenHive(string hive) => hive switch
    {
        "HKCU" or "HKEY_CURRENT_USER"   => Registry.CurrentUser,
        "HKLM" or "HKEY_LOCAL_MACHINE"  => Registry.LocalMachine,
        "HKU"  or "HKEY_USERS"          => Registry.Users,
        "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
        "HKCR" or "HKEY_CLASSES_ROOT"   => Registry.ClassesRoot,
        _ => null
    };
}
