#nullable enable

using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Resources;
using TubaWinUi3.Compatible.Models;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.PECompatible;

internal sealed class MainWindow
{
    private static bool IsLightTheme => AppSettings.GetBool("ThemeLight", false);
    private static Color Canvas => IsLightTheme ? Color.FromRgb(244, 245, 248) : Color.FromRgb(20, 22, 27);
    private static Color Surface => IsLightTheme ? Color.FromRgb(255, 255, 255) : Color.FromRgb(29, 32, 39);
    private static Color SurfaceAlt => IsLightTheme ? Color.FromRgb(228, 230, 236) : Color.FromRgb(37, 40, 48);
    private static Color SecondaryText => IsLightTheme ? Color.FromRgb(96, 99, 108) : Color.FromRgb(165, 170, 181);

    private ContentControl _pageHost = null!;
    private TextBlock _statusText = null!;
    private TextBox _searchBox = null!;
    private Window _window = null!;
    private string? _activeCategory;
    private bool _showHardwarePage;
    private bool _showFavorites;
    private int _loadVersion;
    private DispatcherTimer? _uptimeTimer;
    private TextBlock? _uptimeText;

    public Window CreateWindow()
    {
        _window = new Window()
            .Title("空岛图吧工具箱 PE 兼容版")
            .Resizable(1180, 780)
            .StartCenterScreen()
            .Padding(0)
            .Content(BuildShell())
            .OnLoaded(() => _ = LoadToolsAsync())
            .OnClosed(StopUptimeTimer);

        return _window;
    }

    private Element BuildShell()
    {
        _pageHost = new ContentControl();
        _statusText = new TextBlock();
        _searchBox = new TextBox()
            .Placeholder("搜索工具名称、路径或标签")
            .Width(340)
            .Height(34)
            .OnTextChanged(OnSearchChanged);

        var header = new Border
        {
            Background = Surface,
            Padding = new Thickness(16, 10, 16, 10),
            Child = new StackPanel()
                .Horizontal()
                .Spacing(12)
                .Children(
                    new TextBlock()
                        .Text("空岛图吧工具箱 PE")
                        .FontSize(18)
                        .Bold()
                        .VerticalAlignment(VerticalAlignment.Center),
                    new TextBlock()
                        .Text("GDI 兼容渲染")
                        .Foreground(SecondaryText)
                        .VerticalAlignment(VerticalAlignment.Center),
                    _searchBox,
                    new Button()
                        .Content(IsLightTheme ? "深色主题" : "浅色主题")
                        .OnClick(ToggleTheme)
                )
        };
        DockPanel.SetDock(header, Dock.Top);

        var footer = new Border
        {
            Background = Surface,
            Padding = new Thickness(12, 7, 12, 7),
            Child = _statusText
                .Text("就绪")
                .Foreground(SecondaryText)
                .FontSize(11)
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var navigation = BuildNavigation();
        var sidebar = new Border
        {
            Width = 218,
            Background = Surface,
            Child = new ScrollViewer()
                .VerticalScroll(ScrollMode.Auto)
                .Content(navigation)
        };
        DockPanel.SetDock(sidebar, Dock.Left);

        var body = new DockPanel { LastChildFill = true, Spacing = 0 };
        body.Add(sidebar);
        body.Add(_pageHost);

        var root = new DockPanel { LastChildFill = true, Spacing = 0 };
        root.Add(header);
        root.Add(footer);
        root.Add(body);
        return new Border { Background = Canvas, Child = root };
    }

    private StackPanel BuildNavigation()
    {
        var navigation = new StackPanel()
            .Vertical()
            .Spacing(6)
            .Padding(12);

        navigation.Add(NavigationButton("全部工具", () =>
        {
            _showHardwarePage = false;
            _showFavorites = false;
            _activeCategory = null;
            StopUptimeTimer();
            _searchBox.Text = string.Empty;
            _ = LoadToolsAsync();
        }));
        navigation.Add(NavigationButton("硬件信息", () =>
        {
            _showHardwarePage = true;
            _showFavorites = false;
            _activeCategory = null;
            _searchBox.Text = string.Empty;
            _ = LoadHardwareAsync(forceRefresh: false);
        }));
        navigation.Add(NavigationButton("收藏工具", () =>
        {
            _showHardwarePage = false;
            _showFavorites = true;
            _activeCategory = null;
            StopUptimeTimer();
            _searchBox.Text = string.Empty;
            _ = LoadToolsAsync();
        }));

        navigation.Add(new TextBlock()
            .Text("工具分类")
            .FontSize(11)
            .Foreground(SecondaryText)
            .Margin(8, 14, 0, 4));

        foreach (var category in ToolCatalog.GetCategories())
        {
            var selectedCategory = category;
            navigation.Add(NavigationButton(category, () =>
            {
                _showHardwarePage = false;
                _showFavorites = false;
                _activeCategory = selectedCategory;
                StopUptimeTimer();
                _searchBox.Text = string.Empty;
                _ = LoadToolsAsync();
            }));
        }

        return navigation;
    }

    private static Button NavigationButton(string title, Action action)
    {
        return new Button()
            .Content(title)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .OnClick(action);
    }

    private void ToggleTheme()
    {
        var light = !AppSettings.GetBool("ThemeLight", false);
        var query = _searchBox.Text;
        AppSettings.Set("ThemeLight", light);
        Application.Current.SetTheme(light ? ThemeVariant.Light : ThemeVariant.Dark);
        _window.Content = BuildShell();
        _searchBox.Text = query;
        if (_showHardwarePage)
        {
            _ = LoadHardwareAsync(forceRefresh: false);
        }
        else
        {
            _ = LoadToolsAsync();
        }
    }

    private void OnSearchChanged(string query)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            _showHardwarePage = false;
            _showFavorites = false;
            _activeCategory = null;
            StopUptimeTimer();
        }
        _ = LoadToolsAsync();
    }

    private async Task LoadToolsAsync()
    {
        var requestId = ++_loadVersion;
        var query = _searchBox.Text.Trim();
        var category = _activeCategory;
        var favoritesOnly = _showFavorites;
        _statusText.Text = "正在扫描工具目录…";

        try
        {
            var tools = await Task.Run<IReadOnlyList<ToolItem>>(() =>
            {
                IReadOnlyList<ToolItem> result;
                if (!string.IsNullOrEmpty(query))
                {
                    result = ToolCatalog.Search(query);
                }
                else if (favoritesOnly)
                {
                    result = ToolCatalog.GetAllToolsDeduped()
                        .Where(tool => FavoritesService.IsFavorite(tool.EffectivePath))
                        .ToList();
                }
                else if (category == null)
                {
                    result = ToolCatalog.GetAllToolsDeduped();
                }
                else
                {
                    result = ToolCatalog.GetTools(category);
                }

                try
                {
                    ToolIconService.LoadIcons(result);
                }
                catch
                {
                }
                return result;
            });

            if (requestId != _loadVersion || _showHardwarePage)
            {
                return;
            }

            _pageHost.Content = BuildToolPage(tools, query, category, favoritesOnly);
            var title = !string.IsNullOrEmpty(query) ? "搜索结果" :
                favoritesOnly ? "收藏工具" : category ?? "全部工具";
            _statusText.Text = $"{title} · {tools.Count} 个工具 · {ToolCatalog.ToolsRoot}";
        }
        catch (Exception ex)
        {
            if (requestId == _loadVersion)
            {
                _pageHost.Content = MessagePanel("工具目录读取失败", ex.Message);
                _statusText.Text = "工具目录读取失败";
            }
        }
    }

    private Element BuildToolPage(
        IReadOnlyList<ToolItem> tools,
        string query,
        string? category,
        bool favoritesOnly)
    {
        var title = !string.IsNullOrEmpty(query) ? "搜索结果" :
            favoritesOnly ? "收藏工具" : category ?? "全部工具";

        var heading = new StackPanel()
            .Vertical()
            .Spacing(4)
            .Children(
                new TextBlock().Text(title).FontSize(22).Bold(),
                new TextBlock()
                    .Text(Directory.Exists(ToolCatalog.ToolsRoot)
                        ? "点击工具卡片启动；可查看架构版本、打开目录或复制路径。"
                        : $"未找到 Tools 目录：{ToolCatalog.ToolsRoot}")
                    .Foreground(SecondaryText)
            );
        DockPanel.SetDock(heading, Dock.Top);

        var cards = new WrapPanel { Spacing = 12 };
        foreach (var tool in tools)
        {
            cards.Add(BuildToolCard(tool));
        }

        var scroll = new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .HorizontalScroll(ScrollMode.Disabled)
            .Content(cards);

        var content = new DockPanel { LastChildFill = true, Spacing = 14, Padding = new Thickness(22) };
        content.Add(heading);
        content.Add(tools.Count == 0
            ? MessagePanel("未找到工具", "请检查 Tools 目录，或尝试其他搜索关键词。")
            : scroll);
        return content;
    }

    private Element BuildToolCard(ToolItem tool)
    {
        var heading = new StackPanel()
            .Horizontal()
            .Spacing(10)
            .Children(
                BuildToolIcon(tool),
                new StackPanel()
                    .Vertical()
                    .Spacing(3)
                    .Children(
                        new TextBlock().Text(tool.Name).FontSize(14).Bold(),
                        new TextBlock()
                            .Text($"{tool.PrimaryArch ?? "默认架构"} · {tool.Extension}")
                            .FontSize(10)
                            .Foreground(SecondaryText)
                    )
            );

        var description = new TextBlock()
            .Text(string.IsNullOrWhiteSpace(tool.Description) ? tool.RelativePath : tool.Description)
            .FontSize(11)
            .Foreground(SecondaryText)
            .TextWrapping(TextWrapping.Wrap);

        var actions = new StackPanel().Horizontal().Spacing(6);
        actions.Add(new Button()
            .Content(tool.LaunchButtonText)
            .OnClick(() => LaunchTool(tool)));
        actions.Add(new Button()
            .Content("打开目录")
            .OnClick(() => OpenToolFolder(tool)));
        actions.Add(new Button()
            .Content("复制路径")
            .OnClick(() => CopyPath(tool.EffectivePath)));
        actions.Add(new Button()
            .Content(tool.IsFavorite ? "★" : "☆")
            .OnClick(() => ToggleFavorite(tool)));

        var cardContent = new StackPanel()
            .Vertical()
            .Spacing(10)
            .Children(heading, description);

        if (tool.ArchOptions.Count > 1)
        {
            var architecture = new StackPanel().Horizontal().Spacing(5);
            var selectedArchitecture = new TextBlock()
                .Text($"当前：{tool.SelectedArch?.DisplayText ?? "默认"}")
                .Foreground(SecondaryText)
                .VerticalAlignment(VerticalAlignment.Center);
            architecture.Add(new TextBlock()
                .Text("架构")
                .Foreground(SecondaryText)
                .VerticalAlignment(VerticalAlignment.Center));
            architecture.Add(selectedArchitecture);
            foreach (var option in tool.ArchOptions)
            {
                var selectedOption = option;
                architecture.Add(new Button()
                    .Content(option.DisplayText)
                    .OnClick(() =>
                    {
                        tool.SelectedArch = selectedOption;
                        selectedArchitecture.Text = $"当前：{selectedOption.DisplayText}";
                        _statusText.Text = $"已选择 {tool.Name} · {selectedOption.DisplayText}";
                    }));
            }
            cardContent.Add(architecture);
        }

        if (tool.Categories.Count > 1)
        {
            cardContent.Add(new TextBlock()
                .Text(tool.CategoriesText)
                .FontSize(10)
                .Foreground(SecondaryText));
        }

        cardContent.Add(actions);

        return new Border
        {
            Width = 330,
            MinHeight = 158,
            Background = Surface,
            BorderBrush = SurfaceAlt,
            BorderThickness = 1,
            CornerRadius = 8,
            Padding = new Thickness(12),
            Child = cardContent
        };
    }

    private static Element BuildToolIcon(ToolItem tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.IconPath) && File.Exists(tool.IconPath))
        {
            try
            {
                return new Image
                {
                    Source = ImageSource.FromFile(tool.IconPath),
                    Width = 38,
                    Height = 38
                };
            }
            catch
            {
            }
        }

        return new Border
        {
            Width = 38,
            Height = 38,
            Background = SurfaceAlt,
            CornerRadius = 7,
            Child = new TextBlock()
                .Text(tool.Extension.Length > 4 ? tool.Extension[..4] : tool.Extension)
                .FontSize(9)
                .Bold()
                .Center()
        };
    }

    private void LaunchTool(ToolItem tool)
    {
        var path = tool.EffectivePath;
        if (!File.Exists(path))
        {
            var download = tool.DownloadUrl ?? tool.WingetId;
            _statusText.Text = download == null
                ? $"文件不存在：{path}"
                : $"文件不存在，可在完整版中下载：{download}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = tool.EffectiveWorkingDir
            });
            _statusText.Text = $"已启动：{tool.Name}";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"启动失败：{ex.Message}";
        }
    }

    private void OpenToolFolder(ToolItem tool)
    {
        var directory = Path.GetDirectoryName(tool.EffectivePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            _statusText.Text = $"目录不存在：{directory}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _statusText.Text = $"无法打开目录：{ex.Message}";
        }
    }

    private void CopyPath(string path)
    {
        if (WindowsClipboard.SetText(path))
        {
            _statusText.Text = $"已复制路径：{path}";
        }
        else
        {
            _statusText.Text = "复制路径失败";
        }
    }

    private void ToggleFavorite(ToolItem tool)
    {
        FavoritesService.ToggleFavorite(tool.EffectivePath);
        tool.IsFavorite = !tool.IsFavorite;
        _ = LoadToolsAsync();
    }

    private async Task LoadHardwareAsync(bool forceRefresh)
    {
        var requestId = ++_loadVersion;
        StopUptimeTimer();
        _statusText.Text = "正在读取硬件信息…";
        _pageHost.Content = MessagePanel("硬件信息", "正在读取本机硬件和系统信息…");

        try
        {
            var sections = await Task.Run(() => HardwareInfoService.LoadAsync(forceRefresh));
            if (requestId != _loadVersion || !_showHardwarePage)
            {
                return;
            }

            _pageHost.Content = BuildHardwarePage(sections);
            _statusText.Text = $"已读取 {sections.Sum(section => section.Items.Count)} 项硬件信息";
        }
        catch (Exception ex)
        {
            if (requestId == _loadVersion)
            {
                _pageHost.Content = MessagePanel("硬件信息读取失败", ex.Message);
                _statusText.Text = "硬件信息读取失败";
            }
        }
    }

    private Element BuildHardwarePage(IReadOnlyList<HardwareInfoSection> sections)
    {
        var refresh = new Button()
            .Content("刷新")
            .OnClick(() => _ = LoadHardwareAsync(forceRefresh: true));
        var screenshot = new Button()
            .Content("截图到剪贴板")
            .OnClick(() =>
            {
                _statusText.Text = WindowsClipboard.CopyWindowToClipboard()
                    ? "硬件信息截图已复制到剪贴板"
                    : "截图失败，无法访问剪贴板";
            });

        var heading = new StackPanel()
            .Horizontal()
            .Spacing(10)
            .Children(
                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .Children(
                        new TextBlock().Text("硬件信息").FontSize(22).Bold(),
                        new TextBlock()
                            .Text("本机型号、系统和关键硬件参数")
                            .Foreground(SecondaryText)
                    ),
                refresh,
                screenshot
            );
        DockPanel.SetDock(heading, Dock.Top);

        var rows = new StackPanel().Vertical().Spacing(12);
        if (sections.Count >= 3)
        {
            var summary = new WrapPanel { Spacing = 12 };
            summary.Add(BuildSummaryCard("设备型号", FindValue(sections[0], "设备型号")));
            summary.Add(BuildSummaryCard("系统信息", FindValue(sections[1], "系统")));
            _uptimeText = new TextBlock().FontSize(15).Bold();
            UpdateUptime();
            summary.Add(BuildSummaryCard("运行时间", _uptimeText));
            rows.Add(summary);

            foreach (var section in sections)
            {
                rows.Add(new TextBlock()
                    .Text(section.Title)
                    .FontSize(16)
                    .Bold()
                    .Margin(0, 8, 0, 0));

                var sectionCard = new StackPanel()
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 1
                };
                foreach (var item in section.Items)
                {
                    sectionCard.Add(new Button()
                        .Content(new StackPanel()
                            .Horizontal()
                            .Spacing(12)
                            .Children(
                                new TextBlock()
                                    .Text(item.Label)
                                    .Width(110)
                                    .Foreground(SecondaryText),
                                new TextBlock()
                                    .Text(item.Value)
                                    .TextWrapping(TextWrapping.Wrap)
                            ))
                        .HorizontalAlignment(HorizontalAlignment.Stretch)
                        .OnClick(() => CopyHardwareValue(item.Value)));
                }

                rows.Add(new Border
                {
                    Background = Surface,
                    BorderBrush = SurfaceAlt,
                    BorderThickness = 1,
                    CornerRadius = 8,
                    Padding = new Thickness(8),
                    Child = sectionCard
                });
            }
        }
        else
        {
            rows.Add(new TextBlock().Text("未获取到完整的硬件信息。"));
        }

        _uptimeTimer?.Dispose();
        _uptimeTimer = new DispatcherTimer(TimeSpan.FromSeconds(1));
        _uptimeTimer.Tick += UpdateUptime;
        _uptimeTimer.Start();

        var content = new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .HorizontalScroll(ScrollMode.Disabled)
            .Content(rows);
        var page = new DockPanel { LastChildFill = true, Spacing = 14, Padding = new Thickness(22) };
        page.Add(heading);
        page.Add(content);
        return new Border { Background = Canvas, Child = page };
    }

    private Element BuildSummaryCard(string label, string value)
    {
        return BuildSummaryCard(label, new TextBlock().Text(value).FontSize(15).Bold());
    }

    private Element BuildSummaryCard(string label, TextBlock value)
    {
        var copyButton = new Button()
            .Content(new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock().Text(label).Foreground(SecondaryText),
                    value
                ))
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .OnClick(() => CopyHardwareValue(value.Text));
        return new Border
        {
            Width = 270,
            MinHeight = 82,
            Background = Surface,
            BorderBrush = SurfaceAlt,
            BorderThickness = 1,
            CornerRadius = 8,
            Padding = new Thickness(6),
            Child = copyButton
        };
    }

    private void CopyHardwareValue(string value)
    {
        _statusText.Text = WindowsClipboard.SetText(value)
            ? "已复制硬件信息"
            : "复制到剪贴板失败";
    }

    private void UpdateUptime()
    {
        if (_uptimeText == null)
        {
            return;
        }

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        _uptimeText.Text = $"{uptime.Days}天 {uptime.Hours}小时 {uptime.Minutes}分钟 {uptime.Seconds}秒";
    }

    private void StopUptimeTimer()
    {
        _uptimeTimer?.Dispose();
        _uptimeTimer = null;
        _uptimeText = null;
    }

    private static string FindValue(HardwareInfoSection section, string label)
    {
        return section.Items.FirstOrDefault(item => item.Label == label)?.Value ?? "未知";
    }

    private static Element MessagePanel(string title, string message)
    {
        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Padding(24)
            .Children(
                new TextBlock().Text(title).FontSize(18).Bold(),
                new TextBlock().Text(message).Foreground(SecondaryText).TextWrapping(TextWrapping.Wrap)
            );
    }
}
