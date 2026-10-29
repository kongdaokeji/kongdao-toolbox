using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>卷→物理盘映射解析（VOLUME_DISK_EXTENTS 布局回归测试）。</summary>
public class DiskVolumeMappingTests
{
    [Fact]
    public void ParseVolumeDiskNumber_ReadsFirstExtentAtOffset8()
    {
        // 真实布局：偏移 0 = 数量(1)，偏移 4 = LARGE_INTEGER 对齐填充(0)，偏移 8 = 首块物理盘号
        var buffer = new byte[32];
        buffer[0] = 1;
        buffer[4] = 0xFF; // 填充字节可能为任意值，绝不能当作盘号
        buffer[8] = 42;

        Assert.Equal(42u, DiskHealthService.ParseVolumeDiskNumber(buffer));
    }

    [Fact]
    public void ParseVolumeDiskNumber_MultipleExtents_TakesFirst()
    {
        var buffer = new byte[64];
        buffer[0] = 2;
        buffer[8] = 7;
        buffer[32] = 9;

        Assert.Equal(7u, DiskHealthService.ParseVolumeDiskNumber(buffer));
    }

    [Fact]
    public void ParseVolumeDiskNumber_EmptyExtents_ReturnsNull()
    {
        var buffer = new byte[32];
        Assert.Null(DiskHealthService.ParseVolumeDiskNumber(buffer));
    }

    [Fact]
    public void ParseVolumeDiskNumber_ShortBuffer_ReturnsNull()
    {
        Assert.Null(DiskHealthService.ParseVolumeDiskNumber(new byte[16]));
    }
}