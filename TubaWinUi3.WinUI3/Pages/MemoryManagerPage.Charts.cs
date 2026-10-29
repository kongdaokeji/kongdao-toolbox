using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using TubaWinUi3.Services;
using TubaWinUi3.Services.MemoryManagement;

namespace TubaWinUi3.Pages;

public sealed partial class MemoryManagerPage
{
    private readonly List<MemorySnapshot> _graphSamples = [];
    private readonly HashSet<string> _hiddenMetrics = [];
    private int _pickerGroup = -1;
    private bool _chartsReady;
    private string? _graphKey;
    private (double? XMin, double? XMax, double? YMin, double? YMax) _defaultAxes;
    private sealed record GraphMetric(string Name, Func<MemorySnapshot, ulong?> Value, Func<MemorySnapshot, ulong> Limit);

    private void InitializeCharts()
    {
        ChartInitializer.EnsureConfigured();
        GraphTitle.Text = L("Charts", "内存图表", "Memory charts");
        GraphMode.Header = L("ChartMode", "查看方式", "View");
        GraphGroup.Header = L("ChartGroup", "数据分类", "Metric group");
        GraphUnit.Header = L("ChartUnit", "显示单位", "Units");
        GraphWindow.Header = L("ChartWindow", "趋势时间范围", "Trend window");
        SmallScale.Content = L("SmallScale", "放大小项（对数尺度）", "Magnify small values (log scale)");
        PauseGraph.Content = L("PauseSample", "暂停采样", "Pause sampling");
        ResetGraph.Content = L("ResetZoom", "重置缩放", "Reset zoom");
        ExportGraph.Content = L("ExportCsv", "导出采样 CSV", "Export samples as CSV");
        foreach (var item in new[] { L("Trend", "实时趋势", "Live trend"), L("Compare", "当前分类对比", "Current category comparison") }) GraphMode.Items.Add(item);
        foreach (var item in new[] { L("PhysicalGroup", "物理内存", "Physical memory"), L("CacheGroup", "缓存与内核池", "Cache and kernel pools"), L("CommitGroup", "提交预算", "Commit budget"), L("PriorityGroup", "待机优先级 0–7", "Standby priorities 0–7") }) GraphGroup.Items.Add(item);
        GraphUnit.Items.Add(L("CapacityUnit", "容量（自动单位）", "Capacity (automatic units)")); GraphUnit.Items.Add(L("PercentUnit", "占对应总量的百分比", "Percentage of respective total"));
        foreach (var minutes in new[] { 1, 5, 10 }) GraphWindow.Items.Add(minutes + L("Minutes", " 分钟", " minutes"));
        GraphMode.SelectedIndex = GraphGroup.SelectedIndex = GraphUnit.SelectedIndex = 0; GraphWindow.SelectedIndex = 1;
        MemoryChart.ZoomMode = ZoomAndPanMode.X;
        MemoryChart.AnimationsSpeed = TimeSpan.FromMilliseconds(150);
        _chartsReady = true;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateGraph);
    }
    private IReadOnlyList<GraphMetric> GraphMetrics() => GraphGroup.SelectedIndex switch
    {
        1 => new GraphMetric[] {
            new(L("Cache", "系统缓存", "System cache"), s => s.Cache, s => s.Total),
            new(L("PagedPool", "分页池", "Paged pool"), s => s.PagedPool, s => s.Total),
            new(L("NonpagedPool", "非分页池", "Nonpaged pool"), s => s.NonpagedPool, s => s.Total),
            new(L("Modified", "修改页", "Modified pages"), s => s.Modified, s => s.Total) },
        2 => new GraphMetric[] {
            new(L("Commit", "已提交（占提交上限）", "Committed (of commit limit)"), s => s.Commit, s => s.CommitLimit),
            new(L("CommitPeak", "提交峰值（占提交上限）", "Peak commit (of commit limit)"), s => s.CommitPeak, s => s.CommitLimit),
            new(L("CommitLimit", "提交上限", "Commit limit"), s => s.CommitLimit, s => s.CommitLimit) },
        3 => Enumerable.Range(0, 8).Select(i => new GraphMetric(L("Priority", "待机优先级", "Standby priority") + " " + i, s => s.StandbyByPriority?[i], s => s.Total)).ToArray(),
        _ => new GraphMetric[] {
            new(L("Occupied", "已占用（含修改页）", "Occupied (includes modified pages)"), s => s.Used, s => s.Total),
            new(L("Available", "可用内存", "Available memory"), s => s.Available, s => s.Total),
            new(L("Standby", "待机缓存", "Standby cache"), s => s.Standby, s => s.Total),
            new(L("Free", "空闲", "Free"), s => s.Free, s => s.Total),
            new(L("Zeroed", "已清零", "Zeroed"), s => s.Zeroed, s => s.Total) }
    };
    private void AddGraphSample(MemorySnapshot sample)
    {
        _graphSamples.Add(sample); _graphSamples.RemoveAll(s => s.Time < sample.Time.AddMinutes(-10));
        while (_graphSamples.Count > 600) _graphSamples.RemoveAt(0);
        UpdateGraph();
    }
    private void GraphSelectionChanged(object sender, RoutedEventArgs args) => UpdateGraph();
    private void UpdateGraph()
    {
        if (!_chartsReady || _graphSamples.Count == 0) return;
        var metrics = GraphMetrics();
        if (_pickerGroup != GraphGroup.SelectedIndex)
        {
            _pickerGroup = GraphGroup.SelectedIndex; _hiddenMetrics.Clear(); MetricPicker.Children.Clear();
            foreach (var m in metrics)
            {
                var check = new CheckBox { Content = m.Name, IsChecked = true };
                check.Checked += (_, _) => { _hiddenMetrics.Remove(m.Name); UpdateGraph(); };
                check.Unchecked += (_, _) => { _hiddenMetrics.Add(m.Name); UpdateGraph(); }; MetricPicker.Children.Add(check);
            }
        }
        var shown = metrics.Where(m => !_hiddenMetrics.Contains(m.Name)).ToArray();
        var sample = _graphSamples[^1]; bool compare = GraphMode.SelectedIndex == 1, percent = GraphUnit.SelectedIndex == 1;
        bool logarithmic = SmallScale.IsChecked == true;
        GraphUnit.IsEnabled = !logarithmic;
        if (logarithmic && percent) { GraphUnit.SelectedIndex = 0; return; }
        double scale = percent ? 1 : GraphScale(sample, shown);
        double? Value(GraphMetric m, MemorySnapshot s)
        {
            return MemoryPlotScale.Value(m.Value(s), m.Limit(s), percent, logarithmic, scale);
        }
        string Label(double plotted)
        {
            double raw = logarithmic ? Math.Pow(10, plotted) - 1 : plotted;
            return percent ? $"{raw:0.###}%" : MemoryText.Bytes((ulong)Math.Max(0, Math.Round(raw * (logarithmic ? 1 : scale))));
        }
        var color = ((SolidColorBrush)ChartThemeProbe.Foreground).Color;
        SolidColorPaint TextPaint() => new(new SKColor(color.R, color.G, color.B));
        var series = new List<ISeries>();
        string key = $"{GraphMode.SelectedIndex}:{GraphGroup.SelectedIndex}:{GraphUnit.SelectedIndex}:{GraphWindow.SelectedIndex}:{logarithmic}:{scale}:" + string.Join("|", shown.Select(m => m.Name));
        bool retainZoom = _graphKey == key;
        var oldX = MemoryChart.XAxes?.OfType<Axis>().FirstOrDefault(); var oldY = MemoryChart.YAxes?.OfType<Axis>().FirstOrDefault();
        bool userZoom = oldX?.MinLimit != _defaultAxes.XMin || oldX?.MaxLimit != _defaultAxes.XMax
            || oldY?.MinLimit != _defaultAxes.YMin || oldY?.MaxLimit != _defaultAxes.YMax;
        if (compare)
        {
            series.Add(new RowSeries<double?> { Name = L("CurrentValues", "当前值", "Current values"), Values = shown.Select(m => Value(m, sample)).ToArray(),
                XToolTipLabelFormatter = p => Label(p.Coordinate.PrimaryValue), DataLabelsSize = 11, DataLabelsPaint = TextPaint(), DataLabelsPosition = DataLabelsPosition.End,
                DataLabelsFormatter = p => Label(p.Coordinate.PrimaryValue) });
            double largest = shown.Select(m => Value(m, sample) ?? 0).DefaultIfEmpty().Max();
            MemoryChart.XAxes = new Axis[] { new() { MinLimit = 0, MaxLimit = Math.Max(1, largest * 1.25), Labeler = Label, LabelsPaint = TextPaint(), TextSize = 11 } };
            MemoryChart.YAxes = new Axis[] { new() { Labels = shown.Select(m => m.Name).ToArray(), LabelsPaint = TextPaint(), MinStep = 1, TextSize = 11 } };
            MemoryChart.ZoomMode = ZoomAndPanMode.X;
        }
        else
        {
            int minutes = GraphWindow.SelectedIndex switch { 0 => 1, 2 => 10, _ => 5 };
            var window = _graphSamples.Where(s => s.Time >= sample.Time.AddMinutes(-minutes)).ToArray();
            foreach (var m in shown)
                series.Add(new LineSeries<ObservablePoint> { Name = m.Name, Values = window.Select(s => new ObservablePoint(s.Time.ToUnixTimeMilliseconds() / 1000d, Value(m, s))).ToArray(), GeometrySize = 0, Fill = null,
                    XToolTipLabelFormatter = p => DateTimeOffset.FromUnixTimeMilliseconds((long)(p.Coordinate.SecondaryValue * 1000)).ToLocalTime().ToString("HH:mm:ss"),
                    YToolTipLabelFormatter = p => m.Name + ": " + Label(p.Coordinate.PrimaryValue) });
            MemoryChart.XAxes = new Axis[] { new() { Labeler = v => DateTimeOffset.FromUnixTimeMilliseconds((long)(v * 1000)).ToLocalTime().ToString("HH:mm:ss"), LabelsPaint = TextPaint(), MinStep = 10, TextSize = 11 } };
            MemoryChart.YAxes = new Axis[] { new() { MinLimit = 0, Labeler = Label, LabelsPaint = TextPaint(), TextSize = 11 } };
        }
        MemoryChart.Series = series; MemoryChart.LegendPosition = compare ? LegendPosition.Hidden : LegendPosition.Bottom;
        var newX = MemoryChart.XAxes.OfType<Axis>().First(); var newY = MemoryChart.YAxes.OfType<Axis>().First();
        _defaultAxes = (newX.MinLimit, newX.MaxLimit, newY.MinLimit, newY.MaxLimit);
        if (retainZoom && userZoom)
        {
            var x = MemoryChart.XAxes.OfType<Axis>().First(); var y = MemoryChart.YAxes.OfType<Axis>().First();
            x.MinLimit = oldX?.MinLimit; x.MaxLimit = oldX?.MaxLimit; y.MinLimit = oldY?.MinLimit; y.MaxLimit = oldY?.MaxLimit;
        }
        _graphKey = key;
        GraphWindow.IsEnabled = !compare;
        GraphHint.Text = logarithmic ? L("LogHint", "对数尺度放大小项，柱长或曲线高度不表示真实占比。悬停看精确值；勾选指标可单独查看。", "Log scale magnifies small values; height and length do not represent actual shares. Hover for values; select individual metrics.")
            : L("GraphHint", "悬停查看精确值，滚轮缩放；勾选指标可单独比较小项。每项容量和占比始终在下方显示，不把重叠指标相加。", "Hover for values, scroll to zoom and select individual metrics. Exact capacities and shares remain below; overlapping metrics are not added.");
        GraphReadout.Text = sample.Time.ToString("HH:mm:ss") + "\n" + string.Join("\n", shown.Select(m => $"{m.Name}  {MemoryText.Bytes(m.Value(sample))} · {MemoryText.Percentage(m.Value(sample), m.Limit(sample))}"));
    }
    private static double GraphScale(MemorySnapshot sample, IReadOnlyList<GraphMetric> metrics)
    {
        ulong largest = metrics.Select(m => m.Value(sample) ?? 0).DefaultIfEmpty().Max();
        // Stable byte units give log magnification enough range to expose tiny categories.
        return largest >= 1UL << 30 ? 1048576d : largest >= 1UL << 20 ? 1024d : 1d;
    }
    private void ResetGraph_Click(object sender, RoutedEventArgs args) { _graphKey = null; UpdateGraph(); }
    private async void ExportGraph_Click(object sender, RoutedEventArgs args) => await Guard(async () =>
    {
        var path = Win32Dialogs.PickSave("CSV\0*.csv\0\0", "csv", "memory-samples.csv"); if (path is null) return;
        var metrics = GraphMetrics().ToArray(); var samples = _graphSamples.ToArray();
        string Escape(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "Time," + string.Join(",", metrics.Select(m => Escape(m.Name + " (bytes)"))) };
        lines.AddRange(samples.Select(s => s.Time.ToString("O", CultureInfo.InvariantCulture) + "," + string.Join(",", metrics.Select(m => m.Value(s)?.ToString(CultureInfo.InvariantCulture) ?? ""))));
        await File.WriteAllLinesAsync(path, lines, new System.Text.UTF8Encoding(true)); Notice(path);
    });
}
