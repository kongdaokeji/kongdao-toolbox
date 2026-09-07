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

    internal sealed class SpecialMenuEntry
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
        public string ModuleDisplay { get { return SpecialMenuDisplay.Name(Module); } }
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

    internal static class SpecialMenuDisplay
    {
        public static string Name(string module)
        {
            if (module == "ShellNew 新建菜单") return "新建菜单";
            if (module == "SendTo 发送到") return "发送到菜单";
            if (module == "OpenWith 打开方式") return "打开方式";
            if (module == "OpenWith 应用程序") return "打开方式应用程序";
            if (module == "GUID 屏蔽") return "组件屏蔽";
            return module;
        }

        public static string Key(string display)
        {
            if (display == "新建菜单") return "ShellNew 新建菜单";
            if (display == "发送到菜单") return "SendTo 发送到";
            if (display == "打开方式") return "OpenWith 打开方式";
            if (display == "打开方式应用程序") return "OpenWith 应用程序";
            if (display == "组件屏蔽") return "GUID 屏蔽";
            return display;
        }
    }

    internal sealed class SpecialMenuInventory
    {
        public List<SpecialMenuEntry> Entries { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class SpecialMenuBackup
    {
        public string Mode { get; set; }
        public List<ContextMenuTreeBackup> Trees { get; set; }
        public List<ContextMenuToggleBackup> Values { get; set; }
        public string OriginalFile { get; set; }
        public string ChangedFile { get; set; }
        public bool OriginalFileExisted { get; set; }
        public bool ChangedFileExisted { get; set; }
    }

    internal sealed class SpecialMenuInventoryService
    {
        private const string DisabledShellNew = "ShellNew.RogueCleanerDisabled";
        private const string DisabledOpenWith = "OpenWithProgids.RogueCleanerDisabled";
        private const string DisabledOpenWithList = "OpenWithList.RogueCleanerDisabled";
        private const string BlockedRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        private readonly DataStore store;

        public SpecialMenuInventoryService(DataStore store) { this.store = store; }

        public SpecialMenuInventory Enumerate()
        {
            List<SpecialMenuEntry> entries = new List<SpecialMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string hive in new string[] { "HKCU", "HKLM" })
            {
                foreach (string view in new string[] { "Registry64", "Registry32" })
                {
                    EnumerateClasses(hive, view, entries, warnings, seen);
                    EnumerateApplications(hive, view, entries, warnings, seen);
                    EnumerateBlocked(hive, view, entries, warnings, seen);
                }
            }
            EnumerateSendTo(entries);
            return new SpecialMenuInventory { Entries = entries.OrderBy(delegate(SpecialMenuEntry e) { return e.Module; }).ThenBy(delegate(SpecialMenuEntry e) { return e.Name; }).ToList(), Warnings = warnings };
        }

        private void EnumerateClasses(string hive, string view, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget classes = Target(hive, view, @"Software\Classes");
            using (RegistryKey key = Open(classes, "文件类型", warnings))
            {
                if (key == null) return;
                foreach (string extension in SafeSubKeys(key))
                {
                    if (!extension.StartsWith(".", StringComparison.Ordinal) || extension.Length > 24) continue;
                    string extensionRoot = @"Software\Classes\" + extension;
                    AddShellNew(hive, view, extension, extensionRoot + @"\ShellNew", true, entries, warnings, seen);
                    AddShellNew(hive, view, extension, extensionRoot + "\\" + DisabledShellNew, false, entries, warnings, seen);
                    AddOpenWithValues(hive, view, extension, extensionRoot + @"\OpenWithProgids", true, entries, warnings, seen);
                    AddOpenWithValues(hive, view, extension, extensionRoot + "\\" + DisabledOpenWith, false, entries, warnings, seen);
                    AddOpenWithList(hive, view, extension, extensionRoot + @"\OpenWithList", true, entries, warnings, seen);
                    AddOpenWithList(hive, view, extension, extensionRoot + "\\" + DisabledOpenWithList, false, entries, warnings, seen);
                }
            }
        }

        private void AddShellNew(string hive, string view, string extension, string subKey, bool enabled, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget target = Target(hive, view, subKey);
            using (RegistryKey key = Open(target, "新建菜单", warnings))
            {
                if (key == null) return;
                string id = "ShellNew|" + hive + "|" + view + "|" + subKey;
                if (!seen.Add(id)) return;
                string detail = First(Read(key, "FileName"), HasValue(key, "NullFile") ? "空白文件" : string.Empty, Read(key, "Data"), Read(key, "Command"));
                entries.Add(Entry(id, "ShellNew 新建菜单", extension, detail, hive, view, subKey, null, enabled));
            }
        }

        private void AddOpenWithValues(string hive, string view, string extension, string subKey, bool enabled, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget target = Target(hive, view, subKey);
            using (RegistryKey key = Open(target, "打开方式", warnings))
            {
                if (key == null) return;
                foreach (string valueName in SafeValues(key))
                {
                    string id = "OpenWith|" + hive + "|" + view + "|" + subKey + "|" + valueName;
                    if (!seen.Add(id)) continue;
                    SpecialMenuEntry entry = Entry(id, "OpenWith 打开方式", extension + " → " + valueName, valueName, hive, view, subKey, valueName, enabled);
                    entries.Add(entry);
                }
            }
        }

        private void AddOpenWithList(string hive, string view, string extension, string subKey, bool enabled, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget target = Target(hive, view, subKey);
            using (RegistryKey key = Open(target, "打开方式列表", warnings))
            {
                if (key == null) return;
                foreach (string valueName in SafeValues(key))
                {
                    if (string.Equals(valueName, "MRUList", StringComparison.OrdinalIgnoreCase)) continue;
                    string executable = Read(key, valueName);
                    if (string.IsNullOrWhiteSpace(executable)) continue;
                    string id = "OpenWithList|" + hive + "|" + view + "|" + subKey + "|" + valueName;
                    if (!seen.Add(id)) continue;
                    entries.Add(Entry(id, "OpenWith 打开方式", extension + " → " + executable, "打开方式列表 / " + valueName, hive, view, subKey, valueName, enabled));
                }
                foreach (string application in SafeSubKeys(key))
                {
                    string childPath = subKey + "\\" + application;
                    string id = "OpenWithListKey|" + hive + "|" + view + "|" + childPath;
                    if (!seen.Add(id)) continue;
                    entries.Add(Entry(id, "OpenWith 打开方式", extension + " → " + application, "打开方式列表子项", hive, view, childPath, string.Empty, enabled));
                }
            }
        }

        private void EnumerateApplications(string hive, string view, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            string rootPath = @"Software\Classes\Applications";
            using (RegistryKey root = Open(Target(hive, view, rootPath), "打开方式程序", warnings))
            {
                if (root == null) return;
                foreach (string app in SafeSubKeys(root))
                {
                    string appPath = rootPath + "\\" + app;
                    using (RegistryKey key = Open(Target(hive, view, appPath), "打开方式程序", warnings))
                    {
                        if (key == null) continue;
                        string command = ReadChildDefault(hive, view, appPath + @"\shell\open\command", warnings);
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        string id = "Application|" + hive + "|" + view + "|" + appPath;
                        if (!seen.Add(id)) continue;
                        SpecialMenuEntry entry = Entry(id, "OpenWith 应用程序", app, command, hive, view, appPath, "NoOpenWith", !HasValue(key, "NoOpenWith"));
                        entries.Add(entry);
                    }
                }
            }
        }

        private void EnumerateBlocked(string hive, string view, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            using (RegistryKey key = Open(Target(hive, view, BlockedRoot), "GUID 屏蔽", warnings))
            {
                if (key == null) return;
                foreach (string clsid in SafeValues(key))
                {
                    if (!clsid.StartsWith("{", StringComparison.Ordinal) || !seen.Add("Blocked|" + hive + "|" + view + "|" + clsid)) continue;
                    entries.Add(Entry("Blocked|" + hive + "|" + view + "|" + clsid, "GUID 屏蔽", clsid, Read(key, clsid), hive, view, BlockedRoot, clsid, false));
                }
            }
        }

        private void EnumerateSendTo(List<SpecialMenuEntry> entries)
        {
            string active = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            string disabled = DisabledSendToDirectory(store);
            foreach (string file in SafeFiles(active)) if (!string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase)) entries.Add(FileEntry("SendTo|active|" + file, Path.GetFileName(file), file, true));
            foreach (string file in SafeFiles(disabled)) entries.Add(FileEntry("SendTo|disabled|" + file, Path.GetFileName(file), file, false));
        }

        internal static string DisabledSendToDirectory(DataStore store) { return Path.Combine(store.State, "sendto-disabled"); }
        internal static string DisabledOpenWithName { get { return DisabledOpenWith; } }
        internal static string DisabledOpenWithListName { get { return DisabledOpenWithList; } }
        internal static string DisabledShellNewName { get { return DisabledShellNew; } }
        internal static string BlockedPath { get { return BlockedRoot; } }

        private static SpecialMenuEntry FileEntry(string id, string name, string path, bool enabled)
        {
            return new SpecialMenuEntry { Id = id, Module = "SendTo 发送到", Name = name, Detail = path, Scope = "当前用户 / 文件", Status = enabled ? "已启用" : "已禁用", Enabled = enabled, FilePath = path };
        }

        private static SpecialMenuEntry Entry(string id, string module, string name, string detail, string hive, string view, string subKey, string valueName, bool enabled)
        {
            return new SpecialMenuEntry { Id = id, Module = module, Name = name, Detail = detail, Hive = hive, View = view, SubKey = subKey, ValueName = valueName, Scope = (hive == "HKCU" ? "当前用户" : "所有用户") + " / " + (view == "Registry32" ? "32 位" : "64 位"), Status = enabled ? "已启用" : "已禁用", Enabled = enabled, RequiresAdmin = hive == "HKLM" };
        }

        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static RegistryKey Open(ActionTarget target, string stage, List<ScanWarning> warnings) { try { return RegistryHelper.OpenSubKey(target, false); } catch (Exception ex) { if (!(ex is System.Security.SecurityException) && !(ex is UnauthorizedAccessException)) throw; warnings.Add(new ScanWarning { Stage = stage, TechnicalLocation = RegistryHelper.NativePath(target), ErrorType = ex.GetType().FullName, Message = "访问被拒绝，已跳过。" }); return null; } }
        private static string[] SafeSubKeys(RegistryKey key) { try { return key.GetSubKeyNames(); } catch { return new string[0]; } }
        private static string[] SafeValues(RegistryKey key) { try { return key.GetValueNames(); } catch { return new string[0]; } }
        private static string[] SafeFiles(string path) { try { return Directory.Exists(path) ? Directory.GetFiles(path) : new string[0]; } catch { return new string[0]; } }
        private static string Read(RegistryKey key, string name) { try { return Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; } }
        private static string ReadChildDefault(string hive, string view, string subKey, List<ScanWarning> warnings) { using (RegistryKey key = Open(Target(hive, view, subKey), "打开方式程序", warnings)) return key == null ? string.Empty : Read(key, ""); }
        private static bool HasValue(RegistryKey key, string name) { return SafeValues(key).Any(delegate(string item) { return string.Equals(item, name, StringComparison.OrdinalIgnoreCase); }); }
        private static string First(params string[] values) { foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value; return string.Empty; }
    }

    internal sealed class SpecialContextMenuMutationService
    {
        private readonly DataStore store;
        public SpecialContextMenuMutationService(DataStore store) { this.store = store; }

        public CleanupBatch SetEnabled(SpecialMenuEntry entry, bool enabled)
        {
            EnsurePermission(entry);
            if (entry.Module.StartsWith("SendTo", StringComparison.Ordinal)) return MoveSendTo(entry, enabled);
            if (entry.Module.StartsWith("ShellNew", StringComparison.Ordinal)) return MoveTree(entry, enabled);
            if (entry.Module == "OpenWith 打开方式") return MoveOpenWith(entry, enabled);
            if (entry.Module == "OpenWith 应用程序") return ToggleValue(entry, enabled, "NoOpenWith");
            if (entry.Module == "GUID 屏蔽") return ToggleValue(entry, enabled, entry.ValueName);
            throw new InvalidOperationException("不支持的专用模块类型。");
        }

        public CleanupBatch Delete(SpecialMenuEntry entry)
        {
            EnsurePermission(entry);
            if (entry.Module.StartsWith("SendTo", StringComparison.Ordinal)) return DeleteFile(entry);
            if (entry.Module.StartsWith("ShellNew", StringComparison.Ordinal)) return DeleteTree(entry);
            if (entry.Module == "OpenWith 打开方式") return string.IsNullOrEmpty(entry.ValueName) ? DeleteTree(entry) : DeleteValue(entry);
            if (entry.Module == "GUID 屏蔽") return DeleteValue(entry);
            throw new InvalidOperationException("该项目只允许启用或禁用，不提供删除。");
        }

        public CleanupBatch AddShellNew(string extension, string template)
        {
            extension = NormalizeExtension(extension);
            ActionTarget target = Target("HKCU", DefaultView(), @"Software\Classes\" + extension + @"\ShellNew");
            SpecialMenuBackup backup = BackupTrees("AddShellNew", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (string.IsNullOrWhiteSpace(template)) key.SetValue("NullFile", string.Empty, RegistryValueKind.String); else key.SetValue("FileName", template.Trim(), RegistryValueKind.String);
                }
                return Complete(id, batchPath, backupPath, target, extension, "ShellNew 新建菜单", "AddShellNew");
            }
            catch { RestoreBackup(backup); throw; }
        }

        public CleanupBatch AddOpenWith(string extension, string progId)
        {
            extension = NormalizeExtension(extension);
            if (string.IsNullOrWhiteSpace(progId)) throw new ArgumentException("ProgID 不能为空。");
            ActionTarget target = Target("HKCU", DefaultView(), @"Software\Classes\" + extension + @"\OpenWithProgids");
            return SetValueWithBackup(target, progId.Trim(), string.Empty, extension + " → " + progId.Trim(), "OpenWith 打开方式", "AddOpenWith");
        }

        public CleanupBatch AddBlockedGuid(string clsid, string description)
        {
            Guid guid;
            if (!Guid.TryParse(clsid, out guid)) throw new ArgumentException("请输入有效的 GUID/CLSID。");
            string normalized = "{" + guid.ToString().ToUpperInvariant() + "}";
            ActionTarget target = Target("HKCU", DefaultView(), SpecialMenuInventoryService.BlockedPath);
            return SetValueWithBackup(target, normalized, description ?? string.Empty, normalized, "GUID 屏蔽", "AddBlockedGuid");
        }

        public CleanupBatch AddSendTo(string name, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("名称和目标路径不能为空。");
            string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            Directory.CreateDirectory(sendTo);
            string file = Path.Combine(sendTo, SafeFileName(name) + ".lnk");
            if (File.Exists(file)) throw new InvalidOperationException("同名发送到项目已经存在。");
            SpecialMenuBackup backup = new SpecialMenuBackup { Mode = "AddSendTo", Trees = new List<ContextMenuTreeBackup>(), Values = new List<ContextMenuToggleBackup>(), OriginalFile = file, OriginalFileExisted = false };
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("系统没有提供 WScript.Shell，无法创建快捷方式。");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(file);
                shortcut.TargetPath = Environment.ExpandEnvironmentVariables(targetPath.Trim());
                shortcut.Save();
                if (!File.Exists(file)) throw new InvalidOperationException("创建快捷方式后复核失败。");
                return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", FilePath = file }, name, "SendTo 发送到", "AddSendTo");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch MoveTree(SpecialMenuEntry entry, bool enabled)
        {
            string parent = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\'));
            string activePath = parent + @"\ShellNew";
            string disabledPath = parent + "\\" + SpecialMenuInventoryService.DisabledShellNewName;
            ActionTarget active = Target(entry.Hive, entry.View, activePath); ActionTarget disabled = Target(entry.Hive, entry.View, disabledPath);
            SpecialMenuBackup backup = BackupTrees("ToggleShellNew", active, disabled);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { MoveRegistryTree(enabled ? disabled : active, enabled ? active : disabled); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = activePath }, entry.Name, entry.Module, enabled ? "EnableShellNew" : "DisableShellNew"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch MoveOpenWith(SpecialMenuEntry entry, bool enabled)
        {
            if (string.IsNullOrEmpty(entry.ValueName))
            {
                string listRoot = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\'));
                string extensionRoot = listRoot.Substring(0, listRoot.LastIndexOf('\\'));
                string application = entry.SubKey.Substring(entry.SubKey.LastIndexOf('\\') + 1);
                ActionTarget activeTree = Target(entry.Hive, entry.View, extensionRoot + @"\OpenWithList\" + application);
                ActionTarget disabledTree = Target(entry.Hive, entry.View, extensionRoot + "\\" + SpecialMenuInventoryService.DisabledOpenWithListName + "\\" + application);
                SpecialMenuBackup treeBackup = BackupTrees("ToggleOpenWithListKey", activeTree, disabledTree);
                string treeBackupPath; string treeId; string treeBatchPath; SaveBackup(treeBackup, out treeBackupPath, out treeId, out treeBatchPath);
                try { MoveRegistryTree(enabled ? disabledTree : activeTree, enabled ? activeTree : disabledTree); return Complete(treeId, treeBatchPath, treeBackupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = activeTree.SubKey }, entry.Name, entry.Module, enabled ? "EnableOpenWith" : "DisableOpenWith"); }
                catch { RestoreBackup(treeBackup); throw; }
            }
            string parent = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\'));
            bool listMode = entry.SubKey.IndexOf("OpenWithList", StringComparison.OrdinalIgnoreCase) >= 0;
            ActionTarget active = Target(entry.Hive, entry.View, parent + (listMode ? @"\OpenWithList" : @"\OpenWithProgids"));
            ActionTarget disabled = Target(entry.Hive, entry.View, parent + "\\" + (listMode ? SpecialMenuInventoryService.DisabledOpenWithListName : SpecialMenuInventoryService.DisabledOpenWithName));
            SpecialMenuBackup backup = listMode
                ? BackupValues("ToggleOpenWith", ValueTarget(active, entry.ValueName), ValueTarget(disabled, entry.ValueName), ValueTarget(Target(entry.Hive, entry.View, active.SubKey), "MRUList"), ValueTarget(Target(entry.Hive, entry.View, disabled.SubKey), "MRUList"))
                : BackupValues("ToggleOpenWith", ValueTarget(active, entry.ValueName), ValueTarget(disabled, entry.ValueName));
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                ActionTarget source = enabled ? disabled : active; ActionTarget destination = enabled ? active : disabled;
                object value = ReadRegistryValue(source, entry.ValueName);
                WriteRegistryValue(destination, entry.ValueName, value ?? string.Empty);
                DeleteRegistryValue(source, entry.ValueName);
                if (listMode) { UpdateMru(source, entry.ValueName, false); UpdateMru(destination, entry.ValueName, true); }
                return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = active.SubKey, ValueName = entry.ValueName }, entry.Name, entry.Module, enabled ? "EnableOpenWith" : "DisableOpenWith");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleValue(SpecialMenuEntry entry, bool enabled, string valueName)
        {
            ActionTarget target = ValueTarget(Target(entry.Hive, entry.View, entry.SubKey), valueName);
            SpecialMenuBackup backup = BackupValues("ToggleValue", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                bool removeValue = entry.Module == "GUID 屏蔽" ? enabled : enabled;
                if (removeValue) DeleteRegistryValue(target, valueName); else WriteRegistryValue(target, valueName, entry.Module == "GUID 屏蔽" ? "由流氓软件克星屏蔽" : string.Empty);
                return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey, ValueName = valueName }, entry.Name, entry.Module, enabled ? "EnableSpecial" : "DisableSpecial");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch MoveSendTo(SpecialMenuEntry entry, bool enabled)
        {
            string activeDir = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            string disabledDir = SpecialMenuInventoryService.DisabledSendToDirectory(store);
            Directory.CreateDirectory(activeDir); Directory.CreateDirectory(disabledDir);
            string destination = Path.Combine(enabled ? activeDir : disabledDir, Path.GetFileName(entry.FilePath));
            if (File.Exists(destination)) throw new InvalidOperationException("目标位置已有同名文件。");
            SpecialMenuBackup backup = FileBackup("ToggleSendTo", entry.FilePath, destination);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { File.Move(entry.FilePath, destination); if (!File.Exists(destination)) throw new InvalidOperationException("移动后复核失败。"); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", FilePath = destination }, entry.Name, entry.Module, enabled ? "EnableSendTo" : "DisableSendTo"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteFile(SpecialMenuEntry entry)
        {
            string id = NewBatchId();
            string batchPath = Path.Combine(store.Backups, id);
            string filesDir = Path.Combine(batchPath, "files");
            Directory.CreateDirectory(filesDir);
            string backupFile = Path.Combine(filesDir, Path.GetFileName(entry.FilePath));
            SpecialMenuBackup backup = FileBackup("DeleteSendTo", entry.FilePath, backupFile);
            string backupPath = Path.Combine(batchPath, "special-menu.json");
            CleanerEngine.WriteJson(backupPath, backup);
            try { File.Move(entry.FilePath, backupFile); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", FilePath = entry.FilePath }, entry.Name, entry.Module, "DeleteSendTo"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteTree(SpecialMenuEntry entry)
        {
            ActionTarget target = Target(entry.Hive, entry.View, entry.SubKey); SpecialMenuBackup backup = BackupTrees("DeleteTree", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) root.DeleteSubKeyTree(target.SubKey, false); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = target.Hive, View = target.View, SubKey = target.SubKey }, entry.Name, entry.Module, "DeleteSpecialTree"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteValue(SpecialMenuEntry entry)
        {
            ActionTarget target = ValueTarget(Target(entry.Hive, entry.View, entry.SubKey), entry.ValueName); SpecialMenuBackup backup = BackupValues("DeleteValue", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { DeleteRegistryValue(target, entry.ValueName); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = target.Hive, View = target.View, SubKey = target.SubKey, ValueName = target.ValueName }, entry.Name, entry.Module, "DeleteSpecialValue"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch SetValueWithBackup(ActionTarget keyTarget, string valueName, string value, string title, string category, string action)
        {
            ActionTarget target = ValueTarget(keyTarget, valueName); SpecialMenuBackup backup = BackupValues(action, target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { WriteRegistryValue(target, valueName, value); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = target.Hive, View = target.View, SubKey = target.SubKey, ValueName = valueName }, title, category, action); }
            catch { RestoreBackup(backup); throw; }
        }

        private SpecialMenuBackup BackupTrees(string mode, params ActionTarget[] targets) { return new SpecialMenuBackup { Mode = mode, Trees = targets.Select(ContextMenuMutationService.CaptureTree).ToList(), Values = new List<ContextMenuToggleBackup>() }; }
        private SpecialMenuBackup BackupValues(string mode, params ActionTarget[] targets) { return new SpecialMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = targets.Select(delegate(ActionTarget target) { return ContextMenuMutationService.CaptureValue(target, mode); }).ToList() }; }
        private static SpecialMenuBackup FileBackup(string mode, string original, string changed) { return new SpecialMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = new List<ContextMenuToggleBackup>(), OriginalFile = original, ChangedFile = changed, OriginalFileExisted = File.Exists(original), ChangedFileExisted = File.Exists(changed) }; }

        private void SaveBackup(SpecialMenuBackup backup, out string backupPath, out string id, out string batchPath)
        {
            id = NewBatchId(); batchPath = Path.Combine(store.Backups, id); Directory.CreateDirectory(batchPath); backupPath = Path.Combine(batchPath, "special-menu.json"); CleanerEngine.WriteJson(backupPath, backup);
        }

        private CleanupBatch Complete(string id, string batchPath, string backupPath, ActionTarget target, string title, string category, string action)
        {
            if (string.IsNullOrWhiteSpace(target.Kind)) target.Kind = "RestoreSpecialMenu";
            CleanupResult result = new CleanupResult { Id = 1, Title = title, Vendor = "右键管理", Category = category, ActionKind = action, TechnicalLocation = string.IsNullOrWhiteSpace(target.SubKey) ? target.FilePath : RegistryHelper.NativePath(target), Status = "Done", Message = "专用菜单配置已修改。", Backup = backupPath, Target = target };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } }; CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch); return batch;
        }

        public static bool Restore(string backupPath)
        {
            SpecialMenuBackup backup = JsonSerializer.Deserialize<SpecialMenuBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreBackup(backup);
        }

        private static bool RestoreBackup(SpecialMenuBackup backup)
        {
            if (backup == null) return false;
            bool ok = true;
            foreach (ContextMenuTreeBackup tree in backup.Trees ?? new List<ContextMenuTreeBackup>()) ok = ContextMenuMutationService.RestoreTreeSnapshot(tree) && ok;
            foreach (ContextMenuToggleBackup value in backup.Values ?? new List<ContextMenuToggleBackup>()) ok = ContextMenuMutationService.RestoreValueSnapshot(value) && ok;
            if (!string.IsNullOrWhiteSpace(backup.OriginalFile) || !string.IsNullOrWhiteSpace(backup.ChangedFile))
            {
                try
                {
                    if (backup.OriginalFileExisted && !File.Exists(backup.OriginalFile))
                    {
                        string source = File.Exists(backup.ChangedFile) ? backup.ChangedFile : null;
                        if (source != null) { Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalFile)); File.Move(source, backup.OriginalFile); }
                    }
                    if (!string.IsNullOrWhiteSpace(backup.OriginalFile) && File.Exists(backup.OriginalFile) && !backup.OriginalFileExisted) File.Delete(backup.OriginalFile);
                    if (!string.IsNullOrWhiteSpace(backup.ChangedFile) && File.Exists(backup.ChangedFile) && !backup.ChangedFileExisted) File.Delete(backup.ChangedFile);
                    ok = File.Exists(backup.OriginalFile) == backup.OriginalFileExisted && File.Exists(backup.ChangedFile) == backup.ChangedFileExisted && ok;
                }
                catch { ok = false; }
            }
            return ok;
        }

        private static void MoveRegistryTree(ActionTarget source, ActionTarget destination)
        {
            ContextMenuTreeBackup snapshot = ContextMenuMutationService.CaptureTree(source);
            if (!snapshot.KeyExisted) throw new InvalidOperationException("源注册表项不存在。");
            ContextMenuTreeBackup destinationSnapshot = new ContextMenuTreeBackup { Target = destination, KeyExisted = true, Snapshot = snapshot.Snapshot };
            ContextMenuMutationService.RestoreTreeSnapshot(destinationSnapshot);
            using (RegistryKey root = RegistryHelper.OpenBase(source.Hive, source.View, true)) root.DeleteSubKeyTree(source.SubKey, false);
        }

        private static object ReadRegistryValue(ActionTarget target, string name) { using (RegistryKey key = RegistryHelper.OpenSubKey(target, false)) return key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); }
        private static void WriteRegistryValue(ActionTarget target, string name, object value) { using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) key.SetValue(name, value ?? string.Empty); }
        private static void DeleteRegistryValue(ActionTarget target, string name) { using (RegistryKey key = RegistryHelper.OpenSubKey(target, true)) if (key != null) key.DeleteValue(name, false); }
        private static void UpdateMru(ActionTarget target, string token, bool add)
        {
            object raw = ReadRegistryValue(target, "MRUList");
            string current = Convert.ToString(raw) ?? string.Empty;
            current = current.Replace(token, string.Empty);
            if (add) current = token + current;
            if (current.Length == 0) DeleteRegistryValue(target, "MRUList"); else WriteRegistryValue(target, "MRUList", current);
        }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static ActionTarget ValueTarget(ActionTarget target, string name) { target.ValueName = name; return target; }
        private static string DefaultView() { return Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; }
        private static string NormalizeExtension(string value) { string extension = (value ?? string.Empty).Trim(); if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension; if (extension.Length < 2 || extension.Length > 24 || extension.IndexOfAny(new char[] { '\\', '/', ' ', ':' }) >= 0) throw new ArgumentException("文件扩展名无效。"); return extension; }
        private static string SafeFileName(string value) { string name = value.Trim(); foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name; }
        private static string NewBatchId() { return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }
        private static void EnsurePermission(SpecialMenuEntry entry) { if (entry == null) throw new ArgumentNullException("entry"); if (entry.ReadOnly) throw new InvalidOperationException("该项目为只读。"); if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目需要管理员权限。"); }
    }

}
