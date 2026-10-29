using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using TubaWinUi3.Services.AiRuntime;

namespace TubaWinUi3.Services;

/// <summary>
/// 本地 AI 试炼场：在设备本地部署并运行 LLM / 图像分类 / 目标检测 / 图像特征模型（NPU / GPU / CPU）。
/// AI 依赖不随安装包分发：入口先过 <see cref="AiRuntimeGate"/>（未安装则下载运行库）。
/// </summary>
public sealed class AiPlaygroundTool : IBuiltinTool
{
    public string Id => "ai-playground";
    public string Name => LocalizationService.L("Builtin_ai-playground_Name", "本地AI试炼场");
    public string Description => LocalizationService.L("Builtin_ai-playground_Desc",
        "本地部署并运行 AI 模型：LLM 对话、图像分类、目标检测、图像特征对比，支持 NPU / GPU / CPU 加速。");
    public string Glyph => "\uE950";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        if (!await AiRuntimeGate.EnsureReadyAsync(context.XamlRoot)) return;
        OpenPage();
    }

    /// <summary>
    /// 类型解析必须发生在运行库就绪之后：本方法体引用 AiPlaygroundPage（其字段带 ORT 类型），
    /// JIT 时会解析这些类型，所以单独一层 NoInlining（调用方 ExecuteAsync 不得出现 AI 类型）。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OpenPage()
    {
        App.MainWindow?.NavigateToToolPage(typeof(Pages.AiPlaygroundPage));
    }
}
