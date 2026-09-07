using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>功能开关行视图模型（当前配置列表 / 搜索结果共用）。</summary>
public sealed class FeatureRowVm
{
    public FeatureRowVm Self => this;
    public required uint FeatureId { get; init; }
    /// <summary>字典名；无名字时回退编号文案。</summary>
    public required string DisplayName { get; init; }
    public required string IdText { get; init; }
    public required string StateText { get; init; }
    public required SolidColorBrush StateBrush { get; init; }
    public required SolidColorBrush StateBackground { get; init; }
    public required string ExperimentText { get; init; }
    public required SolidColorBrush ExperimentBrush { get; init; }
    public required SolidColorBrush ExperimentBackground { get; init; }
    public required string PriorityText { get; init; }
    public required Visibility PriorityVisibility { get; init; }
    /// <summary>已启用时禁用「启用」按钮。</summary>
    public required bool CanEnable { get; init; }
    /// <summary>已禁用时禁用「禁用」按钮。</summary>
    public required bool CanDisable { get; init; }
    /// <summary>仅系统已有自定义配置时显示「重置」。</summary>
    public required Visibility ResetVisibility { get; init; }
    public required string FlyoutTitle { get; init; }
    public required string FlyoutDesc { get; init; }
    public required FeatureState State { get; init; }
}

/// <summary>
/// Windows 隐藏功能页：调用 Tools/其他工具/ViveTool/ViVeTool.exe 的 /query /enable /disable /reset，
/// 结合 FeatureDictionary.pfs 功能字典（名字 → ID）搜索与管理系统实验性功能开关。
/// 逻辑见 WindowsFeatureService。
/// </summary>
public sealed partial class WindowsFeaturePage : Page
{
    // 品牌调色板（与主题无关）
    private static readonly Color BrandViolet = Color.FromArgb(255, 124, 108, 240);
    private static readonly Color SuccessGreen = Color.FromArgb(255, 43, 182, 115);
    private static readonly Color CautionAmber = Color.FromArgb(255, 245, 166, 35);
    private static readonly Color CriticalRed = Color.FromArgb(255, 242, 80, 59);
    private static readonly Color NeutralGray = Color.FromArgb(255, 142, 142, 142);

    /// <summary>当前配置列表最大展示条数（超出提示用搜索定位）。</summary>
    private const int MaxConfiguredRows = 400;
    /// <summary>搜索结果最大展示条数。</summary>
    private const int MaxSearchRows = 200;

    private CancellationTokenSource? _cts;
    private Dictionary<uint, string> _dictionary = new();
    /// <summary>当前系统已有配置的索引：功能 ID → 条目。</summary>
    private readonly Dictionary<uint, FeatureFlagEntry> _configured = new();
    private bool _busy;

    public WindowsFeaturePage()
    {
        InitializeComponent();
        SearchBox.KeyDown += SearchBox_KeyDown;
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void WindowsFeaturePage_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        _ = LoadAsync();
    }

    private void WindowsFeaturePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();

    private static SolidColorBrush Brush(Color color) => new(color);

    // ───────────────────────────── 加载 ─────────────────────────────

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ContentPanel.Visibility = Visibility.Collapsed;
        MissingPanel.Visibility = Visibility.Collapsed;

        try
        {
            if (!WindowsFeatureService.IsSupported())
            {
                MissingPanel.Visibility = Visibility.Visible;
                MissingText.Text = "当前系统不支持功能配置 API（需要 Windows 10 1903 / build 18963 或更高版本）。" +
                    "系统版本过低或 ntdll 缺少 RtlQueryAllFeatureConfigurations 等导出点时不可用。";
                return;
            }

            // ① 功能字典（名字 → ID，随包 Assets 提供）
            _dictionary = await Task.Run(WindowsFeatureService.LoadDictionary);

            // ② 系统版本
            var build = await Task.Run(WindowsFeatureService.GetOsBuild);

            // ③ 全量配置（ntdll API 直读，毫秒级）
            var query = await Task.Run(WindowsFeatureService.QueryAll);
            _configured.Clear();
            foreach (var entry in query)
                _configured[entry.FeatureId] = entry;

            // 填充界面
            BuildValue.Text = build > 0 ? build.ToString() : "--";
            DictValue.Text = _dictionary.Count.ToString("N0");
            ConfiguredValue.Text = query.Count.ToString("N0");
            EnabledValue.Text = query.Count(e => e.State == FeatureState.Enabled).ToString("N0");

            PopulateConfiguredList(query);
            ApplySearch();

            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorBar.Title = "加载功能配置失败";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>当前配置列表：优先展示带名字的条目（内部 servicing 条目无操作价值），超出上限提示搜索。</summary>
    private void PopulateConfiguredList(List<FeatureFlagEntry> query)
    {
        var rows = query
            .OrderByDescending(e => e.Name is not null)
            .ThenBy(e => e.FeatureId)
            .Take(MaxConfiguredRows)
            .Select(BuildRow)
            .ToList();
        ConfiguredList.ItemsSource = rows;

        var shown = rows.Count;
        ConfiguredHint.Text = query.Count > shown
            ? $"共 {query.Count} 条，仅显示前 {shown} 条（其余可用下方搜索定位）"
            : $"共 {query.Count} 条";
        // 注意：DataTemplate 内 x:Bind 为 OneTime，在模板实例化时自动求值，无需 Bindings.Update()。
        // 页面顶层没有 x:Bind 时 Bindings 字段为 null，调用 Update() 会抛空引用（WinUI 生成代码行为）。
    }

    // ───────────────────────────── 搜索 ─────────────────────────────

    private void SearchButton_Click(object sender, RoutedEventArgs e) => ApplySearch();

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            ApplySearch();
    }

    /// <summary>按名称或 ID 过滤字典，合并系统当前配置状态后展示。</summary>
    private void ApplySearch()
    {
        var keyword = SearchBox.Text.Trim();
        if (_dictionary.Count == 0)
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
            SearchList.Visibility = Visibility.Collapsed;
            SearchStats.Visibility = Visibility.Collapsed;
            return;
        }
        if (keyword.Length == 0)
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
            SearchList.Visibility = Visibility.Collapsed;
            SearchStats.Visibility = Visibility.Collapsed;
            return;
        }

        var lower = keyword.ToLowerInvariant();
        var matched = _dictionary
            .Where(kv => kv.Key.ToString().Contains(keyword, StringComparison.Ordinal)
                         || kv.Value.Contains(lower, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key)
            .Take(MaxSearchRows)
            .Select(kv => BuildRow(kv.Key, kv.Value))
            .ToList();

        SearchPlaceholder.Visibility = Visibility.Collapsed;
        if (matched.Count == 0)
        {
            SearchStats.Visibility = Visibility.Visible;
            SearchStats.Text = $"未找到与「{keyword}」匹配的功能（共检索 {_dictionary.Count:N0} 条字典条目）";
            SearchList.Visibility = Visibility.Collapsed;
            return;
        }
        SearchList.Visibility = Visibility.Visible;
        SearchStats.Visibility = Visibility.Visible;
        SearchStats.Text = $"找到 {matched.Count} 条匹配（字典 {_dictionary.Count:N0} 条）；启用/禁用的功能会同时写入 Boot 存储，重启后保持生效";
        SearchList.ItemsSource = matched;
    }

    // ───────────────────────────── 行构建 ─────────────────────────────

    private FeatureRowVm BuildRow(uint id, string? name)
    {
        _configured.TryGetValue(id, out var entry);
        var hasConfig = entry is not null;
        var state = entry?.State ?? FeatureState.Default;
        var isExperiment = entry?.IsExperiment ?? false;
        var priority = entry?.Priority ?? 8;
        var display = hasConfig && entry!.Name is not null ? entry.Name! : (name ?? $"功能 {id}");

        var (stateText, stateBrush, stateBg) = state switch
        {
            FeatureState.Enabled => ("已启用", Brush(SuccessGreen), Brush(Color.FromArgb(0x14, 43, 182, 115))),
            FeatureState.Disabled => ("已禁用", Brush(CriticalRed), Brush(Color.FromArgb(0x16, 242, 80, 59))),
            _ => ("未配置", Brush(NeutralGray), Brush(Color.FromArgb(0x16, 142, 142, 142)))
        };

        return new FeatureRowVm
        {
            FeatureId = id,
            DisplayName = display,
            IdText = $"#{id}",
            StateText = stateText,
            StateBrush = stateBrush,
            StateBackground = stateBg,
            ExperimentText = isExperiment ? "实验功能" : "系统覆盖",
            ExperimentBrush = Brush(isExperiment ? CautionAmber : NeutralGray),
            ExperimentBackground = Brush(isExperiment
                ? Color.FromArgb(0x16, 245, 166, 35)
                : Color.FromArgb(0x14, 142, 142, 142)),
            PriorityText = hasConfig ? entry!.PriorityText : "User",
            PriorityVisibility = hasConfig ? Visibility.Visible : Visibility.Collapsed,
            CanEnable = state != FeatureState.Enabled,
            CanDisable = state != FeatureState.Disabled,
            ResetVisibility = hasConfig ? Visibility.Visible : Visibility.Collapsed,
            FlyoutTitle = hasConfig
                ? $"功能「{display}」当前为{stateText}，确认执行操作？"
                : $"「{display}」尚无自定义配置，确认启用？",
            FlyoutDesc = BuildFlyoutDesc(state),
            State = state
        };
    }

    private FeatureRowVm BuildRow(FeatureFlagEntry entry) => BuildRow(entry.FeatureId, entry.Name);

    private string BuildFlyoutDesc(FeatureState state) => state switch
    {
        FeatureState.Enabled => "将禁用该功能（User 优先级，同时写入 Runtime 与 Boot 存储，重启后依然生效）。",
        FeatureState.Disabled => "将启用该功能（User 优先级，同时写入 Runtime 与 Boot 存储，重启后依然生效）。实验性功能可能导致系统不稳定，请谨慎开启。",
        _ => "将对功能写入 User 优先级配置（同时写入 Runtime 与 Boot 存储，重启后依然生效）。"
    };

    // ───────────────────────────── 操作（Flyout 确认） ─────────────────────────────

    private void FeatureButton_Click(object sender, RoutedEventArgs e)
    {
        // 按钮自身打开 Flyout，无需额外逻辑
    }

    private void CancelFlyout_Click(object sender, RoutedEventArgs e) => HideParentFlyout((FrameworkElement)sender);

    private async void ConfirmFeature_Click(object sender, RoutedEventArgs e)
    {
        var confirm = (Button)sender;
        HideParentFlyout(confirm);
        if (confirm.Tag is not FeatureRowVm vm || _busy)
            return;

        var action = (confirm.Content as string)?.Replace("确认", "") ?? "启用";
        var resultText = await RunActionAsync(vm, action);
        if (resultText is null)
            return;

        SuccessBar.Title = action switch
        {
            "重置" => $"已重置功能 #{vm.FeatureId}",
            "禁用" => $"已禁用功能 #{vm.FeatureId}",
            _ => $"已启用功能 #{vm.FeatureId}"
        };
        SuccessBar.Message = resultText;
        SuccessBar.IsOpen = true;
        ErrorBar.IsOpen = false;
        await RefreshConfiguredAsync();
    }

    /// <summary>操作后轻量刷新：仅重查配置并重建两个列表，不整页闪烁。</summary>
    private async Task RefreshConfiguredAsync()
    {
        try
        {
            var query = await Task.Run(WindowsFeatureService.QueryAll);
            _configured.Clear();
            foreach (var entry in query)
                _configured[entry.FeatureId] = entry;
            ConfiguredValue.Text = query.Count.ToString("N0");
            EnabledValue.Text = query.Count(e => e.State == FeatureState.Enabled).ToString("N0");
            PopulateConfiguredList(query);
            ApplySearch();
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "刷新功能配置失败";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    /// <summary>执行启用/禁用/重置；返回结果文案；失败时展示错误并返回 null。</summary>
    private async Task<string?> RunActionAsync(FeatureRowVm vm, string action)
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            return await Task.Run(() => action switch
            {
                "重置" => WindowsFeatureService.Reset(vm.FeatureId),
                "禁用" => WindowsFeatureService.SetState(vm.FeatureId, false),
                _ => WindowsFeatureService.SetState(vm.FeatureId, true)
            });
        }
        catch (Exception ex)
        {
            ErrorBar.Title = $"操作失败（{action} #{vm.FeatureId}）";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            SuccessBar.IsOpen = false;
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private static void HideParentFlyout(FrameworkElement element)
    {
        DependencyObject? current = element;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is FlyoutPresenter { } presenter && VisualTreeHelper.GetParent(presenter) is Flyout flyout)
            {
                flyout.Hide();
                return;
            }
        }
    }
}