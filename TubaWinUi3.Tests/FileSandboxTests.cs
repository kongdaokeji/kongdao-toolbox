using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class FileSandboxTests
{
    [Fact]
    public void ValidateWrite_EmptyPath_Rejected()
        => Assert.NotNull(FileSandbox.ValidateWrite(""));

    [Fact]
    public void ValidateWrite_InvalidChars_Rejected()
        => Assert.NotNull(FileSandbox.ValidateWrite("C:\\bad\0path\\file.txt"));

    [Fact]
    public void ValidateWrite_SystemRoot_Rejected()
    {
        var sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var err = FileSandbox.ValidateWrite(Path.Combine(sysRoot, "temp", "x.txt"));
        Assert.NotNull(err);
        Assert.Contains("沙箱", err);
    }

    [Fact]
    public void ValidateWrite_ProgramFiles_Rejected()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(pf)) return;
        Assert.NotNull(FileSandbox.ValidateWrite(Path.Combine(pf, "Test", "x.txt")));
    }

    [Fact]
    public void ValidateWrite_ExecutableExtension_Rejected()
        => Assert.NotNull(FileSandbox.ValidateWrite(Path.Combine(Path.GetTempPath(), "evil.exe")));

    [Fact]
    public void ValidateWrite_ScriptExtension_Rejected()
        => Assert.NotNull(FileSandbox.ValidateWrite(Path.Combine(Path.GetTempPath(), "script.ps1")));

    [Fact]
    public void ValidateWrite_UserProfile_Allowed()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile)) return;
        Assert.Null(FileSandbox.ValidateWrite(Path.Combine(userProfile, "Documents", "note.txt")));
    }

    [Fact]
    public void ValidateWrite_TempFile_Allowed()
        => Assert.Null(FileSandbox.ValidateWrite(Path.Combine(Path.GetTempPath(), $"sandbox-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void IsWithin_CaseInsensitiveAndTrailingSlash()
    {
        var root = Path.Combine(Path.GetTempPath(), "Root");
        Assert.True(FileSandbox.IsWithin(root, Path.Combine(root, "sub", "file.txt")));
        Assert.True(FileSandbox.IsWithin(root, Path.Combine(root, "SUB", "FILE.TXT")));
        // 路径只是前缀相同但不属于该目录（root2 不是 root 的子路径）
        Assert.False(FileSandbox.IsWithin(root, root + "2\\file.txt"));
    }
}
