using System.Security.Cryptography;

namespace TubaWinUi3.Services.Downloads;

/// <summary>
/// 下载完整性校验：合并分段时单遍流式计算 SHA256（不额外读盘），
/// 以及预期哈希比对（大小写不敏感，兼容 hex 前缀写法）。
/// </summary>
public static class DownloadHashVerifier
{
    /// <summary>
    /// 顺序合并分段文件到目标路径，同时流式计算合并结果的 SHA256。
    /// 返回小写十六进制哈希。目标已存在的旧文件会被覆盖。
    /// </summary>
    public static string MergePartsComputeSha256(
        IReadOnlyList<string> partPaths, string destPath, CancellationToken ct = default)
    {
        using var output = new FileStream(destPath, FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var sha = SHA256.Create();

        var buffer = new byte[81920];
        foreach (var part in partPaths)
        {
            using var input = new FileStream(part, FileMode.Open,
                FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                output.Write(buffer, 0, read);
                sha.TransformBlock(buffer, 0, read, null, 0);
            }
        }

        sha.TransformFinalBlock([], 0, 0);
        output.Flush();
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>单遍流式计算已有文件的 SHA256（单流下载完成后的校验路径）。</summary>
    public static string ComputeFileSha256(string filePath, CancellationToken ct = default)
    {
        using var input = new FileStream(filePath, FileMode.Open,
            FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>预期哈希是否与实际一致；未提供预期值视为通过。</summary>
    public static bool Verify(string? expectedSha256, string actualSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
        return string.Equals(expectedSha256.Trim(), actualSha256, StringComparison.OrdinalIgnoreCase);
    }
}
