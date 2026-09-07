using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    public static class ToolIconService
    {
        private static string CacheRoot { get { return ConfigManager.GetIconCacheDir(); } }

        private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(90);

        private static readonly Dictionary<string, string> ExtensionGlyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".bat", "\uE756" },
            { ".cmd", "\uE756" },
            { ".ps1", "\uE943" },
            { ".vbs", "\uE943" },
            { ".msc", "\uEC7A" }
        };

        public static string GetIconGlyph(string toolPath)
        {
            var extension = Path.GetExtension(toolPath);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string glyph;
            return ExtensionGlyphs.TryGetValue(extension, out glyph) ? glyph : "\uE8B7";
        }

        public static string GetCachedIconPath(string toolPath)
        {
            if (!File.Exists(toolPath))
                return null;

            var extension = Path.GetExtension(toolPath);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return null;

            Directory.CreateDirectory(CacheRoot);
            var iconPath = Path.Combine(CacheRoot, Hash(toolPath) + ".png");

            if (!File.Exists(iconPath))
                return null;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
            if (age >= CacheMaxAge)
            {
                try { File.Delete(iconPath); } catch { }
                return null;
            }

            return iconPath;
        }

        public static string ExtractIconToCache(string toolPath)
        {
            if (!File.Exists(toolPath))
                return null;

            var extension = Path.GetExtension(toolPath);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                return null;

            Directory.CreateDirectory(CacheRoot);
            var iconPath = Path.Combine(CacheRoot, Hash(toolPath) + ".png");

            if (File.Exists(iconPath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
                if (age < CacheMaxAge)
                    return iconPath;

                try { File.Delete(iconPath); } catch { return iconPath; }
            }

            try
            {
                using (var icon = Icon.ExtractAssociatedIcon(toolPath))
                {
                    if (icon == null)
                        return null;

                    using (var bitmap = icon.ToBitmap())
                    {
                        bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                        return iconPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Unable to extract icon for " + toolPath + ": " + ex.Message);
                return null;
            }
        }

        public static void LoadIcons(IReadOnlyList<ToolItem> tools)
        {
            foreach (var tool in tools)
            {
                if (!string.IsNullOrEmpty(tool.IconPath) || string.IsNullOrWhiteSpace(tool.Path))
                    continue;

                var ext = Path.GetExtension(tool.Path);
                if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    continue;

                var cached = GetCachedIconPath(tool.Path);
                if (cached != null)
                {
                    tool.IconPath = cached;
                    continue;
                }

                var iconPath = ExtractIconToCache(tool.Path);
                if (iconPath != null)
                    tool.IconPath = iconPath;
            }
        }

        public static void CleanExpiredCache()
        {
            if (!Directory.Exists(CacheRoot))
                return;

            var cutoff = DateTime.UtcNow - CacheMaxAge;

            foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length && i < 8; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
