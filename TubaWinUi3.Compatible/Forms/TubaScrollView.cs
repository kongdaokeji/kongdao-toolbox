using System.Drawing;
using ReaLTaiizor.Controls;

namespace TubaWinUi3.Compatible.Forms
{
    /// <summary>
    /// CrownScrollView 为抽象类（要求子类自绘内容）。本类用于承载真实子控件组成的内容层：
    /// 滚动位移由外部将内容层 Top 与 Viewport.Y 对齐实现，故无需自绘内容。
    /// </summary>
    public sealed class TubaScrollView : CrownScrollView
    {
        protected override void PaintContent(Graphics g)
        {
            // 内容由子控件呈现，此处无自绘
        }
    }
}