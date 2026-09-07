using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Services;

/// <summary>
/// 空岛卡片 3D 交互特效（对齐空岛安装器）：
/// 鼠标跟随轻微倾斜（PlaneProjection）+ 悬停蓝色辉光描边。
/// 用法：挂到卡片根 Border 的 PointerEntered / PointerMoved / PointerExited。
/// </summary>
public static class KdCardFx
{
    private const double MaxTilt = 6.0;

    public static void Enter(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card)
            return;
        card.Projection ??= new PlaneProjection { RotationX = 0, RotationY = 0 };
        // 辉光：空岛亮蓝半透明描边
        card.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB4, 0x5D, 0x9F, 0xFF));
        Move(sender, e);
    }

    public static void Move(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card || card.Projection is not PlaneProjection proj)
            return;
        var p = e.GetCurrentPoint(card).Position;
        double w = Math.Max(card.ActualWidth, 1);
        double h = Math.Max(card.ActualHeight, 1);
        double nx = p.X / w * 2 - 1;
        double ny = p.Y / h * 2 - 1;
        proj.RotationY = Math.Clamp(nx * MaxTilt, -MaxTilt, MaxTilt);
        proj.RotationX = Math.Clamp(-ny * MaxTilt, -MaxTilt, MaxTilt);
    }

    public static void Exit(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border card)
            return;
        if (card.Projection is PlaneProjection proj)
        {
            proj.RotationX = 0;
            proj.RotationY = 0;
        }
        // 恢复 XAML 里的 {ThemeResource} 描边
        card.ClearValue(Border.BorderBrushProperty);
    }
}
