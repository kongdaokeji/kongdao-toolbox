using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3;

public sealed partial class MainWindow : Window
{
    private bool _syncingNavSelection;
    private bool _navFromSidebar;
    private bool _suppressSearch;
    private bool _searchDismissed;
    private readonly ObservableCollection<SearchResult> _searchResults = [];
    private readonly DispatcherQueueTimer _searchDebounceTimer;
    private Flyout? _downloadFlyout;
    private int _lastBadgeCount;
    private bool _refreshCategoriesInFlight;
    private bool _refreshCategoriesPending;

    /// <summary>当前正在执行的内置工具名称（入口页在 ExecuteAsync 前设置），供独立窗口标题使用。</summary>
    public static string? ActiveToolName { get; set; }

    public Image? GetBackgroundImage() => BackgroundImg;

    public void ShowUpdateBanner(Models.UpdateInfo update, bool autoDownload)
    {
        if (autoDownload)
        {
            UpdateBanner.ShowDownloading();
            var item = UpdateService.AutoDownloadUpdate(update);
            if (item is not null)
            {
                item.PropertyChanged += (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (s is not DownloadItem di) return;
                        switch (e.PropertyName)
                        {
                            case nameof(DownloadItem.State):
                                if (di.State == DownloadItemState.Completed)
                                    UpdateBanner.ShowDownloadComplete();
                                else if (di.State == DownloadItemState.Failed)
                                    UpdateBanner.ShowDownloadFailed(di.ErrorMessage ?? "未知错误");
                                break;
                            case nameof(DownloadItem.Progress):
                                if (di.Progress is not null && di.Progress.TotalBytes > 0)
                                    UpdateBanner.ShowDownloadProgress(di.Progress.Percentage);
                                break;
                        }
                    });
                };
            }
            else
            {
                UpdateBanner.ShowUpdateAvailable(update);
            }
        }
        else
        {
            UpdateBanner.ShowUpdateAvailable(update);
        }
    }

    public void ShowUpdateAlreadyDownloaded(Models.UpdateInfo update)
    {
        UpdateBanner.ShowUpdateAvailable(update);
        UpdateBanner.ShowDownloadComplete();
    }

    private DispatcherTimer? _toolUpdateToastTimer;

    public void ShowToolUpdateToast(string toolName)
    {
        ToolUpdateToast.Title = "工具更新完成";
        ToolUpdateToast.Message = $"「{toolName}」已更新到最新版本";
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    public void ShowToolUpdateProgressToast(string toolName)
    {
        ToolUpdateToast.Title = "正在更新工具";
        ToolUpdateToast.Message = $"「{toolName}」正在同步更新...";
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
        ToolUpdateToast.IsOpen = true;
    }

    public void ShowToolUpdateFailedToast(string toolName, string error)
    {
        ToolUpdateToast.Title = "工具更新失败";
        ToolUpdateToast.Message = $"「{toolName}」更新失败：{error}";
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    private void StartToastAutoClose()
    {
        _toolUpdateToastTimer?.Stop();
        _toolUpdateToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toolUpdateToastTimer.Tick += (s, e) =>
        {
            ToolUpdateToast.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toolUpdateToastTimer.Start();
    }

#pragma warning disable CS0414
    private bool _initialized;
#pragma warning restore CS0414

    public MainWindow()
    {
        InitializeComponent();

        SearchListView.ItemsSource = _searchResults;

        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
        _searchDebounceTimer.Tick += OnSearchDebounceTick;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        ApplyTitleBarTheme(ElementTheme.Default);

        BackdropService.ApplyBackdrop(this);
        BackdropService.BackdropChanged += OnBackdropChanged;

        WindowSizeService.ApplySavedWindowSize(this);

        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;
        NavFrame.Navigated += NavFrame_Navigated;
        NavView.ItemInvoked += NavView_ItemInvoked;

        if (RuntimeHelper.IsMsixPackaged)
        {
            NavView.MenuItems.Remove(CommunityNavItem);
        }

        SplashVersionText.Text = UpdateService.CurrentVersion.ToString();
        _ = InitializeAfterSplashAsync();
    }

    private async Task InitializeAfterSplashAsync()
    {
        _initialized = true;

        NavigateToDefaultPage();

        // 仅应用本地自定义背景（品牌壁纸彩蛋已移除：不再自动检测主板品牌、下载或加载壁纸）
        ApplyBackground();

        _ = Task.Run(async () =>
        {
            try
            {
                _ = ToolCatalog.ToolsRoot;
                var categories = ToolCatalog.GetCategories().ToList();

                if (!ToolCatalog.IsCacheReady)
                {
                    await ToolCatalog.GetAllToolsAsync();
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    PopulateCategories(categories);
                    ApplyNavLayoutMode();

                    // 仅在「默认页 = 动态分类」时补一次导航：首次导航时分类菜单
                    // 尚未填充无法选中，需要填充后重新定位；默认页为静态项（如"全部
                    // 工具"）时首次导航已生效，跳过以免 HomePage 重复实例化
                    var defaultPage = AppSettings.Get("DefaultPage") ?? "all";
                    if (categories.Any(c => c.Equals(defaultPage, StringComparison.OrdinalIgnoreCase)))
                        NavigateToDefaultPage();

                    // P1-6：顶部标签页（Top）模式下 PaneDisplayMode 切换会重建导航容器，
                    // 选中态可能被清空 —— 默认页为静态项（如"全部工具"）时首次导航随之失效。
                    // 选中为空则重新定位默认页；选中仍在则跳过，避免 HomePage 重复实例化。
                    if (NavView.SelectedItem is null)
                        NavigateToDefaultPage();

                    NavLayoutModeService.NavLayoutModeChanged += OnNavLayoutModeChanged;
                });
            }
            catch { }
        });

        DownloadQueueService.Initialize(DispatcherQueue);
        DownloadQueueService.QueueChanged += OnDownloadQueueChanged;
        ToolUpdateService.Initialize(DispatcherQueue);
        UpdateDownloadBadge();

        AppSettings.SettingChanged += OnBackgroundSettingChanged;

        await FadeOutSplashAsync();
    }

    private async Task FadeOutSplashAsync()
    {
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        storyboard.Children.Add(CreateSplashAnimation("Opacity", 1.0, 0.0, duration, easing));
        storyboard.Children.Add(CreateSplashAnimation("(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 1.0, 0.95, duration, easing));
        storyboard.Children.Add(CreateSplashAnimation("(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 1.0, 0.95, duration, easing));

        var tcs = new TaskCompletionSource();
        storyboard.Completed += (_, _) => tcs.TrySetResult();
        storyboard.Begin();
        await tcs.Task;

        SplashOverlay.Visibility = Visibility.Collapsed;
    }

    private DoubleAnimation CreateSplashAnimation(string targetProperty, double from, double to, TimeSpan duration, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, SplashOverlay);
        Storyboard.SetTargetProperty(animation, targetProperty);
        return animation;
    }

    private void OnBackgroundSettingChanged(string key)
    {
        if (key == "BackgroundImagePath" || key == "BackgroundOpacity")
            DispatcherQueue.TryEnqueue(ApplyBackground);
    }

    private void ApplyBackground()
    {
        var bmp = BackgroundService.LoadBackgroundImage();
        if (bmp is not null)
        {
            BackgroundImg.Source = bmp;
            BackgroundImg.Opacity = BackgroundService.GetBackgroundOpacity();
            BackgroundImg.Visibility = Visibility.Visible;
        }
        else
        {
            BackgroundImg.Source = null;
            BackgroundImg.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSplashStatus(string text)
    {
        DispatcherQueue.TryEnqueue(() => SplashStatusText.Text = text);
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = NavFrame.CanGoBack;

        if (_navFromSidebar)
        {
            _navFromSidebar = false;
            return;
        }

        _syncingNavSelection = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            var targetTag = ResolvePageTag(e.SourcePageType, e.Parameter);
            if (targetTag is not null)
            {
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag is string t && t == targetTag)
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }

        _syncingNavSelection = false;
    }

    private static string? ResolvePageTag(Type pageType, object? parameter)
    {
        if (pageType == typeof(SettingsPage)) return "settings";
        if (pageType == typeof(FavoritesPage)) return "favorites";
        if (pageType == typeof(HardwarePage)) return "hardware";
        if (pageType == typeof(BuiltinToolsPage)) return "builtin";
        if (pageType == typeof(CommunityToolsPage)) return "community";

        if (pageType == typeof(HomePage))
        {
            if (parameter is string category) return category;
            return "all";
        }
        return null;
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(null).Properties;
        var frame = NavFrame;

        if (props.IsXButton1Pressed)
        {
            if (frame.CanGoBack)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }
        else if (props.IsXButton2Pressed)
        {
            if (frame.CanGoForward)
            {
                frame.GoForward();
                e.Handled = true;
            }
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Gracefully stop EnergyStar throttling so any throttled processes recover
        // their normal scheduling priority before the app exits. If the user has
        // enabled the scheduled-task auto-start, the next logon will re-enable it.
        try { EnergyStarService.Shutdown(); } catch { }

        if (App.IsLiteMode)
        {
            args.Handled = true;
            AppWindow.Hide();
            return;
        }
        BackdropService.BackdropChanged -= OnBackdropChanged;
        AppWindow.Changed -= AppWindow_Changed;
        WindowSizeService.SaveWindowSize(this);
        DownloadQueueService.QueueChanged -= OnDownloadQueueChanged;
        AppSettings.SettingChanged -= OnBackgroundSettingChanged;
        NavLayoutModeService.NavLayoutModeChanged -= OnNavLayoutModeChanged;
        // AppSettings 落盘是去抖的（500ms 合并），退出前同步刷一次避免丢最后变更
        AppSettings.Flush();
    }

    private void OnDownloadQueueChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateDownloadBadge);
    }

    private void UpdateDownloadBadge()
    {
        var count = DownloadQueueService.PendingCount;
        if (count > 0)
        {
            DownloadQueueBadge.Value = count > 99 ? 99 : count;
            DownloadQueueBadge.Visibility = Visibility.Visible;
        }
        else
        {
            DownloadQueueBadge.Visibility = Visibility.Collapsed;
        }

        if (count > _lastBadgeCount)
        {
            PlayDownloadPulseAnimation();
        }
        _lastBadgeCount = count;
    }

    private void PlayDownloadPulseAnimation()
    {
        var btn = DownloadQueueButton;
        btn.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        btn.RenderTransform = new ScaleTransform();

        var scaleX = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTarget(scaleX, btn);
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var scaleY = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        Storyboard.SetTarget(scaleY, btn);
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var sb = new Storyboard();
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Begin();
    }

    private void DownloadQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadFlyout is null)
        {
            _downloadFlyout = new Flyout
            {
                Content = new DownloadQueueFlyout(),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            };
        }
        _downloadFlyout.ShowAt(DownloadQueueButton);
    }

    private void AiQuickButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout
        {
            Content = new AiQuickAskFlyout(),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        // FlyoutPresenter 默认 MaxWidth=456（FlyoutThemeMaxWidth），会把内容钳在 456px；
        // 覆盖 presenter 宽度/高度限制，让 850px 面板完整显示。
        flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(FlyoutPresenter.MaxWidthProperty, 880.0),
                new Setter(FlyoutPresenter.MaxHeightProperty, 810.0)
            }
        };
        flyout.ShowAt(AiQuickButton);
    }

    private void NewbieTutorialButton_Click(object sender, RoutedEventArgs e)
    {
        NewbieTutorialWindow.Show();
    }

    private void OnBackdropChanged()
    {
        DispatcherQueue.TryEnqueue(() => BackdropService.ApplyBackdrop(this));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;
        var size = sender.Size;
        var minWidth = 800;
        var minHeight = 600;
        var needsResize = false;
        var newW = size.Width;
        var newH = size.Height;

        if (size.Width < minWidth)
        {
            newW = minWidth;
            needsResize = true;
        }
        if (size.Height < minHeight)
        {
            newH = minHeight;
            needsResize = true;
        }

        if (needsResize)
        {
            sender.Resize(new Windows.Graphics.SizeInt32(newW, newH));
        }
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        TitleBarPalette.Apply(AppWindow.TitleBar, isDark);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        var frame = NavFrame;
        if (frame.CanGoBack)
            frame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavSelection) return;

        // 设置入口改由 ItemInvoked 统一处理：布局切换（Top/侧边栏）后 NavigationView
        // 内部选中状态可能残留（设置项与菜单项同时选中），点击设置不再触发
        // SelectionChanged；而 ItemInvoked 与选中状态无关、必然触发。
        if (args.IsSettingsSelected) return;

        _navFromSidebar = true;

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "all":
                    NavigateToPage(typeof(HomePage));
                    break;
                case "favorites":
                    NavigateToPage(typeof(FavoritesPage));
                    break;
                case "hardware":
                    NavigateToPage(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavigateToPage(typeof(BuiltinToolsPage));
                    break;
                case "community":
                    if (RuntimeHelper.IsMsixPackaged) break;
                    NavigateToPage(typeof(CommunityToolsPage));
                    break;

                case "benchmark":
                    _navFromSidebar = false;
                    _ = ExecuteBenchmarkToolAsync();
                    break;
                case string category:
                    NavigateToPage(typeof(HomePage), category);
                    break;
            }
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (!args.IsSettingsInvoked) return;
        // 设置项点击与选中状态无关，保证布局切换后仍能打开设置
        _navFromSidebar = true;
        NavFrame.Navigate(typeof(SettingsPage));
    }

    // T3：统一页面切换过渡（滑入 ~250ms），提升切换顺滑度
    private void NavigateToPage(Type pageType, object? parameter = null)
    {
        NavFrame.Navigate(pageType, parameter,
            new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "all";

        NavigationViewItem? targetItem = null;

        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string tag && tag == defaultPage)
            {
                targetItem = navItem;
                break;
            }
        }

        if (targetItem is not null)
        {
            NavView.SelectedItem = targetItem;
        }
        else
        {
            NavigateToPage(typeof(HomePage));
        }
    }

    private int _dynamicMenuItemCount;

    private void PopulateCategories(IReadOnlyList<string> categories)
    {
        while (_dynamicMenuItemCount > 0)
        {
            NavView.MenuItems.RemoveAt(NavView.MenuItems.Count - 1);
            _dynamicMenuItemCount--;
        }

        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = category.Replace("工具", ""),
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(category) }
            });
            _dynamicMenuItemCount++;
        }

        if (otherCategory != null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = otherCategory.Replace("工具", ""),
                Tag = otherCategory,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(otherCategory) }
            });
            _dynamicMenuItemCount++;
        }
    }

    public static string GetCategoryGlyphStatic(string category)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph))
            return customGlyph;

        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA1";
        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uF211";
        if (category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F4";
        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA2";
        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA0";
        if (category.Contains("外设", StringComparison.CurrentCultureIgnoreCase))
            return "\uE962";
        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7FC";
        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F5";
        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA3";
        if (category.Contains("烤鸡", StringComparison.CurrentCultureIgnoreCase))
            return "\uECAD";
        if (category.Contains("综合", StringComparison.CurrentCultureIgnoreCase))
            return "\uEC4E";
        if (category.Contains("其他", StringComparison.CurrentCultureIgnoreCase))
            return "\uE712";

        return "\uE8B7";
    }

    public void RefreshToolCategories()
    {
        if (_refreshCategoriesInFlight)
        {
            _refreshCategoriesPending = true;
            return;
        }
        _refreshCategoriesInFlight = true;
        _ = RefreshToolCategoriesCoreAsync();
    }

    private async Task RefreshToolCategoriesCoreAsync()
    {
        try
        {
            var categories = await Task.Run(() => ToolCatalog.GetCategories().ToList());

            if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    PopulateCategories(categories);
                }
                finally
                {
                    _refreshCategoriesInFlight = false;
                    if (_refreshCategoriesPending)
                    {
                        _refreshCategoriesPending = false;
                        RefreshToolCategories();
                    }
                }
            }))
            {
                _refreshCategoriesInFlight = false;
                _refreshCategoriesPending = false;
            }
        }
        catch
        {
            _refreshCategoriesInFlight = false;
            _refreshCategoriesPending = false;
        }
    }

    private void OnNavLayoutModeChanged(string mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyNavLayoutMode();
            // PaneDisplayMode 切换会重建侧边栏容器，NavigationView 的选中内部状态
            // 会残留（设置项与菜单项可能同时处于选中态），导致之后点击设置不再触发
            // SelectionChanged。先清空再按默认页重选，重建干净状态。
            NavView.SelectedItem = null;
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
                settingsItem.IsSelected = false;
            NavView.UpdateLayout();
            NavigateToDefaultPage();
        });
    }

    private void ApplyNavLayoutMode()
    {
        var isTabMode = NavLayoutModeService.IsTabMode();
        NavView.PaneDisplayMode = isTabMode
            ? NavigationViewPaneDisplayMode.Top
            : NavigationViewPaneDisplayMode.Auto;
        AppTitleBar.IsPaneToggleButtonVisible = !isTabMode;
    }

    private async Task ExecuteBenchmarkToolAsync()
    {
        NavigateToBenchmark();
    }

    public void NavigateToBenchmark()
    {
        NavigateToPage(typeof(PerformanceBenchmarkPage));
    }

    public void NavigateToToolPage(Type pageType, object? parameter = null)
    {
        // 设置"独立窗口"模式，或处于 AI 助手强制独立窗口作用域时，内置工具在新窗口的 Frame 中打开
        if (BuiltinToolWindow.ForceWindowMode || AppSettings.GetBool("BuiltinToolsOpenInWindow", false))
        {
            var title = parameter is ToolContentPageParam p
                ? p.Title
                : (ActiveToolName ?? "内置工具");
            BuiltinToolWindow.Show(pageType, parameter, title);
            return;
        }
        NavFrame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
    }

    public void NavigateToSettings(string? highlightSettingKey = null)
    {
        NavigateToPage(typeof(SettingsPage),
            highlightSettingKey is null
                ? null
                : new SearchNavigationTarget { HighlightSettingKey = highlightSettingKey });
        SyncNavSelection("settings");
    }

    public void NavigateBack()
    {
        // 若前台是独立工具窗口，返回/关闭操作作用于该窗口，避免误操作主窗口导航
        if (BuiltinToolWindow.ActiveWindow is { } toolWindow)
        {
            toolWindow.GoBackOrClose();
            return;
        }
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    public bool CanNavigateBack()
    {
        return NavFrame.CanGoBack;
    }

    private void PopulateSearchSuggestions()
    {
        var items = UnifiedSearchService.GetQuickPanelItems();
        _searchResults.Clear();
        foreach (var item in items)
            _searchResults.Add(item);
    }

    private void ShowSearchPopup()
    {
        if (_searchDismissed) return;
        SearchPopup.IsOpen = _searchResults.Count > 0;
    }

    private void HideSearchPopup()
    {
        SearchPopup.IsOpen = false;
    }

    private void SearchPopup_GettingFocus(object sender, GettingFocusEventArgs e)
    {
        e.TryCancel();
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSearch || _searchDismissed) return;
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0)
            PopulateSearchSuggestions();
        ShowSearchPopup();
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!SearchTextBox.FocusState.HasFlag(FocusState.Programmatic))
                HideSearchPopup();
        });
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch) return;

        _searchDismissed = false;
        var query = SearchTextBox.Text.Trim();

        if (query.Length == 0)
        {
            _searchDebounceTimer.Stop();
            PopulateSearchSuggestions();
            ShowSearchPopup();
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0) return;

        _ = SearchInBackgroundAsync(query);
    }

    private async Task SearchInBackgroundAsync(string query)
    {
        try
        {
            var results = await Task.Run(() => UnifiedSearchService.Search(query));
            _searchResults.Clear();
            foreach (var r in results)
                _searchResults.Add(r);
            SearchPopup.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] {ex}");
        }
    }

    private void SearchListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResult result)
        {
            HideSearchPopup();
            HandleSearchResult(result);
        }
    }

    private void SearchSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var first = _searchResults.FirstOrDefault();
        if (first is not null)
        {
            HideSearchPopup();
            HandleSearchResult(first);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            SearchListView.SelectedIndex = -1;
            HideSearchPopup();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var idx = SearchListView.SelectedIndex;
            SearchResult? result = idx >= 0 && idx < _searchResults.Count
                ? _searchResults[idx]
                : _searchResults.Count > 0 ? _searchResults[0] : null;

            if (result is not null)
            {
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            if (SearchListView.Items.Count > 0)
            {
                var next = SearchListView.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchListView.SelectedIndex + 1, SearchListView.Items.Count - 1);
                SearchListView.SelectedIndex = next;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            if (SearchListView.Items.Count > 0)
            {
                var prev = SearchListView.SelectedIndex <= 0
                    ? 0
                    : SearchListView.SelectedIndex - 1;
                SearchListView.SelectedIndex = prev;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void SearchListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (SearchListView.SelectedItem is SearchResult result)
            {
                _suppressSearch = true;
                SearchTextBox.Text = string.Empty;
                _suppressSearch = false;
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideSearchPopup();
            e.Handled = true;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        var frame = NavFrame;

        switch (result.Kind)
        {
            case SearchItemKind.ExternalTool:
            case SearchItemKind.CustomTool:
                NavigateToTool(result.MatchKey);
                break;
            case SearchItemKind.BuiltinTool:
                NavigateToPage(typeof(BuiltinToolsPage),
                    new SearchNavigationTarget { HighlightBuiltinId = result.MatchKey });
                SyncNavSelection("builtin");
                break;

            case SearchItemKind.Setting:
                NavigateToPage(typeof(SettingsPage),
                    new SearchNavigationTarget { HighlightSettingKey = result.MatchKey });
                SyncNavSelection("settings");
                break;
            case SearchItemKind.QuickAction:
                HandleQuickAction(result.MatchKey);
                break;
        }
    }

    private void SyncNavSelection(string tag)
    {
        _syncingNavSelection = true;
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
        _syncingNavSelection = false;
    }

    private void NavigateToTool(string toolPath)
    {
        try
        {
            var tools = ToolCatalog.GetAllToolsCached();
            var tool = tools.FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                NavFrame.Navigate(typeof(HomePage),
                    new SearchNavigationTarget { HighlightToolPath = toolPath });

                if (!string.IsNullOrEmpty(tool.Category))
                    SyncNavSelection(tool.Category);
            }
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        if (!action.StartsWith("navigate:")) return;
        var target = action["navigate:".Length..];
        var frame = NavFrame;

        switch (target)
        {
            case "hardware":
                frame.Navigate(typeof(HardwarePage));
                SyncNavSelection("hardware");
                break;
            case "favorites":
                frame.Navigate(typeof(FavoritesPage));
                SyncNavSelection("favorites");
                break;
            case "builtin":
                frame.Navigate(typeof(BuiltinToolsPage));
                SyncNavSelection("builtin");
                break;
            case "benchmark":
                _navFromSidebar = false;
                _ = ExecuteBenchmarkToolAsync();
                break;

            case "settings":
                frame.Navigate(typeof(SettingsPage));
                break;
        }
    }
}
