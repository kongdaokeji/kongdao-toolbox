using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls.AgentChat;

/// <summary>
/// AI 助手历史记录菜单的共享构建器（完整版页面与标题栏快捷面板共用）：
/// 每个会话一行子菜单，内含「打开 / 重命名… / 删除」。
/// </summary>
public static class AiHistoryMenu
{
    /// <summary>构建历史记录 MenuFlyout。动作回调由调用方实现（页面上下文不同）。</summary>
    public static MenuFlyout Build(
        IReadOnlyList<ConversationMeta> conversations,
        Action<ConversationMeta> onOpen,
        Action<ConversationMeta> onRename,
        Action<ConversationMeta> onDelete)
    {
        var flyout = new MenuFlyout();

        if (conversations.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "暂无历史记录", IsEnabled = false });
            return flyout;
        }

        foreach (var conv in conversations.Take(20))
        {
            var item = new MenuFlyoutSubItem
            {
                Text = $"{conv.Title}  ({conv.CreatedAt:MM/dd HH:mm})"
            };
            item.Items.Add(BuildAction("打开", "\uE8A7", () => onOpen(conv)));
            item.Items.Add(BuildAction("重命名…", "\uE8AC", () => onRename(conv)));
            item.Items.Add(BuildAction("删除", "\uE74D", () => onDelete(conv)));
            flyout.Items.Add(item);
        }

        return flyout;
    }

    /// <summary>重命名输入对话框；返回 trim 后的新标题，取消或留空返回 null。</summary>
    public static async Task<string?> PromptRenameAsync(XamlRoot xamlRoot, string currentTitle)
    {
        var input = new TextBox
        {
            Text = currentTitle,
            MaxLength = 30,
            PlaceholderText = "输入新名称"
        };
        string? submitted = null;
        var dialog = new ContentDialog
        {
            Title = "重命名会话",
            Content = input,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        input.Loaded += (_, _) => input.SelectAll();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                submitted = input.Text.Trim();
                dialog.Hide(); // Hide 不携带结果，经 submitted 通道回传
            }
        };

        var result = await dialog.ShowAsync();
        if (submitted is { Length: > 0 }) return submitted; // 回车直接提交
        if (result != ContentDialogResult.Primary) return null;
        var title = input.Text.Trim();
        return title.Length == 0 ? null : title;
    }

    /// <summary>删除确认对话框。</summary>
    public static async Task<bool> ConfirmDeleteAsync(XamlRoot xamlRoot, string title)
    {
        var dialog = new ContentDialog
        {
            Title = "删除会话",
            Content = $"确定删除「{title}」吗？此操作不可恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static MenuFlyoutItem BuildAction(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph, FontSize = 14 }
        };
        item.Click += (_, _) => action();
        return item;
    }
}
