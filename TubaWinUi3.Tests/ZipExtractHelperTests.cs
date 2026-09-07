using System.IO.Compression;
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
}
