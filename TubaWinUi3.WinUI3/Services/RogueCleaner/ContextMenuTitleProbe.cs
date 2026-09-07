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

    internal sealed class ContextCommandProbeResult
    {
        public string Title { get; set; }
        public string Icon { get; set; }
        public string Error { get; set; }
        public string Source { get; set; }
    }

    internal static class ContextCommandTitleProbe
    {
        private const int ProbeTimeoutMilliseconds = 1400;
        // 子进程总等待上限：进程冷启动 + 进程内 1.4s COM 探测超时的余量
        private const int ChildProbeTimeoutMilliseconds = 3200;
        // 并发探测子进程上限：避免同一时刻实例化过多右键扩展
        private static readonly SemaphoreSlim ChildProbeGate = new SemaphoreSlim(4, 4);
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, ContextCommandProbeResult> Cache = new Dictionary<string, ContextCommandProbeResult>(StringComparer.OrdinalIgnoreCase);

        public static ContextCommandProbeResult ProbeIsolated(string clsid, string itemType, string componentPath)
        {
            string key = (clsid ?? string.Empty) + "|" + NormalizeItemType(itemType) + "|" + (componentPath ?? string.Empty);
            lock (CacheLock)
            {
                ContextCommandProbeResult cached;
                if (Cache.TryGetValue(key, out cached)) return cached;
            }

            // 探测放在独立子进程（--context-title-probe）中执行：挂死的扩展 COM 调用只会拖垮子进程，
            // 主进程等待超时后直接结束子进程，不泄漏线程、COM 对象与扩展 DLL。
            ContextCommandProbeResult result = ProbeInChildProcess(clsid, itemType, componentPath)
                ?? new ContextCommandProbeResult { Error = "读取动态命令文字超时，已放弃。" };
            lock (CacheLock) Cache[key] = result;
            return result;
        }

        private static ContextCommandProbeResult ProbeInChildProcess(string clsid, string itemType, string componentPath)
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return ProbeFromComponent(componentPath, itemType, "无法启动探测子进程。");
            string outFile = Path.Combine(Path.GetTempPath(), "tuba-ctxprobe-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                ChildProbeGate.Wait();
                try
                {
                    var info = new ProcessStartInfo { FileName = exe, UseShellExecute = false, CreateNoWindow = true };
                    info.ArgumentList.Add("--context-title-probe");
                    info.ArgumentList.Add(clsid ?? string.Empty);
                    info.ArgumentList.Add(itemType ?? string.Empty);
                    info.ArgumentList.Add(componentPath ?? string.Empty);
                    info.ArgumentList.Add("--probe-out");
                    info.ArgumentList.Add(outFile);
                    using (Process process = Process.Start(info))
                    {
                        if (process == null) return ProbeFromComponent(componentPath, itemType, "无法启动探测子进程。");
                        if (!process.WaitForExit(ChildProbeTimeoutMilliseconds))
                        {
                            try { process.Kill(true); } catch { }
                            return null; // 子进程被 COM 拖死：直接放弃，绝不在主进程内等待
                        }
                    }
                    if (!File.Exists(outFile)) return ProbeFromComponent(componentPath, itemType, "探测子进程没有返回结果。");
                    string json = File.ReadAllText(outFile, Encoding.UTF8);
                    ContextCommandProbeResult probe = JsonSerializer.Deserialize<ContextCommandProbeResult>(json);
                    return probe ?? new ContextCommandProbeResult { Error = "探测子进程返回了空结果。" };
                }
                finally { ChildProbeGate.Release(); }
            }
            catch (Exception ex)
            {
                return new ContextCommandProbeResult { Error = "探测失败：" + ex.Message };
            }
            finally { try { File.Delete(outFile); } catch { } }
        }

        // 子进程模式入口（App OnLaunched 的 --context-title-probe 分支调用）：
        // 不查缓存、不再派生子进程，直接进程内探测；探测完成后子进程立即退出，
        // 即使内部超时线程被 COM 拖住也只是后台线程，不会阻止进程退出，无泄漏。
        internal static ContextCommandProbeResult ProbeForChildProcess(string clsid, string itemType, string componentPath)
        {
            return ProbeWithTimeout(clsid, itemType, componentPath);
        }

        // 原版通过 `--context-title-probe` 子进程隔离动态菜单探测（COM 可能长时间不返回）；
        // 此处实现为子进程内部的 STA 线程 + 超时保护，失败降级为组件资源文字提取。
        private static ContextCommandProbeResult ProbeWithTimeout(string clsid, string itemType, string componentPath)
        {
            ContextCommandProbeResult result = null;
            Exception failure = null;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                try { result = ProbeInProcess(clsid, itemType, componentPath); }
                catch (Exception ex) { failure = ex; }
            }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(ProbeTimeoutMilliseconds))
            {
                // 超时后线程继续在后台运行，结果丢弃；后续调用走缓存。
                return new ContextCommandProbeResult { Error = "读取动态命令文字超时，已放弃。" };
            }
            if (failure != null || result == null)
            {
                // COM 创建/调用失败（组件未注册、需要更高权限等）→ 降级为组件资源文字提取
                return ProbeFromComponent(componentPath, itemType, string.Empty);
            }
            return result;
        }

        internal static ContextCommandProbeResult ProbeInProcess(string clsid, string itemType, string componentPath)
        {
            Guid classId;
            if (!Guid.TryParse(clsid, out classId)) return ProbeFromComponent(componentPath, itemType, "组件编号无效。");
            object shellItem = null;
            object shellItemArray = null;
            object instance = null;
            try
            {
                shellItemArray = CreateSampleArray(itemType, out shellItem);
                Type type = Type.GetTypeFromCLSID(classId, false);
                if (type == null) return ProbeFromComponent(componentPath, itemType, "组件未注册。");
                instance = Activator.CreateInstance(type);
                IExplorerCommand command = instance as IExplorerCommand;
                if (command == null) return ProbeFromComponent(componentPath, itemType, "组件不支持读取动态命令文字。");

                List<string> titles = new List<string>();
                string icon = string.Empty;
                AddCommand(command, shellItemArray, titles, ref icon, 0);
                string title = SelectTitle(titles);
                ContextCommandProbeResult result = new ContextCommandProbeResult
                {
                    Title = TranslateTitle(title),
                    Icon = icon,
                    Error = string.IsNullOrWhiteSpace(title) ? "组件没有返回可显示的命令文字。" : string.Empty,
                    Source = string.IsNullOrWhiteSpace(title) ? string.Empty : "动态接口"
                };
                return string.IsNullOrWhiteSpace(result.Title) ? ProbeFromComponent(componentPath, itemType, result.Error) : result;
            }
            finally
            {
                ReleaseCom(instance);
                ReleaseCom(shellItemArray);
                ReleaseCom(shellItem);
            }
        }

        internal static string SelectTitle(IEnumerable<string> candidates)
        {
            List<string> titles = (candidates ?? Enumerable.Empty<string>())
                .Select(CleanTitle)
                .Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (titles.Count == 0) return string.Empty;
            if (titles.Count == 1) return titles[0];

            // 根命令通常只是软件名；真正可操作的子命令更接近资源管理器里用户看到的文字。
            List<string> actionable = titles.Skip(1).Where(delegate(string value) { return value.Length <= 80; }).ToList();
            if (actionable.Count == 1) return actionable[0];
            if (actionable.Count > 1) return string.Join(" / ", actionable.Take(3).ToArray());
            return titles[0];
        }

        private static ContextCommandProbeResult ProbeFromComponent(string componentPath, string itemType, string previousError)
        {
            if (string.IsNullOrWhiteSpace(componentPath) || !File.Exists(componentPath)) return new ContextCommandProbeResult { Error = previousError };
            try
            {
                byte[] bytes = File.ReadAllBytes(componentPath);
                HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectStrings(Encoding.Unicode.GetString(bytes), candidates);
                CollectStrings(Encoding.ASCII.GetString(bytes), candidates);
                string title = string.Empty;
                int bestScore = 0;
                foreach (string candidate in candidates)
                {
                    int score = RankCandidate(candidate, itemType);
                    if (score > bestScore || (score == bestScore && score > 0 && (string.IsNullOrEmpty(title) || candidate.Length < title.Length)))
                    {
                        title = candidate;
                        bestScore = score;
                    }
                }
                if (string.IsNullOrWhiteSpace(title) || bestScore < 16) return new ContextCommandProbeResult { Error = "未能读取动态命令文字。" };
                return new ContextCommandProbeResult { Title = TranslateTitle(CleanTitle(title)), Icon = string.Empty, Source = "组件文字资源", Error = string.Empty };
            }
            catch (Exception) { return new ContextCommandProbeResult { Error = "未能读取动态命令文字。" }; }
        }

        private static void CollectStrings(string text, HashSet<string> candidates)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match match in Regex.Matches(text, "[\\u0020-\\u007E\\u4E00-\\u9FFF]{4,96}"))
            {
                string value = match.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value)) candidates.Add(value);
            }
        }

        private static int RankCandidate(string value, string itemType)
        {
            string text = CleanTitle(value);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 4 || text.Length > 80) return -1;
            string lower = text.ToLowerInvariant();
            if (lower.IndexOf("%s", StringComparison.Ordinal) >= 0 || lower.IndexOf("\\", StringComparison.Ordinal) >= 0 || lower.IndexOf(".dll", StringComparison.Ordinal) >= 0 || lower.IndexOf(".exe", StringComparison.Ordinal) >= 0 || lower.IndexOf("copyright", StringComparison.Ordinal) >= 0 || lower.IndexOf("software\\", StringComparison.Ordinal) >= 0) return -1;
            string[] errorPhrases = { "failed to", "unable to", "cannot ", "can't ", "could not", "not found", "invalid ", "exception", "error", "doesn't", "does not", "not support", "unsupported", "this object", "synchroniz", "失败", "无法", "不能", "找不到", "错误", "异常" };
            foreach (string phrase in errorPhrases) if (lower.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0) return -1;
            if (text.StartsWith(":", StringComparison.Ordinal) || text.StartsWith("!", StringComparison.Ordinal)) return -1;
            if (text.IndexOf(' ') < 0 && text.All(delegate(char character) { return character < 128; })) return -1;
            string[] actions = { "open", "compare", "select", "edit", "scan", "share", "upload", "extract", "compress", "send", "sync", "copy", "move", "print", "play", "convert", "打开", "比较", "选择", "编辑", "扫描", "共享", "上传", "解压", "压缩", "发送", "同步", "复制", "移动", "打印", "播放", "转换" };
            int score = 0;
            foreach (string action in actions) if (lower.IndexOf(action, StringComparison.OrdinalIgnoreCase) >= 0) score += 9;
            if (score == 0) return -1;
            string normalized = NormalizeItemType(itemType);
            if (normalized == "Directory" && (lower.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0 || lower.IndexOf("文件夹", StringComparison.Ordinal) >= 0)) score += 10;
            if (normalized == "*" && (lower.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 || lower.IndexOf("文件", StringComparison.Ordinal) >= 0)) score += 10;
            if (lower.IndexOf(" for ", StringComparison.OrdinalIgnoreCase) >= 0 || lower.IndexOf(" with ", StringComparison.OrdinalIgnoreCase) >= 0 || lower.IndexOf(" to ", StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
            if (text.Length >= 8 && text.Length <= 45) score += 5;
            return score;
        }

        internal static string TranslateTitle(string title)
        {
            string value = CleanTitle(title);
            if (string.Equals(value, "Select Left File for Compare", StringComparison.OrdinalIgnoreCase)) return "选择左边文件进行比较";
            if (string.Equals(value, "Select Left Folder for Compare", StringComparison.OrdinalIgnoreCase)) return "选择左边文件夹进行比较";
            if (string.Equals(value, "Open for Compare", StringComparison.OrdinalIgnoreCase)) return "打开并比较";
            if (string.Equals(value, "Compare Files", StringComparison.OrdinalIgnoreCase)) return "比较文件";
            if (string.Equals(value, "Compare Folders", StringComparison.OrdinalIgnoreCase)) return "比较文件夹";
            if (string.Equals(value, "Compare to Clipboard", StringComparison.OrdinalIgnoreCase)) return "与剪贴板比较";
            return ChineseDisplayText.ContextMenuName(value);
        }

        internal static List<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            if (SelectTitle(new string[] { "Beyond Compare", "Select Left File for Compare" }) != "Select Left File for Compare") failures.Add("动态右键标题：没有优先选择实际子命令");
            if (TranslateTitle("Select Left File for Compare") != "选择左边文件进行比较") failures.Add("动态右键标题：文件比较命令未中文化");
            if (TranslateTitle("Select Left Folder for Compare") != "选择左边文件夹进行比较") failures.Add("动态右键标题：文件夹比较命令未中文化");
            if (AdvancedMenuInventoryService.NormalizePackagedItemType("Folder") != "Directory") failures.Add("打包右键标题：传统 Folder 场景未与 Directory 归一化");
            return failures;
        }

        private static object CreateSampleArray(string itemType, out object shellItem)
        {
            string normalized = NormalizeItemType(itemType);
            string appPath = Environment.ProcessPath ?? string.Empty;
            string sample;
            if (normalized == "Drive") sample = Path.GetPathRoot(Environment.SystemDirectory);
            else if (normalized == "Directory" || normalized == @"Directory\Background" || normalized == "Folder") sample = Path.GetDirectoryName(appPath);
            else sample = appPath;

            Guid shellItemId = typeof(IShellItem).GUID;
            int hr = SHCreateItemFromParsingName(sample, IntPtr.Zero, ref shellItemId, out shellItem);
            Marshal.ThrowExceptionForHR(hr);
            Guid arrayId = typeof(IShellItemArray).GUID;
            object array;
            hr = SHCreateShellItemArrayFromShellItem(shellItem, ref arrayId, out array);
            Marshal.ThrowExceptionForHR(hr);
            return array;
        }

        private static void AddCommand(IExplorerCommand command, object shellItemArray, List<string> titles, ref string icon, int depth)
        {
            if (command == null || depth > 2) return;
            EXPCMDSTATE state;
            try
            {
                command.GetState(shellItemArray, false, out state);
                if ((state & EXPCMDSTATE.Hidden) != 0) return;
            }
            catch { }

            IntPtr titlePointer = IntPtr.Zero;
            try
            {
                command.GetTitle(shellItemArray, out titlePointer);
                string title = titlePointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(titlePointer);
                if (!string.IsNullOrWhiteSpace(title)) titles.Add(title);
            }
            catch { }
            finally { if (titlePointer != IntPtr.Zero) Marshal.FreeCoTaskMem(titlePointer); }

            if (string.IsNullOrWhiteSpace(icon))
            {
                IntPtr iconPointer = IntPtr.Zero;
                try
                {
                    command.GetIcon(shellItemArray, out iconPointer);
                    if (iconPointer != IntPtr.Zero) icon = Marshal.PtrToStringUni(iconPointer);
                }
                catch { }
                finally { if (iconPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(iconPointer); }
            }

            IEnumExplorerCommand children = null;
            try
            {
                command.EnumSubCommands(out children);
                if (children == null) return;
                while (true)
                {
                    IExplorerCommand child;
                    uint fetched;
                    int hr = children.Next(1, out child, out fetched);
                    if (hr != 0 || fetched == 0 || child == null) break;
                    try { AddCommand(child, shellItemArray, titles, ref icon, depth + 1); }
                    finally { ReleaseCom(child); }
                }
            }
            catch { }
            finally { ReleaseCom(children); }
        }

        private static string NormalizeItemType(string itemType)
        {
            string value = (itemType ?? string.Empty).Trim();
            return string.Equals(value, "Folder", StringComparison.OrdinalIgnoreCase) ? "Directory" : value;
        }

        private static string CleanTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&").Replace("\t", " ").Trim();
            return text.Length > 100 ? text.Substring(0, 100).Trim() : text;
        }

        private static void ReleaseCom(object value) { if (value != null && Marshal.IsComObject(value)) try { Marshal.FinalReleaseComObject(value); } catch { } }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object shellItem);

        [DllImport("shell32.dll", PreserveSig = true)]
        private static extern int SHCreateShellItemArrayFromShellItem([MarshalAs(UnmanagedType.Interface)] object shellItem, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object shellItemArray);

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem { }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray { }

        [Flags]
        private enum EXPCMDSTATE { Enabled = 0, Disabled = 1, Hidden = 2, Checkbox = 4, Checked = 8, RadioCheck = 16 }

        [ComImport, Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IExplorerCommand
        {
            void GetTitle([MarshalAs(UnmanagedType.Interface)] object shellItemArray, out IntPtr name);
            void GetIcon([MarshalAs(UnmanagedType.Interface)] object shellItemArray, out IntPtr icon);
            void GetToolTip([MarshalAs(UnmanagedType.Interface)] object shellItemArray, out IntPtr toolTip);
            void GetCanonicalName(out Guid commandName);
            void GetState([MarshalAs(UnmanagedType.Interface)] object shellItemArray, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out EXPCMDSTATE state);
            void Invoke([MarshalAs(UnmanagedType.Interface)] object shellItemArray, IntPtr bindContext);
            void GetFlags(out int flags);
            void EnumSubCommands(out IEnumExplorerCommand commands);
        }

        [ComImport, Guid("A88826F8-186F-4987-AADE-EA0CEF8FBFE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumExplorerCommand
        {
            [PreserveSig] int Next(uint count, out IExplorerCommand command, out uint fetched);
            [PreserveSig] int Skip(uint count);
            void Reset();
            void Clone(out IEnumExplorerCommand clone);
        }
    }
}
