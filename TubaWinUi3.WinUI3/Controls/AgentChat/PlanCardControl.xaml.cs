using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Controls.AgentChat;

/// <summary>计划卡片决定事件参数。</summary>
public sealed class PlanResolvedEventArgs : EventArgs
{
    public bool Approved { get; init; }
}

/// <summary>
/// 多步任务计划确认卡片（create_plan 工具）：展示目标与分步计划，
/// 用户批准/拒绝后触发 Resolved。
/// </summary>
public sealed partial class PlanCardControl : UserControl
{
    private bool _resolved;

    public event EventHandler<PlanResolvedEventArgs>? Resolved;

    /// <summary>计划确认请求（x:Bind 注入）。</summary>
    public AgentConfirmationRequest Request
    {
        get => (AgentConfirmationRequest?)GetValue(RequestProperty) ?? throw new InvalidOperationException("Request 未设置");
        set => SetValue(RequestProperty, value);
    }

    public static readonly DependencyProperty RequestProperty = DependencyProperty.Register(
        nameof(Request), typeof(AgentConfirmationRequest), typeof(PlanCardControl),
        new PropertyMetadata(null, (d, e) =>
        {
            if (e.NewValue is AgentConfirmationRequest request)
                ((PlanCardControl)d).Show(request);
        }));

    public PlanCardControl()
    {
        InitializeComponent();
    }

    private void Show(AgentConfirmationRequest request)
    {
        GoalText.Text = string.IsNullOrWhiteSpace(request.PlanGoal)
            ? request.Summary
            : request.PlanGoal;
        StepList.ItemsSource = request.PlanSteps ?? [];
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
        => Resolve(true);

    private void Reject_Click(object sender, RoutedEventArgs e)
        => Resolve(false);

    private void Resolve(bool approved)
    {
        if (_resolved) return;
        _resolved = true;
        ApproveButton.IsEnabled = false;
        RejectButton.IsEnabled = false;
        Resolved?.Invoke(this, new PlanResolvedEventArgs { Approved = approved });
    }
}
