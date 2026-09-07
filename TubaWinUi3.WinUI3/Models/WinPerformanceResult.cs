using System;

namespace TubaWinUi3.Models;

/// <summary>
/// 一轮 WinUI 性能子测试的结果：记录列表加载 / 图片列表渲染 / 标签切换 / 滚动 /
/// 树形列表展开 / 数据排序过滤 / 长文本渲染 等真实 UI 操作的耗时（毫秒），以及本轮总耗时。
/// 该分数独立于「游戏性能 / 办公性能」，不参与两者的总分计算。
/// </summary>
public sealed class WinPerformanceRunResult
{
    /// <summary>列表加载：向 ListView 批量添加大量条目并完成布局。</summary>
    public double ListLoadMs { get; set; }

    /// <summary>图片列表渲染：渲染大量工具图标缓存图片。</summary>
    public double ImageListMs { get; set; }

    /// <summary>快速切换标签页：连续切换 TabView 选中项。</summary>
    public double TabSwitchMs { get; set; }

    /// <summary>滚动流畅度：ScrollViewer 滚动到底再回顶。</summary>
    public double ScrollMs { get; set; }

    /// <summary>树形列表展开：展开/折叠大型 TreeView。</summary>
    public double TreeExpandMs { get; set; }

    /// <summary>数据排序过滤：对大数据集合反复排序与过滤。</summary>
    public double SortFilterMs { get; set; }

    /// <summary>长文本渲染：渲染超长文本并完成排版。</summary>
    public double TextRenderMs { get; set; }

    /// <summary>本轮总耗时（各子测试之和）。</summary>
    public double TotalMs =>
        ListLoadMs + ImageListMs + TabSwitchMs + ScrollMs + TreeExpandMs + SortFilterMs + TextRenderMs;
}

/// <summary>
/// 一次完整的 Win性能测试：重复执行 5 轮，去掉耗时最高（得分最低）的一轮，
/// 用剩余 4 轮的平均耗时计算最终得分。FinalScore 单独展示，不并入游戏/办公总分。
/// </summary>
public sealed class WinPerformanceResult
{
    public DateTime TestTime { get; set; }

    public string CpuName { get; set; } = "";

    public string OsName { get; set; } = "";

    public System.Collections.Generic.List<WinPerformanceRunResult> Runs { get; set; } = new();

    public int RunCount { get; set; } = 5;

    public int DroppedRunCount { get; set; } = 1;

    /// <summary>去掉最高耗时轮后，剩余轮次的平均耗时（毫秒）。</summary>
    public double BestAvgMs { get; set; }

    /// <summary>去掉最高耗时轮后，各子测试的平均耗时（毫秒），用于展示明细。</summary>
    public double AvgListLoadMs { get; set; }

    public double AvgImageListMs { get; set; }

    public double AvgTabSwitchMs { get; set; }

    public double AvgScrollMs { get; set; }

    public double AvgTreeExpandMs { get; set; }

    public double AvgSortFilterMs { get; set; }

    public double AvgTextRenderMs { get; set; }

    /// <summary>最终得分（100 分制），与游戏/办公总分互相独立。</summary>
    public int FinalScore { get; set; }

    public string Grade { get; set; } = "";
}
