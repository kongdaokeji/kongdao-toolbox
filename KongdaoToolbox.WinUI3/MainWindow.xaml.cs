using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using KongdaoToolbox.Controls;
using KongdaoToolbox.Models;
using KongdaoToolbox.Pages;
using KongdaoToolbox.Services;
using KongdaoToolbox.Services.Plugins;
using Windows.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace KongdaoToolbox;

public sealed partial class MainWindow : Window
{
    private bool _syncingNavSelection;
    private bool _navFromSidebar;
    private bool _suppressSearch;
    private bool _searchDismissed;
    private string? _searchResultsQuery;
    private bool _searchSubmitting;
    private readonly ObservableCollection<SearchResult> _searchResults = [];
    private readonly DispatcherQueueTimer _searchDebounceTimer;
    private Flyout? _downloadFlyout;
    private int _lastBadgeCount;
    private bool _refreshCategoriesInFlight;
    private bool _refreshCategoriesPending;

    /// <summary>用户明确要退出程序（托盘菜单「退出」）——关闭拦截必须放行，不再隐藏到托盘。</summary>
    private bool _exitRequested;

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
                                    UpdateBanner.ShowDownloadFailed(di.ErrorMessage ?? LocalizationService.L("Common_UnknownError", "未知错误"));
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
        ToolUpdateToast.Title = LocalizationService.L("MainWindow_ToolUpdateDoneTitle", "工具更新完成");
        ToolUpdateToast.Message = string.Format(LocalizationService.L("MainWindow_ToolUpdateDoneMessage", "「{0}」已更新到最新版本"), toolName);
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    public void ShowToolUpdateProgressToast(string toolName)
    {
        ToolUpdateToast.Title = LocalizationService.L("MainWindow_ToolUpdateProgressTitle", "正在更新工具");
        ToolUpdateToast.Message = string.Format(LocalizationService.L("MainWindow_ToolUpdateProgressMessage", "「{0}」正在同步更新..."), toolName);
        ToolUpdateToast.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
        ToolUpdateToast.IsOpen = true;
    }

    public void ShowToolUpdateFailedToast(string toolName, string error)
    {
        ToolUpdateToast.Title = LocalizationService.L("MainWindow_ToolUpdateFailedTitle", "工具更新失败");
        ToolUpdateToast.Message = string.Format(LocalizationService.L("MainWindow_ToolUpdateFailedMessage", "「{0}」更新失败：{1}"), toolName, error);
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
        Services.AiAgent.ComputerControlService.PendingInteractionRequested += OpenAssistantInteraction;
        FavoritesService.SaveFailed += OnPersistenceFailed;
        AppSettings.SaveFailed += OnPersistenceFailed;

        ApplyNavigationIcons();
        InitializeAiAssistantIntro();
        NavView.ActualThemeChanged += NavigationThemeChanged;
        NavView.Loaded += PluginNavigationLoaded;
        Activated += NavigationWindowActivated;

        SearchListView.ItemsSource = _searchResults;

        // AutoSuggestBox 会先于实例处理 Escape（置 Handled），键盘导航必须用 handledEventsToo 注册
        SearchBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(SearchBox_KeyDown), handledEventsToo: true);

        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
        _searchDebounceTimer.Tick += OnSearchDebounceTick;

        // 标题栏定制一律走 SafeTitleBar：部分 Windows 10 版本 AppWindow.TitleBar 为 null，
        // 构造函数里的空引用会以 STOWED_EXCEPTION_80004003 让进程闪退（见 SafeTitleBar 注释）
        SafeTitleBar.ApplyExtendedTall(this, AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        try
        {
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 设置窗口图标失败（已忽略）: {ex.Message}");
        }

        ApplyTitleBarTheme(ElementTheme.Default);

        BackdropService.ApplyBackdrop(this);
        BackdropService.BackdropChanged += OnBackdropChanged;

        WindowSizeService.ApplySavedWindowSize(this);

        // 窗口尺寸固定（1280×800）：禁止拖动边框缩放，但保留最小化、最大化/还原、关闭
        // 三个系统按钮的行为（最大化后还原仍回到固定尺寸）。
        if (AppWindow.Presenter is OverlappedPresenter fixedPresenter)
        {
            fixedPresenter.IsResizable = false;
            fixedPresenter.IsMinimizable = true;
            fixedPresenter.IsMaximizable = true;
        }

        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;
        // 系统关闭入口（标题栏 ×、Alt+F4、系统菜单）走 AppWindow.Closing，可取消：
        // 「关闭时最小化到系统托盘」在这里把关闭改写成隐藏。Window.Closed 里的
        // Handled 分支保留给编程式 Close()（App.MainWindow?.Close()，后台工具页面在用），
        // 两条路径共用同一个判据，先到先得。
        AppWindow.Closing += MainWindow_Closing;
        NavFrame.Navigated += NavFrame_Navigated;
        NavView.ItemInvoked += NavView_ItemInvoked;

        if (RuntimeHelper.IsPackagedContext)
        {
            // MSIX 只关闭插件的侧边栏入口；插件页仍可从「内置工具」索引打开，
            // AI 制作插件与插件工具照常可用。
            NavView.MenuItems.Remove(CommunityNavItem);
        }

        SplashVersionText.Text = UpdateService.CurrentVersion.ToString();
        // 命令行可能在 Loaded 前直接执行内置工具，下载队列须先具备 UI dispatcher。
        DownloadQueueService.Initialize(DispatcherQueue);
        DownloadQueueService.QueueChanged += OnDownloadQueueChanged;
        ToolUpdateService.Initialize(DispatcherQueue);
        UpdateDownloadBadge();
        AppSettings.SettingChanged += OnBackgroundSettingChanged;
        NavView.Loaded += NavView_Loaded;
        LocalizationService.LanguageChanged += OnLanguageChanged;

        // 拖动期间卸掉最贵的合成层：Win10 上移动消息一旦积压，表现就是拖影、跟随延迟、
        // 鼠标停下后窗口还在继续动（详见 DragPerformanceService 的说明）。
        DragPerformanceService.DragStateChanged += OnDragStateChanged;
        _uiQueue = DispatcherQueue;
        DragPerformanceService.CustomDragRequested = OnCustomDragRequested;
        HookDragPerformance();
    }

    // ---- 自定义拖动：绕开系统 modal 移动/缩放循环 ----
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOREDRAW = 0x0008;
    private const int SWP_NOACTIVATE = 0x0010;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;
    private const int VK_LBUTTON = 0x01;

    // 命中测试码：标题栏 2，八个方向的边框 10~17
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int MinWindowWidth = 800;
    private const int MinWindowHeight = 600;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint p);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private DispatcherQueue? _uiQueue;
    private bool _customDragging;
    private DispatcherQueueTimer? _customDragTimer;
    private IntPtr _customDragHwnd;
    private NativePoint _customDragStartCursor;
    private NativeRect _customDragStartRect;
    private int _customDragHit;

    /// <summary>
    /// 接管标题栏拖动。
    ///
    /// <para>
    /// 系统内置的 modal 移动循环（DefWindowProc 处理 WM_NCLBUTTONDOWN/HTCAPTION）会按鼠标原始速率
    /// 反复 SetWindowPos，并要求 WinUI 每次都重新提交整窗内容。对 Win10 上的合成窗口
    /// （无 redirection bitmap）而言等于每秒几十上百次整树重绘 —— 表现为卡顿、拖影、
    /// 松手后还在动。这里改成自己按 8ms 节拍挪窗口，且带 SWP_NOREDRAW（只挪位置不重绘），
    /// 松手后一次性 RedrawWindow 刷出来，等价于 DWM 对普通 Win32 窗口做的「位图平移」。
    /// </para>
    /// <para>代价：失去 Aero Snap（拖到屏幕边缘吸附、拖到顶部最大化）。</para>
    /// </summary>
    private bool OnCustomDragRequested(IntPtr hwnd, int hit)
    {
        // 只接管标题栏移动与八个方向的边框缩放；客户区点击、系统按钮仍交回系统处理。
        bool isMove = hit == HTCAPTION;
        bool isResize = hit is >= HTLEFT and <= HTBOTTOMRIGHT;
        if (!isMove && !isResize) return false;

        try
        {
            if (!GetCursorPos(out var cursor)) return false;
            if (!GetWindowRect(hwnd, out var rect)) return false;

            _customDragHwnd = hwnd;
            _customDragStartCursor = cursor;
            _customDragStartRect = rect;
            _customDragHit = hit;
            _customDragging = true;

            SetCapture(hwnd);
            SetForegroundWindow(hwnd);
            // 当前在 WndProc 线程，不能碰 XAML；定时器丢回 UI 线程启动。
            _uiQueue?.TryEnqueue(StartCustomDragLoop);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 接管拖动/缩放失败（回退系统处理）: {ex.Message}");
            _customDragging = false;
            return false;
        }
    }

    private void StartCustomDragLoop()
    {
        if (!_customDragging) return;
        // 注意：这里刻意不调 ApplyDragPerformanceMode(true)。SWP_NOREDRAW 的意思就是
        // 拖动期间不重绘客户区、由 DWM 平移上一帧的内容，所以内容必须留在原地；
        // 把它隐藏掉反而会让拖动中的窗口变成空白块。
        _customDragTimer = DispatcherQueue.CreateTimer();
        _customDragTimer.Interval = TimeSpan.FromMilliseconds(8);
        _customDragTimer.IsRepeating = true;
        _customDragTimer.Tick += CustomDragTick;
        _customDragTimer.Start();
    }

    private void CustomDragTick(DispatcherQueueTimer sender, object args)
    {
        if (!_customDragging) { EndCustomDrag(); return; }
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0) { EndCustomDrag(); return; }
        if (!GetCursorPos(out var cursor)) return;

        int dx = cursor.X - _customDragStartCursor.X;
        int dy = cursor.Y - _customDragStartCursor.Y;

        int left = _customDragStartRect.Left;
        int top = _customDragStartRect.Top;
        int right = _customDragStartRect.Right;
        int bottom = _customDragStartRect.Bottom;

        switch (_customDragHit)
        {
            case HTCAPTION:
                left += dx; top += dy; right += dx; bottom += dy;
                break;
            case HTLEFT: left += dx; break;
            case HTRIGHT: right += dx; break;
            case HTTOP: top += dy; break;
            case HTBOTTOM: bottom += dy; break;
            case HTTOPLEFT: left += dx; top += dy; break;
            case HTTOPRIGHT: right += dx; top += dy; break;
            case HTBOTTOMLEFT: left += dx; bottom += dy; break;
            case HTBOTTOMRIGHT: right += dx; bottom += dy; break;
        }

        // 缩放时限制最小尺寸；被拖的是哪条边就把哪条边挪回去，避免窗口「跳」。
        if (_customDragHit != HTCAPTION)
        {
            if (right - left < MinWindowWidth)
            {
                if (_customDragHit is HTLEFT or HTTOPLEFT or HTBOTTOMLEFT) left = right - MinWindowWidth;
                else right = left + MinWindowWidth;
            }
            if (bottom - top < MinWindowHeight)
            {
                if (_customDragHit is HTTOP or HTTOPLEFT or HTTOPRIGHT) top = bottom - MinWindowHeight;
                else bottom = top + MinWindowHeight;
            }
        }

        // 绝不能带 SWP_NOREDRAW：WinUI 3 是合成窗口，移动/缩放期间不重绘会让 composition
        // 表面变成黑块（实测整窗全黑）。这里让内容正常提交，只靠「自己按 8ms 节流」
        // 把位置/尺寸的更新频率压下来，绕开系统 modal 循环的洪泛。
        SetWindowPos(_customDragHwnd, IntPtr.Zero, left, top, right - left, bottom - top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void EndCustomDrag()
    {
        if (!_customDragging) return;
        _customDragging = false;
        _customDragTimer?.Stop();
        _customDragTimer = null;
        ReleaseCapture();
        // 拖动期间内容是被「冻住」的，松手后强制整窗重绘一次。
        RedrawWindow(_customDragHwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        ApplyDragPerformanceMode(false);
    }

    private void HookDragPerformance()
    {
        try
        {
            DragPerformanceService.Attach(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 拖动性能钩子安装失败（已忽略）: {ex.Message}");
        }
    }

    private void OnDragStateChanged(bool dragging)
    {
        // WndProc 线程触发；XAML 对象只能在 UI 线程改。拖动开始的消息在进入系统移动循环
        // 之前送达，此时 UI 线程空闲，排队任务会被立即执行。
        DispatcherQueue.TryEnqueue(() => ApplyDragPerformanceMode(dragging));
    }

    /// <summary>
    /// 拖动/缩放期间卸掉最贵的合成层。Win10 上 WinUI 不是 DWM 原生合成窗口，窗口每移动一次
    /// 都要重新提交整棵可视化树；1920×1080 的 15% 半透明背景图叠在几十张卡片上时，
    /// 单帧成本会超过输入消息的产生间隔，消息在队列里堆积 —— 表现就是拖影、跟随延迟、
    /// 鼠标停下了窗口还在动。卸掉背景图（并暂停 drag region 重算）后单帧成本大幅下降。
    /// </summary>
    private void ApplyDragPerformanceMode(bool dragging)
    {
        try
        {
            // 不要再隐藏背景图：之前那版把 BackgroundImg 摘掉，既被误判成「背景导致卡顿」，
            // 又让回退路径下窗口露出底色发黑。背景与卡顿无关（空白窗口同样卡），
            // 这里只暂停 drag region 重算。
            AppTitleBar.AutoRefreshDragRegions = !dragging;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 拖动性能模式切换失败（已忽略）: {ex.Message}");
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.Loaded -= NavView_Loaded;
        ApplyNavigationIcons();
        ApplyLocalizedShellText();
        _ = InitializeAfterSplashAsync();
    }

    private void OnLanguageChanged()
    {
        ApplyLocalizedShellText();
        // 工具元数据/标签随语言切换（Tools_<lang>.json 覆盖），必须先失效再重建导航与页面
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
        RefreshToolCategories();
        if (NavFrame.Content is ILocalizablePage page)
            page.ApplyLocalization();
    }

    /// <summary>刷新 shell 上由代码赋值、不随 Uid 自动更新的文本（窗口标题、按钮无障碍名、内建设置项）。</summary>
    private void ApplyLocalizedShellText()
    {
        var title = LocalizationService.L("App_Title", "空岛图吧工具箱");
        Title = title;
        AppTitleBar.Title = title;

        AutomationProperties.SetName(NewbieTutorialButton, LocalizationService.L("MainWindow_NewbieTutorialButton", "新手教程"));
        AutomationProperties.SetName(DownloadQueueButton, LocalizationService.L("MainWindow_DownloadQueueButton", "下载队列"));

        // NavigationView 内建「设置」项文本由框架按系统语言渲染，必须手动覆写为目标语言
        if (NavView.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.Content = LocalizationService.L("MainWindow_NavSettings", "设置");

        // 安全模式横幅的文案与按钮都是代码赋值，语言切换后要重建
        ApplySafeModeBanner();

        // AI 助手上线引导与其他代码赋值文案一样，语言切换后重取一次
        ConfigureAiAssistantIntroTip();
    }

    private async Task InitializeAfterSplashAsync()
    {
        _initialized = true;

        // 仅应用本地自定义背景（品牌壁纸彩蛋已移除：不再自动检测主板品牌、下载或加载壁纸）
        ApplyBackground();

        // 分类菜单只依赖目录名/元数据，不必等待所有可执行文件扫描。
        // Loaded 后初始化也让 App 先设置 UI dispatcher、处理命令行导航，
        // 避免 --open-builtin 等启动先创建并扫描一个马上被替换的首页。
        try
        {
            var categories = await Task.Run(() => ToolCatalog.GetVisibleCategories().ToList());
            if (App.IsExiting) return;
            PopulateCategories(categories);
            ApplyNavLayoutMode();
            NavLayoutModeService.NavLayoutModeChanged += OnNavLayoutModeChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Startup] 分类加载失败: {ex.Message}");
        }

        // 命令行激活或用户已导航时不覆盖当前页面。
        if (NavFrame.Content is null)
            NavigateToDefaultPage();

        await FadeOutSplashAsync();

        // 启动画面退场后再弹上线引导，避免和 Splash/首次向导抢焦点
        _ = MaybeShowAiAssistantIntroAsync();
    }

    /// <summary>
    /// 启动页退场：走 Composition 合成层做 250ms 纯 Opacity 淡出。
    ///
    /// 之前用 Storyboard 做 350ms 的 Opacity + ScaleTransform，实测掉到约 32fps、
    /// 单帧最长 58ms，观感是「顿一下再闪掉」；而且缩放会让 64×64 图标在动画过程中
    /// 被重采样，看起来更糊。合成动画在合成线程上播放，不逐帧占用 UI 线程；
    /// 去掉缩放后图标全程保持原始像素对齐。
    /// </summary>
    private async Task FadeOutSplashAsync()
    {
        // 不定量进度条是持续动画，淡出期间继续跑只会白白占用 UI 线程。
        SplashProgress.IsIndeterminate = false;

        const int FadeMs = 250;
        // Composition 动画没有 Completed 回调，必须固定等待把它播完：
        // +20ms 余量保证合成线程已经走到最后一帧再隐藏元素。
        const int SettleMs = FadeMs + 20;

        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(SplashOverlay);
            var compositor = visual.Compositor;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(FadeMs);
            // 与 Material/Fluent 的「标准减速」曲线一致：起步快、收尾稳，观感干脆。
            fade.InsertKeyFrame(1f, 0f, compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.4f, 0f),
                new System.Numerics.Vector2(1f, 1f)));

            visual.StartAnimation("Opacity", fade);
        }
        catch (Exception ex)
        {
            // 合成器不可用（极端环境）时直接隐藏，不能让启动流程卡在这里。
            System.Diagnostics.Debug.WriteLine($"[Splash] 合成淡出失败（直接隐藏）: {ex.Message}");
            SplashOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        await Task.Delay(SettleMs);

        // 动画停止后合成视觉的 Opacity 会停在动画的末值（0），不会自动回到 1。
        // 必须先 StopAnimation 再显式复位：否则 SplashOverlay 若再次可见会是全透明的。
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(SplashOverlay);
            visual.StopAnimation("Opacity");
            visual.Opacity = 1f;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Splash] 复位 Opacity 失败: {ex.Message}");
        }

        SplashOverlay.Visibility = Visibility.Collapsed;
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

        // 只要真的打开过 AI 助手（侧栏、内置工具、搜索结果、外部激活都走同一条 Frame），
        // 就算用户已经发现这个入口，收起侧栏角标并结束引导。
        if (e.SourcePageType == typeof(AiAssistantPage))
            OnAiAssistantVisited();

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
        if (pageType == typeof(DashboardPage)) return "home";
        if (pageType == typeof(SettingsPage)) return "settings";
        if (pageType == typeof(FavoritesPage)) return "home";
        if (pageType == typeof(HardwarePage)) return "hardware";
        if (pageType == typeof(BuiltinToolsPage)) return "builtin";
        if (pageType == typeof(AiAssistantPage)) return "ai-assistant";
        if (pageType == typeof(PluginsPage)) return "community";

        if (pageType == typeof(HomePage))
        {
            if (parameter is string category) return category;
            return "home";
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

    /// <summary>这次关闭是否应改写成「隐藏到托盘」（判据集中在 CloseToTrayService，单测覆盖）。</summary>
    private bool ShouldHideToTray()
        => CloseToTrayService.ShouldHideOnClose(
            CloseToTrayService.IsEnabled,
            App.IsLiteMode,
            _exitRequested || App.IsExiting,
            CloseToTrayService.IsSessionEnding);

    /// <summary>系统关闭入口（标题栏 ×、Alt+F4、系统菜单）：取消关闭，改为隐藏到系统托盘。</summary>
    private void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!ShouldHideToTray()) return;

        // 隐藏失败就不要取消关闭：宁可正常退出，也不能让用户点了 × 却毫无反应
        args.Cancel = HideToTray();
    }

    /// <summary>
    /// 隐藏主窗口并常驻托盘——进程继续在后台运行，硬件信息等已加载的数据全部保留。
    /// 返回 false 表示隐藏失败（调用方应放行真正的关闭）。
    /// </summary>
    private bool HideToTray()
    {
        try
        {
            AppWindow.Hide();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 隐藏窗口失败，改为正常关闭: {ex.Message}");
            return false;
        }

        // 窗口位置/大小仍在隐藏前落盘：之后从托盘退出时窗口已经不可见，读的是同一份尺寸
        try { WindowSizeService.SaveWindowSize(this); } catch { }

        // 托盘图标/首次气泡在这里创建：失败不能让异常从关闭回调里逃出去
        // （宁可退回「正常关闭」，也不能让用户点了 × 既没隐藏也没退出）
        try
        {
            CloseToTrayService.OnHiddenToTray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 托盘常驻设置失败，改为正常关闭: {ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 从托盘恢复主窗口（显示 + 取消最小化 + 激活）。
    /// 返回窗口是否真的显示出来了——失败时调用方不能收起托盘图标，
    /// 否则用户会同时失去窗口和托盘入口。
    /// </summary>
    public bool RestoreFromTray()
    {
        try
        {
            if (!AppWindow.IsVisible)
                AppWindow.Show();

            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
                presenter.Restore();

            Activate();
            return AppWindow.IsVisible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 从托盘恢复窗口失败: {ex.Message}");
            try { return AppWindow.IsVisible; } catch { return false; }
        }
    }

    /// <summary>
    /// 被外部启动（重复启动工具箱、桌面快捷方式、右键菜单）时唤醒主窗口：
    /// 从托盘恢复、取消最小化后尽力置到前台。返回窗口是否可见。
    /// 与 <see cref="RestoreFromTray"/> 的差别只在「强制前台」——恢复本身已由
    /// AppWindow_Changed 复位托盘/会话状态。
    /// </summary>
    public bool ActivateFromExternal()
    {
        var visible = RestoreFromTray();
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_SHOW);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 外部激活置前失败（已忽略）: {ex.Message}");
        }
        return visible;
    }

    /// <summary>退出程序：放行关闭拦截，让 <see cref="MainWindow_Closed"/> 跑完整清理。</summary>
    public void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ShouldHideToTray() && HideToTray())
        {
            // 编程式 Close()（后台工具页面的 App.MainWindow?.Close()）下的兜底：拦下这次关闭只做隐藏。
            // 系统关闭入口已在 MainWindow_Closing 取消，不会走到这里。
            args.Handled = true;
            return;
        }

        // Gracefully stop EnergyStar throttling so any throttled processes recover
        // their normal scheduling priority before the app exits. If the user has
        // enabled the scheduled-task auto-start, the next logon will re-enable it.
        // 必须放在「隐藏到托盘」分支之后：隐藏后程序还在后台跑（后台节流本身就是常驻功能），
        // 这时候解除节流等于把用户开着的省电功能关掉。
        try { Services.AiAgent.ComputerControlService.Shutdown(); } catch { }
        try { Services.AiAgent.AiBrowserHostService.Shutdown(); } catch { }
        Services.AiAgent.ComputerControlService.PendingInteractionRequested -= OpenAssistantInteraction;
        try { SteamAccelerationService.Shutdown(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Steam] 加速还原失败：" + ex.Message); }
        try { EnergyStarService.Shutdown(); } catch { }

        FavoritesService.SaveFailed -= OnPersistenceFailed;
        AppSettings.SaveFailed -= OnPersistenceFailed;
        BackdropService.BackdropChanged -= OnBackdropChanged;
        NavView.ActualThemeChanged -= NavigationThemeChanged;
        NavView.Loaded -= PluginNavigationLoaded;
        Activated -= NavigationWindowActivated;
        AppWindow.Changed -= AppWindow_Changed;
        AppWindow.Closing -= MainWindow_Closing;
        try { WindowSizeService.SaveWindowSize(this); } catch { }
        DownloadQueueService.QueueChanged -= OnDownloadQueueChanged;
        AppSettings.SettingChanged -= OnBackgroundSettingChanged;
        NavLayoutModeService.NavLayoutModeChanged -= OnNavLayoutModeChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        DragPerformanceService.DragStateChanged -= OnDragStateChanged;
        DragPerformanceService.CustomDragRequested = null;
        EndCustomDrag();
        // AppSettings 落盘是去抖的（500ms 合并），退出前同步刷一次避免丢最后变更
        AppSettings.Flush();

        // 遥测：尽力把队列中剩余数据发出去（有界等待），随后停止采集
        try { Services.Telemetry.TelemetryService.Shutdown(); } catch { }

        // 硬件监控句柄与 FPS 的 ETW 会话必须显式释放：内核 ETW 会话不会随进程终止自动回收，
        // 残留会让下次启动的 FPS 采集失效；轮询定时器/自动覆盖层/未落盘记录一并收尾。
        try { LiteMonitorService.Instance.Dispose(); } catch { }
        try { GameOverlayAutoService.Instance.Stop(); } catch { }
        // 本地 ONNX 模型句柄（AI 助手的「本地模型」提供商）显式释放；有界等待——
        // 模型正在加载时绝不阻塞后面的托盘/单实例清理（超时则放弃，进程退出由内核回收）
        try { Services.Ai.Onnx.LocalModelRuntime.TryUnload(TimeSpan.FromSeconds(3)); } catch { }
        try { Services.DisplayTools.DisplayColorAdjustment.RestoreAll(); } catch { }
        // 游戏联机：停掉 EasyTier 子进程并释放配置（作业对象之外的第二道保险）
        try { Services.TunnelSession.Instance.Shutdown(); } catch { }
        // 托盘图标必须显式移除：进程退出后残留的图标要等鼠标划过去才消失
        try { TrayIconService.Dispose(); } catch { }

        // 单实例：停激活监听、释放互斥体（让提权重启的新实例能立即接管）、删 PID 文件
        try { SingleInstanceService.Shutdown(); } catch { }

        // 走到这里＝走完了正常退出流程：记下「干净退出」，下次启动不再询问是否进安全模式。
        // 必须放在清理末尾——前面任何一步崩溃都应该算「没干净退出」。
        try { Services.Plugins.PluginSafeMode.MarkCleanExit(); } catch { }

        // 通知退出兜底看门狗「清理已跑完」：它据此决定何时可以硬退，
        // 避免 3 秒硬超时砍在清理中途（FPS 的 ETW 会话会因此泄漏）
        App.NotifyCleanupFinished();
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
        // 窗口被重新显示（托盘单击图标、页面里的 App.MainWindow?.Activate() 等任何路径）：
        // 收起「已最小化到托盘」标记，否则会留下一个提示"已最小化到系统托盘"的过期图标
        if (args.DidVisibilityChange && sender.IsVisible)
        {
            TrayIconService.OnMainWindowRestored();
            // 用户又能看到窗口了 = 上次注销/关机没有真的发生（被取消），恢复正常关闭行为
            CloseToTrayService.ResetSessionEnding();
        }

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
        TitleBarPalette.Apply(SafeTitleBar.Get(this), isDark);
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

    /// <summary>
    /// 标题栏 Content 列宽变化：按可用宽度收缩搜索框、空间不足时依次收起右侧按钮和搜索框
    /// （Issue #262）。TitleBar 会把内容里每个交互控件的布局矩形原样登记为「直通区域」，
    /// 内容一旦溢出列宽，这些区域就会盖住右侧的最小拖拽区和系统标题按钮。
    /// </summary>
    private void TitleBarContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var layout = TitleBarLayout.ForContentWidth(e.NewSize.Width);

        TitleSearchArea.Visibility = layout.ShowSearchBox ? Visibility.Visible : Visibility.Collapsed;
        TitleActionButtons.Visibility = layout.ShowActionButtons ? Visibility.Visible : Visibility.Collapsed;

        if (layout.ShowSearchBox)
        {
            TitleSearchArea.Width = layout.SearchBoxWidth;
            // 搜索建议弹层与搜索框同宽（XAML 里的 318 = 设计宽度 320 - 边框），
            // 窗口变窄时不能还是 318 宽地悬在窗口外面。
            SearchPopupBorder.Width = Math.Max(0, layout.SearchBoxWidth - 2);
        }
        else
        {
            HideSearchPopup();
        }
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
                case string pluginTag when pluginTag.StartsWith("plugin-page:", StringComparison.Ordinal):
                    if (BuiltinToolRegistry.GetById(pluginTag[12..]) is PluginXamlTool plugin) _ = LaunchPluginPageAsync(plugin);
                    break;
                case "home":
                    NavFrame.Navigate(typeof(DashboardPage));
                    break;
                case "all":
                    NavFrame.Navigate(typeof(DashboardPage));
                    break;
                case "favorites":
                    NavFrame.Navigate(typeof(DashboardPage), "favorites");
                    break;
                case "hardware":
                    NavFrame.Navigate(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavFrame.Navigate(typeof(BuiltinToolsPage));
                    break;
                case "ai-assistant":
                    NavFrame.Navigate(typeof(AiAssistantPage));
                    break;
                case "community":
                    if (RuntimeHelper.IsPackagedContext) break;
                    NavFrame.Navigate(typeof(PluginsPage));
                    break;

                case "benchmark":
                    _navFromSidebar = false;
                    _ = ExecuteBenchmarkToolAsync();
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
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

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "home";

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
            NavFrame.Navigate(typeof(DashboardPage));
        }
    }

    private readonly List<NavigationViewItem> _dynamicMenuItems = [];

    private void PopulateCategories(IReadOnlyList<string> categories)
    {
        foreach (var old in _dynamicMenuItems) NavView.MenuItems.Remove(old);
        _dynamicMenuItems.Clear();
        void AddDynamic(NavigationViewItem item) { NavView.MenuItems.Add(item); _dynamicMenuItems.Add(item); }

        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            AddDynamic(new NavigationViewItem
            {
                Content = LocalizationService.GetCategoryDisplayName(category),
                Tag = category,
                Icon = NavigationIconService.Create(category, GetCategoryGlyphStatic(category), _navigationHighContrast)
            });
        }

        if (otherCategory != null)
        {
            AddDynamic(new NavigationViewItem
            {
                Content = LocalizationService.GetCategoryDisplayName(otherCategory),
                Tag = otherCategory,
                Icon = NavigationIconService.Create(otherCategory, GetCategoryGlyphStatic(otherCategory), _navigationHighContrast)
            });
        }
        // MSIX 下插件工具不占用侧边栏入口（只隐藏展示，工具本身仍在内置工具索引里）。
        if (!RuntimeHelper.IsPackagedContext)
            foreach (var plugin in BuiltinToolRegistry.Tools.Where(p => p.ShowInSidebar))
            {
                AddDynamic(new NavigationViewItem
                {
                    Content = plugin.Name, Tag = "plugin-page:" + plugin.Id,
                    Icon = !_navigationHighContrast && BuiltinIconService.Get(plugin.Id) is { } icon
                        ? new ImageIcon { Source = icon } : new FontIcon { Glyph = plugin.Glyph }
                });
            }
        TryApplyPluginNavigationCustomization();
    }

    private async Task LaunchPluginPageAsync(PluginXamlTool plugin)
    {
        try { await plugin.ExecuteTrackedAsync(new BuiltinToolContext { XamlRoot = NavFrame.XamlRoot }); }
        catch (Exception ex) { ShowCatalogToolStatus(plugin.Name, ex.Message, InfoBarSeverity.Error); }
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
            var categories = await Task.Run(() => ToolCatalog.GetVisibleCategories().ToList());

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
        NavFrame.Navigate(typeof(PerformanceBenchmarkPage));
    }

    public void LaunchCatalogTool(ToolItem tool)
    {
        _ = CatalogToolLauncher.LaunchAsync(tool, DispatcherQueue, () => NavFrame.XamlRoot, ShowCatalogToolStatus);
    }

    private void OnPersistenceFailed(string message) => DispatcherQueue.TryEnqueue(() =>
        ShowCatalogToolStatus(LocalizationService.L("Persistence_ErrorTitle", "保存失败"), message, InfoBarSeverity.Error));

    private void ShowCatalogToolStatus(string title, string message, InfoBarSeverity severity)
    {
        ToolUpdateToast.Title = title;
        ToolUpdateToast.Message = message;
        ToolUpdateToast.Severity = severity;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
    }

    public void NavigateToToolPage(Type pageType, object? parameter = null)
    {
        // AI 助手也可能在独立窗口里打开（内置工具入口），同样算用户已经发现该入口
        if (pageType == typeof(AiAssistantPage))
            OnAiAssistantVisited();

        // 设置"独立窗口"模式，或处于 AI 助手强制独立窗口作用域时，内置工具在新窗口的 Frame 中打开
        if (BuiltinToolWindow.ForceWindowMode || AppSettings.GetBool("BuiltinToolsOpenInWindow", false))
        {
            var title = parameter is ToolContentPageParam p
                ? p.Title
                : pageType == typeof(PluginToolPage) && parameter is string pluginId && BuiltinToolRegistry.GetById(pluginId) is { } plugin ? plugin.Name
                : (ActiveToolName ?? LocalizationService.L("MainWindow_BuiltinToolFallback", "内置工具"));
            BuiltinToolWindow.Show(pageType, parameter, title);
            return;
        }
        NavFrame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
    }

    public void NavigateToSettings(string? highlightSettingKey = null)
    {
        NavFrame.Navigate(typeof(SettingsPage),
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

    private int _suggestionVersion;

    private void PopulateSearchSuggestions()
    {
        // Fixed actions are immediately available even while the catalog is cold.
        ApplySearchSuggestions(UnifiedSearchService.GetQuickPanelItems(includeRecent: false));
        _ = PopulateRecentSuggestionsAsync(++_suggestionVersion);
    }

    private async Task PopulateRecentSuggestionsAsync(int version)
    {
        try
        {
            var items = await Task.Run(() => UnifiedSearchService.GetQuickPanelItems());
            if (version != _suggestionVersion || _searchDismissed || SearchBox.Text.Trim().Length != 0 ||
                SearchBox.FocusState == FocusState.Unfocused) return;
            ApplySearchSuggestions(items);
            ShowSearchPopup();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search suggestions] {ex}");
        }
    }

    private void ApplySearchSuggestions(IReadOnlyList<SearchResult> items)
    {
        _searchResults.Clear();
        foreach (var item in items) _searchResults.Add(item);
        _searchResultsQuery = string.Empty;
        SearchListView.SelectedIndex = -1;
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

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSearch) return;
        _searchDismissed = false;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
            PopulateSearchSuggestions();
        ShowSearchPopup();
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!SearchBox.FocusState.HasFlag(FocusState.Programmatic))
                HideSearchPopup();
        });
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_suppressSearch) return;

        _searchDismissed = false;
        var query = SearchBox.Text.Trim();

        if (query.Length == 0)
        {
            _searchDebounceTimer.Stop();
            PopulateSearchSuggestions();
            ShowSearchPopup();
            return;
        }

        _searchDebounceTimer.Stop();
        HideSearchPopup();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) return;

        _ = SearchInBackgroundAsync(query);
    }

    private async Task SearchInBackgroundAsync(string query)
    {
        try
        {
            var results = await Task.Run(() => UnifiedSearchService.Search(query));
            if (_searchDismissed || SearchBox.Text.Trim() != query) return;
            _searchResults.Clear();
            foreach (var r in results)
                _searchResults.Add(r);
            _searchResultsQuery = query;
            SearchListView.SelectedIndex = -1;
            ShowSearchPopup();
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

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_searchSubmitting) return;
        _searchSubmitting = true;
        try
        {
            _searchDebounceTimer.Stop();
            var query = sender.Text.Trim();
            if (_searchResultsQuery != query)
            {
                if (query.Length == 0)
                    PopulateSearchSuggestions();
                else
                    await SearchInBackgroundAsync(query);
            }
            if (sender.Text.Trim() != query || _searchResultsQuery != query) return;

            var result = SearchListView.SelectedItem as SearchResult ?? _searchResults.FirstOrDefault();
            if (result is not null)
                HandleSearchResult(result);
        }
        finally
        {
            _searchSubmitting = false;
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _searchDismissed = true;
            SearchListView.SelectedIndex = -1;
            HideSearchPopup();
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
                SearchBox.Text = string.Empty;
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
        _searchDebounceTimer.Stop();
        _searchDismissed = true;
        HideSearchPopup();
        var frame = NavFrame;

        switch (result.Kind)
        {
            case SearchItemKind.ExternalTool:
            case SearchItemKind.CustomTool:
                if (!string.IsNullOrWhiteSpace(result.BuiltinToolId))
                    _ = OpenSearchBuiltinAsync(result.BuiltinToolId, result.MatchKey);
                else
                    LaunchSearchTool(result.MatchKey);
                break;
            case SearchItemKind.BuiltinTool:
                _ = OpenSearchBuiltinAsync(result.MatchKey);
                break;

            case SearchItemKind.Setting:
                frame.Navigate(typeof(SettingsPage),
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

    private async void LaunchSearchTool(string toolPath)
    {
        try
        {
            var tools = await ToolCatalog.GetAllToolsAsync();
            var tool = tools.FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                // 下载、安装和脚本沿用目录页的完整流程；已就绪的程序直接启动。
                var ext = Path.GetExtension(tool.EffectivePath);
                if (!string.IsNullOrWhiteSpace(tool.RemoteUrl))
                {
                    BrowserPage.Open(tool.RemoteUrl, tool.Name);
                    Services.Telemetry.TelemetryService.TrackExternalToolLaunch(tool.Path);
                    LaunchHistoryService.RecordLaunch(tool.Path);
                }
                else if (((!string.IsNullOrWhiteSpace(tool.DownloadUrl) || !string.IsNullOrWhiteSpace(tool.WingetId)) &&
                    !File.Exists(tool.EffectivePath)) ||
                    ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchCatalogTool(tool);
                }
                else
                {
                    ToolProcessLauncher.Launch(tool.EffectivePath, tool.EffectiveWorkingDir);
                    LaunchHistoryService.RecordLaunch(tool.Path);
                }
            }
        }
        catch (Exception ex)
        {
            ShowSearchLaunchError(ex);
        }
    }

    private async Task OpenSearchBuiltinAsync(string id, string? catalogPath = null)
    {
        try
        {
            var tool = BuiltinToolRegistry.GetById(id)
                ?? throw new InvalidOperationException(LocalizationService.L("HomePage_BuiltinNotFound", "找不到对应的内置工具"));
            ActiveToolName = tool.Name;
            await tool.ExecuteTrackedAsync(new BuiltinToolContext
            {
                XamlRoot = SearchBox.XamlRoot,
                OnProgress = message => DispatcherQueue.TryEnqueue(() =>
                {
                    ToolUpdateToast.Title = tool.Name;
                    ToolUpdateToast.Message = message;
                    ToolUpdateToast.Severity = InfoBarSeverity.Informational;
                    ToolUpdateToast.IsOpen = true;
                    StartToastAutoClose();
                }),
                ConfirmDownload = async (name, description, size) =>
                    await new FluentContentDialog
                    {
                        XamlRoot = SearchBox.XamlRoot,
                        Title = name,
                        Content = $"{description}\n{size}",
                        PrimaryButtonText = LocalizationService.L("Common_Download", "下载"),
                        CloseButtonText = LocalizationService.L("Common_Cancel", "取消"),
                        RequestedTheme = ThemeService.CurrentElementTheme
                    }.ShowAsync() == ContentDialogResult.Primary
            });
            if (catalogPath is not null)
                LaunchHistoryService.RecordLaunch(catalogPath);
        }
        catch (Exception ex)
        {
            ShowSearchLaunchError(ex);
        }
        finally
        {
            ActiveToolName = null;
        }
    }

    private void ShowSearchLaunchError(Exception ex)
    {
        ToolUpdateToast.Title = LocalizationService.L("Common_LaunchFailed", "启动失败");
        ToolUpdateToast.Message = ex.Message;
        ToolUpdateToast.Severity = InfoBarSeverity.Error;
        ToolUpdateToast.IsOpen = true;
        StartToastAutoClose();
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
                frame.Navigate(typeof(DashboardPage), "favorites");
                SyncNavSelection("home");
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

    // ---------- 外部激活置前（ActivateFromExternal） ----------

    private const int SW_SHOW = 5;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
