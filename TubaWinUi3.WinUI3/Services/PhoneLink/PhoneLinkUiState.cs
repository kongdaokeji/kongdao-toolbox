namespace TubaWinUi3.Services;

/// <summary>
/// 连接手机相关弹窗的可见状态：通知只在用户看不到对应内容时才弹
/// （聊天弹窗开着不发消息通知；任务弹窗开着不发任务开始通知）。
/// </summary>
public static class PhoneLinkUiState
{
    private static volatile bool _chatDialogVisible;
    private static volatile bool _jobsDialogVisible;

    public static bool ChatDialogVisible
    {
        get => _chatDialogVisible;
        set => _chatDialogVisible = value;
    }

    public static bool JobsDialogVisible
    {
        get => _jobsDialogVisible;
        set => _jobsDialogVisible = value;
    }
}
