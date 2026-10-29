using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class DownloadQueueErrorTests
{
    [Fact]
    public void DescribeError_ReplacesRawEncryptionFailure_WithActionableHint()
    {
        var raw = Marshal.GetExceptionForHR(unchecked((int)0x80071770))!;

        var message = DownloadQueueService.DescribeError(raw);

        Assert.Contains("EFS", message);
        Assert.Contains("内存盘", message);
    }

    [Fact]
    public void DescribeError_KeepsFriendlyWrapperMessage_EvenWhenInnerIsEncryptionFailure()
    {
        var inner = Marshal.GetExceptionForHR(unchecked((int)0x80071770))!;
        var wrapper = new IOException("内核安装失败：目标文件夹带「加密」属性，但所在磁盘不支持文件加密（EFS）。", inner);

        Assert.Equal(wrapper.Message, DownloadQueueService.DescribeError(wrapper));
    }
}
