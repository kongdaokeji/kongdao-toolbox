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

    internal static class WindowsTaskApi
    {
        public static bool TryGetEnabled(string taskPath, out bool enabled)
        {
            enabled = false;
            try { dynamic task = GetTask(taskPath); enabled = Convert.ToBoolean(task.Enabled); return true; }
            catch { return false; }
        }

        public static bool SetEnabled(string taskPath, bool enabled)
        {
            dynamic task = GetTask(taskPath); task.Enabled = enabled; bool actual; return TryGetEnabled(taskPath, out actual) && actual == enabled;
        }

        public static string GetXml(string taskPath)
        {
            try { dynamic task = GetTask(taskPath); return Convert.ToString(task.Xml); }
            catch { return string.Empty; }
        }

        public static bool RegisterFromXml(string taskPath, string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return false; string folderPath, name; Split(taskPath, out folderPath, out name);
            dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath);
            folder.RegisterTask(name, xml, 6, null, null, 3, null);
            bool enabled; return TryGetEnabled(taskPath, out enabled);
        }

        public static bool CreateValidationTask(string taskPath, string executable)
        {
            string folderPath, name; Split(taskPath, out folderPath, out name); dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath); dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "RogueCleaner validation task"; definition.Settings.Enabled = true; definition.Settings.StartWhenAvailable = true;
            definition.Principal.UserId = WindowsIdentity.GetCurrent().Name; definition.Principal.LogonType = 3; definition.Principal.RunLevel = 1;
            dynamic trigger = definition.Triggers.Create(2); trigger.StartBoundary = DateTime.Now.AddMinutes(10).ToString("s"); trigger.DaysInterval = 1;
            dynamic action = definition.Actions.Create(0); action.Path = executable;
            folder.RegisterTaskDefinition(name, definition, 6, null, null, 3, null);
            bool enabled; return TryGetEnabled(taskPath, out enabled) && enabled;
        }

        public static bool Delete(string taskPath)
        {
            try { string folderPath, name; Split(taskPath, out folderPath, out name); dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath); folder.DeleteTask(name, 0); bool enabled; return !TryGetEnabled(taskPath, out enabled); }
            catch { bool enabled; return !TryGetEnabled(taskPath, out enabled); }
        }

        private static dynamic GetTask(string taskPath) { string folderPath, name; Split(taskPath, out folderPath, out name); dynamic service = Connect(); dynamic folder = service.GetFolder(folderPath); return folder.GetTask(name); }
        private static dynamic Connect() { Type type = Type.GetTypeFromProgID("Schedule.Service"); if (type == null) throw new InvalidOperationException("系统未提供任务计划 COM 服务。"); dynamic service = Activator.CreateInstance(type); service.Connect(); return service; }
        private static void Split(string taskPath, out string folderPath, out string name) { string normalized = (taskPath ?? string.Empty).Trim(); if (!normalized.StartsWith("\\", StringComparison.Ordinal)) normalized = "\\" + normalized; int slash = normalized.LastIndexOf('\\'); folderPath = slash <= 0 ? "\\" : normalized.Substring(0, slash); name = normalized.Substring(slash + 1); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("计划任务路径无效。"); }
    }

}
