using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class UpdateBanner : UserControl
{
    private UpdateInfo? _updateInfo;
    private bool _isDownloaded;
    private bool _isDownloading;

    public UpdateBanner()
    {
        InitializeComponent();
    }

    public void ShowUpdateAvailable(UpdateInfo update)
    {
        _updateInfo = update;
        _isDownloaded = false;
        _isDownloading = false;

        BannerText.Text = $"发现新版本 v{update.Version}";
        ActionButton.Content = "开始更新";
        ActionButton.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloading()
    {
        _isDownloading = true;
        _isDownloaded = false;

        BannerText.Text = "正在下载更新...";
        BannerText.Visibility = Visibility.Collapsed;
        DownloadProgressBar.Visibility = Visibility.Visible;
        ActionButton.Content = "下载中";
        ActionButton.IsEnabled = false;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloadProgress(double percentage)
    {
        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = percentage;
    }

    public void ShowDownloadComplete()
    {
        _isDownloaded = true;
        _isDownloading = false;

        // 下载完成的文件可能已被安全软件/清理工具移除或损坏，先校验再提示"已就绪"
        if (!UpdateService.IsPendingUpdateReady())
        {
            ShowDownloadFailed("更新文件无效或已被清理，请重新下载");
            return;
        }

        BannerText.Text = "更新已就绪，点击开始安装";
        BannerText.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ActionButton.Content = "立即安装";
        ActionButton.IsEnabled = true;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloadFailed(string error)
    {
        _isDownloading = false;
        _isDownloaded = false; // 失败后"重试"应重新下载而不是再次启动失效文件

        BannerText.Text = $"更新下载失败：{error}";
        BannerText.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ActionButton.Content = "重试";
        ActionButton.IsEnabled = true;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloaded)
        {
            if (UpdateService.LaunchUpdateFromItem()) return;

            // 启动失败：文件已被移除/损坏，给出恢复选项而不是崩溃
            await ShowLaunchFailedDialogAsync();
            return;
        }

        if (_isDownloading) return;

        if (_updateInfo is not null)
        {
            var item = UpdateService.AutoDownloadUpdate(_updateInfo);
            if (item is not null)
            {
                ShowDownloading();
                item.PropertyChanged += OnDownloadItemPropertyChanged;
            }
        }
    }

    /// <summary>更新安装包无法启动时的恢复对话框：重新下载 / 打开下载文件夹 / 关闭。</summary>
    private async Task ShowLaunchFailedDialogAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "更新文件不可用",
                Content = new TextBlock
                {
                    Text = "更新安装包已被移除或损坏（可能受安全软件、清理工具或磁盘错误影响），无法启动。请重新下载后再安装。",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                },
                PrimaryButtonText = "重新下载",
                SecondaryButtonText = "打开下载文件夹",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                UpdateService.ClearPendingUpdate();
                if (_updateInfo is not null)
                {
                    var item = UpdateService.AutoDownloadUpdate(_updateInfo);
                    if (item is not null)
                    {
                        ShowDownloading();
                        item.PropertyChanged += OnDownloadItemPropertyChanged;
                    }
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                UpdateService.OpenUpdateFolder();
            }
        }
        catch
        {
            // 对话框自身异常不影响主流程
        }
    }

    private void OnDownloadItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DownloadItem item) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(DownloadItem.State):
                    switch (item.State)
                    {
                        case DownloadItemState.Completed:
                            item.PropertyChanged -= OnDownloadItemPropertyChanged;
                            ShowDownloadComplete();
                            break;
                        case DownloadItemState.Failed:
                            item.PropertyChanged -= OnDownloadItemPropertyChanged;
                            ShowDownloadFailed(item.ErrorMessage ?? "未知错误");
                            break;
                    }
                    break;
                case nameof(DownloadItem.Progress):
                    if (item.Progress is not null && item.Progress.TotalBytes > 0)
                    {
                        ShowDownloadProgress(item.Progress.Percentage);
                    }
                    break;
            }
        });
    }

    private void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        WhatsNewWindow.Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        UpdateService.ClearPendingUpdate();
    }
}
