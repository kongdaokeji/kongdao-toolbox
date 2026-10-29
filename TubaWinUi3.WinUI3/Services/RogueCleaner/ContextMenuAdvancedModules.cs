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
using System.Xml;
using Microsoft.Win32;

namespace TubaWinUi3.Services.RogueCleaner
{

    internal sealed class AdvancedMenuEntry
    {
        public string Id { get; set; }
        public string Module { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public bool Enabled { get; set; }
        public bool ReadOnly { get; set; }
        public bool RequiresAdmin { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public string ValueName { get; set; }
        public string FilePath { get; set; }
        public string Group { get; set; }
        public string PackageName { get; set; }
        public string PublisherName { get; set; }
        public string ItemType { get; set; }
        public string CommandTitle { get; set; }
        public string CommandIcon { get; set; }
        public string TitleProbeStatus { get; set; }
        public int Contexts { get; set; }
        public string ModuleDisplay { get { return AdvancedMenuDisplay.Name(Module); } }
                [JsonIgnore] public Image SoftwareIcon { get; set; }
        // WinUI 行模板直接绑定的图标（页面水合后填充）
        [JsonIgnore] public Microsoft.UI.Xaml.Media.ImageSource IconDisplay { get; set; }
        [JsonIgnore] public string SoftwareName { get; set; }
        [JsonIgnore] public string IdentityConfidence { get; set; }
        [JsonIgnore] public string IconSource { get; set; }
        [JsonIgnore] public string IdentityExplanation { get; set; }
        public SoftwarePresentationEvidence PresentationEvidence() { return new SoftwarePresentationEvidence { DeclaredName = Name, FilePath = FilePath, Command = Detail, TechnicalLocation = Hive + "\\" + SubKey }; }
        public void ApplyPresentation(SoftwarePresentation p) { if (p == null) return; SoftwareIcon = p.Icon; SoftwareName = p.SoftwareName; IdentityConfidence = p.Confidence; IconSource = p.IconSource; IdentityExplanation = p.Explanation; }
    }

    internal static class AdvancedMenuDisplay
    {
        public static string Name(string module)
        {
            if (module == "WinX 快捷菜单") return "系统快捷菜单";
            if (module == "现代 / UWP 菜单") return "Windows 现代菜单";
            if (module == "IE 旧式菜单") return "IE 旧式菜单";
            return module;
        }

        public static string Key(string display)
        {
            if (display == "系统快捷菜单") return "WinX 快捷菜单";
            if (display == "Windows 现代菜单") return "现代 / UWP 菜单";
            return display;
        }
    }

    internal sealed class AdvancedMenuInventory
    {
        public List<AdvancedMenuEntry> Entries { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class AdvancedFileSnapshot
    {
        public string Path { get; set; }
        public bool Existed { get; set; }
        public byte[] Bytes { get; set; }
        public int Attributes { get; set; }
    }

    internal sealed class AdvancedMenuBackup
    {
        public string Mode { get; set; }
        public List<ContextMenuTreeBackup> Trees { get; set; }
        public List<ContextMenuToggleBackup> Values { get; set; }
        public List<AdvancedFileSnapshot> Files { get; set; }
    }

    internal sealed class EnhancedMenuRecipe
    {
        public string Id;
        public string Name;
        public string Root;
        public string Icon;
        public string Command;
    }

    internal sealed class AdvancedMenuInventoryService
    {
        internal const string IeRoot = @"Software\Microsoft\Internet Explorer\MenuExt";
        internal const string IeDisabledRoot = @"Software\Microsoft\Internet Explorer\MenuExt.RogueCleanerDisabled";
        internal const string BlockedRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        private readonly DataStore store;
        private readonly string winxRoot;
        private readonly string ieRoot;
        private readonly string ieDisabledRoot;

        public AdvancedMenuInventoryService(DataStore store) : this(store, null, null, null) { }
        internal AdvancedMenuInventoryService(DataStore store, string winxRootOverride) : this(store, winxRootOverride, null, null) { }
        internal AdvancedMenuInventoryService(DataStore store, string winxRootOverride, string ieRootOverride, string ieDisabledRootOverride)
        {
            this.store = store;
            winxRoot = string.IsNullOrWhiteSpace(winxRootOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WinX")
                : winxRootOverride;
            ieRoot = string.IsNullOrWhiteSpace(ieRootOverride) ? IeRoot : ieRootOverride;
            ieDisabledRoot = string.IsNullOrWhiteSpace(ieDisabledRootOverride) ? IeDisabledRoot : ieDisabledRootOverride;
        }

        public AdvancedMenuInventory Enumerate()
        {
            List<AdvancedMenuEntry> entries = new List<AdvancedMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            EnumerateWinX(entries, warnings);
            EnumerateIe(entries, warnings);
            EnumeratePackagedMenus(entries, warnings, true);
            EnumerateRecipes(entries);
            return new AdvancedMenuInventory
            {
                Entries = entries.OrderBy(delegate(AdvancedMenuEntry e) { return e.Module; }).ThenBy(delegate(AdvancedMenuEntry e) { return e.Group; }).ThenBy(delegate(AdvancedMenuEntry e) { return e.Name; }).ToList(),
                Warnings = warnings
            };
        }

        internal AdvancedMenuInventory EnumeratePackagedOnly(bool probeTitles)
        {
            List<AdvancedMenuEntry> entries = new List<AdvancedMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            EnumeratePackagedMenus(entries, warnings, probeTitles);
            return new AdvancedMenuInventory { Entries = entries, Warnings = warnings };
        }

        private void EnumerateWinX(List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            AddWinXDirectory(winxRoot, true, entries, warnings);
            AddWinXDirectory(DisabledWinXDirectory(store), false, entries, warnings);
        }

        private void AddWinXDirectory(string root, bool enabled, List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (string groupDir in Directory.GetDirectories(root, "Group*"))
                {
                    string group = Path.GetFileName(groupDir);
                    foreach (string file in Directory.GetFiles(groupDir, "*.lnk").OrderBy(delegate(string p) { return Path.GetFileName(p); }))
                    {
                        string target = ShortcutTarget(file);
                        entries.Add(new AdvancedMenuEntry
                        {
                            Id = "WinX|" + (enabled ? "1" : "0") + "|" + file,
                            Module = "WinX 快捷菜单", Name = ChineseDisplayText.SystemShortcutName(Path.GetFileNameWithoutExtension(file)), Detail = target,
                            Scope = "当前用户 / " + ChineseDisplayText.GroupName(group), Status = enabled ? "已启用" : "已禁用", Enabled = enabled,
                            FilePath = file, Group = group
                        });
                    }
                }
            }
            catch (Exception ex) { AddWarning(warnings, "WinX", root, ex); }
        }

        private void EnumerateIe(List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            foreach (string hive in new string[] { "HKCU", "HKLM" })
            foreach (string view in Views())
            {
                AddIeRoot(hive, view, ieRoot, true, entries, warnings);
                AddIeRoot(hive, view, ieDisabledRoot, false, entries, warnings);
            }
        }

        private void AddIeRoot(string hive, string view, string rootPath, bool enabled, List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            ActionTarget rootTarget = Target(hive, view, rootPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenSubKey(rootTarget, false))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    using (RegistryKey key = root.OpenSubKey(name, false))
                    {
                        if (key == null) continue;
                        string url = Convert.ToString(key.GetValue("", ""));
                        int contexts = ToInt(key.GetValue("Contexts", 0));
                        entries.Add(new AdvancedMenuEntry
                        {
                            Id = "IE|" + hive + "|" + view + "|" + rootPath + "|" + name,
                            Module = "IE 旧式菜单", Name = name, Detail = url, Contexts = contexts,
                            Scope = (hive == "HKCU" ? "当前用户" : "所有用户") + " / " + (view == "Registry32" ? "32 位" : "64 位"),
                            Status = enabled ? "已启用" : "已禁用", Enabled = enabled, RequiresAdmin = hive == "HKLM",
                            Hive = hive, View = view, SubKey = rootPath + "\\" + name
                        });
                    }
                }
            }
            catch (Exception ex) { if (IsDenied(ex)) AddWarning(warnings, "IE 旧式菜单", RegistryHelper.NativePath(rootTarget), ex); else throw; }
        }

        private void EnumeratePackagedMenus(List<AdvancedMenuEntry> entries, List<ScanWarning> warnings, bool probeTitles)
        {
            HashSet<string> packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
                using (RegistryKey index = classes.OpenSubKey(@"PackagedCom\ClassIndex", false))
                {
                    if (index != null)
                    foreach (string clsid in index.GetSubKeyNames())
                    using (RegistryKey classKey = index.OpenSubKey(clsid, false))
                        if (classKey != null) foreach (string package in classKey.GetSubKeyNames()) packages.Add(package);
                }
            }
            catch (Exception ex) { AddWarning(warnings, "现代菜单", @"HKCR\PackagedCom\ClassIndex", ex); return; }

            string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string package in packages)
            {
                string manifest = Path.Combine(windowsApps, package, "AppxManifest.xml");
                if (!File.Exists(manifest)) continue;
                try
                {
                    XmlDocument xml = new XmlDocument(); xml.XmlResolver = null; xml.Load(manifest);
                    XmlNode identity = xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']");
                    string packageName = identity == null || identity.Attributes["Name"] == null ? package : identity.Attributes["Name"].Value;
                    string packageDisplayName = PackageDisplayName(xml, packageName);
                    string publisherName = PackagePublisherName(xml);
                    XmlNodeList verbs = xml.SelectNodes("//*[local-name()='Extension' and @Category='windows.fileExplorerContextMenus']//*[local-name()='ItemType']/*[local-name()='Verb']");
                    foreach (XmlNode verb in verbs)
                    {
                        string itemType = verb.ParentNode == null ? string.Empty : Attribute(verb.ParentNode, "Type");
                        AddPackagedMenu(entries, seen, xml, manifest, package, packageName, packageDisplayName, publisherName, Attribute(verb, "Clsid"), itemType, "现代", probeTitles);
                    }

                    XmlNodeList classicHandlers = xml.SelectNodes("//*[local-name()='Extension' and @Category='windows.fileExplorerClassicContextMenuHandler']//*[local-name()='ExtensionHandler']");
                    foreach (XmlNode handler in classicHandlers)
                    {
                        AddPackagedMenu(entries, seen, xml, manifest, package, packageName, packageDisplayName, publisherName, Attribute(handler, "Clsid"), Attribute(handler, "Type"), "传统兼容", probeTitles);
                    }
                }
                catch (UnauthorizedAccessException ex) { AddWarning(warnings, "现代菜单", manifest, ex); }
                catch (System.Security.SecurityException ex) { AddWarning(warnings, "现代菜单", manifest, ex); }
                catch (XmlException ex) { AddWarning(warnings, "现代菜单", manifest, ex); }
            }
        }

        private void AddPackagedMenu(List<AdvancedMenuEntry> entries, HashSet<string> seen, XmlDocument xml, string manifest, string package, string packageName, string packageDisplayName, string publisherName, string clsid, string itemType, string declarationKind, bool probeTitles)
        {
            Guid parsed;
            if (!Guid.TryParse(clsid, out parsed)) return;
            string normalizedClsid = "{" + parsed.ToString().ToUpperInvariant() + "}";
            string normalizedItemType = NormalizePackagedItemType(itemType);
            string id = package + "|" + normalizedClsid + "|" + normalizedItemType;
            if (!seen.Add(id)) return;

            bool blocked = RegistryValueExists("HKCU", DefaultView(), BlockedRoot, normalizedClsid);
            string componentPath = PackagedComponentPath(xml, manifest, normalizedClsid);
            string productName = FileProductName(componentPath);
            // ms-resource 引用（如 "ms-resource:AppStoreName"）通过 PackageManager 解析为本地化显示名
            string softwareName = string.IsNullOrWhiteSpace(productName) ? ResolvePackagedDisplayName(package, packageDisplayName) : productName;
            bool microsoft = IsMicrosoftPackage(packageName, publisherName);
            ContextCommandProbeResult probe = microsoft || !probeTitles
                ? new ContextCommandProbeResult { Error = "系统组件不做动态文字探测。" }
                : ContextCommandTitleProbe.ProbeIsolated(normalizedClsid, normalizedItemType, componentPath);
            string title = probe == null ? string.Empty : probe.Title;
            string display = string.IsNullOrWhiteSpace(title)
                ? softwareName + " 动态右键命令（文字未读取）"
                : title;

            entries.Add(new AdvancedMenuEntry
            {
                Id = "UWP|" + id,
                Module = "现代 / UWP 菜单",
                Name = display,
                CommandTitle = title,
                CommandIcon = probe == null ? string.Empty : probe.Icon,
                TitleProbeStatus = probe == null ? "没有返回探测结果。" : (string.IsNullOrWhiteSpace(probe.Error) ? "命令文字来源：" + probe.Source + "。" : probe.Error),
                Detail = normalizedClsid + (string.IsNullOrWhiteSpace(normalizedItemType) ? string.Empty : " / " + normalizedItemType) + " / " + declarationKind,
                Scope = "当前用户 / Windows 11",
                Status = blocked ? "已禁用" : "已启用",
                Enabled = !blocked,
                Hive = "HKCU",
                View = DefaultView(),
                SubKey = BlockedRoot,
                ValueName = normalizedClsid,
                PackageName = package,
                PublisherName = publisherName,
                ItemType = normalizedItemType,
                FilePath = componentPath
            });
        }

        internal static string NormalizePackagedItemType(string itemType)
        {
            string value = (itemType ?? string.Empty).Trim();
            return string.Equals(value, "Folder", StringComparison.OrdinalIgnoreCase) ? "Directory" : value;
        }

        private static bool IsMicrosoftPackage(string packageName, string publisherName)
        {
            return (packageName ?? string.Empty).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                || (publisherName ?? string.Empty).IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string PackageDisplayName(XmlDocument xml, string fallback)
        {
            XmlNode node = xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='DisplayName']");
            string value = node == null ? fallback : node.InnerText;
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            value = System.Text.RegularExpressions.Regex.Replace(value, "(?i)\\s+(shell extension|context menu handler|context menu)$", string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        // ms-resource 引用（如 "ms-resource:AppStoreName"）通过 PackageManager 解析为本地化显示名。
        // package 是 WindowsApps 下的包完整名称目录（如 Microsoft.Windows.Photos_2026.11060.2004.0_x64__8wekyb3d8bbwe）。
        private static readonly object PackageDisplayLock = new object();
        private static readonly Dictionary<string, string> PackageDisplayCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string ResolvePackagedDisplayName(string packageFullName, string fallback)
        {
            if (string.IsNullOrWhiteSpace(fallback) || !fallback.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return fallback;
            string key = packageFullName ?? string.Empty;
            lock (PackageDisplayLock)
            {
                string cached;
                if (PackageDisplayCache.TryGetValue(key, out cached)) return string.IsNullOrWhiteSpace(cached) ? fallback : cached;
                cached = ResolvePackagedDisplayNameCore(packageFullName);
                PackageDisplayCache[key] = cached ?? string.Empty;
                return string.IsNullOrWhiteSpace(cached) ? fallback : cached;
            }
        }

        private static string ResolvePackagedDisplayNameCore(string packageFullName)
        {
            try
            {
                // 完整名称形如 Name_Version_Arch__Publisher，取 Name_Publisher 作为包系列名
                string[] parts = (packageFullName ?? string.Empty).Split('_');
                if (parts.Length < 2) return null;
                string family = parts[0] + "_" + parts[parts.Length - 1];
                var manager = new Windows.Management.Deployment.PackageManager();
                var packages = manager.FindPackages(family);
                foreach (var package in packages)
                {
                    string name = package.DisplayName;
                    if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        return name.Trim();
                }
            }
            catch
            {
            }
            return null;
        }

        private static string PackagePublisherName(XmlDocument xml)
        {
            XmlNode node = xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='PublisherDisplayName']");
            return node == null ? string.Empty : node.InnerText.Trim();
        }

        private static string PackagedComponentPath(XmlDocument xml, string manifest, string normalizedClsid)
        {
            string wanted = normalizedClsid.Trim('{', '}');
            foreach (XmlNode node in xml.SelectNodes("//*[local-name()='Class']"))
            {
                string id = Attribute(node, "Id").Trim('{', '}');
                if (!string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                string relative = Attribute(node, "Path");
                if (string.IsNullOrWhiteSpace(relative)) continue;
                string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest), relative));
                if (File.Exists(path)) return path;
            }
            return string.Empty;
        }

        private static string FileProductName(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
            try { return FileVersionInfo.GetVersionInfo(path).ProductName; } catch { return string.Empty; }
        }

        private void EnumerateRecipes(List<AdvancedMenuEntry> entries)
        {
            foreach (EnhancedMenuRecipe recipe in Recipes())
            {
                bool installed = RegistryKeyExists("HKCU", DefaultView(), recipe.Root);
                entries.Add(new AdvancedMenuEntry
                {
                    Id = "Recipe|" + recipe.Id, Module = "安全增强菜单", Name = recipe.Name,
                    Detail = installed ? recipe.Command : "内置安全配方，安装前会完整备份目标项。",
                    Scope = "当前用户", Status = installed ? "已安装" : "未安装", Enabled = installed,
                    Hive = "HKCU", View = DefaultView(), SubKey = recipe.Root, ValueName = recipe.Id
                });
            }
        }

        internal static List<EnhancedMenuRecipe> Recipes()
        {
            string exe = (Environment.ProcessPath ?? string.Empty).Replace("\"", string.Empty);
            return new List<EnhancedMenuRecipe>
            {
                new EnhancedMenuRecipe { Id = "CopyPath", Name = "复制完整路径", Root = @"Software\Classes\*\shell\RogueCleaner.CopyPath", Icon = exe + ",0", Command = "\"" + exe + "\" --copy-path \"%1\"" },
                new EnhancedMenuRecipe { Id = "OpenNotepad", Name = "用记事本打开", Root = @"Software\Classes\*\shell\RogueCleaner.OpenNotepad", Icon = "notepad.exe,0", Command = "notepad.exe \"%1\"" },
                new EnhancedMenuRecipe { Id = "CommandPrompt", Name = "在此处打开命令提示符", Root = @"Software\Classes\Directory\Background\shell\RogueCleaner.CommandPrompt", Icon = "cmd.exe,0", Command = "cmd.exe /s /k pushd \"%V\"" }
            };
        }

        internal static string DisabledWinXDirectory(DataStore store) { return Path.Combine(store.State, "winx-disabled"); }
        internal string WinXRoot { get { return winxRoot; } }
        private static string ShortcutTarget(string file) { try { Type type = Type.GetTypeFromProgID("WScript.Shell"); if (type == null) return string.Empty; dynamic shell = Activator.CreateInstance(type); dynamic shortcut = shell.CreateShortcut(file); return Convert.ToString(shortcut.TargetPath); } catch { return string.Empty; } }
        private static string[] Views() { return Environment.Is64BitOperatingSystem ? new string[] { "Registry64", "Registry32" } : new string[] { "Default" }; }
        private static string DefaultView() { return Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; }
        private static int ToInt(object value) { try { return Convert.ToInt32(value); } catch { return 0; } }
        private static string Attribute(XmlNode node, string name) { return node == null || node.Attributes == null || node.Attributes[name] == null ? string.Empty : node.Attributes[name].Value; }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static bool RegistryKeyExists(string hive, string view, string subKey) { try { using (RegistryKey key = RegistryHelper.OpenSubKey(Target(hive, view, subKey), false)) return key != null; } catch { return false; } }
        private static bool RegistryValueExists(string hive, string view, string subKey, string name) { try { using (RegistryKey key = RegistryHelper.OpenSubKey(Target(hive, view, subKey), false)) return key != null && key.GetValueNames().Any(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); }); } catch { return false; } }
        private static bool IsDenied(Exception ex) { return ex is UnauthorizedAccessException || ex is System.Security.SecurityException; }
        private static void AddWarning(List<ScanWarning> warnings, string stage, string location, Exception ex) { warnings.Add(new ScanWarning { Stage = stage, TechnicalLocation = location, ErrorType = ex.GetType().FullName, Message = "无法读取，已跳过：" + ex.Message }); }
    }

    internal sealed class AdvancedContextMenuMutationService
    {
        private readonly DataStore store;
        private readonly string winxRoot;
        private readonly string ieRoot;
        private readonly string ieDisabledRoot;
        public AdvancedContextMenuMutationService(DataStore store) : this(store, null, null, null) { }
        internal AdvancedContextMenuMutationService(DataStore store, string winxRootOverride) : this(store, winxRootOverride, null, null) { }
        internal AdvancedContextMenuMutationService(DataStore store, string winxRootOverride, string ieRootOverride, string ieDisabledRootOverride)
        {
            this.store = store;
            winxRoot = string.IsNullOrWhiteSpace(winxRootOverride) ? new AdvancedMenuInventoryService(store).WinXRoot : winxRootOverride;
            ieRoot = string.IsNullOrWhiteSpace(ieRootOverride) ? AdvancedMenuInventoryService.IeRoot : ieRootOverride;
            ieDisabledRoot = string.IsNullOrWhiteSpace(ieDisabledRootOverride) ? AdvancedMenuInventoryService.IeDisabledRoot : ieDisabledRootOverride;
        }

        public CleanupBatch SetEnabled(AdvancedMenuEntry entry, bool enabled)
        {
            Ensure(entry);
            if (entry.Module == "WinX 快捷菜单") return ToggleWinX(entry, enabled);
            if (entry.Module == "IE 旧式菜单") return ToggleIe(entry, enabled);
            if (entry.Module == "现代 / UWP 菜单") return ToggleUwp(entry, enabled);
            if (entry.Module == "安全增强菜单") return ToggleRecipe(entry, enabled);
            throw new InvalidOperationException("不支持的高级菜单类型。");
        }

        public CleanupBatch Delete(AdvancedMenuEntry entry)
        {
            Ensure(entry);
            if (entry.Module == "WinX 快捷菜单") return DeleteFile(entry);
            if (entry.Module == "IE 旧式菜单") return DeleteTree(entry);
            if (entry.Module == "安全增强菜单") return ToggleRecipe(entry, false);
            throw new InvalidOperationException("现代应用菜单只允许启用或禁用，不修改应用包注册。");
        }

        public CleanupBatch MoveWinX(AdvancedMenuEntry entry, int direction)
        {
            Ensure(entry); if (entry.Module != "WinX 快捷菜单" || !entry.Enabled) throw new InvalidOperationException("只能调整已启用的 WinX 项。");
            string dir = Path.GetDirectoryName(entry.FilePath);
            string[] files = Directory.GetFiles(dir, "*.lnk").OrderBy(delegate(string p) { return Path.GetFileName(p); }, StringComparer.OrdinalIgnoreCase).ToArray();
            int index = Array.FindIndex(files, delegate(string p) { return string.Equals(p, entry.FilePath, StringComparison.OrdinalIgnoreCase); });
            int other = index + direction; if (index < 0 || other < 0 || other >= files.Length) throw new InvalidOperationException("已经位于当前分组边界。");
            string first = files[index], second = files[other]; string[] firstParts = WinXNameParts(first), secondParts = WinXNameParts(second);
            string firstNew = Path.Combine(dir, secondParts[0] + " - " + firstParts[1] + ".lnk"); string secondNew = Path.Combine(dir, firstParts[0] + " - " + secondParts[1] + ".lnk");
            string temp1 = Path.Combine(dir, ".roguecleaner-" + Guid.NewGuid().ToString("N") + ".tmp"); string temp2 = temp1 + "2";
            AdvancedMenuBackup backup = BackupFiles("ReorderWinX", first, second, firstNew, secondNew, temp1, temp2); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                File.Move(first, temp1); File.Move(second, temp2); File.Move(temp1, firstNew); File.Move(temp2, secondNew);
                return Complete(id, batchPath, backupPath, entry, direction < 0 ? "MoveWinXUp" : "MoveWinXDown", "WinX 顺序已调整；重新打开 Win+X 菜单后生效。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        public CleanupBatch AddOrEditIe(AdvancedMenuEntry existing, string name, string url, int contexts)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\\') >= 0) throw new ArgumentException("菜单名称无效。");
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("脚本或页面地址不能为空。");
            string view = Environment.Is64BitOperatingSystem ? "Registry64" : "Default";
            ActionTarget destination = Target(existing == null ? "HKCU" : existing.Hive, existing == null ? view : existing.View, (existing == null ? ieRoot : Parent(existing.SubKey)) + "\\" + name.Trim());
            List<ActionTarget> targets = new List<ActionTarget> { destination };
            if (existing != null && !string.Equals(existing.SubKey, destination.SubKey, StringComparison.OrdinalIgnoreCase)) targets.Add(Target(existing.Hive, existing.View, existing.SubKey));
            AdvancedMenuBackup backup = BackupTrees(existing == null ? "AddIE" : "EditIE", targets.ToArray()); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                if (existing != null && !string.Equals(existing.SubKey, destination.SubKey, StringComparison.OrdinalIgnoreCase)) DeleteIeTree(Target(existing.Hive, existing.View, existing.SubKey));
                RegAdd(destination, string.Empty, url.Trim(), "REG_SZ");
                RegAdd(destination, "Contexts", contexts.ToString(), "REG_DWORD");
                AdvancedMenuEntry resultEntry = existing ?? new AdvancedMenuEntry { Module = "IE 旧式菜单", Name = name, Hive = destination.Hive, View = destination.View, SubKey = destination.SubKey };
                resultEntry.Name = name.Trim(); resultEntry.SubKey = destination.SubKey;
                return Complete(id, batchPath, backupPath, resultEntry, existing == null ? "AddIE" : "EditIE", "IE 旧式菜单配置已保存。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleWinX(AdvancedMenuEntry entry, bool enabled)
        {
            string destinationRoot = enabled ? winxRoot : AdvancedMenuInventoryService.DisabledWinXDirectory(store);
            string destination = Path.Combine(destinationRoot, entry.Group, Path.GetFileName(entry.FilePath));
            if (File.Exists(destination)) throw new InvalidOperationException("目标位置已经存在同名 WinX 项。");
            AdvancedMenuBackup backup = BackupFiles("ToggleWinX", entry.FilePath, destination); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Move(entry.FilePath, destination); return Complete(id, batchPath, backupPath, entry, enabled ? "EnableWinX" : "DisableWinX", "WinX 项已移动；重新打开 Win+X 菜单后生效。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteFile(AdvancedMenuEntry entry)
        {
            string destination = Path.Combine(store.Backups, "advanced-files", Guid.NewGuid().ToString("N"), Path.GetFileName(entry.FilePath));
            AdvancedMenuBackup backup = BackupFiles("DeleteWinX", entry.FilePath, destination); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Move(entry.FilePath, destination); return Complete(id, batchPath, backupPath, entry, "DeleteWinX", "WinX 项已备份并删除。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleIe(AdvancedMenuEntry entry, bool enabled)
        {
            string name = entry.SubKey.Substring(entry.SubKey.LastIndexOf('\\') + 1);
            ActionTarget active = Target(entry.Hive, entry.View, ieRoot + "\\" + name);
            ActionTarget disabled = Target(entry.Hive, entry.View, ieDisabledRoot + "\\" + name);
            AdvancedMenuBackup backup = BackupTrees("ToggleIE", active, disabled); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { MoveIeTree(enabled ? disabled : active, enabled ? active : disabled); return Complete(id, batchPath, backupPath, entry, enabled ? "EnableIE" : "DisableIE", "IE 旧式菜单状态已修改。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleUwp(AdvancedMenuEntry entry, bool enabled)
        {
            ActionTarget target = Target("HKCU", DefaultView(), AdvancedMenuInventoryService.BlockedRoot); target.ValueName = entry.ValueName;
            AdvancedMenuBackup backup = BackupValues("ToggleUWP", target); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    if (enabled) key.DeleteValue(target.ValueName, false); else key.SetValue(target.ValueName, entry.PackageName ?? "由流氓软件克星禁用", RegistryValueKind.String);
                return Complete(id, batchPath, backupPath, entry, enabled ? "EnableUWP" : "DisableUWP", "仅修改当前用户的右键扩展屏蔽状态；应用包注册保持不变。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleRecipe(AdvancedMenuEntry entry, bool enabled)
        {
            EnhancedMenuRecipe recipe = AdvancedMenuInventoryService.Recipes().FirstOrDefault(delegate(EnhancedMenuRecipe r) { return r.Id == entry.ValueName; });
            if (recipe == null) throw new InvalidOperationException("找不到内置配方。");
            ActionTarget target = Target("HKCU", DefaultView(), recipe.Root);
            AdvancedMenuBackup backup = BackupTrees("ToggleRecipe", target); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                {
                    root.DeleteSubKeyTree(target.SubKey, false);
                    if (enabled)
                    using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        key.SetValue("MUIVerb", recipe.Name, RegistryValueKind.String); key.SetValue("Icon", recipe.Icon, RegistryValueKind.String);
                        using (RegistryKey command = key.CreateSubKey("command", RegistryKeyPermissionCheck.ReadWriteSubTree)) command.SetValue("", recipe.Command, RegistryValueKind.String);
                    }
                }
                return Complete(id, batchPath, backupPath, entry, enabled ? "InstallRecipe" : "RemoveRecipe", enabled ? "安全增强菜单已安装。" : "安全增强菜单已移除。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteTree(AdvancedMenuEntry entry)
        {
            ActionTarget target = Target(entry.Hive, entry.View, entry.SubKey); AdvancedMenuBackup backup = BackupTrees("DeleteIE", target); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { DeleteIeTree(target); return Complete(id, batchPath, backupPath, entry, "DeleteIE", "IE 旧式菜单已备份并删除。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private static void MoveTree(ActionTarget source, ActionTarget destination)
        {
            ContextMenuTreeBackup snapshot = ContextMenuMutationService.CaptureTree(source); if (!snapshot.KeyExisted) throw new InvalidOperationException("源注册表项不存在。");
            ContextMenuMutationService.RestoreTreeSnapshot(new ContextMenuTreeBackup { Target = destination, KeyExisted = true, Snapshot = snapshot.Snapshot });
            using (RegistryKey root = RegistryHelper.OpenBase(source.Hive, source.View, true)) root.DeleteSubKeyTree(source.SubKey, false);
        }

        private static void MoveIeTree(ActionTarget source, ActionTarget destination)
        {
            ContextMenuTreeBackup snapshot = ContextMenuMutationService.CaptureTree(source); if (!snapshot.KeyExisted) throw new InvalidOperationException("源 IE 菜单项不存在。");
            DeleteIeTree(destination); RestoreIeTree(new ContextMenuTreeBackup { Target = destination, KeyExisted = true, Snapshot = snapshot.Snapshot }); DeleteIeTree(source);
        }

        private AdvancedMenuBackup BackupTrees(string mode, params ActionTarget[] targets) { return new AdvancedMenuBackup { Mode = mode, Trees = targets.Select(ContextMenuMutationService.CaptureTree).ToList(), Values = new List<ContextMenuToggleBackup>(), Files = new List<AdvancedFileSnapshot>() }; }
        private AdvancedMenuBackup BackupValues(string mode, params ActionTarget[] targets) { return new AdvancedMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = targets.Select(delegate(ActionTarget t) { return ContextMenuMutationService.CaptureValue(t, mode); }).ToList(), Files = new List<AdvancedFileSnapshot>() }; }
        private static AdvancedMenuBackup BackupFiles(string mode, params string[] paths) { return new AdvancedMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = new List<ContextMenuToggleBackup>(), Files = paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(CaptureFile).ToList() }; }
        private static AdvancedFileSnapshot CaptureFile(string path) { bool exists = File.Exists(path); return new AdvancedFileSnapshot { Path = path, Existed = exists, Bytes = exists ? File.ReadAllBytes(path) : null, Attributes = exists ? (int)File.GetAttributes(path) : 0 }; }

        private void Save(AdvancedMenuBackup backup, out string backupPath, out string id, out string batchPath)
        {
            id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); batchPath = Path.Combine(store.Backups, id); Directory.CreateDirectory(batchPath); backupPath = Path.Combine(batchPath, "advanced-menu.json"); CleanerEngine.WriteJson(backupPath, backup);
        }

        private CleanupBatch Complete(string id, string batchPath, string backupPath, AdvancedMenuEntry entry, string action, string message)
        {
            ActionTarget target = new ActionTarget { Kind = "RestoreAdvancedMenu", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey, ValueName = entry.ValueName, FilePath = entry.FilePath };
            string location = !string.IsNullOrWhiteSpace(entry.FilePath) ? entry.FilePath : (!string.IsNullOrWhiteSpace(entry.SubKey) ? RegistryHelper.NativePath(target) : entry.Detail);
            CleanupResult result = new CleanupResult { Id = 1, Title = entry.Name, Vendor = "右键管理", Category = entry.Module, ActionKind = action, TechnicalLocation = location, Status = "Done", Message = message, Backup = backupPath, Target = target };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } }; CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch); return batch;
        }

        public static bool Restore(string backupPath)
        {
            AdvancedMenuBackup backup = JsonSerializer.Deserialize<AdvancedMenuBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreBackup(backup);
        }

        private static bool RestoreBackup(AdvancedMenuBackup backup)
        {
            if (backup == null) return false; bool ok = true;
            foreach (ContextMenuTreeBackup tree in backup.Trees ?? new List<ContextMenuTreeBackup>()) ok = IsIeTree(tree.Target) ? RestoreIeTree(tree) && ok : ContextMenuMutationService.RestoreTreeSnapshot(tree) && ok;
            foreach (ContextMenuToggleBackup value in backup.Values ?? new List<ContextMenuToggleBackup>()) ok = ContextMenuMutationService.RestoreValueSnapshot(value) && ok;
            foreach (AdvancedFileSnapshot file in backup.Files ?? new List<AdvancedFileSnapshot>())
            {
                try
                {
                    if (!file.Existed) { if (File.Exists(file.Path)) File.Delete(file.Path); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(file.Path)); File.WriteAllBytes(file.Path, file.Bytes ?? new byte[0]); File.SetAttributes(file.Path, (FileAttributes)file.Attributes); }
                    ok = (File.Exists(file.Path) == file.Existed) && ok;
                }
                catch { ok = false; }
            }
            return ok;
        }

        private static bool IsIeTree(ActionTarget target) { return target != null && target.SubKey != null && (target.SubKey.Equals(AdvancedMenuInventoryService.IeRoot, StringComparison.OrdinalIgnoreCase) || target.SubKey.StartsWith(AdvancedMenuInventoryService.IeRoot + "\\", StringComparison.OrdinalIgnoreCase) || target.SubKey.Equals(AdvancedMenuInventoryService.IeDisabledRoot, StringComparison.OrdinalIgnoreCase) || target.SubKey.StartsWith(AdvancedMenuInventoryService.IeDisabledRoot + "\\", StringComparison.OrdinalIgnoreCase)); }
        private static bool RestoreIeTree(ContextMenuTreeBackup tree)
        {
            if (tree == null || tree.Target == null) return false;
            DeleteIeTree(tree.Target); if (tree.KeyExisted) RestoreIeNode(tree.Target, tree.Snapshot);
            using (RegistryKey verify = RegistryHelper.OpenSubKey(tree.Target, false)) return tree.KeyExisted ? verify != null : verify == null;
        }
        private static void RestoreIeNode(ActionTarget target, RegistryTreeSnapshot node)
        {
            RunReg("add " + Quote(Native(target)) + " /f" + ViewArg(target), true);
            if (node == null) return;
            foreach (RegistryTreeValueSnapshot value in node.Values ?? new List<RegistryTreeValueSnapshot>())
            {
                string type = "REG_SZ", data = value.Text ?? string.Empty;
                if (value.Kind == RegistryValueKind.DWord.ToString()) { type = "REG_DWORD"; data = Convert.ToInt32(value.Number).ToString(); }
                else if (value.Kind == RegistryValueKind.QWord.ToString()) { type = "REG_QWORD"; data = value.Number.ToString(); }
                else if (value.Kind == RegistryValueKind.ExpandString.ToString()) type = "REG_EXPAND_SZ";
                else if (value.Kind == RegistryValueKind.MultiString.ToString()) { type = "REG_MULTI_SZ"; data = string.Join("\\0", value.TextArray ?? new string[0]); }
                else if (value.Kind == RegistryValueKind.Binary.ToString()) { type = "REG_BINARY"; data = BitConverter.ToString(value.Bytes ?? new byte[0]).Replace("-", string.Empty); }
                RegAdd(target, value.Name ?? string.Empty, data, type);
            }
            foreach (KeyValuePair<string, RegistryTreeSnapshot> child in node.Children ?? new Dictionary<string, RegistryTreeSnapshot>()) RestoreIeNode(Target(target.Hive, target.View, target.SubKey + "\\" + child.Key), child.Value);
        }
        private static void RegAdd(ActionTarget target, string name, string data, string type)
        {
            string selector = string.IsNullOrEmpty(name) ? "/ve" : "/v " + Quote(name);
            RunReg("add " + Quote(Native(target)) + " " + selector + " /t " + type + " /d " + Quote(data ?? string.Empty) + " /f" + ViewArg(target), true);
        }
        private static void DeleteIeTree(ActionTarget target) { RunReg("delete " + Quote(Native(target)) + " /f" + ViewArg(target), false); }
        private static string Native(ActionTarget target) { return (target.Hive == "HKLM" ? "HKLM\\" : "HKCU\\") + target.SubKey; }
        private static string ViewArg(ActionTarget target) { return target.View == "Registry32" ? " /reg:32" : target.View == "Registry64" ? " /reg:64" : string.Empty; }
        private static string Quote(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\""; }
        private static void RunReg(string arguments, bool required)
        {
            ProcessStartInfo info = new ProcessStartInfo { FileName = "reg.exe", Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
            using (Process process = Process.Start(info)) { string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit(); if (required && process.ExitCode != 0) throw new InvalidOperationException("reg.exe 写入失败，退出码 " + process.ExitCode + "：" + (string.IsNullOrWhiteSpace(error) ? output : error).Trim()); }
        }

        private static void Ensure(AdvancedMenuEntry entry) { if (entry == null) throw new ArgumentNullException("entry"); if (entry.ReadOnly) throw new InvalidOperationException("该项目为只读。"); if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目属于所有用户范围，需要管理员权限。"); }
        private static string[] WinXNameParts(string path) { string name = Path.GetFileNameWithoutExtension(path); int split = name.IndexOf(" - ", StringComparison.Ordinal); return split > 0 ? new string[] { name.Substring(0, split), name.Substring(split + 3) } : new string[] { name, name }; }
        private static string Parent(string path) { return path.Substring(0, path.LastIndexOf('\\')); }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static string DefaultView() { return Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; }
    }

}
