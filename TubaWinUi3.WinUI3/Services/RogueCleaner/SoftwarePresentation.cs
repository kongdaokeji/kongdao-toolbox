#nullable disable
// 移植自 RogueCleaner（https://github.com/aakk007/RogueCleaner），MIT License，Copyright (c) 2026 aakk007
// 原版为 .NET Framework 4.x WinForms；此处为 WinUI 3 移植，逻辑保持一致。

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TubaWinUi3.Services.RogueCleaner
{

    internal sealed class SoftwarePresentationEvidence
    {
        public string DeclaredName { get; set; }
        public string DeclaredVendor { get; set; }
        public string IconValue { get; set; }
        public string Command { get; set; }
        public string FilePath { get; set; }
        public string ServiceName { get; set; }
        public string Clsid { get; set; }
        public string TechnicalLocation { get; set; }
    }

    internal sealed class SoftwarePresentation
    {
        public Image Icon { get; set; }
        public string SoftwareName { get; set; }
        public string Vendor { get; set; }
        public string Confidence { get; set; }
        public string IconSource { get; set; }
        public string Explanation { get; set; }
    }

    internal static class SoftwarePresentationResolver
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string fileName, int iconIndex, IntPtr[] largeIcons, IntPtr[] smallIcons, uint iconCount);

        private sealed class IconCandidate
        {
            public string Path;
            public int Index;
            public bool Explicit;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Image> IconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> RepresentativeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> InstalledRepresentativeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Image fallbackIcon;

        public static Image PlaceholderIcon
        {
            get
            {
                lock (Sync)
                {
                    if (fallbackIcon == null)
                    {
                        // 中性占位图标：Windows 通用应用图标（不提取本程序自身图标）
                        try { fallbackIcon = Resize(SystemIcons.Application.ToBitmap(), 20, 20); }
                        catch { fallbackIcon = new Bitmap(20, 20); }
                    }
                    return fallbackIcon;
                }
            }
        }

        public static SoftwarePresentation Resolve(SoftwarePresentationEvidence evidence)
        {
            evidence = evidence ?? new SoftwarePresentationEvidence();
            IconCandidate declaredIcon = ParseIconCandidate(evidence.IconValue);
            string path = FirstExistingExecutable(evidence.FilePath, evidence.Command);
            string reason = string.Empty;

            if (string.IsNullOrEmpty(path) && declaredIcon != null)
            {
                path = declaredIcon.Path;
                reason = "来自菜单声明的图标资源";
            }

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(evidence.ServiceName))
            {
                path = ResolveServiceBinary(evidence.ServiceName);
                if (!string.IsNullOrEmpty(path)) reason = "来自服务注册信息";
            }
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(evidence.Clsid))
            {
                path = ResolveClsidBinary(evidence.Clsid);
                if (!string.IsNullOrEmpty(path)) reason = "来自右键扩展注册信息";
            }

            string vendor = CleanIdentity(evidence.DeclaredVendor);
            string name = CleanIdentity(evidence.DeclaredName);
            string confidence = "Unknown";
            if (!string.IsNullOrEmpty(path))
            {
                string fileName = Path.GetFileName(path);
                bool windows = IsWindowsBinary(path);
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                    if (string.IsNullOrEmpty(vendor)) vendor = CleanIdentity(info.CompanyName);
                    string product = CleanIdentity(info.ProductName);
                    if (!string.IsNullOrEmpty(product)) name = product;
                }
                catch { }
                if (!windows && IsWindowsAppsBinary(path) && IsMicrosoftVendor(vendor)) windows = true;
                if (windows)
                {
                    if (string.IsNullOrEmpty(vendor)) vendor = "微软 / Windows";
                    name = "Windows 系统组件";
                    confidence = "System";
                }
                else
                {
                    if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(fileName);
                    confidence = "Confirmed";
                }
                if (string.IsNullOrEmpty(reason)) reason = "来自实际执行文件";
            }

            IconCandidate iconCandidate = declaredIcon;
            if (iconCandidate == null && !string.IsNullOrEmpty(path)) iconCandidate = new IconCandidate { Path = path, Index = 0, Explicit = false };
            if (iconCandidate != null && iconCandidate.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !IsWindowsBinary(iconCandidate.Path))
            {
                string representative = FindRepresentativeExecutable(iconCandidate.Path, evidence, name, vendor);
                if (!string.IsNullOrEmpty(representative)) iconCandidate = new IconCandidate { Path = representative, Index = 0, Explicit = false };
            }

            if (string.IsNullOrEmpty(name)) name = "来源未确认";
            if (string.IsNullOrEmpty(vendor)) vendor = confidence == "System" ? "微软 / Windows" : "来源未确认";
            string iconPath = iconCandidate == null ? string.Empty : iconCandidate.Path;
            return new SoftwarePresentation
            {
                Icon = string.IsNullOrEmpty(iconPath) ? PlaceholderIcon : IconFor(iconPath, iconCandidate.Index),
                SoftwareName = ChineseDisplayText.SoftwareName(name),
                Vendor = vendor,
                Confidence = confidence,
                IconSource = iconPath,
                Explanation = string.IsNullOrEmpty(path) ? "没有找到可验证的程序文件，未猜测软件来源" : reason + "：" + path + (string.IsNullOrEmpty(iconPath) || string.Equals(iconPath, path, StringComparison.OrdinalIgnoreCase) ? string.Empty : "；图标取自同软件主程序：" + iconPath)
            };
        }

        private static IconCandidate ParseIconCandidate(string value)
        {
            string path = FirstExistingFile(value);
            if (string.IsNullOrEmpty(path)) return null;
            int index = 0;
            if (!string.IsNullOrWhiteSpace(value))
            {
                Match match = Regex.Match(Environment.ExpandEnvironmentVariables(value), @",\s*(?<i>-?\d+)\s*$");
                if (match.Success) int.TryParse(match.Groups["i"].Value, out index);
            }
            return new IconCandidate { Path = path, Index = index, Explicit = true };
        }

        private static string FindRepresentativeExecutable(string componentPath, SoftwarePresentationEvidence evidence, string softwareName, string vendor)
        {
            lock (Sync)
            {
                string cached;
                if (RepresentativeCache.TryGetValue(componentPath, out cached)) return cached;
            }

            string installed = FindInstalledRepresentativeExecutable(softwareName, vendor, evidence);
            if (!string.IsNullOrEmpty(installed))
            {
                lock (Sync) RepresentativeCache[componentPath] = installed;
                return installed;
            }

            string selected = string.Empty;
            int bestScore = int.MinValue;
            try
            {
                DirectoryInfo directory = new FileInfo(componentPath).Directory;
                string componentName = Path.GetFileNameWithoutExtension(componentPath);
                string evidenceText = JoinEvidence(evidence).ToLowerInvariant();
                for (int level = 0; directory != null && level < 3; level++, directory = directory.Parent)
                {
                    FileInfo[] candidates;
                    try { candidates = directory.EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly).Take(80).ToArray(); }
                    catch { continue; }
                    foreach (FileInfo candidate in candidates)
                    {
                        string baseName = Path.GetFileNameWithoutExtension(candidate.Name);
                        string lower = baseName.ToLowerInvariant();
                        int score = 100 - level * 12;
                        if (string.Equals(baseName, componentName, StringComparison.OrdinalIgnoreCase)) score += 100;
                        if (string.Equals(baseName, directory.Name, StringComparison.OrdinalIgnoreCase)) score += 80;
                        if (evidenceText.IndexOf(lower) >= 0 && lower.Length >= 4) score += 35;
                        if (Regex.IsMatch(lower, "uninst|uninstall|setup|update|helper|crash|report|repair|notify|toast|installer|inst$", RegexOptions.IgnoreCase)) score -= 70;
                        if (score > bestScore) { bestScore = score; selected = candidate.FullName; }
                    }
                }
            }
            catch { selected = string.Empty; }
            lock (Sync) RepresentativeCache[componentPath] = selected;
            return selected;
        }

        private static string FindInstalledRepresentativeExecutable(string softwareName, string vendor, SoftwarePresentationEvidence evidence)
        {
            string cacheKey = NormalizeIdentity(vendor) + "|" + NormalizeIdentity(softwareName);
            lock (Sync)
            {
                string cached;
                if (InstalledRepresentativeCache.TryGetValue(cacheKey, out cached)) return cached;
            }

            string selected = string.Empty;
            int bestScore = 0;
            string evidenceText = softwareName + " " + JoinEvidence(evidence);
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (RegistryView view in Environment.Is64BitOperatingSystem ? new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 } : new RegistryView[] { RegistryView.Default })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false))
                    {
                        if (uninstall == null) continue;
                        foreach (string subKeyName in uninstall.GetSubKeyNames())
                        using (RegistryKey entry = uninstall.OpenSubKey(subKeyName, false))
                        {
                            if (entry == null) continue;
                            string displayName = Convert.ToString(entry.GetValue("DisplayName", string.Empty));
                            string publisher = Convert.ToString(entry.GetValue("Publisher", string.Empty));
                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            int score = PublisherScore(vendor, publisher) + IdentityTokenScore(evidenceText, displayName);
                            if (score < 90 || score < bestScore) continue;
                            string candidate = FirstExistingFile(Convert.ToString(entry.GetValue("DisplayIcon", string.Empty)));
                            if (string.IsNullOrEmpty(candidate) || !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                candidate = BestExecutableInInstallLocation(Convert.ToString(entry.GetValue("InstallLocation", string.Empty)), displayName);
                            }
                            if (string.IsNullOrEmpty(candidate)) continue;
                            bestScore = score;
                            selected = candidate;
                        }
                    }
                }
                catch { }
            }

            lock (Sync) InstalledRepresentativeCache[cacheKey] = selected;
            return selected;
        }

        private static int PublisherScore(string expected, string actual)
        {
            string left = NormalizeIdentity(expected);
            string right = NormalizeIdentity(actual);
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return 0;
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return 100;
            return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0 || right.IndexOf(left, StringComparison.OrdinalIgnoreCase) >= 0 ? 70 : 0;
        }

        private static int IdentityTokenScore(string expected, string actual)
        {
            HashSet<string> left = IdentityTokens(expected);
            HashSet<string> right = IdentityTokens(actual);
            int shared = left.Count(delegate(string token) { return right.Contains(token); });
            return Math.Min(100, shared * 25);
        }

        private static HashSet<string> IdentityTokens(string value)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] ignored = { "shell", "extension", "context", "menu", "handler", "software", "windows", "component", "组件", "菜单", "右键" };
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"[A-Za-z0-9\u4E00-\u9FFF]{2,}"))
            {
                string token = match.Value.ToLowerInvariant();
                if (token.All(delegate(char character) { return char.IsDigit(character); })) continue;
                if (ignored.Contains(token, StringComparer.OrdinalIgnoreCase)) continue;
                result.Add(token);
            }
            return result;
        }

        private static string NormalizeIdentity(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fff]+", string.Empty);
        }

        private static string BestExecutableInInstallLocation(string installLocation, string displayName)
        {
            if (string.IsNullOrWhiteSpace(installLocation)) return string.Empty;
            string directory = Environment.ExpandEnvironmentVariables(installLocation.Trim().Trim('"'));
            if (!Directory.Exists(directory)) return string.Empty;
            string selected = string.Empty;
            int bestScore = int.MinValue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Take(80))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    int score = IdentityTokenScore(displayName, name);
                    if (Regex.IsMatch(name, "unins|uninstall|setup|update|helper|crash|report|repair|notify|toast|installer", RegexOptions.IgnoreCase)) score -= 100;
                    try { score += IdentityTokenScore(displayName, FileVersionInfo.GetVersionInfo(file).ProductName); } catch { }
                    if (score > bestScore) { bestScore = score; selected = file; }
                }
            }
            catch { return string.Empty; }
            return bestScore >= 25 ? selected : string.Empty;
        }

        private static string JoinEvidence(SoftwarePresentationEvidence evidence)
        {
            return string.Join(" ", new string[] { evidence.DeclaredName, evidence.DeclaredVendor, evidence.IconValue, evidence.Command, evidence.FilePath, evidence.TechnicalLocation }.Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); }).ToArray());
        }

        private static string ResolveServiceBinary(string serviceName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    if (key == null) return string.Empty;
                    string imagePath = Convert.ToString(key.GetValue("ImagePath", string.Empty));
                    string resolved = FirstExistingExecutable(imagePath);
                    if (!string.IsNullOrEmpty(resolved) && !string.Equals(Path.GetFileName(resolved), "svchost.exe", StringComparison.OrdinalIgnoreCase)) return resolved;
                    using (RegistryKey parameters = key.OpenSubKey("Parameters", false))
                    {
                        string serviceDll = parameters == null ? string.Empty : Convert.ToString(parameters.GetValue("ServiceDll", string.Empty));
                        string dll = FirstExistingFile(serviceDll);
                        return !string.IsNullOrEmpty(dll) ? dll : resolved;
                    }
                }
            }
            catch { return string.Empty; }
        }

        private static string ResolveClsidBinary(string clsid)
        {
            string clean = clsid.Trim();
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view))
                    {
                        foreach (string leaf in new string[] { "LocalServer32", "InprocServer32" })
                        using (RegistryKey key = root.OpenSubKey("CLSID\\" + clean + "\\" + leaf, false))
                        {
                            string value = key == null ? string.Empty : Convert.ToString(key.GetValue(string.Empty, string.Empty));
                            string path = FirstExistingFile(value);
                            if (!string.IsNullOrEmpty(path)) return path;
                        }
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        private static string FirstExistingExecutable(params string[] values)
        {
            foreach (string value in values)
            {
                string path = ExtractFile(value, true);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return string.Empty;
        }

        private static string FirstExistingFile(params string[] values)
        {
            foreach (string value in values)
            {
                string path = ExtractFile(value, false);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return string.Empty;
        }

        private static string ExtractFile(string value, bool executableOnly)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            Match match = Regex.Match(expanded, "(?:\\\"(?<p>[^\\\"]+?\\.(?:exe|dll|ico))\\\"|(?<p>[A-Za-z]:\\\\[^\\r\\n,;]+?\\.(?:exe|dll|ico)))", RegexOptions.IgnoreCase);
            string path = match.Success ? match.Groups["p"].Value : expanded.Trim(' ', '\"');
            int comma = path.LastIndexOf(',');
            if (comma > 2 && Regex.IsMatch(path.Substring(comma + 1), @"^\s*-?\d+\s*$")) path = path.Substring(0, comma).Trim();
            path = path.Trim(' ', '\"');
            if (executableOnly && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            try { return File.Exists(path) ? Path.GetFullPath(path) : string.Empty; }
            catch { return string.Empty; }
        }

        private static Image IconFor(string path, int index)
        {
            string key;
            try { key = path.ToUpperInvariant() + "|" + index + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return PlaceholderIcon; }
            lock (Sync)
            {
                Image cached;
                if (IconCache.TryGetValue(key, out cached)) return cached;
                Image image = null;
                try
                {
                    IntPtr[] small = new IntPtr[1];
                    if (ExtractIconEx(path, index, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                    {
                        try { using (Icon icon = (Icon)Icon.FromHandle(small[0]).Clone()) image = Resize(icon.ToBitmap(), 20, 20); }
                        finally { DestroyIcon(small[0]); }
                    }
                }
                catch { }
                if (image == null)
                {
                    try { using (Icon icon = Icon.ExtractAssociatedIcon(path)) if (icon != null) image = Resize(icon.ToBitmap(), 20, 20); }
                    catch { }
                }
                if (image == null) image = PlaceholderIcon;
                IconCache[key] = image;
                return image;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Image Resize(Image source, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return bitmap;
        }

        private static bool IsWindowsBinary(string path)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsAppsBinary(string path)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMicrosoftVendor(string vendor)
        {
            return !string.IsNullOrWhiteSpace(vendor) && (vendor.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 || vendor.IndexOf("微软", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string CleanIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Trim();
            if (clean == "未知第三方" || clean == "未知" || clean == "仅提示") return string.Empty;
            string lower = clean.ToLowerInvariant();
            if (lower.IndexOf("todo") >= 0 || lower.IndexOf("<产品名>") >= 0 || lower.IndexOf("<product") >= 0) return string.Empty;
            return clean;
        }
    }

    // WinUI 3 移植：原版以 Form 的 BeginInvoke 回 UI 线程，这里改用 DispatcherQueue。
    internal static class SoftwarePresentationQueue
    {
        public static void Hydrate(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, IList<Finding> items, Action repaint)
        {
            Queue(dispatcher, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, IList<ContextMenuEntry> items, Action repaint)
        {
            Queue(dispatcher, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, IList<CleanupResult> items, Action repaint)
        {
            Queue(dispatcher, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, IList<SpecialMenuEntry> items, Action repaint)
        {
            Queue(dispatcher, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, IList<AdvancedMenuEntry> items, Action repaint)
        {
            Queue(dispatcher, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        // 水合并行度：条目互相独立（各自的缓存均有锁保护），并行后整表在几秒内完成，
        // 不再被串行解析（含 COM 标题探测等秒级操作）拖成"列表一直增长、一直刷新"。
        private const int HydrateParallelism = 4;

        private static void Queue(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, int count, Action<int> resolver, Action repaint)
        {
            if (dispatcher == null || count == 0) return;
            int completed = 0;
            Task.Factory.StartNew(delegate
            {
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = HydrateParallelism }, delegate(int i)
                {
                    try { resolver(i); } catch { }
                    int done = Interlocked.Increment(ref completed);
                    if (done % 24 == 0) Repaint(dispatcher, repaint);
                });
                Repaint(dispatcher, repaint);
            });
        }

        private static void Repaint(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Action repaint)
        {
            try { if (repaint != null) dispatcher.TryEnqueue(delegate { try { repaint(); } catch { } }); } catch { }
        }
    }

}
