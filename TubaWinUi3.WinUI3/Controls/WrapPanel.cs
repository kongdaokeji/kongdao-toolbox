using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TubaWinUi3.Controls;

/// <summary>
/// 轻量自动换行面板：在有限可用宽度内把子元素按行排布，
/// 放不下的元素自动换到下一行；可用宽度为无穷大时退化为单行横向布局。
/// 用于“全部”页标签栏展开态的多行显示，无需引入第三方控件包。
/// </summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(0d, static (d, _) => ((WrapPanel)d).InvalidateMeasure()));

    /// <summary>同一行相邻子元素之间的间距。</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private readonly List<Size> _itemSizes = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        double spacing = Spacing;
        bool unbounded = double.IsInfinity(availableSize.Width);
        double width = unbounded ? 0 : Math.Max(0, availableSize.Width);
        double x = 0;
        double rowHeight = 0;
        double totalHeight = 0;
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        _itemSizes.Clear();

        foreach (UIElement child in Children)
        {
            child.Measure(infinite);
            Size desired = child.DesiredSize;
            _itemSizes.Add(desired);

            if (!unbounded && x > 0 && x + desired.Width > width)
            {
                totalHeight += rowHeight + spacing;
                x = 0;
                rowHeight = 0;
            }
            x += desired.Width + spacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }
        totalHeight += rowHeight;

        double outWidth = unbounded ? Math.Max(0, x - spacing) : width;
        return new Size(outWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double spacing = Spacing;
        bool unbounded = double.IsInfinity(finalSize.Width);
        double width = unbounded ? double.MaxValue : Math.Max(0, finalSize.Width);
        double x = 0;
        double y = 0;
        double rowHeight = 0;
        int index = 0;

        foreach (UIElement child in Children)
        {
            Size desired = index < _itemSizes.Count ? _itemSizes[index++] : child.DesiredSize;
            if (!unbounded && x > 0 && x + desired.Width > width)
            {
                y += rowHeight + spacing;
                x = 0;
                rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
            x += desired.Width + spacing;
            rowHeight = Math.Max(rowHeight, desired.Height);
        }
        return finalSize;
    }
}
