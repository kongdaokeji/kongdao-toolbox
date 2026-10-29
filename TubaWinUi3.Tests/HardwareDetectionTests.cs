using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwareDetectionTests
{
    [Fact]
    public void MemoryChannels_ControllerNumbersDoNotCountAsExtraChannels()
    {
        Assert.Equal("单通道", HardwareInfoService.InferMemoryChannel(
            ["Controller0-ChannelA-DIMM0", "Controller1-ChannelA-DIMM0"]));
    }

    [Theory]
    [InlineData("ChannelA-DIMM0", "ChannelB-DIMM0", "双通道")]
    [InlineData("ChannelA-DIMM0", "ChannelA-DIMM1", "单通道")]
    [InlineData("DIMM_Channel_A", "DIMM_Channel_B", "双通道")]
    [InlineData("controller0-channel0-dimm0", "Controller0-Channel1-DIMM0", "双通道")]
    [InlineData("ChannelA-DIMM0", "DIMM 1", null)]
    [InlineData("ChannelA-DIMM0", "Controller0-ChannelA-DIMM1", "单通道")]
    [InlineData("ChannelA-DIMM0", null, null)]
    [InlineData("ChannelAmbiguous", "ChannelB-DIMM0", null)]
    public void MemoryChannels_RequireCompleteUnambiguousTopology(string first, string? second, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.InferMemoryChannel([first, second]));
    }

    [Fact]
    public void CpuzChannelMode_ReplacesInference()
    {
        var data = new HardwareDetailData { Memory = new() { ChannelMode = "单通道", ChannelModeInferred = true } };
        HardwareInfoService.ApplyCpuzDetailOverride(data, new CpuzInfo { MemoryChannel = "Dual" });
        Assert.Equal("Dual", data.Memory.ChannelMode);
        Assert.False(data.Memory.ChannelModeInferred);
    }

    [Theory]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, false, true, false, null)]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(false, false, false, true, false)]
    [InlineData(null, false, false, false, null)]
    public void ProcessorFeatures_HypervisorDoesNotCauseFalseNegative(
        bool? reported, bool osSupported, bool? hypervisor, bool virtualization, bool? expected)
    {
        Assert.Equal(expected, HardwareInfoService.ResolveProcessorFeature(reported, osSupported, hypervisor, virtualization));
    }

    [Theory]
    [InlineData(1600, 199, "3.18 GHz")]
    [InlineData(1900, 100, "1.90 GHz")]
    [InlineData(1900, 50, "0.95 GHz")]
    [InlineData(0, 100, null)]
    [InlineData(1900, 0, null)]
    public void CpuClock_AllowsTurboAndRejectsMissingCounters(int frequency, int percent, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.CalculateCpuClock(frequency, percent));
    }
}
