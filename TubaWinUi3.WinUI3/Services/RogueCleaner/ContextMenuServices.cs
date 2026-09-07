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

    internal sealed class ContextMenuEntry
    {
        public string Id { get; set; }
        public string Scene { get; set; }
        public string Name { get; set; }
        public string RawName { get; set; }
        public string Type { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public string Command { get; set; }
        public string Icon { get; set; }
        public string Clsid { get; set; }
        public string SubCommands { get; set; }
        public string DisableValueName { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public bool Enabled { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool ReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
        public string NameReadStatus { get; set; }
        public bool AdvancedOnly { get; set; }
        // 用户通过本工具「添加菜单」创建（写入 RogueCleanerUserAdded 标记，管理列表始终显示）
        [JsonIgnore]
        public bool UserAdded { get; set; }
        [JsonIgnore]
        public bool DynamicTitleProbeEligible { get; set; }

        [JsonIgnore]
        public Image SoftwareIcon { get; set; }
        // WinUI 行模板直接绑定的图标（页面水合后填充）
        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.ImageSource IconDisplay { get; set; }
        [JsonIgnore]
        public string SoftwareName { get; set; }
        [JsonIgnore]
        public string DeclaredVendor { get; set; }
        [JsonIgnore]
        public string IdentityConfidence { get; set; }
        [JsonIgnore]
        public string IconSource { get; set; }
        [JsonIgnore]
        public string IdentityExplanation { get; set; }
        [JsonIgnore]
        public bool PresentationResolved { get; set; }
        [JsonIgnore]
        public bool IsThirdParty { get; set; }
        [JsonIgnore]
        public string StatusText { get { return Enabled ? "已启用" : "已禁用"; } }

        // 供 WinUI 行模板与专用/高级条目统一展示（原版由 WinForms 网格自行取列）。
        [JsonIgnore]
        public string ModuleDisplay { get { return Type; } }

        [JsonIgnore]
        public string Detail { get { return Command; } }

        public string TechnicalLocation
        {
            get
            {
                string viewText = ChineseDisplayText.RegistryView(View);
                return Hive + "\\" + SubKey + (string.IsNullOrEmpty(viewText) ? string.Empty : "（" + viewText + "）");
            }
        }

        public SoftwarePresentationEvidence PresentationEvidence()
        {
            return new SoftwarePresentationEvidence { DeclaredName = Name, DeclaredVendor = DeclaredVendor, IconValue = Icon, Command = Command, Clsid = Clsid, TechnicalLocation = TechnicalLocation };
        }

        public void ApplyPresentation(SoftwarePresentation presentation)
        {
            if (presentation == null) return;
            SoftwareIcon = presentation.Icon; SoftwareName = presentation.SoftwareName; IdentityConfidence = presentation.Confidence; IconSource = presentation.IconSource; IdentityExplanation = presentation.Explanation;
            IsThirdParty = string.Equals(presentation.Confidence, "Confirmed", StringComparison.OrdinalIgnoreCase);
            if (IsThirdParty && DynamicTitleProbeEligible && ShouldProbeDynamicTitle(Name, RawName))
            {
                string componentPath = FirstExistingPath(IconSource, Command, Icon);
                ContextCommandProbeResult probe = ContextCommandTitleProbe.ProbeIsolated(Clsid, ProbeItemType(Scene), componentPath);
                if (probe != null && !string.IsNullOrWhiteSpace(probe.Title))
                {
                    Name = ChineseDisplayText.ContextMenuName(probe.Title);
                    NameReadStatus = "命令文字来源：" + (string.IsNullOrWhiteSpace(probe.Source) ? "右键扩展" : probe.Source) + "。";
                }
                else if (probe != null && !string.IsNullOrWhiteSpace(probe.Error)) NameReadStatus = probe.Error;
            }
            Name = ChineseDisplayText.EnsureChineseContextMenuName(Name, SoftwareName, Scene);
            PresentationResolved = true;
        }

        private static bool ShouldProbeDynamicTitle(string displayName, string rawName)
        {
            string value = (displayName ?? string.Empty).Trim();
            string lower = value.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value) || value == "名称未识别") return true;
            if (value.EndsWith("右键菜单", StringComparison.Ordinal) || value.EndsWith("右键扩展", StringComparison.Ordinal) || value.EndsWith("右键命令", StringComparison.Ordinal) || value.EndsWith("操作", StringComparison.Ordinal) || value.IndexOf("具体功能未识别", StringComparison.Ordinal) >= 0) return true;
            string raw = (rawName ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(raw) && string.Equals(value, raw, StringComparison.OrdinalIgnoreCase) && raw.IndexOf(' ') < 0 && raw.All(delegate(char character) { return character < 128; });
        }

        private static string ProbeItemType(string scene)
        {
            if ((scene ?? string.Empty).IndexOf("空白处", StringComparison.Ordinal) >= 0) return @"Directory\Background";
            if ((scene ?? string.Empty).IndexOf("文件夹", StringComparison.Ordinal) >= 0) return "Directory";
            if ((scene ?? string.Empty).IndexOf("磁盘", StringComparison.Ordinal) >= 0 || (scene ?? string.Empty).IndexOf("驱动器", StringComparison.Ordinal) >= 0) return "Drive";
            return "*";
        }

        private static string FirstExistingPath(params string[] values)
        {
            foreach (string value in values ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                string text = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                int comma = text.LastIndexOf(',');
                int iconIndex;
                if (comma > 0 && int.TryParse(text.Substring(comma + 1).Trim(), out iconIndex)) text = text.Substring(0, comma).Trim().Trim('"');
                if (File.Exists(text)) return text;
            }
            return string.Empty;
        }
    }

    internal sealed class ContextMenuInventory
    {
        public List<ContextMenuEntry> Entries { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class ContextMenuToggleBackup
    {
        public string Mode { get; set; }
        public ActionTarget Target { get; set; }
        public bool ValueExisted { get; set; }
        public string ValueName { get; set; }
        public object Value { get; set; }
        public string ValueKind { get; set; }
    }

    internal sealed class RegistryTreeValueSnapshot
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Text { get; set; }
        public string[] TextArray { get; set; }
        public byte[] Bytes { get; set; }
        public long Number { get; set; }
    }

    internal sealed class RegistryTreeSnapshot
    {
        public List<RegistryTreeValueSnapshot> Values { get; set; }
        public Dictionary<string, RegistryTreeSnapshot> Children { get; set; }
    }

    internal sealed class ContextMenuTreeBackup
    {
        public ActionTarget Target { get; set; }
        public bool KeyExisted { get; set; }
        public RegistryTreeSnapshot Snapshot { get; set; }
    }

    internal sealed class ContextMenuInventoryService
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder output, uint outputCount, IntPtr reserved);

        private sealed class RootDefinition
        {
            public string Scene;
            public string Path;
            public string Type;
        }

        private static readonly RootDefinition[] Roots = new RootDefinition[]
        {
            Root("所有文件", @"Software\Classes\*\shell", "Shell 命令"),
            Root("所有文件", @"Software\Classes\*\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shell", "Shell 命令"),
            Root("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("文件夹", @"Software\Classes\Directory\shell", "Shell 命令"),
            Root("文件夹", @"Software\Classes\Directory\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("文件夹背景", @"Software\Classes\Directory\Background\shell", "Shell 命令"),
            Root("文件夹背景", @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("桌面背景", @"Software\Classes\DesktopBackground\shell", "Shell 命令"),
            Root("桌面背景", @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("磁盘", @"Software\Classes\Drive\shell", "Shell 命令"),
            Root("磁盘", @"Software\Classes\Drive\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("磁盘拖放", @"Software\Classes\Drive\shellex\DragDropHandlers", "Shell 扩展"),
            Root("文件夹对象", @"Software\Classes\Folder\shell", "Shell 命令"),
            Root("文件夹对象", @"Software\Classes\Folder\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("文件夹拖放", @"Software\Classes\Folder\shellex\DragDropHandlers", "Shell 扩展"),
            Root("快捷方式", @"Software\Classes\lnkfile\shell", "Shell 命令"),
            Root("快捷方式", @"Software\Classes\lnkfile\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("可执行文件", @"Software\Classes\exefile\shell", "Shell 命令"),
            Root("可执行文件", @"Software\Classes\exefile\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("未知文件", @"Software\Classes\Unknown\shell", "Shell 命令")
        };

        private const string CommandStoreRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";

        private static RootDefinition Root(string scene, string path, string type)
        {
            return new RootDefinition { Scene = scene, Path = path, Type = type };
        }

        public ContextMenuInventory Enumerate()
        {
            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            foreach (string hive in new string[] { "HKCU", "HKLM" })
            {
                foreach (string view in new string[] { "Registry64", "Registry32" })
                {
                    foreach (RootDefinition root in Roots) EnumerateRoot(hive, view, root, entries, warnings);
                    EnumerateRoot(hive, view, Root("命令仓库", CommandStoreRoot, "命令仓库"), entries, warnings, true);
                    EnumerateFileTypes(hive, view, entries, warnings);
                }
            }
            return new ContextMenuInventory
            {
                Entries = entries.OrderBy(delegate(ContextMenuEntry e) { return e.Scene; }).ThenBy(delegate(ContextMenuEntry e) { return e.Name; }).ToList(),
                Warnings = warnings
            };
        }

        private void EnumerateFileTypes(string hive, string view, List<ContextMenuEntry> entries, List<ScanWarning> warnings)
        {
            ActionTarget classes = Target(hive, view, @"Software\Classes");
            using (RegistryKey key = Open(classes, "文件类型", warnings))
            {
                if (key == null) return;
                foreach (string name in SafeNames(key))
                {
                    if (!name.StartsWith(".", StringComparison.Ordinal) || name.Length > 24) continue;
                    List<string> owners = new List<string> { name };
                    using (RegistryKey extensionKey = Open(Target(hive, view, @"Software\Classes\" + name), "文件类型", warnings))
                    {
                        string progId = Read(extensionKey, "");
                        if (!string.IsNullOrWhiteSpace(progId) && progId.IndexOf('\\') < 0) owners.Add(progId);
                    }
                    foreach (string owner in owners.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        foreach (string suffix in new string[] { @"\shell", @"\shellex\ContextMenuHandlers" })
                        {
                            RootDefinition root = Root("文件类型 " + name, @"Software\Classes\" + owner + suffix, suffix.IndexOf("shellex", StringComparison.OrdinalIgnoreCase) >= 0 ? "Shell 扩展" : "Shell 命令");
                            EnumerateRoot(hive, view, root, entries, warnings, true);
                        }
                    }
                }
            }
        }

        private void EnumerateRoot(string hive, string view, RootDefinition root, List<ContextMenuEntry> entries, List<ScanWarning> warnings, bool advancedOnly = false)
        {
            ActionTarget rootTarget = Target(hive, view, root.Path);
            using (RegistryKey key = Open(rootTarget, root.Scene, warnings))
            {
                if (key == null) return;
                foreach (string childName in SafeNames(key))
                {
                    ActionTarget childTarget = Target(hive, view, root.Path + "\\" + childName);
                    using (RegistryKey child = Open(childTarget, root.Scene, warnings))
                    {
                        if (child == null) continue;
                        bool shellEx = root.Type == "Shell 扩展";
                        string clsid = shellEx ? Read(child, "") : Read(child, "ExplorerCommandHandler");
                        string command = shellEx ? string.Empty : ReadChildDefault(childTarget, "command", warnings);
                        string rawDisplay = First(Read(child, "MUIVerb"), Read(child, ""), childName);
                        string display = FriendlyMenuName(rawDisplay, childName, command);
                        bool hasLegacyDisable = HasValue(child, "LegacyDisable");
                        bool hasProgrammaticDisable = HasValue(child, "ProgrammaticAccessOnly");
                        bool disabled = shellEx ? IsBlocked(hive, view, clsid, warnings) : hasLegacyDisable || hasProgrammaticDisable;
                        bool ambiguousDisable = !shellEx && hasLegacyDisable && hasProgrammaticDisable;
                        bool userAdded = HasValue(child, "RogueCleanerUserAdded");
                        entries.Add(new ContextMenuEntry
                        {
                            Id = hive + "|" + view + "|" + childTarget.SubKey,
                            Scene = root.Scene,
                            Name = display,
                            RawName = rawDisplay,
                            Type = root.Type,
                            Scope = (hive == "HKCU" ? "当前用户" : "所有用户") + " / " + (view == "Registry32" ? "32 位" : "64 位"),
                            Status = disabled ? "已禁用" : "已启用",
                            Command = command,
                            Icon = ResolveEntryIcon(child, hive, view, root, childName, warnings),
                            Clsid = clsid,
                            SubCommands = Read(child, "SubCommands"),
                            DisableValueName = shellEx ? clsid : (hasProgrammaticDisable ? "ProgrammaticAccessOnly" : "LegacyDisable"),
                            Hive = hive,
                            View = view,
                            SubKey = childTarget.SubKey,
                            Enabled = !disabled,
                            RequiresAdmin = hive == "HKLM",
                            ReadOnly = (shellEx && string.IsNullOrWhiteSpace(clsid)) || ambiguousDisable,
                            ReadOnlyReason = shellEx && string.IsNullOrWhiteSpace(clsid) ? "没有读取到 CLSID，不能安全启停。" : (ambiguousDisable ? "同时存在两个禁用标记，当前版本先保持只读，避免破坏程序的条件显示逻辑。" : string.Empty),
                            DynamicTitleProbeEligible = !string.IsNullOrWhiteSpace(clsid),
                            UserAdded = userAdded,
                            AdvancedOnly = advancedOnly
                        });
                    }
                }
            }
        }

        internal static string FriendlyMenuName(string raw, string childName, string command)
        {
            string value = (raw ?? string.Empty).Trim();
            string lower = (value + " " + childName + " " + command).ToLowerInvariant();
            if (lower.IndexOf("safe360ext") >= 0) return "360 安全扫描";
            if (lower.IndexOf("softmgrext") >= 0) return "360 软件管家";
            if (lower.IndexOf("qingshellext") >= 0) return "上传到 WPS 云文档";
            if (lower.IndexOf("qingnsecontextmenu") >= 0) return "WPS 云文档操作菜单";
            if (lower.IndexOf("sgshellext") >= 0) return "搜狗右键菜单";
            if (lower.IndexOf("bdeunlock") >= 0 || lower.IndexOf("unlock-bde") >= 0) return "解锁 BitLocker 驱动器";
            if (lower.IndexOf("fvewiz") >= 0 || lower.IndexOf("manage-bde") >= 0) return "管理 BitLocker";
            if (value.StartsWith("@", StringComparison.Ordinal))
            {
                try
                {
                    StringBuilder resolved = new StringBuilder(512);
                    if (SHLoadIndirectString(value, resolved, (uint)resolved.Capacity, IntPtr.Zero) == 0 && resolved.Length > 0) value = resolved.ToString();
                }
                catch { }
            }
            string cleanValue = CleanMenuText(value);
            value = ChineseDisplayText.ContextMenuName(cleanValue);
            if (IsReadableMenuText(value) && (ChineseDisplayText.HasChinese(value) || !string.Equals(value, cleanValue, StringComparison.OrdinalIgnoreCase))) return value;
            string key = (childName ?? string.Empty).Trim();
            string keyLower = key.ToLowerInvariant();
            if (keyLower == "open") return "打开";
            if (keyLower == "runas" || keyLower == "runasuser") return "以管理员身份运行";
            if (keyLower == "edit") return "编辑";
            if (keyLower == "print" || keyLower == "printto") return "打印";
            if (keyLower == "share") return "共享";
            string cleanedKey = ChineseDisplayText.ContextMenuName(CleanMenuText(key));
            if (IsReadableMenuText(cleanedKey) && ChineseDisplayText.HasChinese(cleanedKey)) return cleanedKey;
            string executable = ExtractExecutable(command);
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
                    string description = First(info.FileDescription, info.ProductName);
                    if (IsReadableMenuText(description)) return ChineseDisplayText.EnsureChineseContextMenuName(CleanMenuText(description), description, string.Empty);
                }
                catch { }
            }
            return "第三方软件右键菜单";
        }

        private static string ResolveEntryIcon(RegistryKey child, string hive, string view, RootDefinition root, string childName, List<ScanWarning> warnings)
        {
            string icon = Read(child, "Icon");
            if (!string.IsNullOrWhiteSpace(icon)) return icon;

            string commandStoreId = Read(child, "CommandStore");
            if (string.IsNullOrWhiteSpace(commandStoreId)) commandStoreId = Read(child, "SubCommands");
            if (!string.IsNullOrWhiteSpace(commandStoreId) && commandStoreId.IndexOf(';') < 0)
            {
                ActionTarget commandStore = Target(hive, view, CommandStoreRoot + "\\" + commandStoreId.Trim());
                using (RegistryKey commandKey = Open(commandStore, "命令仓库图标", warnings))
                {
                    icon = Read(commandKey, "Icon");
                    if (!string.IsNullOrWhiteSpace(icon)) return icon;
                }
            }

            if (root.Type == "命令仓库")
            {
                ActionTarget commandStore = Target(hive, view, CommandStoreRoot + "\\" + childName);
                using (RegistryKey commandKey = Open(commandStore, "命令仓库图标", warnings)) return Read(commandKey, "Icon");
            }
            return string.Empty;
        }

        private static string CleanMenuText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&").Trim();
        }

        private static bool IsReadableMenuText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
            if (value.StartsWith("@", StringComparison.Ordinal) || value.IndexOf("System32", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (value.IndexOf("\\", StringComparison.Ordinal) >= 0 || value.IndexOf("{", StringComparison.Ordinal) >= 0) return false;
            return value.Any(delegate(char character) { return char.IsLetter(character) || character > 127; });
        }

        private static string ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string text = Environment.ExpandEnvironmentVariables(command.Trim());
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : string.Empty;
            }
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe >= 0 ? text.Substring(0, exe + 4).Trim() : string.Empty;
        }

        private static bool IsBlocked(string hive, string view, string clsid, List<ScanWarning> warnings)
        {
            if (string.IsNullOrWhiteSpace(clsid)) return false;
            ActionTarget target = Target(hive, view, @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
            using (RegistryKey key = Open(target, "GUID 屏蔽", warnings))
            {
                return key != null && HasValue(key, clsid);
            }
        }

        private static string ReadChildDefault(ActionTarget target, string child, List<ScanWarning> warnings)
        {
            ActionTarget childTarget = Target(target.Hive, target.View, target.SubKey + "\\" + child);
            using (RegistryKey key = Open(childTarget, "Shell 命令", warnings)) { return Read(key, ""); }
        }

        private static RegistryKey Open(ActionTarget target, string stage, List<ScanWarning> warnings)
        {
            try { return RegistryHelper.OpenSubKey(target, false); }
            catch (Exception ex)
            {
                if (!(ex is SecurityException) && !(ex is UnauthorizedAccessException)) throw;
                warnings.Add(new ScanWarning { Stage = stage, TechnicalLocation = RegistryHelper.NativePath(target), ErrorType = ex.GetType().FullName, Message = "访问被系统拒绝，已跳过。" });
                return null;
            }
        }

        private static string[] SafeNames(RegistryKey key) { try { return key.GetSubKeyNames(); } catch { return new string[0]; } }
        private static string Read(RegistryKey key, string name) { try { return key == null ? string.Empty : Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; } }
        private static bool HasValue(RegistryKey key, string name) { try { return key != null && key.GetValueNames().Any(delegate(string item) { return string.Equals(item, name, StringComparison.OrdinalIgnoreCase); }); } catch { return false; } }
        private static string First(params string[] values) { foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value; return string.Empty; }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
    }

    internal sealed class ContextMenuDiscoveryService
    {
        private readonly DataStore store;

        public ContextMenuDiscoveryService(DataStore store)
        {
            this.store = store;
        }

        public ContextMenuInventory Enumerate(bool probePackagedTitles)
        {
            ContextMenuInventory result = new ContextMenuInventoryService().Enumerate();
            AdvancedMenuInventory packagedInventory = new AdvancedMenuInventoryService(store).EnumeratePackagedOnly(probePackagedTitles);
            if (packagedInventory.Warnings != null) result.Warnings.AddRange(packagedInventory.Warnings);
            foreach (AdvancedMenuEntry packaged in packagedInventory.Entries)
            {
                result.Entries.Add(new ContextMenuEntry
                {
                    Id = "Packaged|" + packaged.Id,
                    Scene = PackagedScene(packaged.ItemType),
                    Name = ChineseDisplayText.ContextMenuName(packaged.Name),
                    DeclaredVendor = packaged.PublisherName,
                    RawName = packaged.Name,
                    Type = "现代右键扩展",
                    Scope = packaged.Scope,
                    Status = packaged.Status,
                    Command = packaged.FilePath,
                    Icon = string.IsNullOrWhiteSpace(packaged.CommandIcon) ? packaged.FilePath : packaged.CommandIcon,
                    Clsid = packaged.ValueName,
                    DisableValueName = packaged.ValueName,
                    Hive = packaged.Hive,
                    View = packaged.View,
                    SubKey = packaged.SubKey,
                    Enabled = packaged.Enabled,
                    RequiresAdmin = false,
                    ReadOnly = string.IsNullOrWhiteSpace(packaged.ValueName),
                    ReadOnlyReason = string.IsNullOrWhiteSpace(packaged.ValueName) ? "没有读取到组件编号，不能安全显示或隐藏。" : string.Empty,
                    NameReadStatus = string.IsNullOrWhiteSpace(packaged.CommandTitle) ? packaged.TitleProbeStatus : "已从右键扩展读取资源管理器实际命令文字。",
                    AdvancedOnly = false
                });
            }
            result.Entries = result.Entries.OrderBy(delegate(ContextMenuEntry entry) { return entry.Scene; }).ThenBy(delegate(ContextMenuEntry entry) { return entry.Name; }).ToList();
            return result;
        }

        private static string PackagedScene(string itemType)
        {
            if (string.Equals(itemType, "*", StringComparison.OrdinalIgnoreCase)) return "文件右键";
            if (string.Equals(itemType, "Directory", StringComparison.OrdinalIgnoreCase)) return "文件夹右键";
            if (string.Equals(itemType, @"Directory\Background", StringComparison.OrdinalIgnoreCase)) return "文件夹空白处右键";
            if (string.Equals(itemType, "Drive", StringComparison.OrdinalIgnoreCase)) return "磁盘右键";
            return "文件资源管理器右键";
        }
    }

    internal sealed class ContextMenuMutationService
    {
        private static readonly string[] WritableRoots = new string[]
        {
            @"Software\Classes\*\shell",
            @"Software\Classes\AllFilesystemObjects\shell",
            @"Software\Classes\Directory\shell",
            @"Software\Classes\Directory\Background\shell",
            @"Software\Classes\DesktopBackground\shell",
            @"Software\Classes\Drive\shell",
            @"Software\Classes\Folder\shell",
            @"Software\Classes\lnkfile\shell",
            @"Software\Classes\exefile\shell",
            @"Software\Classes\Unknown\shell",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell"
        };

        private readonly DataStore store;
        public ContextMenuMutationService(DataStore store) { this.store = store; }

        public CleanupBatch SetEnabled(ContextMenuEntry entry, bool enabled)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (entry.ReadOnly) throw new InvalidOperationException(entry.ReadOnlyReason);
            if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目属于所有用户范围，需要管理员权限。");
            bool shellEx = string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
            ActionTarget target = shellEx
                ? new ActionTarget { Kind = "RestoreContextMenuToggle", Hive = entry.Hive, View = entry.View, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", ValueName = entry.Clsid }
                : new ActionTarget { Kind = "RestoreContextMenuToggle", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey, ValueName = string.IsNullOrWhiteSpace(entry.DisableValueName) ? "LegacyDisable" : entry.DisableValueName };
            string id = NewBatchId();
            string batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            string backupPath = Path.Combine(batchPath, "context-menu-toggle.json");
            ContextMenuToggleBackup backup = CaptureValue(target, shellEx ? "ShellExBlocked" : "LegacyDisable");
            CleanerEngine.WriteJson(backupPath, backup);
            Apply(target, shellEx, enabled);
            bool actualEnabled = shellEx ? !ValueExists(target) : !ValueExists(target);
            if (actualEnabled != enabled)
            {
                Restore(backupPath);
                throw new InvalidOperationException("写入后复核失败，已尝试回滚。");
            }
            CleanupResult result = new CleanupResult
            {
                Id = 1,
                Title = entry.Name,
                Vendor = "右键管理",
                Category = entry.Scene + " / " + entry.Type,
                ActionKind = enabled ? "EnableContextMenu" : "DisableContextMenu",
                TechnicalLocation = entry.TechnicalLocation,
                Status = "Done",
                Message = enabled ? "右键项已启用。" : "右键项已禁用。",
                Backup = backupPath,
                Target = target
            };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } };
            CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            CleanerEngine.WriteJson(Path.Combine(store.Reports, "context-menu-" + id + ".json"), result);
            return batch;
        }

        public CleanupBatch Edit(ContextMenuEntry entry, string displayName, string icon, string command, string subCommands)
        {
            EnsureWritableEntry(entry);
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("显示名称不能为空。");
            if (string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("右键扩展由组件编号驱动，当前编辑器不改写它的注册结构。");
            if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(subCommands) && string.IsNullOrWhiteSpace(entry.Clsid)) throw new ArgumentException("命令、子菜单引用和 ExplorerCommandHandler 不能同时为空。");
            ActionTarget target = new ActionTarget { Kind = "RestoreContextMenuTree", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey };
            return MutateTree(target, entry.Name, entry.Scene + " / " + entry.Type, "EditContextMenu", delegate(RegistryKey key)
            {
                key.SetValue("MUIVerb", displayName.Trim(), RegistryValueKind.String);
                SetOrDelete(key, "Icon", icon);
                SetOrDelete(key, "SubCommands", subCommands);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    using (RegistryKey commandKey = key.CreateSubKey("command", RegistryKeyPermissionCheck.ReadWriteSubTree)) commandKey.SetValue("", command.Trim(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteSubKeyTree("command", false);
                }
            }, delegate
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
                {
                    return key != null && string.Equals(Convert.ToString(key.GetValue("MUIVerb", "")), displayName.Trim(), StringComparison.Ordinal);
                }
            });
        }

        public CleanupBatch Add(string scene, string rootSubKey, string keyName, string displayName, string icon, string command, string subCommands)
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("显示名称不能为空。");
            string safeName = SafeKeyName(string.IsNullOrWhiteSpace(keyName) ? displayName : keyName);
            if (string.IsNullOrWhiteSpace(safeName)) throw new ArgumentException("注册表项名称无效。");
            string normalizedRoot = NormalizeWritableRoot(rootSubKey);
            ActionTarget target = new ActionTarget { Kind = "RestoreContextMenuTree", Hive = "HKCU", View = Environment.Is64BitOperatingSystem ? "Registry64" : "Default", SubKey = normalizedRoot + "\\" + safeName };
            using (RegistryKey existing = RegistryHelper.OpenSubKey(target, false)) if (existing != null) throw new InvalidOperationException("同名菜单项已经存在：" + safeName);
            if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(subCommands)) throw new ArgumentException("命令和子菜单引用不能同时为空。");
            return MutateTree(target, displayName.Trim(), scene, "AddContextMenu", delegate(RegistryKey key)
            {
                key.SetValue("MUIVerb", displayName.Trim(), RegistryValueKind.String);
                SetOrDelete(key, "Icon", icon);
                SetOrDelete(key, "SubCommands", subCommands);
                // 用户添加标记：右键菜单管理列表始终显示该条目，便于再次开关/删除
                key.SetValue("RogueCleanerUserAdded", "1", RegistryValueKind.String);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    using (RegistryKey commandKey = key.CreateSubKey("command", RegistryKeyPermissionCheck.ReadWriteSubTree)) commandKey.SetValue("", command.Trim(), RegistryValueKind.String);
                }
            }, delegate { using (RegistryKey key = RegistryHelper.OpenSubKey(target, false)) return key != null; });
        }

        public CleanupBatch Delete(ContextMenuEntry entry)
        {
            if (entry != null && string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Windows 打包右键扩展只允许显示或隐藏，不能删除应用包注册。");
            EnsureWritableEntry(entry);
            ActionTarget target = new ActionTarget { Kind = "RestoreContextMenuTree", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey };
            ContextMenuTreeBackup backup = CaptureTree(target);
            if (!backup.KeyExisted) throw new InvalidOperationException("目标菜单项已经不存在。");
            string id;
            string batchPath;
            string backupPath;
            PrepareTreeBackup(target, backup, out id, out batchPath, out backupPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) root.DeleteSubKeyTree(target.SubKey, false);
                using (RegistryKey verify = RegistryHelper.OpenSubKey(target, false)) if (verify != null) throw new InvalidOperationException("删除后复核失败。");
                return CompleteTreeBatch(id, batchPath, backupPath, target, entry.Name, entry.Scene + " / " + entry.Type, "DeleteContextMenu", "右键项已备份并删除。");
            }
            catch
            {
                RestoreTree(backupPath);
                throw;
            }
        }

        private CleanupBatch MutateTree(ActionTarget target, string title, string category, string actionKind, Action<RegistryKey> mutation, Func<bool> verify)
        {
            ContextMenuTreeBackup backup = CaptureTree(target);
            string id;
            string batchPath;
            string backupPath;
            PrepareTreeBackup(target, backup, out id, out batchPath, out backupPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) mutation(key);
                if (!verify()) throw new InvalidOperationException("写入后复核失败。");
                return CompleteTreeBatch(id, batchPath, backupPath, target, title, category, actionKind, "右键菜单配置已修改。");
            }
            catch
            {
                RestoreTree(backupPath);
                throw;
            }
        }

        private void PrepareTreeBackup(ActionTarget target, ContextMenuTreeBackup backup, out string id, out string batchPath, out string backupPath)
        {
            id = NewBatchId();
            batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            backupPath = Path.Combine(batchPath, "context-menu-tree.json");
            CleanerEngine.WriteJson(backupPath, backup);
        }

        private CleanupBatch CompleteTreeBatch(string id, string batchPath, string backupPath, ActionTarget target, string title, string category, string actionKind, string message)
        {
            CleanupResult result = new CleanupResult { Id = 1, Title = title, Vendor = "右键管理", Category = category, ActionKind = actionKind, TechnicalLocation = RegistryHelper.NativePath(target), Status = "Done", Message = message, Backup = backupPath, Target = target };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } };
            CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            CleanerEngine.WriteJson(Path.Combine(store.Reports, "context-menu-" + id + ".json"), result);
            return batch;
        }

        // P1-3：快照深度/节点/值数量上限 —— 防御异常巨大的注册表子树被整树驻留内存导致卡死。
        // 右键菜单键实际层级通常 ≤4、值 ≤几十，16 层 + 5 万节点 + 每键 1000 值远超正常范围。
        private const int MaxSnapshotDepth = 16;
        private const int MaxSnapshotNodes = 50000;
        private const int MaxSnapshotValuesPerKey = 1000;

        internal static ContextMenuTreeBackup CaptureTree(ActionTarget target)
        {
            ContextMenuTreeBackup backup = new ContextMenuTreeBackup { Target = target };
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                backup.KeyExisted = key != null;
                if (key != null)
                {
                    int budget = MaxSnapshotNodes;
                    backup.Snapshot = CaptureNode(key, 0, ref budget);
                    if (budget <= 0)
                        TubaWinUi3.Services.Agent.AgentDebugLog.Error(
                            "[P1-3] 注册表快照达到节点上限，已截断（仅影响极端异常键的完整还原）: " + target.SubKey);
                }
            }
            return backup;
        }

        private static RegistryTreeSnapshot CaptureNode(RegistryKey key, int depth, ref int budget)
        {
            RegistryTreeSnapshot node = new RegistryTreeSnapshot { Values = new List<RegistryTreeValueSnapshot>(), Children = new Dictionary<string, RegistryTreeSnapshot>(StringComparer.OrdinalIgnoreCase) };
            if (budget <= 0 || depth > MaxSnapshotDepth)
                return node;
            budget--;

            int valueCount = 0;
            foreach (string valueName in key.GetValueNames())
            {
                if (++valueCount > MaxSnapshotValuesPerKey) break;
                RegistryValueKind kind = key.GetValueKind(valueName);
                object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryTreeValueSnapshot item = new RegistryTreeValueSnapshot { Name = valueName, Kind = kind.ToString() };
                if (kind == RegistryValueKind.Binary) item.Bytes = value as byte[];
                else if (kind == RegistryValueKind.MultiString) item.TextArray = value as string[];
                else if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord) item.Number = Convert.ToInt64(value);
                else item.Text = Convert.ToString(value);
                node.Values.Add(item);
            }

            if (depth >= MaxSnapshotDepth)
                return node;
            foreach (string childName in key.GetSubKeyNames())
            {
                if (budget <= 0) break;
                using (RegistryKey child = key.OpenSubKey(childName, false)) if (child != null) node.Children[childName] = CaptureNode(child, depth + 1, ref budget);
            }
            return node;
        }

        public static bool RestoreTree(string backupPath)
        {
            ContextMenuTreeBackup backup = JsonSerializer.Deserialize<ContextMenuTreeBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreTreeSnapshot(backup);
        }

        internal static bool RestoreTreeSnapshot(ContextMenuTreeBackup backup)
        {
            if (backup == null || backup.Target == null) return false;
            using (RegistryKey root = RegistryHelper.OpenBase(backup.Target.Hive, backup.Target.View, true))
            {
                root.DeleteSubKeyTree(backup.Target.SubKey, false);
                if (backup.KeyExisted)
                {
                    using (RegistryKey key = root.CreateSubKey(backup.Target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) RestoreNode(key, backup.Snapshot);
                }
            }
            using (RegistryKey verify = RegistryHelper.OpenSubKey(backup.Target, false)) return backup.KeyExisted ? verify != null : verify == null;
        }

        internal static void RestoreNode(RegistryKey key, RegistryTreeSnapshot node)
        {
            if (node == null) return;
            foreach (RegistryTreeValueSnapshot item in node.Values ?? new List<RegistryTreeValueSnapshot>())
            {
                RegistryValueKind kind = ParseKind(item.Kind);
                object value = kind == RegistryValueKind.Binary ? (object)(item.Bytes ?? new byte[0]) : kind == RegistryValueKind.MultiString ? (object)(item.TextArray ?? new string[0]) : kind == RegistryValueKind.DWord ? (object)Convert.ToInt32(item.Number) : kind == RegistryValueKind.QWord ? (object)item.Number : (object)(item.Text ?? string.Empty);
                key.SetValue(item.Name ?? string.Empty, value, kind);
            }
            foreach (KeyValuePair<string, RegistryTreeSnapshot> child in node.Children ?? new Dictionary<string, RegistryTreeSnapshot>())
            {
                using (RegistryKey childKey = key.CreateSubKey(child.Key, RegistryKeyPermissionCheck.ReadWriteSubTree)) RestoreNode(childKey, child.Value);
            }
        }

        private static void EnsureWritableEntry(ContextMenuEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目属于所有用户范围，需要管理员权限。");
            bool regular = WritableRoots.Any(delegate(string root) { return entry.SubKey.Equals(root, StringComparison.OrdinalIgnoreCase) || entry.SubKey.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase); });
            bool shellExtension = entry.SubKey.StartsWith(@"Software\Classes\", StringComparison.OrdinalIgnoreCase) &&
                (entry.SubKey.IndexOf(@"\shellex\ContextMenuHandlers\", StringComparison.OrdinalIgnoreCase) >= 0 || entry.SubKey.IndexOf(@"\shellex\DragDropHandlers\", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!regular && !shellExtension) throw new InvalidOperationException("该注册表位置不在受控编辑范围内。");
        }

        private static string NormalizeWritableRoot(string rootSubKey)
        {
            string match = WritableRoots.FirstOrDefault(delegate(string item) { return string.Equals(item, rootSubKey, StringComparison.OrdinalIgnoreCase); });
            if (match == null) throw new InvalidOperationException("不支持向该位置添加菜单项。");
            return match;
        }

        private static void SetOrDelete(RegistryKey key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) key.DeleteValue(name, false); else key.SetValue(name, value.Trim(), RegistryValueKind.String);
        }

        private static string SafeKeyName(string value)
        {
            string text = (value ?? string.Empty).Trim();
            foreach (char invalid in new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) text = text.Replace(invalid, '_');
            return text.Length > 80 ? text.Substring(0, 80) : text;
        }

        private static string NewBatchId() { return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }

        internal static ContextMenuToggleBackup CaptureValue(ActionTarget target, string mode)
        {
            ContextMenuToggleBackup backup = new ContextMenuToggleBackup { Mode = mode, Target = target, ValueName = target.ValueName };
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                if (key == null) return backup;
                string actualName = key.GetValueNames().FirstOrDefault(delegate(string name) { return string.Equals(name, target.ValueName, StringComparison.OrdinalIgnoreCase); });
                backup.ValueExisted = actualName != null;
                if (backup.ValueExisted)
                {
                    backup.Value = key.GetValue(actualName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    backup.ValueKind = key.GetValueKind(actualName).ToString();
                }
            }
            return backup;
        }

        private static void Apply(ActionTarget target, bool shellEx, bool enabled)
        {
            using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
            using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (enabled) key.DeleteValue(target.ValueName, false);
                else key.SetValue(target.ValueName, shellEx ? "由流氓软件克星禁用" : string.Empty, RegistryValueKind.String);
            }
        }

        internal static bool SetShellExtensionBlocked(ActionTarget target, bool blocked)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ValueName)) return false;
            Apply(target, true, !blocked);
            return ValueExists(target) == blocked;
        }

        private static bool ValueExists(ActionTarget target)
        {
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                return key != null && key.GetValueNames().Any(delegate(string name) { return string.Equals(name, target.ValueName, StringComparison.OrdinalIgnoreCase); });
            }
        }

        public static bool Restore(string backupPath)
        {
            ContextMenuToggleBackup backup = JsonSerializer.Deserialize<ContextMenuToggleBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreValueSnapshot(backup);
        }

        internal static bool RestoreValueSnapshot(ContextMenuToggleBackup backup)
        {
            if (backup == null || backup.Target == null) return false;
            using (RegistryKey root = RegistryHelper.OpenBase(backup.Target.Hive, backup.Target.View, true))
            using (RegistryKey key = root.CreateSubKey(backup.Target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (!backup.ValueExisted) key.DeleteValue(backup.ValueName, false);
                else key.SetValue(backup.ValueName, backup.Value ?? string.Empty, ParseKind(backup.ValueKind));
            }
            return ValueExists(backup.Target) == backup.ValueExisted;
        }

        private static RegistryValueKind ParseKind(string value)
        {
            RegistryValueKind kind;
            return Enum.TryParse<RegistryValueKind>(value, out kind) ? kind : RegistryValueKind.String;
        }
    }

}
