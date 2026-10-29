using System.IO.Compression;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;

namespace TubaWinUi3.Tests;

public class ZipExtractHelperTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TubaZipTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateZip(string zipPath, params (string Name, byte[] Content, bool IsDir)[] entries)
    {
        using var fs = File.Create(zipPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content, isDir) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (isDir) continue;
            using var es = entry.Open();
            es.Write(content, 0, content.Length);
        }
    }

    [Fact]
    public void ExtractTolerant_ExtractsAllEntries_WhenZipIsClean()
    {
        var root = CreateTempDir();
        try
        {
            var zipPath = Path.Combine(root, "clean.zip");
            CreateZip(zipPath,
                ("工具/readme.txt", "hello"u8.ToArray(), false),
                ("工具/子目录/data.bin", new byte[] { 1, 2, 3 }, false));

            var dest = Path.Combine(root, "out");
            var skipped = ZipExtractHelper.ExtractTolerant(zipPath, dest);

            Assert.Empty(skipped);
            Assert.True(File.Exists(Path.Combine(dest, "工具", "readme.txt")));
            Assert.True(File.Exists(Path.Combine(dest, "工具", "子目录", "data.bin")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtractTolerant_SkipsDuplicateEntries_CaseInsensitive()
    {
        var root = CreateTempDir();
        try
        {
            var zipPath = Path.Combine(root, "dup.zip");
            CreateZip(zipPath,
                ("工具/a.txt", "first"u8.ToArray(), false),
                ("工具/A.TXT", "second"u8.ToArray(), false));

            var dest = Path.Combine(root, "out");
            var skipped = ZipExtractHelper.ExtractTolerant(zipPath, dest);

            Assert.Empty(skipped);
            Assert.True(File.Exists(Path.Combine(dest, "工具", "a.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtractTolerant_ResolvesFileDirectoryNameConflict()
    {
        var root = CreateTempDir();
        try
        {
            var zipPath = Path.Combine(root, "conflict.zip");
            // 同一路径既出现为文件又出现为目录：应跳过冲突条目而不中断其他文件
            CreateZip(zipPath,
                ("其他工具/DirectX Repair", "conflicting-file"u8.ToArray(), false),
                ("其他工具/DirectX Repair/DirectX Repair.exe.config", "config-content"u8.ToArray(), false),
                ("其他工具/正常工具/ok.txt", "ok"u8.ToArray(), false));

            var dest = Path.Combine(root, "out");
            var skipped = ZipExtractHelper.ExtractTolerant(zipPath, dest);

            Assert.True(File.Exists(Path.Combine(dest, "其他工具", "正常工具", "ok.txt")),
                "正常条目必须解压成功");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtractTolerant_OverwritesReadOnlyExistingFile()
    {
        var root = CreateTempDir();
        try
        {
            var zipPath = Path.Combine(root, "ro.zip");
            CreateZip(zipPath,
                ("工具/a.txt", "new"u8.ToArray(), false));

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(Path.Combine(dest, "工具"));
            var existing = Path.Combine(dest, "工具", "a.txt");
            File.WriteAllText(existing, "old");
            File.SetAttributes(existing, FileAttributes.ReadOnly);

            var skipped = ZipExtractHelper.ExtractTolerant(zipPath, dest);

            Assert.Empty(skipped);
            Assert.Equal("new", File.ReadAllText(existing));
            File.SetAttributes(existing, FileAttributes.Normal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtractTolerant_SkipsLockedFile_AndKeepsOthers()
    {
        var root = CreateTempDir();
        try
        {
            var zipPath = Path.Combine(root, "locked.zip");
            CreateZip(zipPath,
                ("工具/locked.txt", "data"u8.ToArray(), false),
                ("工具/ok.txt", "ok"u8.ToArray(), false));

            var dest = Path.Combine(root, "out");
            Directory.CreateDirectory(Path.Combine(dest, "工具"));
            var locked = Path.Combine(dest, "工具", "locked.txt");
            File.WriteAllText(locked, "old");
            File.SetAttributes(locked, FileAttributes.ReadOnly);

            // 只读 + 目标已存在：会被清除只读后覆盖，此处验证不会抛异常
            var skipped = ZipExtractHelper.ExtractTolerant(zipPath, dest);

            Assert.True(File.Exists(Path.Combine(dest, "工具", "ok.txt")),
                "未冲突条目必须解压成功");
            File.SetAttributes(locked, FileAttributes.Normal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CreateStagingDirectory_PrefersDestinationSibling_AndLeavesNoProbeFile()
    {
        var root = CreateTempDir();
        try
        {
            var destination = Path.Combine(root, "Tools");
            Directory.CreateDirectory(destination);

            var staging = ZipExtractHelper.CreateStagingDirectory(destination);

            // 同级目录 = 同卷，最终替换才是原子重命名；也避开不支持 EFS 的 TEMP 卷
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(Path.GetDirectoryName(staging)!));
            Assert.True(Directory.Exists(staging));
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging));

            Directory.Delete(staging, true);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CreateStagingDirectory_KeepsCommunityToolStagingOutsideToolsRoot()
    {
        var root = CreateTempDir();
        try
        {
            var destination = Path.Combine(root, "Tools", "分类", "某工具");
            Directory.CreateDirectory(destination);

            var staging = ZipExtractHelper.CreateStagingDirectory(destination);

            // 暂存目录里满是 exe，放进 Tools 分类目录会被工具扫描误收录
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(Path.GetDirectoryName(staging)!));

            Directory.Delete(staging, true);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtractTolerantAndReplace_LeavesNoStagingDirectoryNextToDestination()
    {
        var root = CreateTempDir();
        try
        {
            var zipPath = Path.Combine(root, "tool.zip");
            CreateZip(zipPath, ("app/run.exe", "bin"u8.ToArray(), false));
            var destination = Path.Combine(root, "Tools");

            ZipExtractHelper.ExtractTolerantAndReplace(
                zipPath, destination, ExtractReplaceProfile.CommunityTool);

            Assert.True(File.Exists(Path.Combine(destination, "app", "run.exe")));
            Assert.Empty(Directory.GetDirectories(root, ".tuba_extract_*"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsEncryptionFailure_DetectsWin32Error1770_AndIgnoresOtherErrors()
    {
        var encryptionError = Marshal.GetExceptionForHR(unchecked((int)0x80071770))!;

        Assert.True(ZipExtractHelper.IsEncryptionFailure(encryptionError));
        Assert.True(ZipExtractHelper.IsEncryptionFailure(
            new IOException("wrapper", encryptionError)));
        Assert.False(ZipExtractHelper.IsEncryptionFailure(new IOException("other")));
    }
}
