namespace TubaWinUi3.Services.Ai;

/// <summary>
/// AI 助手面板（FieldCure ChatPanel）销毁竞态异常的识别（Issue #194）。
///
/// 成因：宿主调用 <c>ChatPanel.Dispose()</c>（关闭 AI 助手页 / 快捷问询弹窗、切走页面）时，
/// 组件先 <c>Cancel</c> 流式请求、再 <c>Close()</c> 掉 WebView2；而组件内部仍在途的收尾渲染
/// （<c>OnMessageSent</c> 的取消/异常/收尾分支、<c>ClearConversation</c> 的续跑等）随后才执行，
/// 撞上已关闭的 WebView2（CoreWebView2 == null），由控件抛
/// <c>InvalidOperationException</c>："ExecuteScriptAsync(): Failed because a valid CoreWebView2
/// is not present. Make sure one was created, for example by calling EnsureCoreWebView2Async() API."
/// 组件库 0.21.0 / 0.22.0 的渲染层都没有 CoreWebView2 判空，宿主也拦不住（async void 事件里抛出）。
///
/// 处置：面板此刻已从界面上移除、本轮回复按"用户离开即停止"作废，属于第三方组件销毁竞态，
/// 记日志留痕即可，不该再弹错误窗口打断用户（口径同 App.OnUnobservedTaskException）。
/// </summary>
internal static class ChatPanelCrashFilter
{
    /// <summary>WebView2 控件在 CoreWebView2 为空时的固定报错片段（方法名 + 该句）。</summary>
    private const string MissingCoreWebViewMessage = "CoreWebView2 is not present";

    /// <summary>组件库所在命名空间：报错栈里出现它才认账。</summary>
    private const string ChatPanelStackMarker = "FieldCure.AssistStudio";

    /// <summary>
    /// 是否为本组件库的销毁竞态异常。报错文本与调用栈来源同时匹配才返回 true——
    /// 工具箱自身 WebView2 代码若报同样的错，那仍是真的 bug，必须走错误窗口。
    /// </summary>
    internal static bool IsTeardownRace(Exception? ex)
        => ex is not null
           && ex.Message.Contains(MissingCoreWebViewMessage, StringComparison.OrdinalIgnoreCase)
           && ex.StackTrace?.Contains(ChatPanelStackMarker, StringComparison.Ordinal) == true;
}
