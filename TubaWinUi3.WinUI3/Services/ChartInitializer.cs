using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace TubaWinUi3.Services;

/// <summary>
/// LiveCharts/SkiaSharp 惰性初始化：图表配置从 App 构造函数移到这里，
/// 仅在第一个图表页面（性能测试/流量监控/云端排行）首次访问时执行，
/// 启动不再加载 SkiaSharp 原生库。幂等，重复调用无副作用。
/// </summary>
public static class ChartInitializer
{
    private static int _configured;

    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
            return;

        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers()
            .AddDefaultTheme());
    }
}
