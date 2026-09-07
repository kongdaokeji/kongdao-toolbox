using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class WindowsImagePage : Page
{
    private List<WindowsImageEntry>? _allEntries;
    private string _filter = "";
    private string _categoryFilter = "全部";
    private string _langFilter = "全部语言";
    private WindowsImageEntry? _msResolvedEntry;

    private List<UupBuildInfo>? _uupBuilds;
    private UupBuildInfo? _selectedUupBuild;
    private List<UupLanguageInfo>? _uupLanguages;
    private List<UupEditionInfo>? _uupEditions;
    private string _uupSelectedLanguage = "";
    private string _uupCategoryFilter = "全部";
    private CancellationTokenSource? _uupCts;
    private bool _isPageAlive = true;

    public WindowsImagePage()
    {
        InitializeComponent();

        HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);

        UupBuildHeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        UupBuildListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);

        InitUupQuickGrid();

        Unloaded += (_, _) =>
        {
            _isPageAlive = false;
            _uupCts?.Cancel();
        };

        LoadMsEditions();
        _ = LoadDataAsync();
    }



    private void LoadMsEditions()
    {
        var editions = MicrosoftOfficialService.GetAvailableEditions();
        MsEditionCombo.ItemsSource = editions;
        MsEditionCombo.DisplayMemberPath = "Name";
    }

    private async Task LoadDataAsync()
    {
        LoadingRing.IsActive = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        HeaderBorder.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;

        try
        {
            _allEntries = await WindowsImageService.LoadAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "加载失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        if (_allEntries is null) return;

        var filtered = _allEntries.AsEnumerable();

        if (_categoryFilter != "全部")
            filtered = filtered.Where(e => e.Category == _categoryFilter);

        if (_langFilter != "全部语言")
            filtered = filtered.Where(e => e.Language == _langFilter);

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            filtered = filtered.Where(e =>
                e.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.FileName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Language.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Arch.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (e.Updated ?? "").Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        RenderList(list);

        EmptyPanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListBorder.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderBorder.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderList(List<WindowsImageEntry> entries)
    {
        ListContainer.Children.Clear();
        foreach (var entry in entries)
            ListContainer.Children.Add(CreateRow(entry));
    }

    private Border CreateRow(WindowsImageEntry entry)
    {
        var nameText = new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420
        };

        var fileNameText = new TextBlock
        {
            Text = entry.FileName,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var sizeText = new TextBlock
        {
            Text = entry.SizeDisplay,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var langBadge = MakeBadge(entry.Language, entry.Language == "简体中文"
            ? ThemeColors.AccentBlue
            : entry.Language == "English"
                ? ThemeColors.AccentGreen
                : ThemeColors.AccentPurple);

        var archBadge = MakeBadge(entry.Arch, ThemeColors.AccentOrange);

        var downloadBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 11 },
                    new TextBlock { Text = "下载", FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = entry
        };
        downloadBtn.Click += DownloadBtn_Click;

        var convertBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE898", FontSize = 11 },
                    new TextBlock { Text = "下载并转ISO", FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = entry,
            Visibility = entry.IsEsd ? Visibility.Visible : Visibility.Collapsed
        };
        convertBtn.Click += DownloadAndConvertBtn_Click;

        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actionPanel.Children.Add(downloadBtn);
        actionPanel.Children.Add(convertBtn);

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(nameText); Grid.SetColumn(nameText, 0);
        grid.Children.Add(fileNameText); Grid.SetColumn(fileNameText, 1);
        grid.Children.Add(sizeText); Grid.SetColumn(sizeText, 2);
        grid.Children.Add(langBadge); Grid.SetColumn(langBadge, 3);
        grid.Children.Add(archBadge); Grid.SetColumn(archBadge, 4);
        grid.Children.Add(actionPanel); Grid.SetColumn(actionPanel, 5);

        var tip = new ToolTip { Content = $"{entry.DisplayName}\n{entry.FileName}\n大小: {entry.SizeDisplay}" };
        if (entry.Sha256 is not null) tip.Content += $"\nSHA256: {entry.Sha256[..16]}...";
        if (entry.Updated is not null) tip.Content += $"\n更新: {entry.Updated}";
        ToolTipService.SetToolTip(grid, tip);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Border MakeBadge(string text, Color color)
    {
        return new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;
        EnqueueDownload(entry, btn);
    }

    private void EnqueueDownload(WindowsImageEntry entry, FrameworkElement? target = null)
    {
        var destDir = WindowsImageService.GetDownloadDir();
        DownloadQueueService.Enqueue(
            entry.DisplayName,
            entry.DownloadUrl,
            destDir,
            postProcessor: null,
            description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay}",
            glyph: "\uE896");

        StatusInfoBar.Title = "已加入下载队列";
        StatusInfoBar.Message = $"{entry.DisplayName} 正在下载至 {destDir}";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(entry.DisplayName, target);
    }

    private void ShowQueueTip(string name, FrameworkElement? target)
    {
        QueueTeachingTip.Title = "已加入下载队列";
        QueueTeachingTip.Subtitle = $"{name}\n点击主页搜索框旁的下载按钮可查看进度";
        QueueTeachingTip.IconSource = new SymbolIconSource { Symbol = Symbol.Download };
        QueueTeachingTip.Target = target;
        QueueTeachingTip.IsOpen = true;
    }

    private async void DownloadAndConvertBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;

        if (!WindowsImageService.IsUltraIsoAvailable)
        {
            var dialog = new ContentDialog
            {
                Title = "需要 UltraISO",
                Content = "ESD 转 ISO 需要安装 UltraISO。\n\n是否前往下载页面？",
                PrimaryButtonText = "前往下载",
                CloseButtonText = "取消",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.ezbsystems.com/ultraiso/")); } catch { }
            }
            return;
        }

        var destDir = WindowsImageService.GetDownloadDir();
        var isoFileName = Path.ChangeExtension(entry.FileName, ".iso");

        var postProcessor = new DelegatePostProcessor("ESD 转 ISO", async (file, dest, progress, ct) =>
        {
            progress?.Report("正在等待下载完成...");
            var esdFile = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(esdFile)) esdFile = file;

            var isoPath = Path.Combine(dest, isoFileName);

            await WindowsImageService.ConvertEsdToIsoAsync(esdFile, isoPath, progress, ct);

            if (File.Exists(esdFile) && File.Exists(isoPath))
            {
                try { File.Delete(esdFile); } catch { }
            }
        });

        DownloadQueueService.Enqueue(
            entry.DisplayName + " (ESD→ISO)",
            entry.DownloadUrl,
            destDir,
            postProcessor,
            description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay} → ISO",
            glyph: "\uE898");

        StatusInfoBar.Title = "已加入下载队列";
        StatusInfoBar.Message = $"{entry.DisplayName} 下载完成后将自动转换为 ISO";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(entry.DisplayName + " (ESD→ISO)", btn);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = sender.Text;
        ApplyFilter();
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is string s)
            _categoryFilter = s;
        ApplyFilter();
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangCombo.SelectedItem is string s)
            _langFilter = s;
        ApplyFilter();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void CancelConvertBtn_Click(object sender, RoutedEventArgs e)
    {
    }

    private void SourcePivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void MsEditionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MsEditionCombo.SelectedItem is not MicrosoftEdition edition) return;

        MsLangCombo.IsEnabled = false;
        MsLangCombo.ItemsSource = null;
        MsFetchBtn.IsEnabled = false;
        MsResultPanel.Visibility = Visibility.Collapsed;

        MsProgressRing.Visibility = Visibility.Visible;
        MsStatusText.Visibility = Visibility.Visible;
        MsStatusText.Text = "正在初始化会话...";

        try
        {
            var sessionId = await MicrosoftOfficialService.InitSessionAsync();

            MsStatusText.Text = "正在获取语言列表...";

            var skuId = edition.SkuIds[0];
            var languages = await MicrosoftOfficialService.GetLanguagesAsync(skuId, sessionId);

            if (edition.SkuIds.Length > 1)
            {
                var sessionId2 = await MicrosoftOfficialService.InitSessionAsync();
                var languages2 = await MicrosoftOfficialService.GetLanguagesAsync(edition.SkuIds[1], sessionId2);
                foreach (var lang in languages2)
                {
                    if (!languages.Any(l => l.Name == lang.Name))
                        languages.Add(lang);
                }
            }

            MsLangCombo.ItemsSource = languages;
            MsLangCombo.DisplayMemberPath = "Name";
            MsLangCombo.IsEnabled = languages.Count > 0;
            MsFetchBtn.IsEnabled = languages.Count > 0;

            MsStatusText.Text = $"已获取 {languages.Count} 种语言";
        }
        catch (Exception ex)
        {
            MsStatusText.Text = $"获取语言列表失败: {ex.Message}";
        }
        finally
        {
            MsProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async void MsFetchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MsEditionCombo.SelectedItem is not MicrosoftEdition edition) return;
        if (MsLangCombo.SelectedItem is not MicrosoftLanguage language) return;

        MsFetchBtn.IsEnabled = false;
        MsResultPanel.Visibility = Visibility.Collapsed;
        MsProgressRing.Visibility = Visibility.Visible;
        MsStatusText.Visibility = Visibility.Visible;
        MsStatusText.Text = "正在获取下载链接...";

        try
        {
            _msResolvedEntry = await MicrosoftOfficialService.ResolveDownloadEntryAsync(edition, language);

            if (_msResolvedEntry is not null)
            {
                MsResultTitle.Text = _msResolvedEntry.DisplayName;
                MsResultInfo.Text = $"架构: {_msResolvedEntry.Arch} | 文件: {_msResolvedEntry.FileName}";
                MsResultPanel.Visibility = Visibility.Visible;
                MsStatusText.Text = "下载链接获取成功（24 小时内有效）";
            }
            else
            {
                MsStatusText.Text = "未能获取下载链接，请稍后重试";
            }
        }
        catch (Exception ex)
        {
            MsStatusText.Text = $"获取失败: {ex.Message}";
        }
        finally
        {
            MsProgressRing.Visibility = Visibility.Collapsed;
            MsFetchBtn.IsEnabled = true;
        }
    }

    private void MsDownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        EnqueueDownload(_msResolvedEntry, sender as FrameworkElement);
    }

    private async void MsOpenBrowserBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_msResolvedEntry.DownloadUrl));
        }
        catch { }
    }

    private void MsCopyLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(_msResolvedEntry.DownloadUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            StatusInfoBar.Title = "已复制";
            StatusInfoBar.Message = "下载链接已复制到剪贴板";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.IsOpen = true;
        }
        catch { }
    }

    private void InitUupQuickGrid()
    {
        var options = UupDumpService.GetQuickFetchOptions();
        UupQuickGrid.ItemsSource = options;
    }

    private async void UupQuickGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not UupQuickFetchOption option) return;

        UupQuickProgress.Visibility = Visibility.Visible;
        UupQuickProgress.IsActive = true;

        try
        {
            _uupCts?.Cancel();
            _uupCts = new CancellationTokenSource();

            var builds = await UupDumpService.FetchLatestBuildsAsync(option.Ring, option.Arch, _uupCts.Token);
            _uupBuilds = builds;
            RenderUupBuilds(builds);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "获取失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            UupQuickProgress.Visibility = Visibility.Collapsed;
            UupQuickProgress.IsActive = false;
        }
    }

    private async void UupSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        UupSearchProgress.Visibility = Visibility.Visible;
        UupSearchProgress.IsActive = true;

        try
        {
            _uupCts?.Cancel();
            _uupCts = new CancellationTokenSource();

            var search = UupSearchBox.Text.Trim();
            var category = _uupCategoryFilter != "全部" ? _uupCategoryFilter : null;

            var builds = await UupDumpService.GetKnownBuildsAsync(
                string.IsNullOrEmpty(search) ? null : search,
                category,
                _uupCts.Token);

            _uupBuilds = builds;
            RenderUupBuilds(builds);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "搜索失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            UupSearchProgress.Visibility = Visibility.Collapsed;
            UupSearchProgress.IsActive = false;
        }
    }

    private void UupCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UupCategoryCombo.SelectedItem is string s)
            _uupCategoryFilter = s;
    }

    private async void UupFetchBuildBtn_Click(object sender, RoutedEventArgs e)
    {
        var buildNum = UupBuildNumber.Text.Trim();
        if (string.IsNullOrEmpty(buildNum))
        {
            StatusInfoBar.Title = "请输入构建号";
            StatusInfoBar.Message = "请输入 Windows 构建号，如 26100.1";
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.IsOpen = true;
            return;
        }

        UupNewBuildProgress.Visibility = Visibility.Visible;
        UupNewBuildProgress.IsActive = true;

        try
        {
            _uupCts?.Cancel();
            _uupCts = new CancellationTokenSource();

            var arch = (UupNewArchCombo.SelectedItem as string) ?? "amd64";
            var ring = (UupNewRingCombo.SelectedItem as string) ?? "WIF";
            var skuItem = UupNewSkuCombo.SelectedItem as ComboBoxItem;
            var sku = skuItem?.Tag is string tag ? int.Parse(tag) : 48;

            var parts = buildNum.Split('.');
            var major = parts[0];
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;

            var req = new UupNewBuildRequest
            {
                Arch = arch,
                Ring = ring,
                Flight = "Mainline",
                Build = $"{major}.{minor}",
                Minor = 0,
                Sku = sku
            };

            var builds = await UupDumpService.FetchNewBuildAsync(req, _uupCts.Token);
            _uupBuilds = builds;
            RenderUupBuilds(builds);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "查找失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            UupNewBuildProgress.Visibility = Visibility.Collapsed;
            UupNewBuildProgress.IsActive = false;
        }
    }

    private void RenderUupBuilds(List<UupBuildInfo> builds)
    {
        UupBuildListContainer.Children.Clear();

        if (builds.Count == 0)
        {
            UupBuildEmptyPanel.Visibility = Visibility.Visible;
            UupBuildHeaderBorder.Visibility = Visibility.Collapsed;
            UupBuildListBorder.Visibility = Visibility.Collapsed;
            UupBuildCountText.Visibility = Visibility.Collapsed;
            return;
        }

        UupBuildEmptyPanel.Visibility = Visibility.Collapsed;
        UupBuildHeaderBorder.Visibility = Visibility.Visible;
        UupBuildListBorder.Visibility = Visibility.Visible;
        UupBuildCountText.Visibility = Visibility.Visible;
        UupBuildCountText.Text = $"共 {builds.Count} 个构建";

        foreach (var build in builds)
            UupBuildListContainer.Children.Add(CreateUupBuildRow(build));
    }

    private Border CreateUupBuildRow(UupBuildInfo build)
    {
        var titleText = new TextBlock
        {
            Text = build.Title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var archBadge = MakeBadge(build.Architecture, ThemeColors.AccentOrange);
        var channelBadge = MakeBadge(
            string.IsNullOrEmpty(build.Channel) ? "正式版" : build.Channel,
            string.IsNullOrEmpty(build.Channel) ? ThemeColors.AccentGreen :
                build.Channel == "Canary" ? ThemeColors.AccentRed :
                build.Channel == "Dev" ? ThemeColors.AccentPurple :
                build.Channel == "Beta" ? ThemeColors.AccentBlue :
                ThemeColors.AccentOrange);
        var categoryBadge = MakeBadge(build.Category, ThemeColors.AccentPurple);

        var selectBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 11 },
                    new TextBlock { Text = "选择", FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = build
        };
        selectBtn.Click += UupSelectBuild_Click;

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(titleText); Grid.SetColumn(titleText, 0);
        grid.Children.Add(archBadge); Grid.SetColumn(archBadge, 1);
        grid.Children.Add(channelBadge); Grid.SetColumn(channelBadge, 2);
        grid.Children.Add(categoryBadge); Grid.SetColumn(categoryBadge, 3);
        grid.Children.Add(selectBtn); Grid.SetColumn(selectBtn, 4);

        var tip = new ToolTip { Content = $"{build.Title}\n构建: {build.Build}\n架构: {build.Architecture}\n渠道: {build.Channel}" };
        ToolTipService.SetToolTip(grid, tip);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private async void UupSelectBuild_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UupBuildInfo build } btn) return;

        btn.IsEnabled = false;
        var origContent = btn.Content;
        btn.Content = new ProgressRing { Width = 16, Height = 16, IsActive = true };

        _selectedUupBuild = build;
        UupLangBuildInfo.Text = $"{build.Title}\n架构: {build.Architecture} | 渠道: {build.Channel} | 构建: {build.Build}";
        UupLangListView.ItemsSource = null;
        UupLangProgress.Visibility = Visibility.Visible;
        UupLangProgress.IsActive = true;

        try
        {
            _uupCts?.Cancel();
            _uupCts = new CancellationTokenSource();

            var langs = await UupDumpService.GetLanguagesAsync(build.UpdateId, _uupCts.Token);
            _uupLanguages = langs;

            UupLangListView.ItemsSource = langs;
            UupLangListView.DisplayMemberPath = "DisplayName";

            if (langs.Count > 0)
            {
                var zhCn = langs.FirstOrDefault(l => l.Code == "zh-cn");
                if (zhCn is not null)
                    UupLangListView.SelectedItem = zhCn;
                else
                    UupLangListView.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "获取语言失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
            return;
        }
        finally
        {
            UupLangProgress.Visibility = Visibility.Collapsed;
            UupLangProgress.IsActive = false;
            btn.Content = origContent;
            btn.IsEnabled = true;
        }

        if (!_isPageAlive) return;
        try
        {
            UupLanguageDialog.XamlRoot = Content.XamlRoot;
        }
        catch { return; }
        UupLanguageDialog.RequestedTheme = ThemeService.CurrentElementTheme;
        await UupLanguageDialog.ShowAsync();
    }

    private async void UupLanguageDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (UupLangListView.SelectedItem is not UupLanguageInfo lang)
        {
            args.Cancel = true;
            return;
        }

        _uupSelectedLanguage = lang.Code;
        sender.Hide();

        await ShowUupEditionDialog();
    }

    private async Task ShowUupEditionDialog()
    {
        if (_selectedUupBuild is null || !_isPageAlive) return;

        UupEditionBuildInfo.Text = $"{_selectedUupBuild.Title}\n语言: {UupDumpService.GetLanguageDisplayName(_uupSelectedLanguage)} ({_uupSelectedLanguage})";
        UupBaseEditionPanel.Children.Clear();
        UupVirtualEditionPanel.Children.Clear();
        UupVirtualEditionLabel.Visibility = Visibility.Collapsed;
        UupVirtualEditionPanel.Visibility = Visibility.Collapsed;
        UupEditionProgress.Visibility = Visibility.Visible;
        UupEditionProgress.IsActive = true;

        try
        {
            UupEditionDialog.XamlRoot = Content.XamlRoot;
        }
        catch { return; }
        UupEditionDialog.RequestedTheme = ThemeService.CurrentElementTheme;

        var showTask = UupEditionDialog.ShowAsync();

        try
        {
            _uupCts?.Cancel();
            _uupCts = new CancellationTokenSource();

            var editions = await UupDumpService.GetEditionsAsync(_selectedUupBuild.UpdateId, _uupSelectedLanguage, _uupCts.Token);
            _uupEditions = editions;

            foreach (var ed in editions.Where(e => e.IsBaseEdition))
            {
                var cb = new CheckBox
                {
                    Content = ed.DisplayName,
                    Tag = ed.Id,
                    IsChecked = ed.Id is "PROFESSIONAL" or "CORE",
                    Margin = new Thickness(0, 2, 0, 2)
                };
                UupBaseEditionPanel.Children.Add(cb);
            }

            var virtualEditions = editions.Where(e => !e.IsBaseEdition).ToList();
            if (virtualEditions.Count > 0)
            {
                UupVirtualEditionLabel.Visibility = Visibility.Visible;
                UupVirtualEditionPanel.Visibility = Visibility.Visible;

                foreach (var ed in virtualEditions)
                {
                    var cb = new CheckBox
                    {
                        Content = ed.DisplayName,
                        Tag = ed.Id,
                        IsChecked = false,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    UupVirtualEditionPanel.Children.Add(cb);
                }
            }
        }
        catch (Exception ex)
        {
            StatusInfoBar.Title = "获取版本失败";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            UupEditionProgress.Visibility = Visibility.Collapsed;
            UupEditionProgress.IsActive = false;
        }

        await showTask;
    }

    private async void UupEditionDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var selectedEditions = new List<string>();
        foreach (var child in UupBaseEditionPanel.Children)
        {
            if (child is CheckBox { IsChecked: true, Tag: string id })
                selectedEditions.Add(id);
        }

        if (selectedEditions.Count == 0)
        {
            args.Cancel = true;
            StatusInfoBar.Title = "请选择版本";
            StatusInfoBar.Message = "至少需要选择一个基础版本";
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.IsOpen = true;
            return;
        }

        var virtualEditions = new List<string>();
        foreach (var child in UupVirtualEditionPanel.Children)
        {
            if (child is CheckBox { IsChecked: true, Tag: string id })
                virtualEditions.Add(id);
        }

        var methodItem = UupDownloadMethodCombo.SelectedItem as ComboBoxItem;
        var autoDl = methodItem?.Tag is string tag ? int.Parse(tag) : 2;

        if (virtualEditions.Count > 0 && autoDl < 3)
            autoDl = 3;

        var info = new UupDownloadInfo
        {
            UpdateId = _selectedUupBuild!.UpdateId,
            Language = _uupSelectedLanguage,
            Editions = selectedEditions,
            AutoDl = autoDl,
            VirtualEditions = virtualEditions
        };

        sender.Hide();

        var editionNames = string.Join(", ", selectedEditions.Select(UupDumpService.GetEditionDisplayName));
        var displayName = $"{_selectedUupBuild.Title} - {editionNames}";
        var destDir = UupDumpService.GetDownloadDir();

        var downloadInfo = info;

        var postProcessor = new DelegatePostProcessor("UUP 转 ISO", async (downloadedFile, dest, progress, ct) =>
        {
            progress?.Report("正在解压转换包...");

            var extractDir = Path.Combine(dest, $"uup_convert_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(extractDir);

            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(downloadedFile, extractDir, true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"解压失败: {ex.Message}", ex);
            }

            try { File.Delete(downloadedFile); } catch { }

            var cmdFile = Directory.GetFiles(extractDir, "aria2_download_windows.cmd", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(extractDir, "aria2_download_windows.cmd", SearchOption.AllDirectories))
                .FirstOrDefault();

            if (cmdFile is null)
                cmdFile = Directory.GetFiles(extractDir, "*.cmd", SearchOption.AllDirectories).FirstOrDefault();

            if (cmdFile is null)
                throw new InvalidOperationException("未找到转换脚本，请手动运行解压目录中的 .cmd 文件。");

            progress?.Report("正在启动转换脚本...");

            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                ScriptRunnerWindow.ShowAndRun(
                    $"cmd.exe /c \"{cmdFile}\"",
                    workingDir: Path.GetDirectoryName(cmdFile),
                    title: $"UUP 转 ISO - {_selectedUupBuild?.Title}");
            });
        });

        var resolverInfo = downloadInfo;
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver = async ct =>
        {
            var url = UupDumpService.BuildGetUrl(resolverInfo);

            if (resolverInfo.AutoDl == 3 && resolverInfo.VirtualEditions.Count > 0)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var content = new FormUrlEncodedContent(
                    resolverInfo.VirtualEditions.Select(ve => new KeyValuePair<string, string>("virtualEditions[]", ve)));
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url) { Content = content };
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var disposition = response.Content.Headers.ContentDisposition;
                var fileName = "uup_dlp.zip";
                if (disposition is not null)
                {
                    var fn = disposition.FileName?.Trim('"');
                    if (!string.IsNullOrEmpty(fn)) fileName = fn;
                }

                return new ResolvedDownloadUrl(url, fileName);
            }

            return new ResolvedDownloadUrl(url, $"uup_{resolverInfo.UpdateId[..8]}.zip");
        };

        DownloadQueueService.EnqueueWithResolver(
            displayName,
            urlResolver,
            destDir,
            postProcessor,
            description: $"UUP Dump | {UupDumpService.GetLanguageDisplayName(_uupSelectedLanguage)} | {string.Join(", ", selectedEditions)}",
            glyph: "\uE896");

        StatusInfoBar.Title = "已加入下载队列";
        StatusInfoBar.Message = $"{displayName} 下载完成后将自动解压并运行转换脚本";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(displayName, null);
    }
}
