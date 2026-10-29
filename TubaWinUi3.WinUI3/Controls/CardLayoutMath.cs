using Windows.Foundation;

namespace TubaWinUi3.Controls;

/// <summary>
/// 信息卡瀑布流的纯布局数学：列数解析、跨列占位的列选择、按列堆叠排布。
/// 与界面无关（唯一的外部类型是 Rect），便于单测覆盖各种极端输入。
/// </summary>
public static class CardLayoutMath
{
    /// <summary>列宽下限：再窄也保证卡片可读，防止窗口极小时算出上千列。</summary>
    private const double HardMinColumnWidth = 120;

    /// <summary>
    /// 按可用宽度解析列数：每列至少 <paramref name="minColumnWidth"/>，列间平均分掉 <paramref name="gap"/>。
    /// 无效宽度（NaN/负值）回退 1 列。
    /// </summary>
    public static int ResolveColumnCount(double width, double minColumnWidth = 336, double gap = 14)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            return 1;

        var unit = Math.Max(minColumnWidth, HardMinColumnWidth);
        var count = (int)Math.Floor((width + gap) / (unit + gap));
        return Math.Max(1, count);
    }

    /// <summary>
    /// 在列高数组里挑出「连续 <paramref name="span"/> 列总和最小」的起始列。
    /// 并列取最左列；span 超出列数时钳到 1。列高列表为空时返回 0。
    /// </summary>
    public static int ShortestColumn(IReadOnlyList<double> heights, int span)
    {
        if (heights is null || heights.Count == 0) return 0;

        var width = Math.Clamp(span, 1, heights.Count);
        var best = 0;
        var bestSum = double.MaxValue;

        for (var start = 0; start + width <= heights.Count; start++)
        {
            var sum = 0d;
            for (var k = 0; k < width; k++)
                sum += Math.Max(0, heights[start + k]);

            if (sum < bestSum - 0.001)
            {
                bestSum = sum;
                best = start;
            }
        }

        return best;
    }

    /// <summary>
    /// 把卡片按「瀑布流」摆进各列：
    /// 每张卡放在其覆盖列中最高的底部，显式指定的列不会因为别处更矮而搬家（布局稳定、不闪动的根源）。
    /// <paramref name="columnCount"/> 与卡片跨度都会被钳制到合法范围；<c>Column = -1</c> 表示自动挑最短列。
    /// </summary>
    public static List<CardPlacement> Arrange(
        IReadOnlyList<BoardItem> items,
        double columnWidth,
        int columnCount,
        double columnGap,
        double rowGap)
    {
        var placements = new List<CardPlacement>(items?.Count ?? 0);
        if (items is null || items.Count == 0) return placements;

        var columns = Math.Max(1, columnCount);
        var width = Math.Max(0, columnWidth);
        var heights = new double[columns];

        foreach (var item in items)
        {
            var span = Math.Clamp(item.ColumnSpan, 1, columns);
            var column = item.Column < 0
                ? ShortestColumn(heights, span)
                : Math.Clamp(item.Column, 0, columns - span);

            var y = 0d;
            for (var k = 0; k < span; k++)
                y = Math.Max(y, heights[column + k]);

            var cardHeight = Math.Max(0, item.Height);
            var rect = new Rect(
                column * (width + columnGap),
                y,
                span * width + (span - 1) * columnGap,
                cardHeight);

            placements.Add(new CardPlacement(column, span, rect));

            for (var k = 0; k < span; k++)
                heights[column + k] = y + cardHeight + Math.Max(0, rowGap);
        }

        return placements;
    }

    /// <summary>
    /// 「铺满」补偿：内容比目标高度矮时，把每列的缺口平均补给该列的<b>单列卡片</b>
    /// （跨列卡片不动，否则会同时影响多列、破坏对齐）。
    /// 返回每张卡片的额外高度（0 = 不拉伸）；调用方把这些高度加回卡片后重新 Arrange，
    /// 下面的卡片会被自然推到目标高度处。
    /// </summary>
    public static List<double> DistributeFill(
        IReadOnlyList<BoardItem> items,
        IReadOnlyList<CardPlacement> placements,
        int columnCount,
        double targetHeight)
    {
        var extras = new List<double>(items?.Count ?? 0);
        if (items is null) return extras;
        for (var i = 0; i < items.Count; i++) extras.Add(0);
        if (items.Count == 0 || placements is null || placements.Count != items.Count) return extras;
        if (columnCount <= 0 || targetHeight <= 0 || double.IsNaN(targetHeight) || double.IsInfinity(targetHeight)) return extras;

        var columns = Math.Max(1, columnCount);
        var bottoms = new double[columns];
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            for (var c = placement.Column; c < placement.Column + placement.ColumnSpan && c < columns; c++)
                bottoms[c] = Math.Max(bottoms[c], placement.Rect.Bottom);
        }

        for (var c = 0; c < columns; c++)
        {
            var deficit = targetHeight - bottoms[c];
            if (deficit <= 0.5) continue;

            var candidates = 0;
            for (var i = 0; i < placements.Count; i++)
            {
                if (placements[i].ColumnSpan == 1 && placements[i].Column == c) candidates++;
            }
            if (candidates == 0) continue;

            var share = deficit / candidates;
            for (var i = 0; i < placements.Count; i++)
            {
                if (placements[i].ColumnSpan == 1 && placements[i].Column == c)
                    extras[i] += share;
            }
        }

        return extras;
    }
}

/// <summary>瀑布流的输入卡片：目标列（-1 = 自动挑最短列）、占几列、测量出来的高度。</summary>
public readonly record struct BoardItem(int Column, int ColumnSpan, double Height);

/// <summary>瀑布流的排布结果：最终列、占几列、矩形（相对面板左上角）。</summary>
public readonly record struct CardPlacement(int Column, int ColumnSpan, Rect Rect);
