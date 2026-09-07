using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using TubaWinUi3.Controls.AgentChat;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Pages;

/// <summary>
/// 标题栏快捷问询面板（轻量版）：与完整版 AI 助手共用 FieldCure ChatPanel 组件库
/// （消息流式渲染 / 思考块 / 内联工具调用 / ToolApprovalPanel 确认 / 输入控制台）。
///
/// 保留弹窗自己的壳与数据面：
/// - 顶部：提供商/模型切换、新对话/历史、快捷问题（填入输入框 + 聚焦，回车发送）、完整版入口
/// - Provider / 工具 / 持久化 / 记忆 / 技能触发与完整版完全一致（AiAssistantService 共享会话）
/// - 关闭（Unloaded）时保存会话并释放 ChatPanel 的 WebView2 资源
/// </summary>
public sealed partial class AiQuickAskFlyout : UserControl
{
    private bool _syncingCombos;
    private readonly DispatcherQueue _dq;
    private readonly TubaChatProvider _provider = new();
    private readonly List<IAssistTool> _toolAdapters = [];
    private readonly HashSet<string> _activeSkillIds = [];
    private readonly List<PersistedMessage> _persisted = [];

    private string _conversationId = Guid.NewGuid().ToString("N")[..12];
    private string _title = "新对话";
    private ConversationMemory? _memory;
    private bool _disposed;
    private DispatcherTimer? _saveTimer;

    // 会话级 token 统计（与完整版共享 display.json meta 条目）
    private int _sessionPromptTokens;
    private int _sessionCompletionTokens;
    private int _sessionCacheHits;
    private int _sessionCacheMisses;

    private static string HistoryDir => Path.Combine(ConfigManager.GetDataDir(), "AiAssistant");

    private sealed record PersistedMessage(string Role, string Content, string? Thinking);

    public AiQuickAskFlyout()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();

        // ===== ChatPanel 组件库接线（与完整版 AiAgentPage 一致）=====
        _toolAdapters.AddRange(AgentToolRegistry.Tools.Select(t => (IAssistTool)new AgentToolAdapter(t)));
        Chat.Provider = _provider;
        Chat.RegisteredTools = _toolAdapters;
        Chat.MaxToolCallRounds = 30;
        Chat.AllowAttachments = false;
        Chat.IsWorkspaceEnabled = false;
        Chat.IsKnowledgeBaseEnabled = false;
        Chat.ShowTitleBar = false;
        Chat.ShowModelSelector = false;
        Chat.ShowProfileSelector = false;
        Chat.AutoTitle = false;
        Chat.AutoSummarize = false;
        Chat.Theme = ChatTheme.System;
        Chat.UserMessageSubmitted += OnUserMessageSubmitted;
        Chat.MessageAdded += OnMessageAdded;
        Chat.EmptyStateContent = BuildWelcomeContent();
        TubaChatProvider.UsageReported += OnUsageReported;
        AgentToolContext.MemoryModified += OnMemoryModified;

        UpdateServiceStatus();
        InitProviderCombos();
        LoadLatestConversation();
    }

    /// <summary>弹窗关闭（从可视树移除）：保存会话并释放 ChatPanel 资源。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        _disposed = true;
        SaveNow();
        _saveTimer?.Stop();
        Chat.UserMessageSubmitted -= OnUserMessageSubmitted;
        Chat.MessageAdded -= OnMessageAdded;
        TubaChatProvider.UsageReported -= OnUsageReported;
        AgentToolContext.MemoryModified -= OnMemoryModified;
        AgentToolContext.SkillTriggerActive = false;
        if (AgentToolContext.ActiveMemory == _memory)
            AgentToolContext.ActiveMemory = null;
        Chat.Dispose();
    }

    // ---------- 发送流（与完整版同构） ----------

    private void OnUserMessageSubmitted(object? sender, MessageSentEventArgs e)
    {
        // 技能触发：命中触发词 → 系统提示词注入强制指令 + 本次发送内禁用 web_search
        var (trigger, fragments) = SkillTriggerHelper.BuildTriggerPayload(e.Text ?? "", _activeSkillIds);
        AgentToolContext.SkillTriggerActive = !string.IsNullOrEmpty(trigger);
        Chat.SystemPrompt = SkillTriggerHelper.BuildSystemPrompt(_activeSkillIds, trigger, fragments);

        // 首条用户消息 → 会话标题
        if (_title == "新对话" && !string.IsNullOrWhiteSpace(e.Text))
            _title = e.Text.Length > 30 ? e.Text[..30] + "…" : e.Text;
        ScheduleSave();
    }

    private void OnMessageAdded(object? sender, ChatMessage msg)
    {
        if (msg.Role == ChatRole.User)
        {
            AddPersisted("user", msg.Content, null);
        }
        else if (msg.Role == ChatRole.Assistant)
        {
            // 工具调用包装消息（中间轮）不入持久化，定稿时保存完整文本+思考
            if (msg.ToolCalls is { Count: > 0 }) return;
            msg.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChatMessage.IsStreaming) && !msg.IsStreaming)
                    OnAssistantFinalized(msg);
            };
        }
        ScheduleSave();
    }

    private void OnAssistantFinalized(ChatMessage msg)
    {
        AddPersisted("assistant", msg.Content ?? "", msg.ThinkingContent);
        AgentToolContext.SkillTriggerActive = false;
        Chat.MemoryText = _memory?.Read() ?? "";
        ScheduleSave();
    }

    private void AddPersisted(string role, string content, string? thinking)
    {
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(thinking)) return;
        _persisted.Add(new PersistedMessage(role, content ?? "", thinking));
    }

    // ---------- 会话控制 ----------

    private void LoadLatestConversation()
    {
        var conversations = AiAssistantService.ListConversations();
        if (conversations.Count > 0)
            LoadConversation(conversations[0]);
    }

    private async void LoadConversation(ConversationMeta meta)
    {
        _saveTimer?.Stop();
        _conversationId = meta.Id;
        _title = meta.Title;
        _persisted.Clear();
        ResetTokenStats();
        LoadSkills(meta.Id);
        _memory = new ConversationMemory(Path.Combine(HistoryDir, $"{meta.Id}.memory.md"));
        AgentToolContext.ActiveMemory = _memory;
        AgentToolContext.SkillTriggerActive = false;
        Chat.MemoryText = _memory.Read() ?? "";

        // 恢复会话级 token 统计（meta 条目）
        var display = AiAssistantService.LoadConversationDisplay(meta.Id);
        var metaItem = display.FirstOrDefault(i => i.Type == "meta");
        if (metaItem is not null)
        {
            _sessionPromptTokens = metaItem.PromptTokens;
            _sessionCompletionTokens = metaItem.CompletionTokens;
            _sessionCacheHits = metaItem.CacheHitTokens;
            _sessionCacheMisses = metaItem.CacheMissTokens;
        }
        Chat.SystemPrompt = SkillTriggerHelper.BuildSystemPrompt(_activeSkillIds);

        // 清空并回填消息树（与完整版共用的恢复路径）
        Chat.ClearConversation();
        await Task.Delay(200); // 让 ClearConversation 的渲染器复位先完成
        var messages = AiAssistantService.LoadConversation(meta.Id);
        foreach (var m in messages)
        {
            if (m.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(m.Content)) continue;
            Chat.AddRestoredMessage(
                m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                m.Content,
                thinkingContent: m.ReasoningContent);
        }
        await WhenChatReadyAsync();
        await Chat.RenderRestoredMessagesAsync();
        Chat.FocusInput();
    }

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
        _saveTimer?.Stop();
        _conversationId = Guid.NewGuid().ToString("N")[..12];
        _title = "新对话";
        _persisted.Clear();
        ResetTokenStats();
        LoadSkills(_conversationId); // 新会话默认全部技能
        _memory = null;
        AgentToolContext.ActiveMemory = null;
        AgentToolContext.SkillTriggerActive = false;
        Chat.MemoryText = null;
        Chat.ClearConversation();
        Chat.SystemPrompt = SkillTriggerHelper.BuildSystemPrompt(_activeSkillIds);
        Chat.FocusInput();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        var flyout = AiHistoryMenu.Build(
            AiAssistantService.ListConversations(),
            onOpen: LoadConversation,
            onRename: OnRenameConversation,
            onDelete: OnDeleteConversation);
        flyout.ShowAt(sender as FrameworkElement);
    }

    private async void OnRenameConversation(ConversationMeta conv)
    {
        var newTitle = await AiHistoryMenu.PromptRenameAsync(XamlRoot, conv.Title);
        if (newTitle is null) return;

        if (_conversationId == conv.Id)
            _title = newTitle;
        AiAssistantService.RenameConversation(conv.Id, newTitle);
    }

    private async void OnDeleteConversation(ConversationMeta conv)
    {
        if (!await AiHistoryMenu.ConfirmDeleteAsync(XamlRoot, conv.Title)) return;

        AiAssistantService.DeleteConversation(conv.Id);
        if (_conversationId == conv.Id)
            NewConversationButton_Click(this, new RoutedEventArgs());
    }

    // ---------- 快捷问题：填入输入框 + 聚焦（ChatPanel 无公开的编程式发送 API）----------

    private void QuickQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string q }) return;
        if (!TrySetComposeText(q))
            Chat.FocusInput();
    }

    /// <summary>
    /// 把快捷问题写入 ChatPanel 的输入框（compose bar 模板内 PART_MessageTextBox）。
    /// 依赖模板内部结构（FieldCure 0.21.0 固定版本）；找不到时静默失败，用户手动输入。
    /// </summary>
    private bool TrySetComposeText(string text)
    {
        try
        {
            // 模板顺序上输入区在最后，从后向前深度优先找第一个 TextBox
            var tb = FindLastDescendant<TextBox>(Chat);
            if (tb is null) return false;
            tb.Text = text;
            tb.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static T? FindLastDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = count - 1; i >= 0; i--)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var found = FindLastDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    // ---------- 提供商 / 模型切换 ----------

    private void InitProviderCombos()
    {
        _syncingCombos = true;
        try
        {
            var providers = AiProviderStore.GetProviders();
            var selectedId = AiProviderStore.SelectedProviderId;
            // 传副本：活列表原地修改会导致 ItemsSourceView 快照过期（E_INVALIDARG）
            FlyoutProviderCombo.ItemsSource = providers.ToList();
            FlyoutProviderCombo.SelectedItem = providers.FirstOrDefault(p => p.Id == selectedId) ?? providers.FirstOrDefault();

            var provider = AiProviderStore.SelectedProvider;
            var modelId = AiProviderStore.SelectedModelId;
            var models = provider.Models.ToList();
            FlyoutModelCombo.ItemsSource = models;
            FlyoutModelCombo.SelectedItem = models
                .FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault();
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void FlyoutProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos) return;
        if (FlyoutProviderCombo.SelectedItem is not AiProvider provider) return;

        var prev = AiProviderStore.SelectedProviderId;
        if (provider.Id == prev) return;

        AiProviderStore.SetSelected(provider.Id);
        InitProviderCombos();
        UpdateServiceStatus();
    }

    private void FlyoutModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos) return;
        if (FlyoutProviderCombo.SelectedItem is not AiProvider provider) return;
        if (FlyoutModelCombo.SelectedItem is not AiModelOption model) return;

        var prev = AiProviderStore.SelectedModelId;
        if (model.Id.Equals(prev, StringComparison.OrdinalIgnoreCase)) return;

        AiProviderStore.SetSelected(provider.Id, model.Id);
        UpdateServiceStatus();
    }

    private void UpdateServiceStatus()
    {
        if (AiService.IsUsingDefaultModel)
        {
            ServiceStatusText.Text = "自带模型（可在设置中配置 AI 服务）";
        }
        else
        {
            var provider = AiProviderStore.SelectedProvider;
            var (_, model, _) = AiService.GetConfig();
            ServiceStatusText.Text = $"已连接 {provider.Name} · {model}";
        }
    }

    private void FullAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = new AiAssistantTool();
        MainWindow.ActiveToolName = tool.Name;
        tool.ExecuteAsync(new BuiltinToolContext
        {
            XamlRoot = XamlRoot
        });
    }

    private void AiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToSettings("AiApiEndpoint");
    }

    // ---------- 技能 / 持久化 ----------

    private static string SkillsPath(string id) => Path.Combine(HistoryDir, $"{id}.skills.json");

    /// <summary>加载会话技能状态：文件缺失时默认全部技能激活（与完整版共用存档）。</summary>
    private void LoadSkills(string id)
    {
        _activeSkillIds.Clear();
        try
        {
            if (File.Exists(SkillsPath(id)))
            {
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SkillsPath(id)));
                if (ids is not null)
                    foreach (var skillId in ids)
                        if (AgentSkillRegistry.Find(skillId) is not null)
                            _activeSkillIds.Add(skillId);
            }
            else
            {
                foreach (var skill in AgentSkillRegistry.All)
                    _activeSkillIds.Add(skill.Id);
            }
        }
        catch
        {
            foreach (var skill in AgentSkillRegistry.All)
                _activeSkillIds.Add(skill.Id);
        }
    }

    private void SaveSkills()
    {
        try
        {
            var path = SkillsPath(_conversationId);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(_activeSkillIds.OrderBy(x => x).ToList()));
        }
        catch { }
    }

    /// <summary>防抖保存（1.2s）；关闭/切换时立即保存。</summary>
    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            _saveTimer.Tick += (_, _) => SaveNow();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();
        if (_persisted.Count == 0) return;
        try
        {
            var messages = _persisted
                .Select(p => new AiChatMessage
                {
                    Role = p.Role,
                    Content = p.Content,
                    ReasoningContent = p.Thinking
                })
                .ToList();
            AiAssistantService.SaveConversation(_conversationId, _title, messages);

            if (_sessionPromptTokens + _sessionCompletionTokens > 0)
            {
                AiAssistantService.SaveConversationDisplay(_conversationId,
                [
                    new ConversationDisplayItem
                    {
                        Type = "meta",
                        PromptTokens = _sessionPromptTokens,
                        CompletionTokens = _sessionCompletionTokens,
                        CacheHitTokens = _sessionCacheHits,
                        CacheMissTokens = _sessionCacheMisses,
                    }
                ]);
            }
            SaveSkills();
        }
        catch { }
    }

    // ---------- 统计 / 记忆 ----------

    private void OnUsageReported(TokenUsage? usage)
    {
        if (usage is null) return;
        _dq.TryEnqueue(() =>
        {
            _sessionPromptTokens += usage.InputTokens;
            _sessionCompletionTokens += usage.OutputTokens;
            if (usage.CacheReadInputTokens is { } hit)
            {
                _sessionCacheHits += (int)hit;
                _sessionCacheMisses = Math.Max(0, _sessionPromptTokens - _sessionCacheHits);
            }
        });
    }

    private void OnMemoryModified()
    {
        _dq.TryEnqueue(() =>
        {
            if (_memory is { } m)
                Chat.MemoryText = m.Read() ?? "";
        });
    }

    private void ResetTokenStats()
        => _sessionPromptTokens = _sessionCompletionTokens = _sessionCacheHits = _sessionCacheMisses = 0;

    /// <summary>等待 ChatPanel 的 WebView2 渲染器初始化（最多 10 秒）。</summary>
    private async Task WhenChatReadyAsync()
    {
        for (var i = 0; i < 100 && !Chat.IsInitialized; i++)
            await Task.Delay(100);
    }

    // ---------- 空状态 / 欢迎 ----------

    private UIElement BuildWelcomeContent()
    {
        var panel = new StackPanel
        {
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Padding = new Thickness(20, 12, 20, 12)
        };

        if (AiService.IsUsingDefaultModel)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "⚠️ 自带模型可能出现排队/限额满速，质量低下等问题。可在 设置 → AI 服务 中配置自己的 API Key 和模型。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "我是图吧助手，可以快速提问：诊断问题、执行修复、读写文件、搜索最新资讯…\n（危险操作会先请你确认）",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        panel.Children.Add(new TextBlock
        {
            Text = "点上面的快捷问题或直接输入需求，Enter 发送。",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });
        return panel;
    }
}