using System.Diagnostics;
using System.Management;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class ErrorWindow : Window
{
    private const string RepoIssuesUrl = "https://github.com/luolangaga/tubatool/issues/new";
    private string _errorDetail = "";
    private string _systemInfo = "";
    private static string? _cachedSystemInfo;
    private bool _sysInfoExpanded;

    public ErrorWindow()
    {
        InitializeComponent();

        AppWindow.Title = "空岛图吧工具箱 - 错误报告";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.55);
        var height = (int)(screenArea.Height * 0.7);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        var ex = App.ConsumePendingException();
        if (ex is not null)
            SetError(ex);

        LoadSystemInfo();
    }

    private void SetError(Exception ex)
    {
        _errorDetail = $"异常类型：{ex.GetType().FullName}\n" +
                       $"消息：{ex.Message}\n" +
                       $"堆栈：\n{ex.StackTrace}";

        if (ex.InnerException is not null)
        {
            _errorDetail += $"\n\n内部异常：{ex.InnerException.GetType().FullName}\n" +
                            $"消息：{ex.InnerException.Message}\n" +
                            $"堆栈：\n{ex.InnerException.StackTrace}";
        }

        ErrorText.Text = _errorDetail;
    }

    private void LoadSystemInfo()
    {
        try
        {
            var info = _cachedSystemInfo ??= CollectSystemInfo();
            _systemInfo = info;
            SysInfoText.Text = info;

            var firstLine = info.Split('\n')[0];
            SysInfoSummary.Text = firstLine;
        }
        catch
        {
            _systemInfo = "无法收集系统信息";
            SysInfoText.Text = _systemInfo;
            SysInfoSummary.Text = _systemInfo;
        }
    }

    private void SysInfoHeader_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _sysInfoExpanded = !_sysInfoExpanded;
        SysInfoContent.Visibility = _sysInfoExpanded ? Visibility.Visible : Visibility.Collapsed;
        SysInfoChevron.Glyph = _sysInfoExpanded ? "\uE70E" : "\uE70D";
        SysInfoSummary.Visibility = _sysInfoExpanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string CollectSystemInfo()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"应用版本：{GetAppVersion()}");
        sb.AppendLine($"操作系统：{GetWindowsVersion()}");
        sb.AppendLine($"系统架构：{Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "Unknown"}");
        sb.AppendLine($".NET 版本：{Environment.Version}");
        sb.AppendLine($"管理员权限：{(IsRunningAsAdmin() ? "是" : "否")}");

        try
        {
            sb.AppendLine($"处理器：{WmiQuery("Win32_Processor", "Name")}");
            sb.AppendLine($"内存：{GetTotalMemory()}");
            sb.AppendLine($"显卡：{WmiQuery("Win32_VideoController", "Name")}");
            sb.AppendLine($"主板：{WmiQuery("Win32_BaseBoard", "Product")}");
        }
        catch { }

        if (HardwareInfoService.HasCache)
            sb.AppendLine("硬件缓存：已加载");

        return sb.ToString().TrimEnd();
    }

    private static string GetWindowsVersion()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            var build = version.Build;
            var releaseId = "";

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key?.GetValue("DisplayVersion") is string dv)
                    releaseId = dv;
                else if (key?.GetValue("ReleaseId") is string ri)
                    releaseId = ri;
            }
            catch { }

            var name = build >= 26100 ? "Windows 11 24H2"
                     : build >= 22631 ? "Windows 11 23H2"
                     : build >= 22621 ? "Windows 11 22H2"
                     : build >= 22000 ? "Windows 11 21H2"
                     : build >= 19045 ? "Windows 10 22H2"
                     : build >= 19044 ? "Windows 10 21H2"
                     : build >= 19043 ? "Windows 10 21H1"
                     : "Windows";

            if (!string.IsNullOrEmpty(releaseId))
                return $"{name} (Build {build}, {releaseId})";
            return $"{name} (Build {build})";
        }
        catch
        {
            return "Windows (版本未知)";
        }
    }

    private static string GetTotalMemory()
    {
        try
        {
            var gcMem = GC.GetGCMemoryInfo();
            var totalMem = gcMem.TotalAvailableMemoryBytes;
            return $"{totalMem / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
        catch
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var val = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    return $"{val / (1024.0 * 1024.0 * 1024.0):F1} GB";
                }
            }
            catch { }
        }
        return "未知";
    }

    private static string WmiQuery(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            foreach (var obj in searcher.Get())
            {
                var val = obj[propertyName]?.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { }
        return "未知";
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetAppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is not null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_errorDetail);
        Clipboard.SetContent(package);
        CopyButtonText.Text = "已复制";
    }

    private void CopySysInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_systemInfo);
        Clipboard.SetContent(package);
        CopySysInfoButtonText.Text = "已复制";
    }

    private async void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        var reproSteps = ReproStepsBox.Text.Trim();

        if (string.IsNullOrEmpty(reproSteps))
        {
            ReproStepsBox.Header = "⚠️ 复现步骤为必填项";
            var dialog = new ContentDialog
            {
                Title = "请填写复现步骤",
                Content = "提交 Issue 前请描述你遇到此错误时的操作步骤，这能帮助我们快速定位和修复问题。",
                CloseButtonText = "知道了",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await dialog.ShowAsync();
            ReproStepsBox.Focus(FocusState.Programmatic);
            return;
        }

        ReproStepsBox.Header = null;

        var body = Uri.EscapeDataString(
            "## 复现步骤\n\n" + reproSteps + "\n\n" +
            "## 异常信息\n\n```\n" + _errorDetail + "\n```\n\n" +
            "## 系统信息\n\n```\n" + _systemInfo + "\n```\n");
        var url = $"{RepoIssuesUrl}?title=[Bug]+未处理异常&body={body}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(Environment.ProcessPath!);
        Close();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // P1-1：将当前错误报告导出到 app_crash.log（与全局异常日志同一路径），便于用户提交排查。
    private async void ExportLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = BuildReport();
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "app_crash.log");
            System.IO.File.WriteAllText(logPath, report, new System.Text.UTF8Encoding(false));

            var dialog = new ContentDialog
            {
                Title = "日志已导出",
                Content = $"错误日志已保存到：\n{logPath}",
                CloseButtonText = "知道了",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await dialog.ShowAsync();
        }
        catch
        {
        }
    }

    private string BuildReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== 空岛图吧工具箱 错误报告 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();
        sb.AppendLine("【异常详情】");
        sb.AppendLine(_errorDetail);
        sb.AppendLine();
        sb.AppendLine("【系统信息】");
        sb.AppendLine(_systemInfo);
        sb.AppendLine();
        sb.AppendLine("【复现步骤】");
        sb.AppendLine(string.IsNullOrWhiteSpace(ReproStepsBox.Text) ? "（未填写）" : ReproStepsBox.Text.Trim());
        return sb.ToString();
    }
}
