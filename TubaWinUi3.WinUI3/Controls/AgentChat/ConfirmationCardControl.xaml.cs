using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Controls.AgentChat;

/// <summary>确认卡片中单个请求的 UI 视图模型。</summary>
public sealed class ConfirmationItemVm : ObservableObject
{
    private readonly AgentConfirmationRequest _request;
    private bool _resolved;

    public ConfirmationItemVm(AgentConfirmationRequest request, int index)
    {
        _request = request;
        Index = index;
    }

    public int Index { get; }
    public AgentConfirmationRequest Request => _request;
    public string Glyph => _request.Glyph;
    public string DisplayName => _request.DisplayName;
    public string Summary => _request.Summary;
    public string Detail => _request.Detail;
    public bool HasDetail => !string.IsNullOrWhiteSpace(_request.Detail);
    public Visibility HasDetailVisibility => HasDetail ? Visibility.Visible : Visibility.Collapsed;
    public string ReasonText => string.IsNullOrWhiteSpace(_request.Reason) ? "" : $"理由：{_request.Reason}";

    /// <summary>确认按钮文案：登录等待类操作提示用户登录完成后继续。</summary>
    public string ConfirmButtonText => _request.Kind == "login" ? "我已登录，继续" : "确认";

    public bool IsResolved
    {
        get => _resolved;
        set
        {
            if (Set(ref _resolved, value))
                OnPropertyChanged(nameof(CanResolve));
        }
    }

    public bool CanResolve => !_resolved;
}

/// <summary>确认卡片全部决定后的事件参数。</summary>
public sealed class ConfirmationResolvedEventArgs : EventArgs
{
    public IReadOnlyList<AgentConfirmationDecision> Decisions { get; init; } = [];
}

/// <summary>
/// 危险操作确认卡片：每项独立确认/拒绝，全部决定后触发 Resolved。
/// 取代旧 [ACTION] 文本协议卡片。
/// </summary>
public sealed partial class ConfirmationCardControl : UserControl, INotifyPropertyChanged
{
    private readonly List<ConfirmationItemVm> _items = [];
    private readonly Dictionary<int, bool> _decisions = new();

    public event EventHandler<ConfirmationResolvedEventArgs>? Resolved;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>待确认请求（x:Bind 注入）。</summary>
    public IReadOnlyList<AgentConfirmationRequest> Requests
    {
        get => (IReadOnlyList<AgentConfirmationRequest>?)GetValue(RequestsProperty) ?? [];
        set => SetValue(RequestsProperty, value);
    }

    public static readonly DependencyProperty RequestsProperty = DependencyProperty.Register(
        nameof(Requests), typeof(IReadOnlyList<AgentConfirmationRequest>), typeof(ConfirmationCardControl),
        new PropertyMetadata(null, (d, e) =>
        {
            if (e.NewValue is IReadOnlyList<AgentConfirmationRequest> requests)
                ((ConfirmationCardControl)d).Show(requests);
        }));

    private string _pendingCountText = "0/0 已选择";
    public string PendingCountText
    {
        get => _pendingCountText;
        private set
        {
            if (_pendingCountText == value) return;
            _pendingCountText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingCountText)));
        }
    }

    public ConfirmationCardControl()
    {
        InitializeComponent();
    }

    private void Show(IReadOnlyList<AgentConfirmationRequest> requests)
    {
        _items.Clear();
        _decisions.Clear();

        var index = 0;
        foreach (var req in requests)
            _items.Add(new ConfirmationItemVm(req, index++));

        RequestList.ItemsSource = _items;
        PendingCountText = $"{_decisions.Count}/{_items.Count} 已选择";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
        => ResolveOne(sender, confirmed: true);

    private void Reject_Click(object sender, RoutedEventArgs e)
        => ResolveOne(sender, confirmed: false);

    private void ResolveOne(object sender, bool confirmed)
    {
        // 用 DataContext 定位条目（避免依赖 Tag 绑定）
        if (sender is not FrameworkElement { DataContext: ConfirmationItemVm vm } fe) return;
        if (vm.IsResolved) return;
        var index = vm.Index;
        if (index < 0 || index >= _items.Count) return;

        _items[index].IsResolved = true;
        _decisions[index] = confirmed;

        // 点击的按钮给出明确反馈：文字变为已确认/已拒绝并禁用
        if (fe is Button btn)
        {
            btn.Content = confirmed ? "✓ 已确认" : "✗ 已拒绝";
            btn.IsEnabled = false;
            if (btn.Parent is StackPanel siblings)
            {
                foreach (var s in siblings.Children)
                    if (s is Button b && b != btn) b.IsEnabled = false;
            }
        }

        PendingCountText = $"{_decisions.Count}/{_items.Count} 已选择";
        TryResolveAll();
    }

    private void ConfirmAll_Click(object sender, RoutedEventArgs e)
        => ResolveAll(confirmed: true);

    private void RejectAll_Click(object sender, RoutedEventArgs e)
        => ResolveAll(confirmed: false);

    private void ResolveAll(bool confirmed)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].IsResolved) continue;
            _items[i].IsResolved = true;
            _decisions[i] = confirmed;
        }

        // 整卡置为不可交互（按钮禁用）
        RequestList.IsEnabled = false;

        PendingCountText = $"{_decisions.Count}/{_items.Count} 已选择";
        TryResolveAll();
    }

    private void TryResolveAll()
    {
        if (_decisions.Count < _items.Count) return;

        var decisions = _items
            .OrderBy(i => i.Index)
            .Select(i => new AgentConfirmationDecision
            {
                Request = i.Request,
                Confirmed = _decisions[i.Index]
            })
            .ToList();

        Resolved?.Invoke(this, new ConfirmationResolvedEventArgs { Decisions = decisions });
    }
}
