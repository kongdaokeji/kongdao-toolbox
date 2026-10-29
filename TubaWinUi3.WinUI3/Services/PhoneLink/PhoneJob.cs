using System.Diagnostics;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>手机端发起的电脑端任务类型。</summary>
public enum PhoneJobKind
{
    Exec,
    Winget
}

public enum PhoneJobStatus
{
    Running,
    Done,
    Failed,
    Cancelled
}

/// <summary>
/// 手机端发起的电脑端任务（PowerShell 命令 / winget 安装）：
/// PC 端可在「手机任务」弹窗里查看实时输出并终止，手机端仍按原接口拿结果。
/// </summary>
public sealed class PhoneJob
{
    private const int MaxOutputChars = 200_000;
    private readonly StringBuilder _output = new();
    private volatile PhoneJobStatus _status = PhoneJobStatus.Running;

    public string Id { get; init; } = "";
    public PhoneJobKind Kind { get; init; }
    /// <summary>展示标题：命令行文本或软件显示名（缺省为 winget id）。</summary>
    public string Title { get; init; } = "";
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime? FinishedAt { get; internal set; }
    public int ExitCode { get; internal set; } = -1;

    public PhoneJobStatus Status
    {
        get => _status;
        internal set => _status = value;
    }

    public bool IsRunning => Status == PhoneJobStatus.Running;

    /// <summary>用户从电脑端点了「终止」或服务停止（用于区分超时 / 手动终止的结束文案）。</summary>
    internal bool CancelRequestedByUser { get; set; }

    internal CancellationTokenSource? Cts { get; set; }
    internal Process? Proc { get; set; }

    /// <summary>追加一行输出（OutputDataReceived 回调与结束文案共用；超长后丢弃）。</summary>
    public void AppendLine(string? line)
    {
        if (line is null) return;
        lock (_output)
        {
            if (_output.Length < MaxOutputChars) _output.AppendLine(line);
        }
    }

    public string Output
    {
        get { lock (_output) return _output.ToString(); }
    }
}
