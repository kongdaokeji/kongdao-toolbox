using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「恶意软件沙盒」页面：上部为 Sandboxie-Plus 安装包下载区域（按系统架构自动匹配，
/// 对接全局下载队列），下部为沙盒使用教程。
/// 主按钮三态：未下载 →「下载安装包」；安装包已下载 →「安装」；已安装 →「打开」。
/// </summary>
public sealed partial class SandboxiePage : Page
{
    private const string ReleaseBaseUrl = "https://gitcode.com/luolangaga/sandboxieddd/releases/download/new";
    private const string VersionTag = "v1.18.3";

    private static readonly (string Arch, string DisplayName, string FileName, string Size)[] ArchOptions =
    [
        ("x64", "x64（64 位系统）", $"Sandboxie-Plus-x64-{VersionTag}.exe", "约 23.7 MB"),
        ("arm64", "ARM64（骁龙等）", $"Sandboxie-Plus-ARM64-{VersionTag}.exe", "约 21.3 MB"),
    ];

    private (string Arch, string FileName, string Size) _selected;
    private bool _x86Blocked;
    private bool _initialized;
    private bool _suppressToggle;
    private DispatcherQueue? _dq;

    private enum ToolState { NotDownloaded, Downloaded, Installed }

    private ToolState _state = ToolState.NotDownloaded;

    private static string InstallerDir => Path.Combine(Path.GetTempPath(), "TubaWinUi3_Sandboxie");

    public SandboxiePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dq = DispatcherQueue.GetForCurrentThread();
        // 下载队列变化时刷新按钮状态（下载完成 → 按钮变「安装」）
        DownloadQueueService.QueueChanged += OnQueueChanged;
        InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DownloadQueueService.QueueChanged -= OnQueueChanged;
    }

    private void InitializeAsync()
    {
        var osArch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        foreach (var opt in ArchOptions)
            ArchCombo.Items.Add(opt.DisplayName);

        var bestIndex = Array.FindIndex(ArchOptions, o => o.Arch == osArch);
        if (bestIndex >= 0)
        {
            ArchCombo.SelectedIndex = bestIndex;
        }
        else
        {
            // x86 等没有对应安装包的架构：展示提示并允许手动查看可用选项
            ArchCombo.SelectedIndex = 0;
            SizeText.Text = "";
            _x86Blocked = true;
            ArchWarnBar.Severity = InfoBarSeverity.Error;
            ArchWarnBar.Title = "当前系统为 32 位（x86）";
            ArchWarnBar.Message = "发布页未提供 x86 安装包，且 32 位系统无法运行 x64 安装包，无法在此设备上安装。";
            ArchWarnBar.IsOpen = true;
        }

        _initialized = true;
        RefreshState();
    }

    private void ArchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = ArchCombo.SelectedIndex >= 0 ? ArchCombo.SelectedIndex : 0;
        var opt = ArchOptions[index];
        _selected = (opt.Arch, opt.FileName, opt.Size);
        SizeText.Text = opt.Size;

        // 架构切换后，已下载的安装包文件名随之变化，需重新判定按钮状态
        if (_initialized)
            RefreshState();
    }

    private void OnQueueChanged()
    {
        // QueueChanged 可能从后台线程触发，切回 UI 线程刷新
        _dq?.TryEnqueue(RefreshState);
    }

    /// <summary>根据「已安装 / 安装包已下载 / 未下载」刷新主按钮文案与提示。</summary>
    private void RefreshState()
    {
        var installedExe = SandboxieShellMenuService.GetSandboxiePlusExe();

        if (installedExe is not null)
        {
            _state = ToolState.Installed;
            ActionText.Text = "打开";
            ActionIcon.Glyph = "\uE768";
            ActionBtn.IsEnabled = true;
            ArchWarnBar.Severity = InfoBarSeverity.Success;
            ArchWarnBar.Title = "已安装 Sandboxie-Plus";
            ArchWarnBar.Message = "点击「打开」直接启动 Sandboxie-Plus；如需重新安装，可先卸载后再下载安装包。";
            ArchWarnBar.IsOpen = true;
            DownloadHint.Opacity = 0;
            RefreshShellMenuToggle();
            return;
        }

        if (_x86Blocked)
        {
            ActionBtn.IsEnabled = false;
            ShellMenuToggle.IsEnabled = false;
            return;
        }

        RefreshShellMenuToggle();

        if (File.Exists(GetInstallerPath()))
        {
            _state = ToolState.Downloaded;
            ActionText.Text = "安装";
            ActionIcon.Glyph = "\uE768";
            ActionBtn.IsEnabled = true;
            ArchWarnBar.IsOpen = false;
            DownloadHint.Text = "安装包已就绪，点击「安装」运行安装程序；安装完成后此按钮会变为「打开」。";
            DownloadHint.Opacity = 1;
        }
        else
        {
            _state = ToolState.NotDownloaded;
            ActionText.Text = "下载安装包";
            ActionIcon.Glyph = "\uE896";
            ActionBtn.IsEnabled = true;
            ArchWarnBar.IsOpen = false;
            DownloadHint.Opacity = 0;
        }
    }

    private void ActionBtn_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case ToolState.Installed:
                Launch(SandboxieShellMenuService.GetSandboxiePlusExe());
                break;

            case ToolState.Downloaded:
                Launch(GetInstallerPath());
                break;

            case ToolState.NotDownloaded:
                DownloadInstaller();
                break;
        }
    }

    private void DownloadInstaller()
    {
        if (string.IsNullOrEmpty(_selected.FileName)) return;

        var url = $"{ReleaseBaseUrl}/{_selected.FileName}";

        DownloadQueueService.Enqueue(
            displayName: $"Sandboxie-Plus 安装包（{_selected.Arch.ToUpperInvariant()}）",
            downloadUrl: url,
            destinationPath: InstallerDir,
            postProcessor: new InstallerLaunchProcessor(),
            description: $"恶意软件沙盒 Sandboxie-Plus 安装包，{_selected.Size}，下载完成后自动启动安装程序",
            glyph: "\uEA18");

        DownloadHint.Text = "已加入下载队列，可点击标题栏下载图标查看进度；下载完成后按钮会自动变为「安装」。";
        DownloadHint.Opacity = 1;
    }

    private string GetInstallerPath()
    {
        if (string.IsNullOrEmpty(_selected.FileName)) return string.Empty;
        return Path.Combine(InstallerDir, _selected.FileName);
    }

    private static void Launch(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>同步「文件夹背景右键菜单」开关：需已安装 Sandboxie-Plus 才可开启。</summary>
    private void RefreshShellMenuToggle()
    {
        var installed = SandboxieShellMenuService.GetSandboxieDir() is not null;
        _suppressToggle = true;
        ShellMenuToggle.IsEnabled = installed;
        ShellMenuToggle.IsOn = installed && SandboxieShellMenuService.IsRegistered();
        _suppressToggle = false;
    }

    private void ShellMenuToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;

        if (ShellMenuToggle.IsOn)
        {
            if (SandboxieShellMenuService.Enable())
            {
                DownloadHint.Text = "已在文件夹背景右键菜单注册「在沙箱中运行」，可在资源管理器中右键文件夹空白处使用。";
                DownloadHint.Opacity = 1;
            }
            else
            {
                _suppressToggle = true;
                ShellMenuToggle.IsOn = false;
                _suppressToggle = false;
                DownloadHint.Text = "注册右键菜单失败：未找到 Sandboxie-Plus 安装目录（需含 Start.exe）。";
                DownloadHint.Opacity = 1;
            }
        }
        else
        {
            SandboxieShellMenuService.Disable();
            DownloadHint.Text = "已移除文件夹背景右键菜单中的「在沙箱中运行」。";
            DownloadHint.Opacity = 1;
        }
    }
}
