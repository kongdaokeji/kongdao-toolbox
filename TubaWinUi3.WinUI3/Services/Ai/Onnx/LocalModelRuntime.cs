using System.Runtime.CompilerServices;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services.Ai.Onnx;

/// <summary>
/// 进程级本地模型运行时门面：AI 助手选中「本地模型」提供商时经此加载/推理；换模型自动卸载重载。
///
/// <para><b>JIT 安全约束</b>：本类型（字段/属性/方法签名）不得出现任何 AI 类型——AI 运行库
/// 可能未安装（DLL 在数据目录，由 AiRuntimeLoader 动态提供），而退出路径（MainWindow_Closed）
/// 会调用 <see cref="TryUnload"/>，类型加载失败必须被调用方的 try/catch 吞掉、不能炸掉退出流程。
/// 真实状态与 AI 类型全部藏在嵌套 <c>Core</c> 里，外层方法只做一层转调。</para>
/// </summary>
public static class LocalModelRuntime
{
    /// <summary>当前已加载的模型 Id（null = 未加载）。</summary>
    public static string? LoadedModelId => Core.LoadedModelId;

    /// <summary>本次加载实际使用的设备说明（如 "CPU"）。</summary>
    public static string RuntimeNote => Core.RuntimeNote;

    /// <summary>
    /// 确保指定模型已加载（同步，耗时较长——调用方应在后台线程执行）。
    /// 平台不支持或模型不可用时抛 <see cref="InvalidOperationException"/>（含友好文案）。
    /// </summary>
    public static void EnsureLoaded(string modelId) => Core.EnsureLoaded(modelId);

    /// <summary>流式生成（要求已 <see cref="EnsureLoaded"/>）。</summary>
    public static IAsyncEnumerable<string> GenerateAsync(
        string prompt, double temperature, double topP, int maxNewTokens, CancellationToken ct)
        => Core.GenerateAsync(prompt, temperature, topP, maxNewTokens, ct);

    /// <summary>取指定对话模型的提示词格式（无则 Phi 默认）。</summary>
    public static AiPromptFormat GetPromptFormat(string modelId) => Core.GetPromptFormat(modelId);

    /// <summary>卸载并释放（数据目录切换 / 应用退出等路径；有界等待，最长阻塞 <paramref name="timeout"/>）。</summary>
    public static bool TryUnload(TimeSpan timeout) => Core.TryUnload(timeout);

    /// <summary>
    /// 尽力卸载（页面关闭 / 切走本地提供商时调用）：先尝试在 <paramref name="firstWait"/> 内直接卸载；
    /// 若模型正在加载（锁被占用）则转后台等待——期间用户若重新打开并加载了新模型（代际变化），
    /// 后台卸载会跳过，避免误卸在用模型。
    /// </summary>
    public static void UnloadWhenIdle(TimeSpan firstWait) => Core.UnloadWhenIdle(firstWait);

    /// <summary>卸载并释放（无等待上限；仅在没有并发加载的确定性场景使用）。</summary>
    public static void Unload() => Core.Unload();

    /// <summary>真实实现（含 AI 类型）：仅在运行库就绪后被触达。</summary>
    private static class Core
    {
        private static readonly object Gate = new();
        private static LlmChatRunner? _runner;
        private static string? _loadedModelId;
        private static int _generation;

        public static string? LoadedModelId
        {
            get { lock (Gate) return _loadedModelId; }
        }

        public static string RuntimeNote
        {
            get { lock (Gate) return _runner?.RuntimeNote ?? ""; }
        }

        public static void EnsureLoaded(string modelId)
        {
            lock (Gate)
            {
                if (_runner is not null && _loadedModelId == modelId && _runner.IsLoaded)
                    return;

                if (AiRuntimeService.UnsupportedReason is { } reason)
                    throw new InvalidOperationException(reason);

                var entry = AiModelLibrary.GetAllEntries().FirstOrDefault(e => e.Id == modelId)
                    ?? throw new InvalidOperationException($"本地模型 '{modelId}' 不存在，请在「本地 AI 试炼场」重新选择。");

                var status = AiModelLibrary.GetStatus(entry);
                if (!status.AllReady)
                    throw new InvalidOperationException($"本地模型尚未就绪（{status.Describe()}），请先在「本地 AI 试炼场」完成下载或导入。");

                var dir = AiModelLibrary.GetEntryDataDir(entry);
                var (provider, deviceType, note) = AiRuntimeService.ResolveLlmTarget(null, null);

                var runner = new LlmChatRunner();
                try
                {
                    runner.Load(dir, provider, deviceType, note);
                }
                catch
                {
                    runner.Dispose();
                    throw;
                }

                _runner?.Dispose();
                _runner = runner;
                _loadedModelId = modelId;
                _generation++;
            }
        }

        public static IAsyncEnumerable<string> GenerateAsync(
            string prompt, double temperature, double topP, int maxNewTokens, CancellationToken ct)
        {
            LlmChatRunner runner;
            lock (Gate)
            {
                runner = _runner ?? throw new InvalidOperationException("本地模型尚未加载。");
            }
            return runner.GenerateAsync(prompt, temperature, topP, maxNewTokens, ct);
        }

        public static AiPromptFormat GetPromptFormat(string modelId)
        {
            var entry = AiModelLibrary.GetAllEntries().FirstOrDefault(e => e.Id == modelId);
            return entry?.Custom?.PromptFormat ?? entry?.Preset?.PromptFormat ?? AiPromptFormat.Phi;
        }

        public static bool TryUnload(TimeSpan timeout)
        {
            if (!Monitor.TryEnter(Gate, timeout)) return false;
            try
            {
                DisposeLocked(trim: false);
                return true;
            }
            finally
            {
                Monitor.Exit(Gate);
            }
        }

        public static void UnloadWhenIdle(TimeSpan firstWait)
        {
            var generation = Volatile.Read(ref _generation);
            if (Monitor.TryEnter(Gate, firstWait))
            {
                try
                {
                    if (Volatile.Read(ref _generation) == generation) DisposeLocked(trim: true);
                }
                finally
                {
                    Monitor.Exit(Gate);
                }
                return;
            }

            // 锁被加载占用：转后台等待；若期间发生了新的加载（代际变化）就跳过，别误卸在用模型
            _ = Task.Run(() =>
            {
                lock (Gate)
                {
                    if (Volatile.Read(ref _generation) == generation) DisposeLocked(trim: true);
                }
            });
        }

        public static void Unload()
        {
            lock (Gate)
            {
                DisposeLocked(trim: true);
            }
        }

        /// <summary>调用方必须持 Gate。dispose 后视 <paramref name="trim"/> 决定是否后台回收工作集。</summary>
        private static void DisposeLocked(bool trim)
        {
            if (_runner is null) return;
            _runner.Dispose();
            _runner = null;
            _loadedModelId = null;
            _generation++;

            if (trim) _ = Task.Run(AiModelMemoryTrimmer.Trim);
        }
    }
}
