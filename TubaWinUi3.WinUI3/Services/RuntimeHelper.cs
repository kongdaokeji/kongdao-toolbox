namespace TubaWinUi3.Services;

public static class RuntimeHelper
{
    private static readonly bool _isMsixPackaged = DetectMsixPackaged();
    private static readonly bool _isLiteBuild = DetectLiteBuild();
    private static readonly bool _isInstalled = DetectInstalled();

    public static bool IsMsixPackaged => _isMsixPackaged;

    public static bool IsLiteBuild => _isLiteBuild;

    public static bool IsInstalled => _isInstalled;

    private static bool DetectMsixPackaged()
    {
        try
        {
            var _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? _localAppDataRoot;

    /// <summary>
    /// 包身份解析失败、数据根目录回滚到共享 %LocalAppData% 的标志。
    /// MSIX 下发生回滚时 ToolsRoot/数据目录会指向非打包路径（可能与旧安装版互相污染），
    /// 启动时据此输出诊断日志。
    /// </summary>
    public static bool LocalAppDataRootUsedFallback { get; private set; }

    /// <summary>
    /// 应用数据根目录（即 %LocalAppData%\TubaWinUi3 中的 %LocalAppData%）。
    /// 优先使用包身份(ApplicationData.Current.LocalFolder)解析 —— 它基于包身份，
    /// 即使进程以管理员身份(提权)运行也依然返回包内目录；而
    /// Environment.GetFolderPath(LocalApplicationData) 在提权后可能丢失
    /// 已知文件夹重定向、错误地返回真实用户目录，导致下载/扫描/打开指向不同位置。
    /// 若包身份解析失败（无包身份 / 路径无效 / 不可访问），自动回滚到 KnownFolder 方式。
    /// </summary>
    public static string GetLocalAppDataRoot()
    {
        if (_localAppDataRoot is not null) return _localAppDataRoot;

        if (_isMsixPackaged)
        {
            try
            {
                var path = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
                {
                    _localAppDataRoot = path;
                    return path;
                }
                System.Diagnostics.Debug.WriteLine("[RuntimeHelper] 包身份路径无效，回滚到 KnownFolder 解析");
                LocalAppDataRootUsedFallback = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RuntimeHelper] 包身份解析失败({ex.GetType().Name}: {ex.Message})，回滚到 KnownFolder 解析");
                LocalAppDataRootUsedFallback = true;
            }
        }

        _localAppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return _localAppDataRoot;
    }

    private static bool DetectLiteBuild()
    {
        if (_isMsixPackaged) return false;

        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ".lite_build");
            return File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectInstalled()
    {
        if (_isMsixPackaged) return true;

        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ".installed");
            return File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }
}
