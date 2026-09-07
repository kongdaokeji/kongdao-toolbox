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
using System.Security.Cryptography;
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

    internal sealed class CleanerEngine
    {
        private readonly DataStore store;

        public CleanerEngine(DataStore store)
        {
            this.store = store;
        }

        public CleanupBatch Clean(IEnumerable<Finding> findings)
        {
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            Directory.CreateDirectory(Path.Combine(batchPath, "registry"));
            Directory.CreateDirectory(Path.Combine(batchPath, "files"));
            Directory.CreateDirectory(Path.Combine(batchPath, "services"));
            Directory.CreateDirectory(Path.Combine(batchPath, "tasks"));
            List<CleanupResult> results = new List<CleanupResult>();

            foreach (Finding finding in findings.Where(delegate(Finding f) { return f.Selected && f.CanClean; }))
            {
                CleanupResult result = CleanOne(finding, batchPath);
                results.Add(result);
            }

            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = results };
            WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            WriteJson(Path.Combine(store.Reports, "cleanup-" + id + ".json"), results);
            return batch;
        }

        private CleanupResult CleanOne(Finding finding, string batchPath)
        {
            CleanupResult result = new CleanupResult();
            result.Id = finding.Id;
            result.Title = finding.UserVisibleName;
            result.Vendor = finding.Vendor;
            result.Category = finding.Category;
            result.ActionKind = finding.ActionKind;
            result.TechnicalLocation = finding.TechnicalLocation;
            result.Target = finding.Target;
            result.Status = "Skipped";
            result.Message = "未执行。";

            try
            {
                ActionTarget target = finding.Target;
                if (target == null || string.IsNullOrEmpty(target.Kind)) throw new InvalidOperationException("缺少清理目标。");
                if (target.Kind == "DeleteRegistryKey")
                {
                    result.Backup = BackupRegistry(batchPath, target);
                    if (string.IsNullOrEmpty(result.Backup))
                    {
                        result.Status = "Failed";
                        result.Message = "注册表备份失败，已取消删除，避免右键菜单或系统设置无法恢复。";
                    }
                    else
                    {
                        RegistryHelper.DeleteKey(target);
                        result.Status = VerifyApplied(target) ? "Done" : "Failed";
                        result.Message = result.Status == "Done" ? "注册表键已删除。" : "复核失败：注册表键仍然存在。";
                    }
                }
                else if (target.Kind == "DeleteRegistryValue")
                {
                    result.Backup = BackupRegistry(batchPath, target);
                    if (string.IsNullOrEmpty(result.Backup))
                    {
                        result.Status = "Failed";
                        result.Message = "注册表备份失败，已取消删除，避免右键菜单或系统设置无法恢复。";
                    }
                    else
                    {
                        RegistryHelper.DeleteValue(target);
                        result.Status = VerifyApplied(target) ? "Done" : "Failed";
                        result.Message = result.Status == "Done" ? "注册表值已删除。" : "复核失败：注册表值仍然存在。";
                    }
                }
                else if (target.Kind == "DisableShellExtension")
                {
                    string backupPath = Path.Combine(Path.Combine(batchPath, "registry"), "shell-extension-" + SafeFileName(target.ValueName) + "-" + SafeFileName(target.View) + ".json");
                    ContextMenuToggleBackup backup = ContextMenuMutationService.CaptureValue(target, "ShellExBlocked");
                    WriteJson(backupPath, backup);
                    result.Backup = backupPath;
                    if (!ContextMenuMutationService.SetShellExtensionBlocked(target, true)) throw new InvalidOperationException("写入 Windows Shell 扩展屏蔽列表后复核失败。");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "右键扩展已通过 Windows 屏蔽列表禁用。" : "复核失败：右键扩展仍未被屏蔽。";
                }
                else if (target.Kind == "MoveFileToBackup")
                {
                    string src = Environment.ExpandEnvironmentVariables(target.FilePath ?? string.Empty);
                    if (!File.Exists(src))
                    {
                        result.Status = "Failed";
                        result.Message = "要移动的源文件不存在，已跳过，避免误报成功。";
                    }
                    else
                    {
                        string dest = Path.Combine(Path.Combine(batchPath, "files"), Path.GetFileName(src));
                        File.Move(src, dest);
                        result.Backup = dest;
                        result.Status = VerifyApplied(target) ? "Done" : "Failed";
                        result.Message = result.Status == "Done" ? "文件已移动到备份。" : "复核失败：文件仍然存在。";
                    }
                }
                else if (target.Kind == "DisableService")
                {
                    string serviceFile = Path.Combine(Path.Combine(batchPath, "services"), SafeFileName(target.ServiceName) + ".json");
                    WriteText(serviceFile, GetServiceState(target.ServiceName));
                    result.Backup = serviceFile;
                    RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= disabled");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "服务已禁用。" : "复核失败：服务仍未禁用。";
                }
                else if (target.Kind == "DisableScheduledTask")
                {
                    string taskDir = Path.Combine(Path.Combine(batchPath, "tasks"), SafeFileName(target.TaskName));
                    Directory.CreateDirectory(taskDir);
                    WriteText(Path.Combine(taskDir, "task.xml"), QueryTaskXml(target.TaskName));
                    bool wasEnabled;
                    WriteText(Path.Combine(taskDir, "state.txt"), TryGetScheduledTaskEnabled(target.TaskName, out wasEnabled) && wasEnabled ? "Enabled" : "Disabled");
                    result.Backup = taskDir;
                    if (!WindowsTaskApi.SetEnabled(target.TaskName, false)) throw new InvalidOperationException("计划任务禁用失败。");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "计划任务已禁用。" : "复核失败：计划任务仍未禁用。";
                }
                else if (target.Kind == "InvokeUninstaller")
                {
                    ValidateTargetedUninstaller(target);
                    LaunchUninstaller(target.UninstallCommand);
                    result.Status = "Launched";
                    result.Message = "已打开独立附带产品“" + target.ExpectedProductName + "”的卸载器。没有卸载来源主程序；请确认产品名称后再决定，完成后重新扫描。";
                }
                else
                {
                    result.Status = "Skipped";
                    result.Message = "只报告，不自动清理。";
                }
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Message = ex.Message;
                Logger.Error("清理失败：" + finding.UserVisibleName, ex);
            }
            return result;
        }

        public bool VerifyApplied(ActionTarget target)
        {
            if (target.Kind == "DeleteRegistryKey") return !RegistryHelper.KeyExists(target);
            if (target.Kind == "DeleteRegistryValue") return !RegistryHelper.ValueExists(target);
            if (target.Kind == "DisableShellExtension") return RegistryHelper.ValueExists(target);
            if (target.Kind == "MoveFileToBackup") return string.IsNullOrEmpty(target.FilePath) || !File.Exists(Environment.ExpandEnvironmentVariables(target.FilePath));
            if (target.Kind == "DisableService") return IsServiceDisabled(target.ServiceName);
            if (target.Kind == "InvokeUninstaller") return true;
            if (target.Kind == "DisableScheduledTask")
            {
                bool enabled;
                return TryGetScheduledTaskEnabled(target.TaskName, out enabled) && !enabled;
            }
            return true;
        }

        private string BackupRegistry(string batchPath, ActionTarget target)
        {
            string native = RegistryHelper.NativePath(target);
            string path = Path.Combine(Path.Combine(batchPath, "registry"), RegistryBackupFileName(target));
            int exitCode = RunHidden("reg.exe", "export \"" + native + "\" \"" + path + "\" /y" + RegistryViewArg(target));
            if (exitCode != 0) Logger.Error("注册表备份失败：" + native, new InvalidOperationException("reg export 退出码 " + exitCode));
            return File.Exists(path) ? path : null;
        }

        private static string RegistryViewArg(ActionTarget target)
        {
            if (target == null) return string.Empty;
            if (string.Equals(target.View, "Registry32", StringComparison.OrdinalIgnoreCase)) return " /reg:32";
            if (string.Equals(target.View, "Registry64", StringComparison.OrdinalIgnoreCase)) return " /reg:64";
            return string.Empty;
        }

        public RestoreBatchResult RestoreBatch(CleanupBatch batch)
        {
            RestoreBatchResult summary = new RestoreBatchResult
            {
                Messages = new List<string>()
            };
            if (batch == null || batch.Results == null) return summary;
            foreach (CleanupResult result in batch.Results)
            {
                summary.Total++;
                string message;
                bool ok = RestoreResult(batch, result, out message);
                if (ok)
                {
                    summary.Succeeded++;
                    if (result != null)
                    {
                        result.Status = "Restored";
                        result.Message = message;
                    }
                }
                else
                {
                    summary.Failed++;
                    if (result != null)
                    {
                        result.Status = "RestoreFailed";
                        result.Message = message;
                    }
                }
                if (!string.IsNullOrWhiteSpace(message)) summary.Messages.Add(message);
            }
            return summary;
        }

        public void RewriteBatchManifest(CleanupBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path) || batch.Results == null) return;
            batch.Results = batch.Results.Where(delegate(CleanupResult r)
            {
                return r == null || !string.Equals(r.Status, "Restored", StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (batch.Results.Count == 0)
            {
                DeleteBatchRecord(batch);
                return;
            }
            WriteJson(Path.Combine(batch.Path, "manifest.json"), batch);
        }

        public bool RestoreResult(CleanupResult result, out string message)
        {
            return RestoreResult(null, result, out message);
        }

        private bool RestoreResult(CleanupBatch batch, CleanupResult result, out string message)
        {
            message = string.Empty;
            if (result == null)
            {
                message = "空恢复项，已跳过。";
                return true;
            }
            if (!string.Equals(result.Status, "Done", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(result.Status, "RestoreFailed", StringComparison.OrdinalIgnoreCase))
            {
                message = result.Title + "：原清理结果为 " + result.Status + "，无需恢复。";
                return true;
            }
            try
            {
                if (result.Target == null || string.IsNullOrEmpty(result.Target.Kind))
                {
                    message = result.Title + "：缺少恢复目标。";
                    return false;
                }

                ActionTarget target = result.Target;
                string backup = ResolveExistingBackupPath(batch, result);
                if (target.Kind == "RestoreContextMenuToggle" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = ContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "右键菜单状态已恢复。" : "右键菜单恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "DisableShellExtension" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = ContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "右键扩展屏蔽状态已恢复。" : "右键扩展恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "RestoreContextMenuTree" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = ContextMenuMutationService.RestoreTree(backup);
                    message = result.Title + "：" + (restored ? "右键菜单配置已恢复。" : "右键菜单配置恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "RestoreSpecialMenu" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = SpecialContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "专用菜单配置已恢复。" : "专用菜单配置恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "RestoreAdvancedMenu" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    bool restored = AdvancedContextMenuMutationService.Restore(backup);
                    message = result.Title + "：" + (restored ? "高级菜单配置已恢复。" : "高级菜单恢复后复核失败。");
                    return restored;
                }
                if ((target.Kind == "DeleteRegistryKey" || target.Kind == "DeleteRegistryValue") &&
                    target != null)
                {
                    string registryBackup = ResolveRegistryBackupPath(batch, result, target);
                    if (string.IsNullOrEmpty(registryBackup))
                    {
                        message = result.Title + "：旧版清理记录没有找到注册表备份文件，无法完整恢复。";
                        return false;
                    }
                    int exitCode = RunHidden("reg.exe", "import \"" + registryBackup + "\"" + RegistryViewArg(target));
                    bool restored = target.Kind == "DeleteRegistryKey" ? RegistryHelper.KeyExists(target) : RegistryHelper.ValueExists(target);
                    message = result.Title + "：" + (restored ? "注册表已恢复。" : "注册表恢复后复核失败。reg import 退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "MoveFileToBackup" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    string dest = Environment.ExpandEnvironmentVariables(target.FilePath);
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (File.Exists(dest))
                    {
                        message = result.Title + "：原位置已经有同名文件，备份已保留，没有覆盖。";
                        return false;
                    }
                    File.Move(backup, dest);
                    bool restored = File.Exists(dest);
                    message = result.Title + "：" + (restored ? "文件已移回原位置。" : "文件恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "DisableService" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    string state = File.ReadAllText(backup, Encoding.UTF8);
                    string start = state.IndexOf("Auto", StringComparison.OrdinalIgnoreCase) >= 0 ? "auto" : (state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ? "disabled" : "demand");
                    int exitCode = RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= " + start);
                    string restoredState = GetServiceState(target.ServiceName);
                    bool restored = start == "auto"
                        ? restoredState.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                        : (start == "disabled" ? restoredState.Equals("Disabled", StringComparison.OrdinalIgnoreCase) : restoredState.Equals("Manual", StringComparison.OrdinalIgnoreCase));
                    message = result.Title + "：" + (restored ? "服务启动状态已恢复。" : "服务恢复后复核失败，当前状态 " + restoredState + "，命令退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "DisableScheduledTask" && !string.IsNullOrEmpty(backup) && Directory.Exists(backup))
                {
                    string xml = Path.Combine(backup, "task.xml");
                    string stateFile = Path.Combine(backup, "state.txt");
                    if (!ScheduledTaskExists(target.TaskName) && File.Exists(xml))
                    {
                        bool created = WindowsTaskApi.RegisterFromXml(target.TaskName, File.ReadAllText(xml));
                        if (!created)
                        {
                            message = result.Title + "：计划任务重建失败。";
                            return false;
                        }
                    }
                    string state = File.Exists(stateFile) ? File.ReadAllText(stateFile, Encoding.UTF8) : "Enabled";
                    bool shouldDisable = state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool changed = WindowsTaskApi.SetEnabled(target.TaskName, !shouldDisable);
                    bool enabled;
                    bool exists = TryGetScheduledTaskEnabled(target.TaskName, out enabled);
                    bool restored = exists && (shouldDisable ? !enabled : enabled);
                    message = result.Title + "：" + (restored ? "计划任务状态已恢复。" : "计划任务恢复后复核失败。");
                    return changed && restored;
                }

                message = result.Title + "：没有可用备份，无法恢复。";
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("恢复失败：" + result.Title, ex);
                message = result.Title + "：" + ex.Message;
                return false;
            }
        }

        private string ResolveExistingBackupPath(CleanupBatch batch, CleanupResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Backup)) return null;
            string backup = Environment.ExpandEnvironmentVariables(result.Backup);
            if (File.Exists(backup) || Directory.Exists(backup)) return backup;
            if (batch != null && !string.IsNullOrWhiteSpace(batch.Path) && !Path.IsPathRooted(backup))
            {
                string combined = Path.Combine(batch.Path, backup);
                if (File.Exists(combined) || Directory.Exists(combined)) return combined;
            }
            if (batch != null && !string.IsNullOrWhiteSpace(batch.Path) && Directory.Exists(batch.Path))
            {
                string name = Path.GetFileName(backup);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    foreach (string candidate in Directory.GetFiles(batch.Path, name, SearchOption.AllDirectories))
                    {
                        if (File.Exists(candidate)) return candidate;
                    }
                    foreach (string candidate in Directory.GetDirectories(batch.Path, name, SearchOption.AllDirectories))
                    {
                        if (Directory.Exists(candidate)) return candidate;
                    }
                }
            }
            return null;
        }

        private string ResolveRegistryBackupPath(CleanupBatch batch, CleanupResult result, ActionTarget target)
        {
            string direct = ResolveExistingBackupPath(batch, result);
            if (!string.IsNullOrEmpty(direct) && direct.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) && File.Exists(direct)) return direct;
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path) || target == null) return null;

            string registryDir = Path.Combine(batch.Path, "registry");
            if (!Directory.Exists(registryDir)) return null;

            string currentName = RegistryBackupFileName(target);
            string currentPath = Path.Combine(registryDir, currentName);
            if (File.Exists(currentPath)) return currentPath;

            string legacyPath = Path.Combine(registryDir, LegacyRegistryBackupFileName(target));
            if (File.Exists(legacyPath)) return legacyPath;

            string needle = RegistryFileNeedle(target);
            if (!string.IsNullOrEmpty(needle))
            {
                foreach (string file in Directory.GetFiles(registryDir, "*.reg", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string text = File.ReadAllText(file);
                        if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return file;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static string RegistryBackupFileName(ActionTarget target)
        {
            return CompactBackupFileName(RegistryBackupRawName(target), ".reg");
        }

        private static string LegacyRegistryBackupFileName(ActionTarget target)
        {
            return SafeFileName(RegistryBackupRawName(target)) + ".reg";
        }

        private static string RegistryBackupRawName(ActionTarget target)
        {
            string backupName = RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) backupName += "__value__" + target.ValueName;
            return backupName;
        }

        private static string RegistryFileNeedle(ActionTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.SubKey)) return string.Empty;
            string hive = string.Equals(target.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
            return "[" + hive + "\\" + target.SubKey + "]";
        }

        private static string CompactBackupFileName(string raw, string extension)
        {
            string safe = SafeFileName(raw);
            if (safe.Length <= 120) return safe + extension;
            string prefix = safe.Substring(0, Math.Min(56, safe.Length));
            string suffix = safe.Substring(Math.Max(0, safe.Length - 44));
            return prefix + "__" + ShortHash(raw) + "__" + suffix + extension;
        }

        private static string ShortHash(string value)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length && builder.Length < 12; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public long GetBatchStorageBytes(CleanupBatch batch)
        {
            if (batch == null) return 0;
            long total = DirectoryBytes(batch.Path);
            foreach (string report in BatchReportPaths(batch))
            {
                try { if (File.Exists(report)) total += new FileInfo(report).Length; } catch { }
            }
            return total;
        }

        public List<CleanupBatch> FindOldBatchRecords(IEnumerable<CleanupBatch> source, DateTime now, int keepLatest, int keepDays)
        {
            List<CleanupBatch> batches = (source ?? Enumerable.Empty<CleanupBatch>()).Where(delegate(CleanupBatch batch) { return batch != null; }).ToList();
            Dictionary<CleanupBatch, DateTime?> created = batches.ToDictionary(delegate(CleanupBatch batch) { return batch; }, ParseBatchCreatedAt);
            List<CleanupBatch> ordered = batches.OrderByDescending(delegate(CleanupBatch batch) { return created[batch] ?? DateTime.MaxValue; }).ThenByDescending(delegate(CleanupBatch batch) { return batch.Id; }).ToList();
            HashSet<CleanupBatch> newest = new HashSet<CleanupBatch>(ordered.Take(Math.Max(0, keepLatest)));
            DateTime cutoff = now.AddDays(-Math.Max(0, keepDays));
            return ordered.Where(delegate(CleanupBatch batch)
            {
                DateTime? date = created[batch];
                return !newest.Contains(batch) && date.HasValue && date.Value < cutoff;
            }).ToList();
        }

        public void DeleteBatchRecord(CleanupBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path)) return;
            string backupRootPath = Path.GetFullPath(store.Backups).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string backupRoot = backupRootPath + Path.DirectorySeparatorChar;
            string batchPath = Path.GetFullPath(batch.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(batchPath, backupRootPath, StringComparison.OrdinalIgnoreCase) || !batchPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("恢复记录路径不在备份目录下，拒绝删除：" + batchPath);
            }
            List<string> reports = BatchReportPaths(batch);
            if (Directory.Exists(batchPath)) Directory.Delete(batchPath, true);
            foreach (string report in reports) if (File.Exists(report)) File.Delete(report);
            if (Directory.Exists(batchPath) || reports.Any(File.Exists)) throw new IOException("恢复记录删除后复核失败：" + batch.Id);
        }

        private DateTime? ParseBatchCreatedAt(CleanupBatch batch)
        {
            DateTime created;
            if (batch != null && DateTime.TryParse(batch.CreatedAt, out created)) return created;
            if (batch != null && DateTime.TryParseExact(batch.Id, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out created)) return created;
            try { if (batch != null && Directory.Exists(batch.Path)) return Directory.GetCreationTime(batch.Path); } catch { }
            return null;
        }

        private List<string> BatchReportPaths(CleanupBatch batch)
        {
            List<string> paths = new List<string>();
            if (batch == null || string.IsNullOrWhiteSpace(batch.Id)) return paths;
            string id = batch.Id.Trim();
            if (!string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("恢复记录编号格式异常，拒绝删除关联报告：" + id);
            }
            string reportRootPath = Path.GetFullPath(store.Reports).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string prefix in new string[] { "cleanup-", "context-menu-" })
            {
                string path = Path.GetFullPath(Path.Combine(reportRootPath, prefix + id + ".json"));
                if (!path.StartsWith(reportRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("报告路径越界，拒绝删除：" + path);
                paths.Add(path);
            }
            return paths;
        }

        private static long DirectoryBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
            long total = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public List<CleanupBatch> LoadBatches()
        {
            List<CleanupBatch> list = new List<CleanupBatch>();
            foreach (string manifest in Directory.GetFiles(store.Backups, "manifest.json", SearchOption.AllDirectories))
            {
                try
                {
                    CleanupBatch batch = JsonSerializer.Deserialize<CleanupBatch>(File.ReadAllText(manifest, Encoding.UTF8));
                    if (batch != null) list.Add(batch);
                }
                catch (Exception ex)
                {
                    Logger.Error("读取恢复清单失败：" + manifest, ex);
                }
            }
            return list.OrderByDescending(delegate(CleanupBatch b) { return b.Id; }).ToList();
        }

        private static string GetServiceState(string serviceName)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,StartMode FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'"))
                {
                    foreach (ManagementObject obj in searcher.Get()) return Convert.ToString(obj["StartMode"]);
                }
            }
            catch { }
            return "Unknown";
        }

        private static bool IsServiceDisabled(string serviceName)
        {
            return string.Equals(GetServiceState(serviceName), "Disabled", StringComparison.OrdinalIgnoreCase);
        }

        private static string QueryTaskXml(string taskName)
        {
            string xml = WindowsTaskApi.GetXml(taskName); if (string.IsNullOrWhiteSpace(xml)) Logger.Error("备份计划任务失败：" + taskName, new InvalidOperationException("任务 XML 为空。")); return xml;
        }

        private static bool ScheduledTaskExists(string taskName)
        {
            bool enabled;
            return TryGetScheduledTaskEnabled(taskName, out enabled);
        }

        private static bool TryGetScheduledTaskEnabled(string taskName, out bool enabled)
        {
            return WindowsTaskApi.TryGetEnabled(taskName, out enabled);
        }

        private static bool TryGetScheduledTaskEnabledFromXml(string taskName, out bool enabled)
        {
            enabled = false;
            string xml = QueryTaskXml(taskName);
            if (string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (xml.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                enabled = false;
                return true;
            }
            enabled = true;
            return true;
        }

        internal static void WriteJson(string path, object value)
        {
            WriteText(path, JsonSerializer.Serialize(value, JsonOptions.Value));
        }

        private static void WriteText(string path, string text)
        {
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tempPath = Path.Combine(dir, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(tempPath, text ?? string.Empty, new UTF8Encoding(true));
            if (File.Exists(fullPath))
            {
                string backupPath = tempPath + ".bak";
                File.Replace(tempPath, fullPath, backupPath, true);
                try { File.Delete(backupPath); } catch { }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }

        private static int RunHidden(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(psi))
                {
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); } catch { }
                        Logger.Error("命令执行超时：" + file + " " + args, new TimeoutException("等待 60 秒仍未退出。"));
                        return -1;
                    }
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("命令执行失败：" + file + " " + args, ex);
                return -1;
            }
        }

        private static void LaunchUninstaller(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("没有卸载命令。");
            string file;
            string args;
            SplitCommandLine(Environment.ExpandEnvironmentVariables(command), out file, out args);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = file;
            psi.Arguments = args;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private static void ValidateTargetedUninstaller(ActionTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ExpectedProductName) || string.IsNullOrWhiteSpace(target.ExpectedUninstallCommand))
                throw new InvalidOperationException("缺少独立产品校验信息，拒绝打开卸载器；请重新扫描。");
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                if (key == null) throw new InvalidOperationException("对应附带产品的卸载项已不存在，请重新扫描。");
                string currentName = Convert.ToString(key.GetValue("DisplayName", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames));
                string currentPublisher = Convert.ToString(key.GetValue("Publisher", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames));
                string currentCommand = Convert.ToString(key.GetValue("UninstallString", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames));
                if (!string.Equals((currentName ?? string.Empty).Trim(), target.ExpectedProductName.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("卸载项产品名称已经变化，拒绝打开，避免卸载错软件；请重新扫描。");
                if (!string.IsNullOrWhiteSpace(target.ExpectedPublisher) && !string.Equals((currentPublisher ?? string.Empty).Trim(), target.ExpectedPublisher.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("卸载项厂商已经变化，拒绝打开，避免卸载错软件；请重新扫描。");
                if (!string.Equals((currentCommand ?? string.Empty).Trim(), target.ExpectedUninstallCommand.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("卸载命令已经变化，拒绝打开，避免卸载错软件；请重新扫描。");
            }
        }

        private static void SplitCommandLine(string command, out string file, out string args)
        {
            command = (command ?? string.Empty).Trim();
            file = command;
            args = string.Empty;
            if (command.Length == 0) return;
            if (command[0] == '"')
            {
                int close = command.IndexOf('"', 1);
                if (close > 0)
                {
                    file = command.Substring(1, close - 1);
                    args = command.Substring(close + 1).Trim();
                    return;
                }
            }
            foreach (string extension in new string[] { ".exe", ".cmd", ".bat", ".com" })
            {
                int exeEnd = command.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
                if (exeEnd > 0)
                {
                    exeEnd += extension.Length;
                    file = command.Substring(0, exeEnd).Trim();
                    args = command.Substring(exeEnd).Trim();
                    return;
                }
            }
            int split = command.IndexOf(' ');
            if (split > 0)
            {
                file = command.Substring(0, split);
                args = command.Substring(split + 1).Trim();
            }
        }

        private static string SafeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }
    }

}
