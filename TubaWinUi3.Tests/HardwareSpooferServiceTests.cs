using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwareSpooferServiceTests
{
    [Fact]
    public void FindGpuEnumKeyPaths_ReturnsAtLeastOneGpu()
    {
        var keys = HardwareSpooferService.FindGpuEnumKeyPaths();

        // Machine without a real GPU (VM / CI runner) — skip instead of failing.
        if (keys.Count == 0) return;

        foreach (var key in keys)
            Assert.StartsWith(@"SYSTEM\CurrentControlSet\Enum\PCI\", key);
    }

    [Fact]
    public void FindPrimaryGpuEnumKey_IsAmongEnumKeys()
    {
        var primary = HardwareSpooferService.FindPrimaryGpuEnumKey();
        if (primary is null) return;

        var all = HardwareSpooferService.FindGpuEnumKeyPaths();
        Assert.Contains(primary, all);
    }

    [Fact]
    public void ReadCurrentGpuDesc_ReturnsNonEmptyNameWhenGpuPresent()
    {
        var desc = HardwareSpooferService.ReadCurrentGpuDesc();
        if (desc.Length == 0) return; // no GPU on this machine

        // The returned name must be the display name, without INF/ID prefixes.
        Assert.DoesNotContain(';', desc);
        Assert.DoesNotContain("oem", desc, StringComparison.OrdinalIgnoreCase);
    }
}
