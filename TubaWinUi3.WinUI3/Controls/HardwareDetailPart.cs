namespace TubaWinUi3.Controls;

/// <summary>
/// 硬件详情页的信息分区。采集侧新增一个数据块时必须同步补上分区定义
/// （<c>SectionDefinitions_CoverEveryDetailPart</c> 会拦截漏配），否则那块数据永远不会显示。
/// </summary>
public enum HardwareDetailPart
{
    /// <summary>Windows 系统：版本、内部版本、授权、DirectX、.NET、缩放、运行时间等。</summary>
    System,

    /// <summary>整机身份：厂商、型号、序列号、UUID、机箱类型、固件模式。</summary>
    Computer,

    /// <summary>处理器。</summary>
    Cpu,

    /// <summary>主板与 BIOS。</summary>
    Motherboard,

    /// <summary>内存与插槽。</summary>
    Memory,

    /// <summary>显卡。</summary>
    Gpu,

    /// <summary>NPU（AI 加速器，仅有该硬件时显示）。</summary>
    Npu,

    /// <summary>硬盘与分区。</summary>
    Disk,

    /// <summary>显示器。</summary>
    Display,

    /// <summary>声卡。</summary>
    Sound,

    /// <summary>网卡。</summary>
    Network,

    /// <summary>电池（仅笔记本显示）。</summary>
    Battery,

    /// <summary>安全特性：TPM、安全启动、VBS、内存完整性。</summary>
    Security,

    /// <summary>USB 控制器与设备。</summary>
    Usb,

    /// <summary>其他设备（驱动异常/未知设备，仅存在时显示）。</summary>
    Devices,
}
