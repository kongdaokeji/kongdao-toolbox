using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using Windows.Graphics;

namespace TubaWinUi3.Services;

public sealed class VolumeShaderTool : IBuiltinTool
{
    public string Id => "volume-shader-test";
    public string Name => "毒蘑菇测试";
    public string Description => "GPU 分形压力测试：轻松 / 中等 / 变态三档压力，超分辨率渲染突破屏幕，实时帧率监控。";
    public string Glyph => "\uE950"; // 显卡图标
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        try
        {
            // 构建本地HTML文件路径
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VolumeShader", "index.html");
            
            if (!File.Exists(htmlPath))
            {
                ShowErrorDialog(context, new FileNotFoundException($"找不到毒蘑菇测试页面: {htmlPath}"));
                return Task.CompletedTask;
            }

            // 使用 file:// 协议打开本地HTML
            var fileUri = new Uri(htmlPath).AbsoluteUri;
            BrowserPage.Open(fileUri, "毒蘑菇显卡测试");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VolumeShaderTool] ExecuteAsync FAILED: {ex.Message}\n{ex.StackTrace}");
            ShowErrorDialog(context, ex);
            return Task.CompletedTask;
        }
    }

    private static async void ShowErrorDialog(BuiltinToolContext context, Exception ex)
    {
        try
        {
            var dialog = context.CreateDialog("毒蘑菇测试 - 启动失败");
            dialog.Content = new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = $"{ex.Message}\n\n{ex.StackTrace}",
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            dialog.CloseButtonText = "确定";
            await dialog.ShowAsync();
        }
        catch { }
    }
}
