using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ToolsBundleDownloadDialog : ContentDialog
{
    private ToolsBundleUpdateInfo? _updateInfo;
    private bool _isBusy;

    public bool DownloadSucceeded { get; private set; }

    public ToolsBundleDownloadDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
    }

    public void SetDescription(string text)
    {
        DescText.Text = text;
    }

    public async Task ShowDownloadAsync(ToolsBundleUpdateInfo? info = null)
    {
        if (info is not null && info.HasUpdate)
        {
            _updateInfo = info;
            UpdateDescriptionFromInfo(info);
        }
        else
        {
            ResolvingSection.Visibility = Visibility.Visible;
            _ = ResolveAndShowAsync();
        }

        await ShowAsync();
    }

    private void UpdateDescriptionFromInfo(ToolsBundleUpdateInfo info)
    {
        var sizeStr = info.Size > 0 ? ToolsBundleService.FormatSize(info.Size) : "";
        DescText.Text = string.IsNullOrEmpty(sizeStr)
            ? $"发现内核 v{info.Version}，下载完成后即可使用全部功能。"
            : $"发现内核 v{info.Version}（{sizeStr}），下载完成后即可使用全部功能。";
    }

    private async Task ResolveAndShowAsync()
    {
        try
        {
            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            ResolvingSection.Visibility = Visibility.Collapsed;

            if (info is null)
            {
                DescText.Text = $"无法获取内核信息，请检查网络连接后重试。";
                return;
            }

            _updateInfo = info;

            if (!info.HasUpdate)
            {
                DescText.Text = $"当前内核已是最新版本，无需下载。";
                IsPrimaryButtonEnabled = false;
                return;
            }

            UpdateDescriptionFromInfo(info);
            ShowSourceSelection(info);
        }
        catch (Exception ex)
        {
            ResolvingSection.Visibility = Visibility.Collapsed;
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private void ShowSourceSelection(ToolsBundleUpdateInfo info)
    {
        // 两个下载源同时竞赛，无需用户手动选择
        SourceSection.Visibility = Visibility.Visible;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isBusy)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            await StartDownloadAsync();
        }
        finally
        {
            try { deferral.Complete(); } catch { }
        }
    }

    private async Task StartDownloadAsync()
    {
        if (_updateInfo is null || !_updateInfo.HasUpdate)
        {
            _updateInfo = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (_updateInfo is null || !_updateInfo.HasUpdate)
            {
                ErrorBar.Message = $"未找到可用的内核更新。";
                ErrorBar.IsOpen = true;
                return;
            }
            UpdateDescriptionFromInfo(_updateInfo);
            ShowSourceSelection(_updateInfo);
            return;
        }

        // 默认 GitCode，下载失败时自动切换 GitHub 兜底
        var resolver = ToolsBundleService.CreateUrlResolver(_updateInfo, preferGitCode: true);
        var fallbackUrl = !string.IsNullOrEmpty(_updateInfo.GitHubUrl) &&
                          !string.Equals(_updateInfo.GitHubUrl, _updateInfo.GitCodeUrl, StringComparison.OrdinalIgnoreCase)
            ? _updateInfo.GitHubUrl
            : null;

        _isBusy = true;
        IsPrimaryButtonEnabled = false;
        CloseButtonText = null;
        PrimaryButtonText = "已加入队列";
        SourceSection.Visibility = Visibility.Collapsed;

        var toolsDir = ToolsBundleService.GetToolsBundleDir();

        var item = DownloadQueueService.EnqueueWithResolver(
            displayName: "内核 " + (_updateInfo.Version ?? ""),
            urlResolver: resolver,
            destinationPath: toolsDir,
            postProcessor: new ToolsBundleExtractProcessor(_updateInfo.Version),
            description: $"图吧工具箱完整内核",
            glyph: "\uE896",
            fallbackUrl: fallbackUrl);

        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DownloadItem.State))
            {
                DispatcherQueue.TryEnqueue(() => OnDownloadItemStateChanged(item));
            }
            else if (e.PropertyName == nameof(DownloadItem.Progress))
            {
                DispatcherQueue.TryEnqueue(() => OnDownloadItemProgressChanged(item));
            }
        };

        ProgressSection.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true;
        ProgressLabel.Text = "已加入下载队列...";
    }

    private void OnDownloadItemStateChanged(DownloadItem item)
    {
        switch (item.State)
        {
            case DownloadItemState.Downloading:
                ProgressLabel.Text = $"正在下载内核...";
                DownloadProgressBar.IsIndeterminate = false;
                break;
            case DownloadItemState.Processing:
                ProgressLabel.Text = $"正在解压内核...";
                DownloadProgressBar.IsIndeterminate = true;
                PercentText.Text = "解压中";
                SpeedText.Text = "--";
                SizeText.Text = "--";
                TimeText.Text = "--";
                break;
            case DownloadItemState.Completed:
                DownloadSucceeded = true;
                Hide();
                _ = ShowSuccessDialogAsync();
                break;
            case DownloadItemState.Failed:
                ErrorBar.Message = LocalizeFailureMessage(item.ErrorMessage);
                ErrorBar.IsOpen = true;
                IsPrimaryButtonEnabled = true;
                PrimaryButtonText = "重试";
                ProgressSection.Visibility = Visibility.Collapsed;
                _isBusy = false;
                CloseButtonText = "跳过";
                break;
        }
    }

    private static string LocalizeFailureMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "内核下载失败，请重试。";

        // 内部异常为 UnauthorizedAccessException 时给出中文提示
        if (message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractQuotedPath(message);
            return string.IsNullOrEmpty(path)
                ? "内核安装失败：目标文件被占用或只读，请关闭正在运行的工具（如 DirectX Repair）后重试。"
                : $"内核安装失败：无法写入 {path}（文件被占用或只读）。请关闭正在运行的工具（如 DirectX Repair）后重试。";
        }

        return message;
    }

    private static string? ExtractQuotedPath(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return null;
        var end = message.IndexOf('\'', start + 1);
        if (end <= start) return null;
        return message[(start + 1)..end];
    }

    private void OnDownloadItemProgressChanged(DownloadItem item)
    {
        if (item.Progress is null) return;
        var p = item.Progress;

        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = p.Percentage;
        PercentText.Text = $"{p.Percentage:F1}%";
        SpeedText.Text = DownloadQueueService.FormatSpeed(p.SpeedMbps);
        SizeText.Text = $"{DownloadQueueService.FormatSize(p.BytesReceived)} / {DownloadQueueService.FormatSize(p.TotalBytes)}";
        TimeText.Text = DownloadQueueService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowSuccessDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = "完成",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        var border = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Green),
            CornerRadius = new CornerRadius(12)
        };
        iconBorder.Child = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 24,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"内核下载完成！",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = "已解压到工具目录，刷新后可直接使用全部工具。",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        if (_updateInfo is not null)
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = $"版本：v{_updateInfo.Version}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        border.Child = grid;
        stack.Children.Add(border);
        dialog.Content = stack;

        await dialog.ShowAsync();
    }
}
