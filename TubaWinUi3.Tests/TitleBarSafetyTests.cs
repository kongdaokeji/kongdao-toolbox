using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 启动期标题栏守卫的回归测试。
///
/// 现场：应用商店崩溃报告里的
/// STOWED_EXCEPTION_80004003_Microsoft.UI.Xaml.dll!DirectUI::FrameworkApplicationGenerated::OnLaunchedProtected。
/// MainWindow 的构造函数运行在 App.OnLaunched 内，其中对 AppWindow.TitleBar 的解引用
/// 在部分 Windows 10 版本上会返回 null（WinUI 已知问题 microsoft-ui-xaml#6101），
/// 空引用跨 ABI 映射为 0x80004003（E_POINTER）后整个进程闪退。
/// 这里锁定「标题栏不可用时必须静默跳过，不能抛异常」这一契约。
/// </summary>
public class TitleBarSafetyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TitleBarPalette_Apply_NullTitleBar_DoesNotThrow(bool isDark)
    {
        var exception = Record.Exception(() => TitleBarPalette.Apply(null, isDark));

        Assert.Null(exception);
    }
}
