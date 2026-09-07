// 移植自 RogueCleaner（https://github.com/aakk007/RogueCleaner），MIT License，Copyright (c) 2026 aakk007
// 「启动项管理」只读视图：列出全部开机启动项（Run 键 + 启动文件夹），供新手核对，不直接处理。

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TubaWinUi3.Services.RogueCleaner
{

    internal sealed class StartupItem
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Location { get; set; }
    }

    internal static class StartupItemEnumerator
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static List<StartupItem> List()
        {
            List<StartupItem> items = new List<StartupItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Run 键：HKCU 默认视图 + HKLM 64/32 视图
            AddRunHive(items, seen, Registry.CurrentUser, RegistryView.Default, "HKCU\\" + RunPath);
            AddRunHive(items, seen, Registry.LocalMachine, RegistryView.Registry64, "HKLM(64)\\Software\\...\\Run");
            if (Environment.Is64BitOperatingSystem)
                AddRunHive(items, seen, Registry.LocalMachine, RegistryView.Registry32, "HKLM(32)\\Software\\...\\Run");

            // 启动文件夹：当前用户 + 所有用户
            AddStartupFolder(items, seen, StartupFolder(false), "启动文件夹（当前用户）");
            AddStartupFolder(items, seen, StartupFolder(true), "启动文件夹（所有用户）");

            return items;
        }

        private static string StartupFolder(bool common)
        {
            if (common)
            {
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return Path.Combine(programData, @"Microsoft\Windows\Start Menu\Programs\StartUp");
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }

        private static void AddRunHive(List<StartupItem> items, HashSet<string> seen, RegistryKey baseKey, RegistryView view, string location)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(baseKey == Registry.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view))
                using (RegistryKey key = root.OpenSubKey(RunPath, false))
                {
                    if (key == null) return;
                    foreach (string name in key.GetValueNames())
                    {
                        string command = Convert.ToString(key.GetValue(name, ""));
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        if (!seen.Add(name + "|" + command)) continue;
                        items.Add(new StartupItem { Name = name, Command = command, Location = location });
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddStartupFolder(List<StartupItem> items, HashSet<string> seen, string folder, string location)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            try
            {
                foreach (string file in Directory.GetFiles(folder).OrderBy(delegate(string p) { return p; }))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ScannerEngine.ResolveShortcutText(file) : string.Empty;
                    string command = string.IsNullOrWhiteSpace(target) ? file : target;
                    if (!seen.Add(name + "|" + command)) continue;
                    items.Add(new StartupItem { Name = name, Command = command, Location = location });
                }
            }
            catch
            {
            }
        }
    }

}
