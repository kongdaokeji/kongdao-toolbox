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

    internal sealed class RecognitionFeedbackReport
    {
        public string SchemaVersion { get; set; }
        public string CreatedAt { get; set; }
        public string FeedbackType { get; set; }
        public string ExpectedResult { get; set; }
        public string ProductVersion { get; set; }
        public string WindowsVersion { get; set; }
        public string Architecture { get; set; }
        public string Category { get; set; }
        public string CurrentRisk { get; set; }
        public string CurrentAction { get; set; }
        public string Vendor { get; set; }
        public string VisibleName { get; set; }
        public string UserImpact { get; set; }
        public string TechnicalLocation { get; set; }
        public string Evidence { get; set; }
        public string FileName { get; set; }
        public string FileSha256 { get; set; }
    }

    internal sealed class SavedFeedback
    {
        public string MarkdownPath { get; set; }
        public string JsonPath { get; set; }
        public string Markdown { get; set; }
        public string IssueUrl { get; set; }
    }

    internal static class FeedbackService
    {
        private const string HiddenUser = "%USERPROFILE%";
        private const string HiddenTemp = "%TEMP%";
        private const string HiddenAccount = "[账号已隐藏]";
        private const string HiddenUrl = "[URL已隐藏]";
        private const string HiddenNetwork = "[网络地址已隐藏]";
        private const string HiddenSecret = "[敏感参数已隐藏]";

        public static RecognitionFeedbackReport CreateReport(Finding finding, string feedbackType, string expectedResult, bool includeHash)
        {
            if (finding == null) throw new ArgumentNullException("finding");

            string filePath = finding.Target == null ? string.Empty : finding.Target.FilePath;
            RecognitionFeedbackReport report = new RecognitionFeedbackReport();
            report.SchemaVersion = "1";
            report.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
            report.FeedbackType = Sanitize(TrimTo(feedbackType, 40));
            report.ExpectedResult = Sanitize(TrimTo(expectedResult, 2000));
            report.ProductVersion = AppMeta.Version;
            report.WindowsVersion = Sanitize(Environment.OSVersion.VersionString);
            report.Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            report.Category = Sanitize(finding.Category);
            report.CurrentRisk = Sanitize(finding.RiskDisplay);
            report.CurrentAction = Sanitize(finding.ActionText);
            report.Vendor = Sanitize(finding.Vendor);
            report.VisibleName = Sanitize(finding.UserVisibleName);
            report.UserImpact = Sanitize(finding.UserImpact);
            report.TechnicalLocation = Sanitize(finding.TechnicalLocation);
            report.Evidence = Sanitize(finding.Evidence);
            report.FileName = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Sanitize(Path.GetFileName(filePath));
            report.FileSha256 = includeHash ? TryHashFile(filePath) : string.Empty;
            return report;
        }

        public static string BuildMarkdown(RecognitionFeedbackReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("## 识别反馈");
            text.AppendLine();
            AppendField(text, "反馈类型", report.FeedbackType);
            AppendField(text, "用户期望", report.ExpectedResult);
            AppendField(text, "软件版本", report.ProductVersion);
            AppendField(text, "Windows", report.WindowsVersion + " / " + report.Architecture);
            text.AppendLine();
            text.AppendLine("## 当前判断");
            text.AppendLine();
            AppendField(text, "类别", report.Category);
            AppendField(text, "风险/展示", report.CurrentRisk);
            AppendField(text, "动作", report.CurrentAction);
            AppendField(text, "厂商", report.Vendor);
            AppendField(text, "显示名称", report.VisibleName);
            AppendField(text, "影响说明", report.UserImpact);
            AppendField(text, "技术位置", report.TechnicalLocation);
            AppendField(text, "证据", report.Evidence);
            AppendField(text, "文件名", report.FileName);
            if (!string.IsNullOrWhiteSpace(report.FileSha256)) AppendField(text, "文件 SHA256", report.FileSha256);
            text.AppendLine();
            text.AppendLine("> 本报告由流氓软件克星在本地生成并脱敏。提交前请再次检查；GitHub Issue 只作为待验证样本，不会被客户端直接执行。 ");
            return text.ToString();
        }

        public static SavedFeedback Save(DataStore store, RecognitionFeedbackReport report)
        {
            if (store == null) throw new ArgumentNullException("store");
            Directory.CreateDirectory(store.Feedbacks);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string baseName = "recognition-feedback-" + stamp;
            string markdownPath = Path.Combine(store.Feedbacks, baseName + ".md");
            string jsonPath = Path.Combine(store.Feedbacks, baseName + ".json");
            string markdown = BuildMarkdown(report);
            File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
            CleanerEngine.WriteJson(jsonPath, report);
            return new SavedFeedback
            {
                MarkdownPath = markdownPath,
                JsonPath = jsonPath,
                Markdown = markdown,
                IssueUrl = BuildIssueUrl(report)
            };
        }

        public static string BuildIssueUrl(RecognitionFeedbackReport report)
        {
            string title = "[识别反馈][" + SafeTitle(report.FeedbackType) + "] " + SafeTitle(report.VisibleName);
            if (title.Length > 120) title = title.Substring(0, 120);
            return AppMeta.Repository + "/issues/new?template=recognition-feedback.yml&title=" + Uri.EscapeDataString(title);
        }

        internal static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value.Replace("\0", string.Empty);

            text = ReplaceKnown(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), HiddenUser);
            text = ReplaceKnown(text, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), HiddenTemp);
            text = ReplaceKnown(text, Environment.UserName, "[用户名已隐藏]");
            text = ReplaceKnown(text, Environment.MachineName, "[计算机名已隐藏]");
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null) text = ReplaceKnown(text, identity.User.Value, "[SID已隐藏]");
            }
            catch
            {
            }

            text = Regex.Replace(text, @"(?i)[a-z]:\\users\\[^\\\s;\""']+", HiddenUser);
            text = Regex.Replace(text, @"(?i)(https?|ftp)://[^\s<>\""']+", HiddenUrl);
            text = Regex.Replace(text, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", HiddenAccount);
            text = Regex.Replace(text, @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?", HiddenNetwork);
            text = Regex.Replace(text, @"(?i)\b(token|access_token|refresh_token|authorization|password|passwd|secret|apikey|api_key)\s*[:=]\s*[^\s;&]+", "$1=" + HiddenSecret);
            text = Regex.Replace(text, @"(?i)(--?(?:token|password|passwd|secret|api-key|apikey)\s+)[^\s]+", "$1" + HiddenSecret);
            return TrimTo(text.Trim(), 6000);
        }

        public static List<string> RunSelfTests(DataStore store)
        {
            List<string> failures = new List<string>();
            string sid = string.Empty;
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null) sid = identity.User.Value;
            }
            catch
            {
            }

            Finding sample = new Finding
            {
                Risk = "高",
                Vendor = "测试厂商",
                Category = "开机启动",
                UserVisibleName = "测试程序",
                UserImpact = "联系 alice@example.com，访问 https://example.com/private",
                TechnicalLocation = @"C:\Users\Alice\Documents\test.exe 192.168.1.9:8080",
                Evidence = "machine=" + Environment.MachineName + "; user=" + Environment.UserName + "; sid=" + sid + "; token=secret-token",
                ActionKind = "ReportOnly",
                Target = new ActionTarget { Kind = "ReportOnly", FilePath = @"C:\Users\Alice\Documents\test.exe" }
            };
            RecognitionFeedbackReport report = CreateReport(sample, "误报", "这是正常软件", false);
            string all = JsonSerializer.Serialize(report, JsonOptions.Value) + BuildMarkdown(report);
            AssertMissing(failures, all, "Alice", "用户目录");
            AssertMissing(failures, all, "alice@example.com", "邮箱");
            AssertMissing(failures, all, "https://example.com/private", "URL");
            AssertMissing(failures, all, "192.168.1.9", "网络地址");
            AssertMissing(failures, all, "secret-token", "令牌");
            if (!string.IsNullOrWhiteSpace(Environment.UserName)) AssertMissing(failures, all, Environment.UserName, "当前用户名");
            if (!string.IsNullOrWhiteSpace(Environment.MachineName)) AssertMissing(failures, all, Environment.MachineName, "当前计算机名");
            if (!string.IsNullOrWhiteSpace(sid)) AssertMissing(failures, all, sid, "当前 SID");
            if (all.IndexOf(HiddenUser, StringComparison.OrdinalIgnoreCase) < 0) failures.Add("用户目录没有替换为占位符");
            if (all.IndexOf(HiddenUrl, StringComparison.OrdinalIgnoreCase) < 0) failures.Add("URL 没有替换为占位符");
            if (all.IndexOf(HiddenNetwork, StringComparison.OrdinalIgnoreCase) < 0) failures.Add("网络地址没有替换为占位符");
            SavedFeedback saved = null;
            try
            {
                saved = Save(store, report);
                string diskText = File.ReadAllText(saved.MarkdownPath, Encoding.UTF8) + File.ReadAllText(saved.JsonPath, Encoding.UTF8);
                AssertMissing(failures, diskText, "Alice", "落盘用户目录");
                AssertMissing(failures, diskText, "secret-token", "落盘令牌");
            }
            catch (Exception ex)
            {
                failures.Add("反馈落盘失败：" + ex.Message);
            }
            finally
            {
                TryDelete(saved == null ? null : saved.MarkdownPath);
                TryDelete(saved == null ? null : saved.JsonPath);
            }
            return failures;
        }

        private static void AssertMissing(List<string> failures, string text, string secret, string label)
        {
            if (!string.IsNullOrEmpty(secret) && text.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0) failures.Add(label + "仍出现在反馈报告中");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static string ReplaceKnown(string value, string secret, string replacement)
        {
            if (string.IsNullOrWhiteSpace(secret)) return value;
            return Regex.Replace(value, Regex.Escape(secret), replacement, RegexOptions.IgnoreCase);
        }

        private static string TryHashFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return string.Empty;
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 hash = SHA256.Create())
                {
                    return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendField(StringBuilder text, string label, string value)
        {
            text.Append("- **").Append(label).Append("**：");
            text.AppendLine(string.IsNullOrWhiteSpace(value) ? "未提供" : value.Replace("\r", " ").Replace("\n", " "));
        }

        private static string SafeTitle(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "未命名" : value;
            return text.Replace("\r", " ").Replace("\n", " ").Replace("[", "（").Replace("]", "）").Trim();
        }

        private static string TrimTo(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? string.Empty;
            return value.Substring(0, maxLength) + "…";
        }
    }

}
