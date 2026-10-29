using TubaWinUi3.Services.FileLock;

namespace TubaWinUi3.Tests;

/// <summary>
/// 目标路径匹配：目录前缀比较是最容易写错的地方
/// （漏掉分隔符就会让 "...\dir" 误配 "...\directory"），这里逐条钉死。
/// </summary>
public class FileLockMatchingTests
{
    private const string Target = @"\Device\HarddiskVolume3\Users\me\docs";

    // ---------- 文件：精确相等 ----------

    [Fact]
    public void File_ExactPath_Matches()
    {
        Assert.True(FileLockService.MatchesTargetPath(
            @"\Device\HarddiskVolume3\temp\a.txt",
            @"\Device\HarddiskVolume3\temp\a.txt",
            isDirectory: false));
    }

    [Fact]
    public void File_CaseInsensitive_Matches()
    {
        Assert.True(FileLockService.MatchesTargetPath(
            @"\Device\HarddiskVolume3\Temp\A.TXT",
            @"\device\harddiskvolume3\temp\a.txt",
            isDirectory: false));
    }

    [Fact]
    public void File_DifferentPath_DoesNotMatch()
    {
        Assert.False(FileLockService.MatchesTargetPath(
            @"\Device\HarddiskVolume3\temp\a.txt",
            @"\Device\HarddiskVolume3\temp\b.txt",
            isDirectory: false));
    }

    [Fact]
    public void File_DoesNotMatchSubPath()
    {
        // 目标是文件时，任何更深的路径都不算占用（文件不可能有子对象）
        Assert.False(FileLockService.MatchesTargetPath(
            @"\Device\HarddiskVolume3\temp\a.txt",
            @"\Device\HarddiskVolume3\temp\a.txt\extra",
            isDirectory: false));
    }

    [Fact]
    public void File_DoesNotMatchShorterPrefix()
    {
        // 目标文件的父目录名不能被当成命中
        Assert.False(FileLockService.MatchesTargetPath(
            @"\Device\HarddiskVolume3\temp\a.txt",
            @"\Device\HarddiskVolume3\temp",
            isDirectory: false));
    }

    // ---------- 目录：相等或带分隔符的前缀 ----------

    [Fact]
    public void Directory_SamePath_Matches()
    {
        Assert.True(FileLockService.MatchesTargetPath(Target, Target, isDirectory: true));
    }

    [Fact]
    public void Directory_ChildFile_Matches()
    {
        Assert.True(FileLockService.MatchesTargetPath(Target, Target + @"\report.docx", isDirectory: true));
    }

    [Fact]
    public void Directory_DeepDescendant_Matches()
    {
        Assert.True(FileLockService.MatchesTargetPath(Target, Target + @"\sub\deeper\file.bin", isDirectory: true));
    }

    [Fact]
    public void Directory_SiblingWithSharedPrefix_DoesNotMatch()
    {
        // 最易错的一条：目录边界必须带 '\'，否则 "...\docs" 会误配 "...\docs-backup"
        Assert.False(FileLockService.MatchesTargetPath(
            @"\Device\HarddiskVolume3\Users\me\docs",
            @"\Device\HarddiskVolume3\Users\me\docs-backup\x.txt",
            isDirectory: true));
    }

    [Fact]
    public void Directory_Parent_DoesNotMatch()
    {
        Assert.False(FileLockService.MatchesTargetPath(Target, @"\Device\HarddiskVolume3\Users\me", isDirectory: true));
    }

    [Fact]
    public void Directory_OtherVolume_DoesNotMatch()
    {
        Assert.False(FileLockService.MatchesTargetPath(
            Target,
            @"\Device\HarddiskVolume4\Users\me\docs\a.txt",
            isDirectory: true));
    }

    // ---------- 末尾反斜杠归一化 ----------

    [Fact]
    public void TrailingBackslash_OnTarget_IsNormalized()
    {
        Assert.True(FileLockService.MatchesTargetPath(Target + @"\", Target + @"\a.txt", isDirectory: true));
        Assert.True(FileLockService.MatchesTargetPath(Target + @"\", Target, isDirectory: true));
    }

    [Fact]
    public void TrailingBackslash_OnCandidate_IsNormalized()
    {
        Assert.True(FileLockService.MatchesTargetPath(Target, Target + @"\", isDirectory: true));
    }

    // ---------- 空/无效输入 ----------

    [Theory]
    [InlineData("", @"\Device\HarddiskVolume3\a.txt")]
    [InlineData("   ", @"\Device\HarddiskVolume3\a.txt")]
    [InlineData(@"\Device\HarddiskVolume3\a.txt", "")]
    [InlineData(@"\Device\HarddiskVolume3\a.txt", "   ")]
    [InlineData(null, @"\Device\HarddiskVolume3\a.txt")]
    [InlineData(@"\Device\HarddiskVolume3\a.txt", null)]
    public void EmptyOrWhitespace_ReturnsFalse(string? target, string? candidate)
    {
        Assert.False(FileLockService.MatchesTargetPath(target!, candidate!, isDirectory: true));
        Assert.False(FileLockService.MatchesTargetPath(target!, candidate!, isDirectory: false));
    }

    // ---------- final path 的显示归一化 ----------

    [Theory]
    [InlineData(@"\\?\C:\Users\me\docs", @"C:\Users\me\docs")]
    [InlineData(@"\\?\C:\", @"C:\")]
    [InlineData(@"\\?\UNC\server\share\dir", @"\\server\share\dir")]
    [InlineData(@"\\?\Volume{9f0e2a3b-0000-0000-0000-100000000000}\dir",
                @"\\?\Volume{9f0e2a3b-0000-0000-0000-100000000000}\dir")]
    [InlineData(@"C:\already\plain", @"C:\already\plain")]
    public void FinalPathDisplay_DropsExtendedPrefixOnlyWhenSafe(string input, string expected)
        => Assert.Equal(expected, FileLockService.NormalizeFinalPath(input));

    [Fact]
    public void FinalPath_MatchingUsesSameRulesAsKernelName()
    {
        // 主路径双方都是 final path（带 \\?\ 前缀），目录边界规则必须与内核名完全一致
        Assert.True(FileLockService.MatchesTargetPath(
            @"\\?\C:\Users\me\docs", @"\\?\C:\Users\me\docs\report.docx", isDirectory: true));
        Assert.False(FileLockService.MatchesTargetPath(
            @"\\?\C:\Users\me\docs", @"\\?\C:\Users\me\docs-backup\x.txt", isDirectory: true));
    }

    [Theory]
    [InlineData(@"C:\Users\me\docs", "C:")]
    [InlineData(@"\\?\C:\Users\me", "C:")]
    [InlineData(@"\\server\share\dir", @"\\server\share")]
    [InlineData(@"\\?\UNC\server\share\dir", @"\\server\share")]
    [InlineData("", "")]
    [InlineData(@"relative\path", "")]
    public void VolumePrefix_IsPureStringComparison(string path, string expected)
        => Assert.Equal(expected, FileLockService.VolumePrefixOf(path));

    // ---------- 高危 GrantedAccess ----------
    //
    // 这里原本钉着 IsGuardedAccess(0x0012019F) == true。该判定已删除：
    // 0x0012019F 就是 FILE_GENERIC_READ|FILE_GENERIC_WRITE（读写打开），
    // 整类跳过会让 Excel / Word / FileStream(ReadWrite) 的占用被静默漏报。
    // 回归覆盖改在 FileLockScanIntegrationTests.Scan_ForFileHeldReadWrite_FindsOwningProcess——
    // 那里真的以读写方式持有文件并断言它必须被报出来，比钉一个常量的真假更有意义。
}
