using TubaWinUi3.Services;
using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「文件占用查看」失败提示的显示层映射：服务层只产原因码（<see cref="FileLockScanFailure"/>
/// 加数值参数），文案在这里翻（仓库约定：服务产数据键、显示层翻译）。
/// 引用的 resw 键由 FileLockErrorTextTests 钉住（中英都要有）。
/// </summary>
internal static class FileLockErrorText
{
    private static string L(string key, string fallback) => LocalizationService.L(key, fallback);

    /// <summary>把失败原因译成一句已本地化的说明。None（没有失败）没有详情可显示。</summary>
    internal static string Describe(FileLockScanFailure failure, int code) => failure switch
    {
        FileLockScanFailure.None => "",
        FileLockScanFailure.CreateFileFailed =>
            string.Format(L("FileLock_DetailCreateFileFailed", "无法打开该路径（错误码 {0}）"), code),
        FileLockScanFailure.NotDiskFile =>
            L("FileLock_DetailNotDiskFile", "目标不是磁盘上的文件或目录"),
        FileLockScanFailure.DeviceNameUnavailable =>
            L("FileLock_DetailDeviceNameUnavailable", "无法解析目标的设备名"),
        FileLockScanFailure.ObjectTypeIndexUnavailable =>
            L("FileLock_DetailTypeIndexUnavailable", "无法确定文件对象的类型索引"),
        FileLockScanFailure.HandleTableTooLarge =>
            string.Format(L("FileLock_DetailHandleTableTooLarge", "系统句柄表过大（需要 {0} 字节），已放弃扫描"), code),
        FileLockScanFailure.NtQuerySystemInformationFailed =>
            string.Format(L("FileLock_DetailQuerySystemFailed", "查询系统句柄表失败（状态码 0x{0:X8}）"), code),
        _ => L("FileLock_DetailUnexpected", "发生未预期的错误"),
    };
}
