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
using System.Management;
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

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkComObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    internal sealed class ScannerEngine
    {
        private readonly object warningGate = new object();
        private readonly List<ScanWarning> warnings = new List<ScanWarning>();
        private readonly HashSet<string> warningKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public List<ScanWarning> Warnings
        {
            get
            {
                lock (warningGate) return new List<ScanWarning>(warnings);
            }
        }

        private static readonly string[] ContextRoots = new string[]
        {
            @"Software\Classes\*\shell",
            @"Software\Classes\*\shellex\ContextMenuHandlers",
            @"Software\Classes\AllFilesystemObjects\shell",
            @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers",
            @"Software\Classes\Directory\shell",
            @"Software\Classes\Directory\shellex\ContextMenuHandlers",
            @"Software\Classes\Directory\Background\shell",
            @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers",
            @"Software\Classes\Drive\shell",
            @"Software\Classes\Drive\shellex\ContextMenuHandlers",
            @"Software\Classes\Drive\shellex\DragDropHandlers",
            @"Software\Classes\Folder\shell",
            @"Software\Classes\Folder\shellex\ContextMenuHandlers",
            @"Software\Classes\Folder\shellex\DragDropHandlers",
            @"Software\Classes\DesktopBackground\shell",
            @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers",
            @"Software\Classes\lnkfile\shell",
            @"Software\Classes\lnkfile\shellex\ContextMenuHandlers",
            @"Software\Classes\exefile\shell",
            @"Software\Classes\exefile\shellex\ContextMenuHandlers",
            @"Software\Classes\Unknown\shell",
            @"Software\Classes\SystemFileAssociations\image\shell",
            @"Software\Classes\SystemFileAssociations\image\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\video\shell",
            @"Software\Classes\SystemFileAssociations\video\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\audio\shell",
            @"Software\Classes\SystemFileAssociations\audio\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\text\shell",
            @"Software\Classes\SystemFileAssociations\text\shellex\ContextMenuHandlers",
            @"Software\Classes\CompressedFolder\shell",
            @"Software\Classes\CompressedFolder\shellex\ContextMenuHandlers"
        };

        private static readonly string[] StartupRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        private static readonly string[] BrowserRoots = new string[]
        {
            @"Software\Google\Chrome\Extensions",
            @"Software\Microsoft\Edge\Extensions",
            @"Software\Google\Chrome\NativeMessagingHosts",
            @"Software\Microsoft\Edge\NativeMessagingHosts",
            @"Software\Mozilla\NativeMessagingHosts",
            @"Software\Policies\Google\Chrome\ExtensionInstallForcelist",
            @"Software\Policies\Microsoft\Edge\ExtensionInstallForcelist",
            @"Software\Policies\Google\Chrome\ExtensionSettings",
            @"Software\Policies\Microsoft\Edge\ExtensionSettings"
        };

        private static readonly string[] InstalledRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        private static readonly string[] ExplorerNamespaceRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\NetworkNeighborhood\NameSpace"
        };

        private static readonly string[] ExplorerNamespaceClsidRoots = new string[]
        {
            @"Software\Classes\CLSID"
        };

        private static readonly string[] FileExtensions = new string[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".psd", ".ico",
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".mp3", ".flac", ".wav",
            ".zip", ".rar", ".7z", ".torrent", ".xlb", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
        };

        public List<Finding> ScanAll(IProgressSink sink)
        {
            lock (warningGate)
            {
                warnings.Clear();
                warningKeys.Clear();
            }
            List<Finding> all = new List<Finding>();
            object gate = new object();
            List<Action> scanners = new List<Action>();

            scanners.Add(delegate { RunScanner(all, gate, sink, "右键菜单", ScanContextMenus); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "此电脑入口", ScanExplorerNamespaces); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "网盘虚拟盘", ScanCloudVirtualDrives); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "开机启动", ScanStartupRegistry); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "启动文件夹", ScanStartupFolders); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "后台服务", ScanServices); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "浏览器插件", ScanBrowserExtensions); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "文件关联", ScanFileAssociations); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "计划任务", ScanScheduledTasks); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "隐藏卸载入口", ScanHiddenInstalledComponents); });
            scanners.Add(delegate { RunScanner(all, gate, sink, "正在运行的弹窗/守护", ScanRunningAdAndGuardProcesses); });

            Parallel.Invoke(new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Math.Min(4, Environment.ProcessorCount))
            }, scanners.ToArray());

            List<Finding> sorted = all
                .GroupBy(delegate(Finding f) { return f.Category + "|" + f.TechnicalLocation + "|" + f.UserVisibleName; })
                .Select(delegate(IGrouping<string, Finding> g) { return g.First(); })
                .OrderBy(delegate(Finding f) { return RiskRank(f.Risk); })
                .ThenBy(delegate(Finding f) { return f.Vendor; })
                .ThenBy(delegate(Finding f) { return f.Category; })
                .ThenBy(delegate(Finding f) { return f.UserVisibleName; })
                .ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].Id = i + 1;
            return sorted;
        }

        private void RunScanner(List<Finding> all, object gate, IProgressSink sink, string stage, Func<List<Finding>> scanner)
        {
            try
            {
                AddRange(all, gate, sink, stage, scanner());
            }
            catch (SecurityException ex)
            {
                RecordWarning(stage, null, ex);
                if (sink != null) sink.Stage("扫描：" + stage + "，部分受保护位置无法读取，已继续");
            }
            catch (UnauthorizedAccessException ex)
            {
                RecordWarning(stage, null, ex);
                if (sink != null) sink.Stage("扫描：" + stage + "，部分受保护位置无法读取，已继续");
            }
        }

        private RegistryKey OpenForScan(ActionTarget target, string stage)
        {
            try
            {
                return RegistryHelper.OpenSubKey(target, false);
            }
            catch (SecurityException ex)
            {
                RecordWarning(stage, target, ex);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                RecordWarning(stage, target, ex);
                return null;
            }
        }

        private void RecordWarning(string stage, ActionTarget target, Exception ex)
        {
            string location = target == null ? "未定位到具体子项" : RegistryHelper.NativePath(target) + (string.IsNullOrWhiteSpace(target.View) || target.View == "Default" ? string.Empty : " (" + target.View + ")");
            string key = stage + "|" + location + "|" + ex.GetType().FullName;
            lock (warningGate)
            {
                if (!warningKeys.Add(key)) return;
                warnings.Add(new ScanWarning
                {
                    Stage = stage,
                    TechnicalLocation = location,
                    ErrorType = ex.GetType().FullName,
                    Message = ex is SecurityException || ex is UnauthorizedAccessException
                        ? "访问被系统拒绝，已跳过该位置并继续扫描。"
                        : "读取该位置时发生异常，已跳过并继续扫描：" + ex.Message
                });
            }
        }

        private static void AddRange(List<Finding> all, object gate, IProgressSink sink, string stage, List<Finding> findings)
        {
            if (sink != null) sink.Stage("扫描：" + stage + "，发现 " + findings.Count + " 项");
            lock (gate)
            {
                foreach (Finding finding in findings)
                {
                    all.Add(finding);
                    if (sink != null) sink.Finding(finding);
                }
            }
        }

        private List<Finding> ScanContextMenus()
        {
            List<Finding> list = new List<Finding>();
            HashSet<string> actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DataStore store = DataStore.CreateDefault();
            ContextMenuInventory inventory = new ContextMenuDiscoveryService(store).Enumerate(false);
            MergeContextMenuWarnings(inventory.Warnings);
            foreach (ContextMenuEntry entry in inventory.Entries)
            {
                if (entry == null) continue;
                bool extension = string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
                string clsidText = extension ? ResolveClsidRegistration(entry.Clsid) : string.Empty;
                string text = Join(entry.Name, entry.RawName, entry.DeclaredVendor, entry.Command, entry.Icon, entry.Clsid, clsidText, entry.Scene, entry.Scope, entry.SubKey);
                VendorEvidence evidence = new VendorEvidence().AddPublisher(entry.DeclaredVendor).AddHuman(entry.Name, entry.RawName)
                    .AddTechnical(entry.Clsid, clsidText).AddCommand(entry.Command, clsidText).AddFile(entry.Icon, entry.Command)
                    .AddOpaque(entry.Scene, entry.Scope, entry.SubKey);
                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                ContextMenuDiagnosisDisposition disposition = ContextMenuDiagnosisPolicy.Classify(entry, identity);
                if (disposition == ContextMenuDiagnosisDisposition.Ignore) continue;
                bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                string actionKey = extension && !string.IsNullOrWhiteSpace(entry.Clsid)
                    ? entry.Hive + "|" + entry.View + "|" + entry.Clsid
                    : entry.Id;
                if (!actions.Add(actionKey)) continue;

                ActionTarget target = new ActionTarget
                {
                    Hive = entry.Hive,
                    View = entry.View,
                    SubKey = entry.SubKey,
                    IconValue = entry.Icon,
                    PresentationCommand = entry.Command,
                    Clsid = entry.Clsid,
                    SourceSubKey = entry.SubKey
                };
                string title = string.IsNullOrWhiteSpace(entry.Name) ? "第三方软件右键插件" : entry.Name;
                if (disposition == ContextMenuDiagnosisDisposition.Governed)
                {
                    target.Kind = "ReportOnly";
                    Finding governed = NewFinding("已治理的右键插件", title, "这个右键插件仍有注册信息，但当前已经禁用。软件更新或重装后如果重新启用，下次扫描会再次列为可处理项。", target, text, 4, identity, badComponent);
                    governed.Status = "已治理";
                    governed.Risk = "低";
                    list.Add(governed);
                    continue;
                }
                if (disposition == ContextMenuDiagnosisDisposition.ReportOnly)
                {
                    target.Kind = "ReportOnly";
                    Finding readOnly = NewFinding("右键插件边界待确认", title, "检测到第三方右键插件，但缺少可安全禁用的组件编号。只提示，不删除注册信息。", target, text, 5, identity, badComponent);
                    readOnly.Risk = "低";
                    list.Add(readOnly);
                    continue;
                }
                if (disposition == ContextMenuDiagnosisDisposition.SystemProtected)
                {
                    target.Kind = "ReportOnly";
                    Finding protectedSystem = NewFinding("系统右键命令（保护）", title, "这是 Windows 系统自带的右键命令，命令路径指向系统目录。自动清理可能破坏右键菜单或系统设置，只提示不删除。", target, text, 5, identity, badComponent);
                    protectedSystem.Risk = "低";
                    list.Add(protectedSystem);
                    continue;
                }

                if (disposition == ContextMenuDiagnosisDisposition.ActionableExtension)
                {
                    target.Kind = "DisableShellExtension";
                    target.SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
                    target.ValueName = entry.Clsid;
                }
                else
                {
                    target.Kind = "DeleteRegistryKey";
                }
                string impact = "检测到“" + title + "”注入" + entry.Scene + "。只备份并禁用这个右键入口，不卸载“" + identity.Vendor + "”主程序；软件更新后若重新写回，下次扫描会再次发现。";
                list.Add(NewFinding("第三方右键插件", title, impact, target, text, 18, identity, badComponent));
            }
            return list;
        }

        private void MergeContextMenuWarnings(IEnumerable<ScanWarning> source)
        {
            if (source == null) return;
            lock (warningGate)
            {
                foreach (ScanWarning warning in source)
                {
                    if (warning == null) continue;
                    string key = "右键菜单|" + warning.TechnicalLocation + "|" + warning.ErrorType;
                    if (warningKeys.Add(key)) warnings.Add(warning);
                }
            }
        }

        private List<Finding> ScanStartupRegistry()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(StartupRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "开机启动"))
                {
                    if (key == null) continue;
                    foreach (string valueName in SafeValueNames(key))
                    {
                        string value = Convert.ToString(key.GetValue(valueName, ""));
                        string text = Join(valueName, value, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(valueName).AddCommand(value).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        string title = FriendlyStartupTitle(text, valueName, value, identity.Vendor);
                        list.Add(NewFinding("开机启动", title, "开机后会自动启动：" + title, target, text, 28, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanStartupFolders()
        {
            List<Finding> list = new List<Finding>();
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string shortcut = ResolveShortcutText(file);
                    string text = Join(file, shortcut);
                    VendorEvidence evidence = new VendorEvidence().AddHuman(Path.GetFileNameWithoutExtension(file)).AddCommand(shortcut).AddOpaque(file);
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                    if (!identity.Confirmed) continue;
                    ActionTarget target = new ActionTarget { Kind = "MoveFileToBackup", FilePath = file };
                    list.Add(NewFinding("启动文件夹", Path.GetFileName(file), "开机后会从启动文件夹拉起：" + Join(Path.GetFileName(file), shortcut), target, text, 28, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                }
            }
            return list;
        }

        private List<Finding> ScanBrowserExtensions()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(BrowserRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "浏览器插件"))
                {
                    if (key == null) continue;
                    foreach (string valueName in SafeValueNames(key))
                    {
                        string value = Convert.ToString(key.GetValue(valueName, ""));
                        string text = Join(valueName, value, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(valueName).AddTechnical(valueName).AddCommand(value).AddFile(value).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        string title = FriendlyBrowserTitle(text, valueName, identity.Vendor);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        string childDefault;
                        using (RegistryKey child = OpenForScan(target, "浏览器插件"))
                        {
                            childDefault = ReadString(child, "");
                        }
                        string text = Join(childName, childDefault, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(childName).AddTechnical(childName).AddCommand(childDefault).AddFile(childDefault).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        string title = FriendlyBrowserTitle(text, childName, identity.Vendor);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanCloudVirtualDrives()
        {
            List<Finding> list = new List<Finding>();
            string[] tokens = new string[] { "网盘", "云盘", "netdisk", "cloud", "baidu", "quark", "aliyun", "onedrive", "dropbox", "115" };
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    string label = drive.IsReady ? drive.VolumeLabel : string.Empty;
                    string evidence = Join(drive.Name, label, drive.DriveFormat, drive.DriveType.ToString());
                    bool namedCloud = tokens.Any(delegate(string token) { return evidence.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0; });
                    if (!namedCloud) continue;
                    ActionTarget target = new ActionTarget { Kind = "ReportOnly", FilePath = drive.Name };
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(new VendorEvidence().AddHuman(label).AddTechnical(evidence));
                    Finding finding = NewFinding("网盘虚拟盘（只读诊断）", string.IsNullOrWhiteSpace(label) ? drive.Name : label + "（" + drive.Name + "）", "检测到可能由网盘创建的盘符。仅展示诊断证据，不修改设备、驱动器、盘符或网盘客户端。", target, evidence, 5, identity, false);
                    finding.Status = "仅提示";
                    list.Add(finding);
                }
                catch (Exception ex) { Logger.Error("读取网盘虚拟盘信息失败", ex); }
            }
            return list;
        }

        private List<Finding> ScanExplorerNamespaces()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(ExplorerNamespaceRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "此电脑入口"))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = OpenForScan(target, "此电脑入口"))
                        {
                            string display = ReadString(child, "");
                            string localized = ReadString(child, "LocalizedString");
                            string itemName = ReadString(child, "System.ItemNameDisplay");
                            string targetFolder = ReadString(child, "TargetFolderPath");
                            string clsidText = ResolveClsidRegistration(childName, display, localized, itemName);
                            string text = Join(childName, display, localized, itemName, targetFolder, ReadString(child, "CodexMarker"), clsidText, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(display, localized, itemName).AddTechnical(clsidText)
                                .AddCommand(clsidText).AddFile(targetFolder).AddOpaque(childName, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            string title = FriendlyExplorerNamespaceTitle(target.SubKey, childName, display, localized, itemName, clsidText);
                            list.Add(NewFinding("此电脑/资源管理器入口", title, "会在“此电脑”、资源管理器导航栏或网络位置里显示入口：" + title + "。清理只移除入口注册表，不卸载主程序。", target, text, 22, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                        }
                    }
                }
            }

            foreach (ActionTarget root in RegistryTargets(ExplorerNamespaceClsidRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "此电脑入口"))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget clsidTarget = CopyTarget(root);
                        clsidTarget.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = OpenForScan(clsidTarget, "此电脑入口"))
                        {
                            string pinned = ReadString(child, "System.IsPinnedToNameSpaceTree");
                            if (!IsTruthy(pinned)) continue;
                            string display = ReadString(child, "");
                            string localized = ReadString(child, "LocalizedString");
                            string itemName = ReadString(child, "System.ItemNameDisplay");
                            string infoTip = ReadString(child, "InfoTip");
                            string icon = ReadChildDefault(clsidTarget, "DefaultIcon");
                            string server = Join(ReadChildDefault(clsidTarget, "InprocServer32"), ReadChildDefault(clsidTarget, "LocalServer32"));
                            string targetFolder = ReadChildValue(clsidTarget, @"Instance\InitPropertyBag", "TargetFolderPath");
                            string text = Join(childName, display, localized, itemName, infoTip, pinned, icon, server, targetFolder, ReadString(child, "CodexMarker"), clsidTarget.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(display, localized, itemName, infoTip).AddTechnical(server)
                                .AddCommand(server).AddFile(icon, targetFolder).AddOpaque(childName, pinned, clsidTarget.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            ActionTarget valueTarget = CopyTarget(clsidTarget);
                            valueTarget.Kind = "DeleteRegistryValue";
                            valueTarget.ValueName = "System.IsPinnedToNameSpaceTree";
                            string title = FriendlyExplorerNamespaceTitle(clsidTarget.SubKey, childName, display, localized, itemName, text);
                            list.Add(NewFinding("此电脑/资源管理器入口", title, "会把入口固定到资源管理器导航栏或“此电脑”附近：" + title + "。清理只取消固定入口，不卸载主程序。", valueTarget, text, 18, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                        }
                    }
                }
            }
            AddPackagedContextMenuRisks(list);
            return list;
        }

        private void AddPackagedContextMenuRisks(List<Finding> list)
        {
            try
            {
                DataStore store = DataStore.CreateDefault();
                AdvancedMenuInventory inventory = new AdvancedMenuInventoryService(store).EnumeratePackagedOnly(false);
                foreach (AdvancedMenuEntry entry in inventory.Entries)
                {
                    string text = Join(entry.Name, entry.PackageName, entry.PublisherName, entry.FilePath, entry.ValueName, entry.ItemType, entry.Detail);
                    if ((entry.PublisherName ?? string.Empty).IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    VendorEvidence evidence = new VendorEvidence().AddHuman(entry.Name, entry.PackageName, entry.PublisherName)
                        .AddTechnical(entry.ValueName, entry.ItemType).AddFile(entry.FilePath).AddOpaque(entry.PackageName, entry.Detail);
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                    bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                    bool abnormalBehavior = LooksLikeAdOrGuard(text);
                    // 快速风险扫描不会启动动态标题/组件探针；空路径表示“本轮未解析”，不能当作文件缺失。
                    bool missingComponent = !string.IsNullOrWhiteSpace(entry.FilePath) && !File.Exists(entry.FilePath);
                    if (!badComponent && !abnormalBehavior && !missingComponent) continue;

                    ActionTarget target = new ActionTarget
                    {
                        Kind = identity.Confirmed && !missingComponent ? "DisableShellExtension" : "ReportOnly",
                        Hive = "HKCU",
                        View = entry.View,
                        SubKey = entry.SubKey,
                        ValueName = entry.ValueName,
                        SourceSubKey = "应用包：" + entry.PackageName,
                        FilePath = entry.FilePath,
                        PresentationCommand = entry.FilePath,
                        IconValue = entry.CommandIcon,
                        Clsid = entry.ValueName
                    };
                    string title = (string.IsNullOrWhiteSpace(entry.Name) ? entry.PackageName + " 动态右键扩展" : entry.Name);
                    string reason = missingComponent ? "应用包声明的右键组件文件缺失" : (badComponent ? "命中已知异常组件特征" : "命中弹窗、守护或推广行为特征");
                    Finding finding = NewFinding("Windows 11 右键菜单", title, reason + "。正常的打包右键菜单只在右键管理中显示，不会进入风险结果。", target, text, 16, identity, badComponent);
                    if (!identity.Confirmed || missingComponent) finding.Risk = "低";
                    list.Add(finding);
                }
            }
            catch (Exception ex)
            {
                RecordWarning("Windows 11 右键菜单", null, ex);
            }
        }

        private List<Finding> ScanHiddenInstalledComponents()
        {
            List<Finding> list = new List<Finding>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ActionTarget root in RegistryTargets(InstalledRoots, true, true))
            {
                using (RegistryKey key = OpenForScan(root, "隐藏卸载入口"))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = OpenForScan(target, "隐藏卸载入口"))
                        {
                            if (child == null) continue;
                            string display = ReadString(child, "DisplayName");
                            string publisher = ReadString(child, "Publisher");
                            string installLocation = ReadString(child, "InstallLocation");
                            string displayIcon = ReadString(child, "DisplayIcon");
                            string uninstall = ReadString(child, "UninstallString");
                            string quietUninstall = ReadString(child, "QuietUninstallString");
                            string systemComponent = ReadString(child, "SystemComponent");
                            string noRemove = ReadString(child, "NoRemove");
                            string parentKey = ReadString(child, "ParentKeyName");
                            string releaseType = ReadString(child, "ReleaseType");
                            string text = Join(childName, display, publisher, installLocation, displayIcon, uninstall, quietUninstall, systemComponent, noRemove, parentKey, releaseType, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddPublisher(publisher).AddProduct(display)
                                .AddFile(installLocation, displayIcon).AddCommand(uninstall, quietUninstall).AddMsi(childName, uninstall, quietUninstall)
                                .AddOpaque(childName, systemComponent, noRemove, parentKey, releaseType, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            bool hidden = IsTruthy(systemComponent) ||
                                IsTruthy(noRemove) ||
                                string.IsNullOrWhiteSpace(display) ||
                                string.IsNullOrWhiteSpace(uninstall) ||
                                !string.IsNullOrWhiteSpace(parentKey);
                            string behaviorText = Join(display, publisher, SafePathFileName(installLocation), SafePathFileName(displayIcon));
                            bool adOrGuard = LooksLikeAdOrGuard(behaviorText);
                            bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                            ProductRemovalDisposition disposition = ProductRemovalPolicy.Classify(display, childName, installLocation, displayIcon, uninstall, hidden, adOrGuard, badComponent);
                            if (disposition == ProductRemovalDisposition.Ignore) continue;
                            string name = string.IsNullOrWhiteSpace(display) ? childName : display;
                            string dedupeKey = Join(name, uninstall, installLocation);
                            if (!seen.Add(dedupeKey)) continue;
                            string reason = HiddenInstallReason(display, uninstall, systemComponent, noRemove, parentKey, hidden, adOrGuard, badComponent);
                            if (disposition == ProductRemovalDisposition.TargetIndependentProduct && identity.Confirmed && !identity.Conflicted)
                            {
                                target.Kind = "InvokeUninstaller";
                                target.UninstallCommand = uninstall;
                                target.FilePath = installLocation;
                                target.ExpectedProductName = display;
                                target.ExpectedPublisher = publisher;
                                target.ExpectedUninstallCommand = uninstall;
                                Finding finding = NewFinding("独立附带产品", name, "检测到独立安装的附带产品：" + reason + "。只会打开“" + name + "”自己的卸载器，不会卸载其来源主程序；是否卸载仍由用户确认。", target, text, 16, identity, badComponent);
                                finding.Risk = badComponent || adOrGuard ? "中" : "低";
                                list.Add(finding);
                            }
                            else
                            {
                                target.Kind = "ReportOnly";
                                string vendorNote = identity.Conflicted ? "厂商强证据冲突，" : (!identity.Confirmed ? "厂商身份无法可靠确认，" : string.Empty);
                                Finding finding = NewFinding("组件卸载边界待确认", name, vendorNote + "检测到组件异常线索：" + reason + "，但无法证明它是可独立卸载的附带产品。只提示，不打开主程序卸载器。", target, text, 5, identity, badComponent);
                                finding.Risk = "低";
                                list.Add(finding);
                            }
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanRunningAdAndGuardProcesses()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        string pid = Convert.ToString(process["ProcessId"]);
                        string name = Convert.ToString(process["Name"]);
                        string path = Convert.ToString(process["ExecutablePath"]);
                        string command = Convert.ToString(process["CommandLine"]);
                        string identity = Join(name, path);
                        string text = Join(pid, name, path, command);
                        if (!LooksLikeAdOrGuard(identity)) continue;
                        VendorEvidence evidence = new VendorEvidence().AddHuman(name).AddTechnical(name).AddFile(path).AddCommand(command).AddOpaque(pid);
                        VendorIdentityResult vendorIdentity = RuleCatalog.ResolveIdentity(evidence);
                        ActionTarget target = new ActionTarget { Kind = "ReportOnly", FilePath = Join(name, path, "PID=" + pid) };
                        Finding finding = NewFinding("正在运行/疑似弹窗守护", name, "后台正在运行，像是弹窗、推广、守护或自动恢复组件：" + Join(name, path), target, text, 12, vendorIdentity, RuleCatalog.HasBadComponent(evidence, vendorIdentity));
                        finding.Risk = "低";
                        list.Add(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("扫描运行进程失败", ex);
            }
            return list;
        }

        private List<Finding> ScanFileAssociations()
        {
            List<Finding> list = new List<Finding>();
            foreach (string ext in FileExtensions)
            {
                foreach (ActionTarget extTarget in RegistryTargets(new string[] { @"Software\Classes\" + ext }, true, true))
                {
                    using (RegistryKey extKey = OpenForScan(extTarget, "文件关联"))
                    {
                        if (extKey == null) continue;
                        string defaultProgId = ReadString(extKey, "");
                        if (!string.IsNullOrEmpty(defaultProgId))
                        {
                            ActionTarget classTarget = CopyTarget(extTarget);
                            classTarget.Kind = "DeleteRegistryKey";
                            classTarget.SubKey = @"Software\Classes\" + defaultProgId;
                            using (RegistryKey classKey = OpenForScan(classTarget, "文件关联"))
                            {
                                string command = ReadDefault(classTarget, @"shell\open\command");
                                string text = Join(ext, defaultProgId, command);
                                VendorEvidence evidence = new VendorEvidence().AddTechnical(defaultProgId).AddCommand(command).AddOpaque(ext, classTarget.SubKey);
                                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                                if (classKey != null && identity.Confirmed)
                                {
                                    classTarget.Kind = "ReportOnly";
                                    string title = ext + " 默认打开：" + FriendlyHandler(defaultProgId);
                                    list.Add(NewFinding("文件关联/默认打开程序", title, "双击/打开 " + ext + " 现在会交给：" + FriendlyHandler(defaultProgId) + "。这类属于主打开方式，只提示，不一键改。", classTarget, text, 8, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                                }
                            }
                        }
                        foreach (string sub in new string[] { "OpenWithList", "OpenWithProgids" })
                        {
                            ActionTarget subTarget = CopyTarget(extTarget);
                            subTarget.SubKey = extTarget.SubKey + "\\" + sub;
                            using (RegistryKey subKey = OpenForScan(subTarget, "文件关联"))
                            {
                                if (subKey == null) continue;
                                foreach (string valueName in SafeValueNames(subKey))
                                {
                                    if (string.Equals(valueName, "MRUList", StringComparison.OrdinalIgnoreCase)) continue;
                                    ActionTarget progTarget = CopyTarget(extTarget);
                                    progTarget.SubKey = @"Software\Classes\" + valueName;
                                    string command = ReadDefault(progTarget, @"shell\open\command");
                                    string text = Join(ext, valueName, command, subTarget.SubKey);
                                    VendorEvidence evidence = new VendorEvidence().AddTechnical(valueName).AddCommand(command).AddOpaque(ext, subTarget.SubKey);
                                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                                    if (!identity.Confirmed) continue;
                                    ActionTarget valueTarget = CopyTarget(subTarget);
                                    valueTarget.Kind = "DeleteRegistryValue";
                                    valueTarget.ValueName = valueName;
                                    string title = ext + " 打开方式：" + FriendlyHandler(valueName);
                                    list.Add(NewFinding("文件关联/打开方式", title, "右键“打开方式”里会出现：" + FriendlyHandler(valueName) + "（影响 " + ext + " 文件）", valueTarget, text, 22, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                                }
                            }
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanServices()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,DisplayName,PathName,Description,StartMode FROM Win32_Service"))
                {
                    foreach (ManagementObject svc in searcher.Get())
                    {
                        string name = Convert.ToString(svc["Name"]);
                        string display = Convert.ToString(svc["DisplayName"]);
                        string path = Convert.ToString(svc["PathName"]);
                        string desc = Convert.ToString(svc["Description"]);
                        string mode = Convert.ToString(svc["StartMode"]);
                        if (mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsWindowsNativeService(name, display, path, desc)) continue;
                        string text = Join(name, display, path, desc, mode);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(display, desc).AddTechnical(name).AddCommand(path).AddOpaque(mode);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                        if (!ProductRemovalPolicy.IsAbnormalPersistence(name, path, badComponent)) continue;
                        ActionTarget target = new ActionTarget { Kind = "DisableService", ServiceName = name };
                        string title = FriendlyServiceTitle(text, name, display, identity.Vendor);
                        Finding finding = NewFinding("异常后台服务", title, "这个服务的名称或执行文件明确命中弹窗、广告、守护或自动恢复特征：" + title + "。只禁用服务“" + name + "”，不卸载所属主程序。", target, text, 42, identity, badComponent);
                        finding.RequiresAdmin = true;
                        list.Add(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("扫描服务失败", ex);
            }
            return list;
        }

        private static bool IsWindowsNativeService(string name, string display, string path, string desc)
        {
            string lowerPath = (Environment.ExpandEnvironmentVariables(path ?? string.Empty)).Trim().Trim('"').ToLowerInvariant();
            if (lowerPath.IndexOf(@"\windows\system32\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lowerPath.IndexOf(@"\windows\syswow64\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lowerPath.StartsWith("svchost.exe", StringComparison.OrdinalIgnoreCase)) return true;

            string text = Join(name, display, desc).ToLowerInvariant();
            bool windowsName = text.IndexOf("windows ") >= 0 || text.IndexOf("microsoft ") >= 0 || text.IndexOf("windows ") >= 0;
            bool systemPath = lowerPath.IndexOf(@"\windows\system32\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf(@"\windows\syswow64\", StringComparison.OrdinalIgnoreCase) >= 0;
            return systemPath && windowsName;
        }

        private List<Finding> ScanScheduledTasks()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return list;
                dynamic service = Activator.CreateInstance(serviceType);
                service.Connect();
                ScanTaskFolder(service.GetFolder("\\"), list);
            }
            catch (Exception ex)
            {
                Logger.Error("扫描计划任务失败", ex);
            }
            return list;
        }

        private void ScanTaskFolder(dynamic folder, List<Finding> list)
        {
            foreach (dynamic task in folder.GetTasks(1))
            {
                bool enabled = true;
                try { enabled = Convert.ToBoolean(task.Enabled); } catch { }
                if (!enabled) continue;
                string name = Convert.ToString(task.Name);
                string path = Convert.ToString(task.Path);
                string text = path;
                string description = string.Empty;
                try { description = Convert.ToString(task.Definition.RegistrationInfo.Description); text = Join(text, description); } catch { }
                VendorEvidence evidence = new VendorEvidence().AddHuman(description).AddTechnical(name).AddOpaque(path);
                try
                {
                    foreach (dynamic action in task.Definition.Actions)
                    {
                        try
                        {
                            string actionPath = Convert.ToString(action.Path);
                            string arguments = Convert.ToString(action.Arguments);
                            text = Join(text, actionPath, arguments);
                            evidence.AddFile(actionPath).AddCommand(Join(actionPath, arguments));
                        }
                        catch { }
                    }
                }
                catch { }
                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                if (identity.Confirmed && ProductRemovalPolicy.IsAbnormalPersistence(name, text, badComponent))
                {
                    ActionTarget target = new ActionTarget { Kind = "DisableScheduledTask", TaskName = path };
                    string title = FriendlyTaskTitle(text, name, identity.Vendor);
                    Finding finding = NewFinding("异常计划任务/定时拉起", title, "任务名称或执行文件明确命中弹窗、广告、守护或自动恢复特征：" + title + "。只禁用任务“" + name + "”，不卸载所属主程序。", target, text, 30, identity, badComponent);
                    finding.RequiresAdmin = true;
                    list.Add(finding);
                }
            }
            foreach (dynamic child in folder.GetFolders(0))
            {
                ScanTaskFolder(child, list);
            }
        }

        private Finding NewFinding(string category, string title, string impact, ActionTarget target, string text, int baseScore, VendorIdentityResult identity, bool badComponent)
        {
            int score = baseScore + RuleCatalog.VendorBoost(identity, badComponent);
            Finding finding = new Finding();
            finding.Selected = false;
            bool reportOnly = string.Equals(target.Kind, "ReportOnly", StringComparison.OrdinalIgnoreCase);
            finding.Risk = reportOnly ? "低" : (score >= 80 ? "高" : (score >= 55 ? "中" : "低"));
            finding.Score = reportOnly ? Math.Min(score, 20) : score;
            finding.Vendor = identity == null ? "未知第三方" : identity.Vendor;
            finding.Category = category;
            finding.UserVisibleName = Clean(title);
            finding.UserImpact = impact;
            finding.TechnicalLocation = DescribeTarget(target);
            finding.ActionKind = target.Kind;
            finding.Target = target;
            finding.RequiresAdmin = target.Hive == "HKLM" || target.Kind == "DisableService" || target.Kind == "DisableScheduledTask";
            finding.CanRestore = true;
            finding.Evidence = Join(text, identity == null ? string.Empty : "身份依据：" + identity.EvidenceSummary);
            finding.Status = "待处理";
            return finding;
        }

        private static IEnumerable<ActionTarget> RegistryTargets(string[] subKeys, bool includeHkcu, bool includeHklm)
        {
            foreach (string subKey in subKeys)
            {
                if (includeHkcu) yield return new ActionTarget { Kind = "Registry", Hive = "HKCU", View = "Default", SubKey = subKey };
                if (includeHklm)
                {
                    yield return new ActionTarget { Kind = "Registry", Hive = "HKLM", View = "Registry64", SubKey = subKey };
                    yield return new ActionTarget { Kind = "Registry", Hive = "HKLM", View = "Registry32", SubKey = subKey };
                }
            }
        }

        private static ActionTarget CopyTarget(ActionTarget source)
        {
            return new ActionTarget { Kind = source.Kind, Hive = source.Hive, View = source.View, SubKey = source.SubKey, ValueName = source.ValueName, FilePath = source.FilePath, ServiceName = source.ServiceName, TaskName = source.TaskName, UninstallCommand = source.UninstallCommand, IconValue = source.IconValue, PresentationCommand = source.PresentationCommand, Clsid = source.Clsid, SourceSubKey = source.SourceSubKey, ExpectedProductName = source.ExpectedProductName, ExpectedPublisher = source.ExpectedPublisher, ExpectedUninstallCommand = source.ExpectedUninstallCommand };
        }

        private static bool IsShellExtensionBlocked(string hive, string view, string clsid)
        {
            if (string.IsNullOrWhiteSpace(clsid)) return false;
            ActionTarget target = new ActionTarget { Hive = hive, View = view, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", ValueName = clsid };
            try { return RegistryHelper.ValueExists(target); }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static string FirstClsid(params string[] values)
        {
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                Match match = Regex.Match(value, @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
                if (match.Success) return match.Value;
            }
            return string.Empty;
        }

        private static string[] SafeSubKeyNames(RegistryKey key)
        {
            try { return key.GetSubKeyNames(); } catch { return new string[0]; }
        }

        private static string[] SafeValueNames(RegistryKey key)
        {
            try { return key.GetValueNames(); } catch { return new string[0]; }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            if (key == null) return string.Empty;
            try { return Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; }
        }

        private string ReadDefault(ActionTarget target, string child)
        {
            ActionTarget t = CopyTarget(target);
            t.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = OpenForScan(t, "右键菜单"))
            {
                return ReadString(key, "");
            }
        }

        private string ResolveClsidRegistration(params string[] values)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string value in values)
            {
                foreach (string clsid in ExtractClsids(value))
                {
                    string info = ReadClsidInfo(clsid);
                    if (string.IsNullOrEmpty(info)) continue;
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append(info);
                }
            }
            return sb.ToString();
        }

        private static IEnumerable<string> ExtractClsids(string value)
        {
            if (string.IsNullOrEmpty(value)) yield break;
            int start = 0;
            while (start < value.Length)
            {
                int open = value.IndexOf('{', start);
                if (open < 0) yield break;
                int close = value.IndexOf('}', open + 1);
                if (close < 0) yield break;
                string clsid = value.Substring(open, close - open + 1);
                if (clsid.Length >= 38) yield return clsid;
                start = close + 1;
            }
        }

        private string ReadClsidInfo(string clsid)
        {
            List<string> parts = new List<string>();
            string subKey = @"Software\Classes\CLSID\" + clsid;
            foreach (ActionTarget target in RegistryTargets(new string[] { subKey }, true, true))
            {
                using (RegistryKey key = OpenForScan(target, "CLSID 解析"))
                {
                    if (key == null) continue;
                    parts.Add(ReadString(key, ""));
                    parts.Add(ReadChildDefault(target, "InprocServer32"));
                    parts.Add(ReadChildDefault(target, "LocalServer32"));
                    parts.Add(ReadChildDefault(target, "ProgID"));
                }
            }
            return Join(parts.ToArray());
        }

        private string ReadChildDefault(ActionTarget target, string child)
        {
            ActionTarget childTarget = CopyTarget(target);
            childTarget.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = OpenForScan(childTarget, "注册表子项"))
            {
                return ReadString(key, "");
            }
        }

        private string ReadChildValue(ActionTarget target, string child, string valueName)
        {
            ActionTarget childTarget = CopyTarget(target);
            childTarget.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = OpenForScan(childTarget, "注册表子项"))
            {
                return ReadString(key, valueName);
            }
        }

        internal static string ResolveShortcutText(string file)
        {
            try
            {
                if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                IShellLinkW link = (IShellLinkW)new ShellLinkComObject();
                ((IPersistFile)link).Load(file, 0);
                StringBuilder target = new StringBuilder(1024);
                StringBuilder args = new StringBuilder(1024);
                StringBuilder workingDirectory = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                link.GetArguments(args, args.Capacity);
                link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
                return Join(target.ToString(), args.ToString(), workingDirectory.ToString());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string HiddenInstallReason(string display, string uninstall, string systemComponent, string noRemove, string parentKey, bool hidden, bool adOrGuard, bool badComponent)
        {
            List<string> reasons = new List<string>();
            if (string.IsNullOrWhiteSpace(display)) reasons.Add("没有显示名称");
            if (string.IsNullOrWhiteSpace(uninstall)) reasons.Add("没有卸载命令");
            if (IsTruthy(systemComponent)) reasons.Add("标记为系统组件，控制面板可能隐藏");
            if (IsTruthy(noRemove)) reasons.Add("标记为不可移除");
            if (!string.IsNullOrWhiteSpace(parentKey)) reasons.Add("挂在其它组件下面");
            if (!hidden && adOrGuard) reasons.Add("命中弹窗/守护特征");
            if (!hidden && badComponent) reasons.Add("命中已知捆绑组件特征");
            return reasons.Count == 0 ? "疑似捆绑组件" : string.Join("，", reasons.ToArray());
        }

        private static int RiskRank(string risk)
        {
            if (risk == "高") return 0;
            if (risk == "中") return 1;
            return 2;
        }

        private static string DescribeTarget(ActionTarget target)
        {
            if (target.Kind == "MoveFileToBackup") return target.FilePath;
            if (target.Kind == "DisableService") return "服务：" + target.ServiceName;
            if (target.Kind == "DisableScheduledTask") return "计划任务：" + target.TaskName;
            if (target.Kind == "ReportOnly" && !string.IsNullOrWhiteSpace(target.FilePath)) return target.FilePath;
            if (target.Kind == "ReportOnly" && string.IsNullOrWhiteSpace(target.SubKey)) return "只报告";
            string path = !string.IsNullOrWhiteSpace(target.SourceSubKey) ? (target.Hive == "HKLM" ? "HKLM\\" : "HKCU\\") + target.SourceSubKey : RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) path += "::" + target.ValueName;
            if (!string.IsNullOrEmpty(target.View) && target.View != "Default") path += " (" + target.View + ")";
            return path;
        }

        private static bool LooksLikeAdOrGuard(string text)
        {
            string[] tokens = new string[]
            {
                "popup", "adpopup", "adservice", "adpush", "advert", "hotnews", "newsfeed", "notifycenter", "pushservice", "minipage",
                "watchdog", "daemon", "guardservice", "protectservice", "keeper", "serviceplatform",
                "弹窗", "广告", "热点", "资讯", "推荐", "迷你页", "守护", "保护", "修复", "恢复", "推送"
            };
            foreach (string token in tokens)
            {
                if (ContainsBehaviorToken(text, token)) return true;
            }
            return false;
        }

        private static string SafePathFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }

        private static bool ContainsBehaviorToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool ascii = token.All(delegate(char c) { return c < 128; });
                if (!ascii || token.Length >= 7) return true;
                int end = index + token.Length;
                bool left = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool right = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (left && right) return true;
                start = index + 1;
            }
        }

        private static string FriendlyContextMenuTitle(string subKey, string childName, string display, string mui, string explorerHandler, string commandStateHandler, string clsidText)
        {
            string where = ContextWhereShort(subKey);
            string candidate = FirstHumanText(display, mui);
            if (!string.IsNullOrEmpty(candidate))
            {
                return where + "：会出现“" + candidate + "”";
            }

            string evidence = Join(childName, display, mui, explorerHandler, commandStateHandler, clsidText);
            string feature = FriendlyContextMenuFeature(evidence);
            return where + "：疑似会出现“" + feature + "”";
        }

        private static string FriendlyContextMenuFeature(string evidence)
        {
            string lower = (evidence ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("softmgrext") >= 0) return "360 软件管家右键菜单";
            if (lower.IndexOf("safe360ext") >= 0) return "360 安全/扫描右键菜单";
            if (lower.IndexOf("360ai") >= 0) return "360AI 图片右键菜单";
            if (lower.IndexOf("360alb") >= 0 || lower.IndexOf("albumviewer") >= 0 || lower.IndexOf("ablumviewer") >= 0) return "360 看图右键菜单";
            if (lower.IndexOf("qingshellext") >= 0 || lower.IndexOf("67f4d210-bfc2-4add-9a2a-c9b9e1f42c4f") >= 0) return "上传到 WPS 云文档";
            if (lower.IndexOf("qingnsecontextmenu") >= 0 || lower.IndexOf("aa147ffb-0b1f-4bb1-9b1e-8d062b35c146") >= 0) return "WPS 云文档操作菜单";
            if (lower.IndexOf("kpdf2wordshellext") >= 0) return "WPS PDF 转 Word";
            if (lower.IndexOf("kingsoftofficepdf.contextmenu") >= 0) return "WPS PDF 操作菜单";
            if (lower.IndexOf("knewdocshellext") >= 0) return "新建 WPS 文档菜单";
            if (lower.IndexOf("kwpsshellext") >= 0 || lower.IndexOf("kwpsshell") >= 0) return "WPS Office 文档操作菜单";
            if (lower.IndexOf("qingnse") >= 0) return "WPS 云文档操作菜单";
            if (lower.IndexOf("kdesktop") >= 0 || lower.IndexOf("qkdesktop") >= 0 || lower.IndexOf("wpsdrive") >= 0) return "WPS 云文档/云盘入口";
            if (lower.IndexOf("baidunetdisk") >= 0 || lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("yunshell") >= 0) return "百度网盘右键菜单";
            bool quarkEvidence = lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0 || lower.IndexOf("vt.quark.cn") >= 0 || lower.IndexOf("external_rclick") >= 0;
            if (lower.IndexOf("quarkclouddrive.upload") >= 0 || lower.IndexOf("上传到夸克") >= 0) return "夸克网盘上传右键菜单";
            if (lower.IndexOf("quarkclouddrive.backup") >= 0) return "夸克网盘备份右键菜单";
            if (quarkEvidence && (lower.IndexOf("quarkpdf") >= 0 || lower.IndexOf("quarkconvert") >= 0 || lower.IndexOf("pdf转换") >= 0 || lower.IndexOf("图片转pdf") >= 0 || lower.IndexOf("万能转换") >= 0 || lower.IndexOf("external_rclick") >= 0 || lower.IndexOf("vt.quark.cn") >= 0)) return "夸克 PDF/万能转换右键菜单";
            if (quarkEvidence) return "夸克右键菜单";
            if (lower.IndexOf("sogou") >= 0) return "搜狗右键菜单";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷右键菜单";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0 || lower.IndexOf("钉盘") >= 0) return "钉钉文件上传右键菜单";
            if (lower.IndexOf("bandiview") >= 0 || lower.IndexOf("honeyview") >= 0) return "BandiView/Honeyview 看图右键菜单";
            if (lower.IndexOf("bandizip") >= 0 || lower.IndexOf("bandisoft") >= 0) return "Bandisoft 右键菜单";
            string vendor = ShortVendorName(evidence);
            if (string.IsNullOrWhiteSpace(vendor) || vendor == "第三方软件" || vendor == "未知第三方") return "未识别的右键扩展";
            return vendor + "右键扩展（具体功能未识别）";
        }

        internal static List<string> RunContextMenuNameSelfTests()
        {
            List<string> failures = new List<string>();
            AssertContextMenuName(failures, "Open With qingshellext {67F4D210-BFC2-4ADD-9A2A-C9B9E1F42C4F}", "上传到 WPS 云文档");
            AssertContextMenuName(failures, "QingNseContextMenu {AA147FFB-0B1F-4BB1-9B1E-8D062B35C146}", "WPS 云文档操作菜单");
            AssertContextMenuName(failures, "kwpsshellext", "WPS Office 文档操作菜单");
            AssertContextMenuName(failures, "knewdocshellext", "新建 WPS 文档菜单");
            AssertContextMenuName(failures, "KingsoftOfficePDF.ContextMenu", "WPS PDF 操作菜单");
            AssertContextMenuName(failures, "kpdf2wordshellext", "WPS PDF 转 Word");
            string fallback = FriendlyContextMenuFeature("WPS unknown shell extension");
            if (fallback.IndexOf("相关", StringComparison.OrdinalIgnoreCase) >= 0) failures.Add("右键名称回归：未知 WPS 扩展仍使用‘相关’泛称");
            return failures;
        }

        private static void AssertContextMenuName(List<string> failures, string evidence, string expected)
        {
            string actual = FriendlyContextMenuFeature(evidence);
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) failures.Add("右键名称回归：" + evidence + " 应为‘" + expected + "’，实际为‘" + actual + "’");
        }

        private static string FriendlyStartupTitle(string evidence, string name, string command, string vendor)
        {
            string lower = Join(evidence, name, command).ToLowerInvariant();
            if (lower.IndexOf("360safetray") >= 0) return "360 安全卫士托盘/防护入口";
            if (lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步启动项";
            if (lower.IndexOf("sogou") >= 0 && LooksLikeAdOrGuard(lower)) return "搜狗弹窗/守护启动项";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷开机启动项";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉开机启动项";
            string human = FirstHumanText(name, Path.GetFileNameWithoutExtension(ExtractExecutableName(command)));
            return ShortVendorName(vendor, evidence) + "开机启动：" + (string.IsNullOrEmpty(human) ? "启动项" : human);
        }

        private static string FriendlyBrowserTitle(string evidence, string rawName, string vendor)
        {
            string lower = Join(evidence, rawName).ToLowerInvariant();
            if (lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("wps") >= 0) return "WPS/金山浏览器扩展宿主";
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘浏览器扩展宿主";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克浏览器/网盘外部宿主";
            if (lower.IndexOf("sogou") >= 0) return "搜狗浏览器扩展/策略";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷浏览器下载助手";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉浏览器扩展/外部宿主";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0) return "360 浏览器扩展/策略";
            if (lower.IndexOf("bandisoft") >= 0 || lower.IndexOf("bandiview") >= 0 || lower.IndexOf("bandizip") >= 0) return "Bandisoft 浏览器/外部宿主";
            return ShortVendorName(vendor, evidence) + "浏览器扩展/宿主";
        }

        private static string FriendlyExplorerNamespaceTitle(string subKey, string childName, string display, string localized, string itemName, string evidence)
        {
            string where = ExplorerNamespaceWhereShort(subKey);
            string human = FirstHumanText(display, localized, itemName);
            if (string.IsNullOrWhiteSpace(human)) human = FriendlyExplorerNamespaceFeature(Join(childName, evidence));
            return where + "：会出现“" + human + "”";
        }

        private static string FriendlyExplorerNamespaceFeature(string evidence)
        {
            string lower = (evidence ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("baidunetdisk") >= 0 || lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("yunshell") >= 0) return "百度网盘入口";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘入口";
            if (lower.IndexOf("wps") >= 0 || lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("金山") >= 0) return "WPS/金山云盘入口";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("迅雷") >= 0) return "迅雷云盘/下载入口";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0 || lower.IndexOf("钉盘") >= 0) return "钉钉/钉盘入口";
            if (lower.IndexOf("tencent") >= 0 || lower.IndexOf("qq") >= 0 || lower.IndexOf("腾讯") >= 0) return "腾讯系云盘入口";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0 || lower.IndexOf("奇虎") >= 0) return "360 云盘/同步入口";
            return ShortVendorName(evidence) + "入口";
        }

        private static string ExplorerNamespaceWhereShort(string subKey)
        {
            string lower = (subKey ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf(@"\mycomputer\namespace") >= 0) return "此电脑";
            if (lower.IndexOf(@"\networkneighborhood\namespace") >= 0) return "网络位置";
            if (lower.IndexOf(@"\desktop\namespace") >= 0) return "桌面/导航栏";
            if (lower.IndexOf(@"\classes\clsid\") >= 0) return "资源管理器导航栏";
            return "资源管理器";
        }

        private static string FriendlyServiceTitle(string evidence, string name, string display, string vendor)
        {
            string lower = Join(evidence, name, display).ToLowerInvariant();
            if (lower.IndexOf("q360amppl") >= 0) return "360 安全防护后台服务";
            if (lower.IndexOf("zhudongfangyu") >= 0 || lower.IndexOf("主动防御") >= 0 || lower.IndexOf("qhactivedefense") >= 0) return "360 主动防御后台服务";
            if (lower.IndexOf("baidunetdiskutility") >= 0 || lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步后台服务";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘后台服务";
            if (lower.IndexOf("wps office cloud service") >= 0 || lower.IndexOf("wpscloud") >= 0) return "WPS 云文档后台服务";
            if (lower.IndexOf("sogousvc") >= 0 || lower.IndexOf("sgimeguard") >= 0) return "搜狗输入法守护/更新服务";
            if (lower.IndexOf("xlservice") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷后台/更新服务";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉后台/更新服务";
            string human = FirstHumanText(display, name);
            return ShortVendorName(vendor, evidence) + "后台服务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FriendlyTaskTitle(string evidence, string name, string vendor)
        {
            string lower = Join(evidence, name).ToLowerInvariant();
            if (lower.IndexOf("wpsupdate") >= 0 || lower.IndexOf("wpswake") >= 0) return "WPS 更新/唤醒计划任务";
            if (lower.IndexOf("getword") >= 0 || lower.IndexOf("wordsearch") >= 0 || lower.IndexOf("searchfetch") >= 0) return "360 划词/搜索计划任务";
            if (lower.IndexOf("qihoo") >= 0 || lower.IndexOf("360") >= 0) return "360 定时扫描/拉起计划任务";
            if (lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("baidunetdisk") >= 0) return "百度网盘检测/同步计划任务";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘更新/拉起计划任务";
            if (lower.IndexOf("sogou") >= 0) return "搜狗更新/弹窗计划任务";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷更新/拉起计划任务";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉更新/拉起计划任务";
            string human = FirstHumanText(name);
            return ShortVendorName(vendor, evidence) + "计划任务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FirstHumanText(params string[] values)
        {
            foreach (string value in values)
            {
                string cleaned = Clean(value);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                if (LooksTechnicalName(cleaned)) continue;
                return cleaned;
            }
            return string.Empty;
        }

        private static bool LooksTechnicalName(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower)) return true;
            if (lower.IndexOf("{") >= 0 || lower.IndexOf("}") >= 0) return true;
            if (lower.IndexOf(".dll") >= 0 || lower.IndexOf(".exe") >= 0 || lower.IndexOf("\\") >= 0 || lower.IndexOf("/") >= 0) return true;
            string[] tokens = new string[] { "shellext", "safe360ext", "softmgrext", "contextmenu", "qingshell", "qingnse", "clsid", "com.", "native", "handler", "class" };
            foreach (string token in tokens)
            {
                if (lower.IndexOf(token) >= 0) return true;
            }
            bool hasLetter = false;
            bool hasChinese = false;
            int digits = 0;
            foreach (char c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLetter = true;
                if (c >= 0x4e00 && c <= 0x9fff) hasChinese = true;
                if (char.IsDigit(c)) digits++;
            }
            return hasLetter && !hasChinese && digits >= 3 && value.Length >= 8;
        }

        private static string ShortVendorName(string evidence)
        {
            string vendor = RuleCatalog.ResolveVendor(evidence);
            if (vendor == "未知第三方") return "第三方软件";
            return vendor.Replace(" 系列", string.Empty).Replace(" / ", "/");
        }

        private static string ShortVendorName(string vendor, string evidence)
        {
            if (string.IsNullOrWhiteSpace(vendor) || vendor == "未知第三方") return ShortVendorName(evidence);
            return vendor.Replace(" 系列", string.Empty).Replace(" / ", "/");
        }

        private static string ExtractExecutableName(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            command = Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) return command.Substring(0, exe + 4).Trim('"');
            int split = command.IndexOf(' ');
            return split > 0 ? command.Substring(0, split).Trim('"') : command.Trim('"');
        }

        private static string ContextWhereShort(string subKey)
        {
            string lower = subKey.ToLowerInvariant();
            if (lower.IndexOf("\\desktopbackground\\") >= 0 || lower.IndexOf("\\directory\\background\\") >= 0) return "桌面/文件夹空白处右键";
            if (lower.IndexOf("\\drive\\") >= 0) return "磁盘盘符右键";
            if (lower.IndexOf("\\directory\\") >= 0) return "文件夹右键";
            if (lower.IndexOf("\\lnkfile\\") >= 0) return "快捷方式右键";
            if (lower.IndexOf("\\*\\") >= 0) return "普通文件右键";
            return "资源管理器右键";
        }

        private static string DescribeContextMenu(string subKey, string title)
        {
            return Clean(title);
        }

        private static string FriendlyProgram(string name, string command)
        {
            if (!string.IsNullOrEmpty(name)) return name;
            return command;
        }

        private static string FriendlyHandler(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘";
            if (lower.IndexOf("quarkclouddrive") >= 0 || lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘";
            if (lower.IndexOf("bandiview") >= 0) return "BandiView 看图";
            if (lower.IndexOf("honeyview") >= 0) return "Honeyview 看图";
            if (lower.IndexOf("bandizip") >= 0) return "Bandizip 压缩";
            if (lower.IndexOf("wps.doc") >= 0 || lower.IndexOf("wps.docx") >= 0) return "WPS 文字";
            if (lower.IndexOf("kwps.pdf") >= 0) return "WPS PDF";
            if (lower.IndexOf("wpp.ppt") >= 0) return "WPS 演示";
            if (lower.IndexOf("et.xls") >= 0) return "WPS 表格";
            if (lower.IndexOf("xunlei") >= 0) return "迅雷";
            return value;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Join(params string[] parts)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(part.Trim());
            }
            return sb.ToString();
        }
    }

}
