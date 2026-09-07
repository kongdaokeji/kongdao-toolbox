using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// ToolProcessLauncher 目录防呆测试：link.json 内置链接工具的 EffectivePath 是目录，
/// 若误把目录传给 Launch，UseShellExecute 会打开资源管理器文件夹而不是工具窗口。
/// 回归测试确保该路径永远抛异常而不是启动目录。
/// </summary>
public sealed class ToolProcessLauncherTests
{
    [Fact]
    public void Launch_DirectoryPath_ThrowsInvalidOperation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tuba-launcher-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ToolProcessLauncher.Launch(dir));
            Assert.Contains("文件夹", ex.Message);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void Launch_NonexistentFile_ThrowsRatherThanOpeningAnything()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tuba-launcher-missing-" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.False(File.Exists(missing));
        // 目录防呆只拦目录；不存在的文件路径不应触发"文件夹"误判
        Assert.ThrowsAny<Exception>(() => ToolProcessLauncher.Launch(missing));
    }
}
