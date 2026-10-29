using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace TubaWinUi3.Services.AiRuntime;

/// <summary>
/// AI 运行库的进程内动态加载器：把数据目录（或应用目录自带的）AI DLL 注册进
/// 默认 <see cref="AssemblyLoadContext"/>（保持类型标识一致，避免 ALC 分裂）：
/// - 托管程序集：首次解析失败时按文件名到运行库目录取（LoadFromAssemblyPath）；
/// - 原生库：每个加载的 AI 程序集挂 DllImportResolver 指向运行库目录，并把目录加入
///   DLL 搜索路径（AddDllDirectory + PATH 前置），覆盖 native→native 的按名加载
///   （onnxruntime-genai.dll → onnxruntime.dll）；
/// - 预加载原生依赖链（DirectML → onnxruntime → WinML → genai），让后续按名加载直接命中。
/// 未安装运行库时 <see cref="EnsureLoaded"/> 抛可读异常；入口都在 <see cref="AiRuntimeGate"/>。
/// </summary>
public static class AiRuntimeLoader
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IntPtr> NativeHandles = new(StringComparer.OrdinalIgnoreCase);
    private static string? _runtimeDir;
    private static bool _resolversRegistered;

    /// <summary>运行库就绪时对应的目录（未安装返回 null）。</summary>
    public static string? RuntimeDir => AiRuntimeBundleService.ResolveRuntimeDir();

    /// <summary>是否已就绪（应用目录自带或数据目录已安装）。</summary>
    public static bool IsReady => RuntimeDir is not null;

    /// <summary>
    /// 注册解析器并按需预加载原生库（幂等）。运行库未安装时抛
    /// <see cref="InvalidOperationException"/>（含可读原因）。
    /// </summary>
    public static void EnsureLoaded(IProgress<string>? progress = null)
    {
        var dir = RuntimeDir ?? throw new InvalidOperationException(
            "AI 运行库尚未初始化——请在打开 本地AI试炼场时按提示下载。");

        lock (Gate)
        {
            if (_resolversRegistered && string.Equals(_runtimeDir, dir, StringComparison.OrdinalIgnoreCase))
                return;

            _runtimeDir = dir;

            if (!_resolversRegistered)
            {
                AssemblyLoadContext.Default.Resolving += OnResolving;
                _resolversRegistered = true;
            }

            PrepareNativeSearchPath(dir);
            PreloadNatives(dir, progress);
        }
    }

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        var dir = _runtimeDir;
        if (dir is null || name.Name is null) return null;

        var path = Path.Combine(dir, name.Name + ".dll");
        if (!File.Exists(path)) return null;

        try
        {
            var assembly = context.LoadFromAssemblyPath(path);
            TrySetDllImportResolver(assembly, dir);
            return assembly;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>为加载的 AI 程序集挂 DllImportResolver（同名原生库到运行库目录取）。</summary>
    private static void TrySetDllImportResolver(Assembly assembly, string dir)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, (libraryName, _, _) =>
            {
                var path = Path.Combine(dir, libraryName + ".dll");
                if (!File.Exists(path)) return IntPtr.Zero;
                return LoadNativeCached(path);
            });
        }
        catch
        {
            // 已设置过解析器（或程序集不支持）——忽略，按名加载仍能命中 AddDllDirectory
        }
    }

    private static IntPtr LoadNativeCached(string path)
    {
        lock (Gate)
        {
            if (NativeHandles.TryGetValue(path, out var handle)) return handle;
            var loaded = NativeLibrary.Load(path);
            NativeHandles[path] = loaded;
            return loaded;
        }
    }

    private static void PrepareNativeSearchPath(string dir)
    {
        try
        {
            NativeMethods.AddDllDirectory(dir);
        }
        catch
        {
        }

        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var hasDir = path.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(p => string.Equals(p.Trim(), dir, StringComparison.OrdinalIgnoreCase));
            if (!hasDir) Environment.SetEnvironmentVariable("PATH", dir + ";" + path);
        }
        catch
        {
        }
    }

    /// <summary>按依赖序预加载原生库（各自 try/catch：缺失/失败留给后续真实使用时报错）。</summary>
    private static void PreloadNatives(string dir, IProgress<string>? progress)
    {
        foreach (var name in new[]
                 {
                     "DirectML.dll", "onnxruntime.dll", "Microsoft.Windows.AI.MachineLearning.dll",
                     "onnxruntime-genai.dll",
                 })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try
            {
                progress?.Report($"正在加载 {name}…");
                LoadNativeCached(path);
            }
            catch
            {
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr AddDllDirectory(string newDirectory);
    }
}
