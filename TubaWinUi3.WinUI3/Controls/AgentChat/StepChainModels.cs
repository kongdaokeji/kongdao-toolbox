using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Controls.AgentChat;

/// <summary>INPC 基类。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>一条 Agent 步骤的 UI 视图模型（绑定步骤链列表行）。</summary>
public sealed class StepRowVm : ObservableObject
{
    private AgentStep _step;

    public StepRowVm(AgentStep step)
    {
        _step = step;
    }

    public string ToolGlyph => _step.Glyph;
    public string DisplayName => _step.DisplayName;
    public string Summary => _step.Summary;
    public string StatusText => _step.StatusText;
    public string CallId => _step.CallId ?? "";

    public bool IsRunning => _step.Status == AgentStepStatus.Running;
    public bool IsWaiting => _step.Status == AgentStepStatus.AwaitingConfirmation;
    public bool IsFailed => _step.Status == AgentStepStatus.Failed;
    public bool IsDone => _step.Status is AgentStepStatus.Success or AgentStepStatus.Failed or AgentStepStatus.Rejected or AgentStepStatus.Cancelled;

    public Visibility IsRunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsWaitingVisibility => IsWaiting ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsDoneVisibility => IsDone ? Visibility.Visible : Visibility.Collapsed;

    public string StatusGlyph => _step.Status switch
    {
        AgentStepStatus.AwaitingConfirmation => "\uE823", // StatusCircleQuestionMark
        AgentStepStatus.Success => "\uE73E",              // CheckMark
        AgentStepStatus.Failed => "\uE783",               // ErrorBadge
        AgentStepStatus.Rejected => "\uE711",             // Cancel
        AgentStepStatus.Cancelled => "\uE711",
        _ => ""
    };

    public Brush StatusBrush => _step.Status switch
    {
        AgentStepStatus.Success => GetBrush("SystemFillColorSuccessBrush"),
        AgentStepStatus.Failed => GetBrush("SystemFillColorCriticalBrush"),
        AgentStepStatus.Rejected => GetBrush("TextFillColorSecondaryBrush"),
        AgentStepStatus.AwaitingConfirmation => GetBrush("AccentTextFillColorPrimaryBrush"),
        _ => GetBrush("AccentTextFillColorPrimaryBrush")
    };

    public string DurationText => _step.Duration is { } d ? $"{d.TotalSeconds:F0}s" : "";

    public string? ResultPreview
    {
        get
        {
            var text = _step.Status == AgentStepStatus.Failed
                ? _step.Error ?? _step.Result
                : _step.Result;
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (text.Length > 200) text = text[..200] + "…";
            return text;
        }
    }

    /// <summary>步骤状态更新（同一实例）。</summary>
    public void Update(AgentStep step)
    {
        _step = step;
        OnPropertyChanged(null); // 刷新全部绑定
    }

    private static Brush GetBrush(string key)
    {
        // 资源键缺失时回退到强调色，避免绑定求值抛异常导致崩溃
        try
        {
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var value) &&
                value is Brush brush)
                return brush;
        }
        catch { }
        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"];
    }
}

/// <summary>
/// 一轮 Agent 步骤链的 UI 视图模型：运行中实时展开，完成后自动折叠为摘要。
/// </summary>
public sealed class RunVm : ObservableObject
{
    public ObservableCollection<StepRowVm> Steps { get; } = [];

    private bool _isRunning;
    /// <summary>整链是否仍在执行（翻转为 false 时控件自动折叠）。</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => Set(ref _isRunning, value);
    }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    private string _summaryText = "正在执行…";
    public string SummaryText
    {
        get => _summaryText;
        set => Set(ref _summaryText, value);
    }

    public void AddStep(StepRowVm vm)
    {
        Steps.Add(vm);
        SummaryText = $"正在执行… 第 {Steps.Count} 步";
    }

    /// <summary>按工具调用 ID 查找步骤行（状态更新用）。</summary>
    public StepRowVm? FindByCallId(string callId)
    {
        if (string.IsNullOrEmpty(callId)) return null;
        return Steps.FirstOrDefault(s => s.CallId == callId);
    }

    /// <summary>整链完成：更新摘要并标记结束（触发折叠动画）。</summary>
    public void Complete(AgentStepGroupSummary summary)
    {
        SummaryText = summary.ToDisplayText();
        IsRunning = false;
    }
}
