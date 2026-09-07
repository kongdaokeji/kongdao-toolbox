using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Models;

public sealed class CommunityTool : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public required string Category { get; init; }
    public string? Publisher { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public string? Icon { get; init; }
    public string? DownloadUrl { get; init; }
    public string? DownloadFilter { get; init; }
    public string? LaunchTarget { get; init; }
    public IReadOnlyList<CommunityArchVariant>? ArchVariants { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public string? Homepage { get; init; }
    public string? RepoPath { get; init; }
    public string? File { get; init; }
    public string? FileSha { get; set; }

    public string TagsText => string.Join(" ", Tags);

    private CommunityToolInstallStatus _installStatus;
    public CommunityToolInstallStatus InstallStatus
    {
        get => _installStatus;
        set { _installStatus = value; OnPropertyChanged(nameof(InstallStatus)); OnPropertyChanged(nameof(InstallStatusText)); OnPropertyChanged(nameof(CanInstall)); OnPropertyChanged(nameof(CanLaunch)); OnPropertyChanged(nameof(LaunchButtonText)); OnPropertyChanged(nameof(InstallStatusColor)); }
    }

    public string InstallStatusText => InstallStatus switch
    {
        CommunityToolInstallStatus.NotInstalled => "未安装",
        CommunityToolInstallStatus.Installed => "已安装",
        CommunityToolInstallStatus.UpdateAvailable => "可更新",
        _ => "未知"
    };

    public bool CanInstall => InstallStatus == CommunityToolInstallStatus.NotInstalled || InstallStatus == CommunityToolInstallStatus.UpdateAvailable;
    public bool CanLaunch => InstallStatus == CommunityToolInstallStatus.Installed;

    public string LaunchButtonText => InstallStatus switch
    {
        CommunityToolInstallStatus.NotInstalled => "下载",
        CommunityToolInstallStatus.Installed => "打开",
        CommunityToolInstallStatus.UpdateAvailable => "更新",
        _ => "下载"
    };

    public Color InstallStatusColor => InstallStatus switch
    {
        CommunityToolInstallStatus.Installed => Color.FromArgb(255, 74, 222, 128),
        CommunityToolInstallStatus.UpdateAvailable => Color.FromArgb(255, 251, 146, 60),
        _ => Color.FromArgb(255, 160, 160, 160)
    };

    public SolidColorBrush InstallStatusBrush => new(InstallStatusColor);
    public SolidColorBrush InstallStatusBrushFaint => new() { Color = InstallStatusColor, Opacity = 0.15 };

    private string? _localPath;
    public string? LocalPath
    {
        get => _localPath;
        set { _localPath = value; OnPropertyChanged(nameof(LocalPath)); }
    }

    private string? _iconPath;
    public string? IconPath
    {
        get => _iconPath;
        set { _iconPath = value; OnPropertyChanged(nameof(IconPath)); }
    }

    private bool _isAuthor;
    public bool IsAuthor
    {
        get => _isAuthor;
        set { _isAuthor = value; OnPropertyChanged(nameof(IsAuthor)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(DeleteButtonVisibility)); }
    }

    public bool CanDelete => IsAuthor && GitHubAuthService.IsLoggedIn;

    public Visibility DeleteButtonVisibility => CanDelete ? Visibility.Visible : Visibility.Collapsed;

    public string? IconGlyph
    {
        get
        {
            if (Category.Contains("处理器")) return "\uEEA1";
            if (Category.Contains("显卡")) return "\uF211";
            if (Category.Contains("显示器")) return "\uE7F4";
            if (Category.Contains("硬盘")) return "\uEDA2";
            if (Category.Contains("内存")) return "\uEEA0";
            if (Category.Contains("外设")) return "\uE962";
            if (Category.Contains("游戏")) return "\uE7FC";
            if (Category.Contains("声卡")) return "\uE7F5";
            if (Category.Contains("网卡")) return "\uEDA3";
            if (Category.Contains("综合")) return "\uEC4E";
            if (Category.Contains("系统")) return "\uE977";
            return "\uE8B7";
        }
    }

    public Visibility IconPathVisibility => string.IsNullOrEmpty(IconPath) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => string.IsNullOrEmpty(IconPath) ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CommunityArchVariant
{
    public required string File { get; init; }
    public required string Arch { get; init; }
}

public enum CommunityToolInstallStatus
{
    NotInstalled,
    Installed,
    UpdateAvailable
}
