using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class GitHubDownloadWindow : Window
{
    private readonly CancellationTokenSource _cts = new();

    public event Action? DownloadSucceeded;

    public enum DownloadMode
    {
        GitHubRelease,
        CommunityTool
    }

    private readonly DownloadMode _mode;
    private readonly string _toolName;

    // GitHub release mode
    private readonly GitHubAssetInfo? _asset;
    private readonly string? _versionTag;
    private string _bestUrl = "";
    private readonly string? _portableDir;

    // Community tool mode
    private readonly CommunityTool? _communityTool;
    private readonly List<(string Name, string Url)> _communitySources = [];

    private bool _downloadStarted;

    public GitHubDownloadWindow(string toolName, string description, GitHubAssetInfo asset, string versionTag,
        string? warningText = null, string? detailInfo = null, string? portableDir = null)
    {
        InitializeComponent();

        _mode = DownloadMode.GitHubRelease;
        _toolName = toolName;
        _asset = asset;
        _versionTag = versionTag;
        _bestUrl = asset.OriginalUrl;
        _portableDir = portableDir;

        InfoTitleText.Text = $"{toolName} v{versionTag.TrimStart('v')}";
        InfoDescText.Text = description;
        InfoDetailText.Text = detailInfo ?? $"文件：{asset.Name}（{ToolDownloaderService.FormatSize(asset.Size)}）";

        if (warningText is not null)
        {
            WarningText.Text = warningText;
            WarningCard.Visibility = Visibility.Visible;
        }

        InitWindow();
        StartSpeedTest();
    }

    public GitHubDownloadWindow(CommunityTool tool)
    {
        InitializeComponent();

        _mode = DownloadMode.CommunityTool;
        _toolName = tool.Name;
        _communityTool = tool;

        var versionText = tool.Version ?? "未知";
        var authorName = tool.Author ?? "未知用户";
        InfoTitleText.Text = tool.Name;
        InfoDescText.Text = tool.Description ?? "无描述";
        InfoDetailText.Text = $"分类：{tool.Category}  ·  版本：{versionText}  ·  提交者：{authorName}";

        WarningText.Text = $"社区包无法保证其安全性，图吧工具箱不对社区包负责，但会尽量避免违规工具。如果你信任 {authorName} 可以开始下载。";
        WarningCard.Visibility = Visibility.Visible;

        InitWindow();
        SetupCommunitySourceSelector();
    }

    private void InitWindow()
    {
        AppWindow.Title = $"图吧工具箱 - 下载 {_toolName}";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.6);
        var height = (int)(screenArea.Height * 0.8);
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
    }

    private void SetupCommunitySourceSelector()
    {
        SpeedCard.Visibility = Visibility.Collapsed;
        SourceCard.Visibility = Visibility.Visible;
        SubtitleText.Text = "选择下载源并开始下载";

        var tool = _communityTool!;
        var sources = CommunityToolService.GetAllDownloadUrls(tool);
        _communitySources.Clear();
        _communitySources.AddRange(sources);

        SourceRadioButtons.Items.Clear();
        foreach (var (name, _) in sources)
        {
            SourceRadioButtons.Items.Add(name);
        }

        if (sources.Count > 0)
        {
            SourceRadioButtons.SelectedIndex = 0;
        }

        DownloadButton.IsEnabled = sources.Count > 0;
    }

    private string GetSelectedCommunityUrl()
    {
        if (_communitySources.Count == 0) return "";
        var idx = Math.Max(0, SourceRadioButtons.SelectedIndex);
        if (idx < _communitySources.Count)
            return _communitySources[idx].Url;
        return _communitySources[0].Url;
    }

    private void StartSpeedTest()
    {
        var asset = _asset!;

        _ = Task.Run(async () =>
        {
            var proxyResults = new List<ProxySpeedResult>();
            var semaphore = new SemaphoreSlim(6);
            var testTasks = new List<Task<ProxySpeedResult>>();

            testTasks.Add(GitHubReleaseService.TestSingleProxyAsync("GitHub 直连", asset.OriginalUrl));

            foreach (var proxy in GitHubReleaseService.GitHubProxies)
            {
                var proxyName = new Uri(proxy).Host;
                var proxyedUrl = GitHubReleaseService.ProxyUrl(proxy, asset.OriginalUrl);
                testTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try { return await GitHubReleaseService.TestSingleProxyAsync(proxyName, proxyedUrl); }
                    finally { semaphore.Release(); }
                }));
            }

            var remaining = testTasks.ToList();
            var finishedCount = 0;
            var totalCount = remaining.Count;

            while (remaining.Count > 0)
            {
                var finished = await Task.WhenAny(remaining);
                remaining.Remove(finished);
                finishedCount++;

                try
                {
                    var r = await finished;
                    proxyResults.Add(r);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        var color = r.Ok
                            ? Color.FromArgb(255, 74, 222, 128)
                            : Color.FromArgb(255, 248, 113, 113);
                        var icon = r.Ok ? "✓" : "✗";
                        var msText = r.Ok ? $"{r.Ms:F0}ms" : "不可用";
                        SpeedListPanel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                        {
                            FontSize = 11,
                            Foreground = new SolidColorBrush(color),
                            Text = $"{icon} {r.Name}  —  {msText}"
                        });

                        var okCount = proxyResults.Count(x => x.Ok);
                        SpeedStatusText.Text = $"正在测速... {finishedCount}/{totalCount}（{okCount} 可用）";
                    });
                }
                catch { }
            }

            var best = proxyResults.Where(r => r.Ok).OrderBy(r => r.Ms).FirstOrDefault();
            if (best is not null && best.Url is not null)
                _bestUrl = best.Url;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (best is not null)
                {
                    SpeedIcon.Glyph = "\uEC61";
                    SpeedStatusText.Text = $"已选择：{best.Name}（{best.Ms:F0}ms）";
                    SpeedStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 74, 222, 128));
                }
                else
                {
                    SpeedIcon.Glyph = "\uE783";
                    SpeedStatusText.Text = "所有代理不可用，将尝试 GitHub 直连";
                    SpeedStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 251, 146, 60));
                }
                DownloadButton.IsEnabled = true;
            });
        }, _cts.Token);
    }

    private void StartCommunityDownload()
    {
        var sourceUrl = GetSelectedCommunityUrl();
        SourceCard.Visibility = Visibility.Collapsed;
        WarningCard.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new FontIcon { FontSize = 14, Glyph = "\uE711" },
                new TextBlock { Text = "取消下载" }
            }
        };

        ProgressCard.Visibility = Visibility.Visible;
        PercentText.Visibility = Visibility.Visible;
        StatsRow.Visibility = Visibility.Visible;

        _downloadStarted = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<ToolDownloadProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            if (p.Percentage > 0)
                            {
                                DownloadProgressBar.IsIndeterminate = false;
                                DownloadProgressBar.Value = p.Percentage;
                            }
                            PercentText.Text = $"{p.Percentage:F1}%";
                            SpeedValText.Text = ToolDownloaderService.FormatSpeed(p.SpeedMbps);
                            SizeValText.Text = $"{ToolDownloaderService.FormatSize(p.BytesReceived)} / {ToolDownloaderService.FormatSize(p.TotalBytes)}";
                            TimeValText.Text = ToolDownloaderService.FormatTime(p.EstimatedRemaining);
                        }
                        catch { }
                    });
                });

                var installPath = await CommunityToolService.InstallPluginAsync(
                    _communityTool!, sourceUrl, progress, _cts.Token);

                DispatcherQueue.TryEnqueue(() =>
                {
                    _communityTool!.InstallStatus = CommunityToolInstallStatus.Installed;
                    _communityTool.LocalPath = CommunityToolService.GetLocalPath(_communityTool);
                });

                DispatcherQueue.TryEnqueue(() => ShowSuccess($"已安装到：{installPath}"));
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => ShowError("下载已取消"));
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (string.IsNullOrWhiteSpace(msg)) msg = ex.GetType().Name;
                if (ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
                    msg += $"\n{inner.Message}";
                DispatcherQueue.TryEnqueue(() => ShowError(msg));
            }
        }, _cts.Token);
    }

    private void StartGitHubDownload()
    {
        SpeedCard.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new FontIcon { FontSize = 14, Glyph = "\uE711" },
                new TextBlock { Text = "取消下载" }
            }
        };

        ProgressCard.Visibility = Visibility.Visible;
        PercentText.Visibility = Visibility.Visible;
        StatsRow.Visibility = Visibility.Visible;

        _downloadStarted = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<ToolDownloadProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            DownloadProgressBar.Value = p.Percentage;
                            PercentText.Text = $"{p.Percentage:F1}%";
                            SpeedValText.Text = ToolDownloaderService.FormatSpeed(p.SpeedMbps);
                            SizeValText.Text = $"{ToolDownloaderService.FormatSize(p.BytesReceived)} / {ToolDownloaderService.FormatSize(p.TotalBytes)}";
                            TimeValText.Text = ToolDownloaderService.FormatTime(p.EstimatedRemaining);
                        }
                        catch { }
                    });
                });

                var destDir = _portableDir ?? Path.Combine(Path.GetTempPath(), $"TubaWinUi3_{_toolName.Replace(" ", "_")}");
                var filePath = await ToolDownloaderService.DownloadToFileAsync(
                    _bestUrl, destDir, _asset!.Name, progress, _cts.Token);

                try
                {
                    Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                }
                catch
                {
                    DispatcherQueue.TryEnqueue(() => ShowSuccess($"{_toolName} 已下载到：{filePath}\n请手动运行。"));
                    return;
                }

                var locationText = _portableDir is not null
                    ? $"{_toolName} 已下载到：{filePath}\n下次可直接从工具箱启动。"
                    : $"{_toolName} 下载完成，已启动安装程序。";
                DispatcherQueue.TryEnqueue(() => ShowSuccess(locationText));
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => ShowError("下载已取消"));
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (string.IsNullOrWhiteSpace(msg)) msg = ex.GetType().Name;
                if (ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
                    msg += $"\n{inner.Message}";
                DispatcherQueue.TryEnqueue(() => ShowError(msg));
            }
        }, _cts.Token);
    }

    private void ShowSuccess(string detail)
    {
        HeaderIcon.Glyph = "\uE73E";
        HeaderIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 15, 123, 15));
        IconBorder.Background = new SolidColorBrush(Color.FromArgb(51, 15, 123, 15));
        TitleText.Text = "下载完成";
        SubtitleText.Text = $"{_toolName} 下载成功";

        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Visible;
        SuccessDetailText.Text = detail;
        ErrorCard.Visibility = Visibility.Collapsed;

        CancelButton.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;

        DownloadSucceeded?.Invoke();
    }

    private void ShowError(string error)
    {
        HeaderIcon.Glyph = "\uE783";
        HeaderIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 68, 68));
        IconBorder.Background = new SolidColorBrush(Color.FromArgb(51, 255, 68, 68));
        TitleText.Text = "下载失败";
        SubtitleText.Text = "下载过程中出现错误";

        SpeedCard.Visibility = Visibility.Collapsed;
        SourceCard.Visibility = Visibility.Collapsed;
        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Visible;
        ErrorText.Text = error;

        CancelButton.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadStarted) return;

        if (_mode == DownloadMode.CommunityTool)
            StartCommunityDownload();
        else
            StartGitHubDownload();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        HeaderIcon.Glyph = "\uE896";
        HeaderIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 102, 204));
        IconBorder.Background = new SolidColorBrush(Color.FromArgb(51, 0, 102, 204));
        TitleText.Text = "正在下载";

        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Collapsed;

        CancelButton.Visibility = Visibility.Visible;
        CancelButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new FontIcon { FontSize = 14, Glyph = "\uE711" },
                new TextBlock { Text = "取消" }
            }
        };
        DownloadButton.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = true;
        CloseButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;

        _downloadStarted = false;

        if (_mode == DownloadMode.GitHubRelease)
        {
            SubtitleText.Text = "正在测速选择最佳下载源...";
            SpeedCard.Visibility = Visibility.Visible;
            SpeedListPanel.Children.Clear();
            SpeedIcon.Glyph = "\uEC27";
            SpeedStatusText.Text = "正在测速选择最佳下载源...";
            SourceCard.Visibility = Visibility.Collapsed;
            StartSpeedTest();
        }
        else
        {
            SubtitleText.Text = "选择下载源并开始下载";
            SpeedCard.Visibility = Visibility.Collapsed;
            WarningCard.Visibility = Visibility.Visible;
            SetupCommunitySourceSelector();
        }
    }
}
