using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>功能开关状态（对照 ViVe FeatureManager 的 EnabledState：0=默认 1=已禁用 2=已启用）。</summary>
public enum FeatureState
{
    Default = 0,
    Disabled = 1,
    Enabled = 2
}

/// <summary>一条 Windows 功能配置（来自功能存储查询或字典搜索命中）。</summary>
public sealed record FeatureFlagEntry(
    uint FeatureId,
    string? Name,
    int Priority,
    FeatureState State,
    bool IsExperiment,
    bool HasConfig)
{
    /// <summary>用户可读的优先级名（对照 ViveTool Priority 枚举）。</summary>
    public string PriorityText => Priority switch
    {
        0 => "ImageDefault",
        1 => "EKB",
        3 => "ImageDefaultEditionOverride",
        4 => "Service",
        8 => "User",
        9 => "Security",
        15 => "ImageOverride",
        _ => $"Priority {Priority}"
    };
}

/// <summary>
/// Windows 隐藏功能开关服务：移植 ViVe（github.com/thebookisclosed/ViVe，GPL-3.0）核心实现，
/// 通过 ntdll 的功能配置 API（RtlQueryAllFeatureConfigurations / RtlSetFeatureConfigurations）与
/// FeatureManagement 注册表直接查询 / 启用 / 禁用 / 重置 Windows 的 A/B 实验功能开关。
/// 与 ViveTool 命令行（Tools\其他工具\ViveTool）语义一致（User 优先级、Runtime+Boot 双存储），
/// 但为进程内 API 调用，查询毫秒级完成，不依赖外部程序。
/// 参考移植：nexbox feature_flags.rs（同为 ViVe 移植）。
/// </summary>
public static unsafe class WindowsFeatureService
{
    // ============ 常量（对照 ViVe NativeEnums.cs） ============

    private const uint CfgTypeBoot = 0;
    private const uint CfgTypeRuntime = 1;

    private const uint OperationFeatureState = 1;
    private const uint OperationVariantState = 2;
    private const uint OperationResetState = 4;

    private const int BsdItemFeatureConfigurationState = 17;
    private const int BsdStateUninitialized = 0;
    private const int BsdStateBootPending = 1;

    private const uint StatusUnsuccessful = 0xC000_0001;
    private const uint StatusObjectNameNotFound = 0xC000_0034;
    private const uint StatusAccessDenied = 0xC000_0022;
    /// <summary>缓冲不足（调用方缓冲装不下全部配置，按 count 返回值扩容重试）</summary>
    private const uint StatusBufferOverflow = 0x8000_0005;
    private const uint StatusBufferTooSmall = 0xC000_0023;

    /// <summary>Velocity 功能配置 API 需要的最低系统版本（Win10 18963）。</summary>
    public const uint MinSupportedBuild = 18963;

    /// <summary>默认写入优先级：User（ViVeTool /enable 同款默认值）。</summary>
    public const uint PriorityUser = 8;

    /// <summary>内核不允许写入的不可变优先级（对照 FeatureManager.ImmutablePriorities）。</summary>
    public static readonly uint[] ImmutablePriorities = [0, 1, 3, 9, 15];

    // ============ ntdll FFI（对照 ViVe NativeMethods.Ntdll.cs） ============
    //
    // 功能配置（Velocity）API 是 Win10 1903（build 18362）才引入 ntdll 的。若用 DllImport 静态导入，
    // 程序在旧系统上加载时会因找不到导出点而无法启动（Entry Point Not Found），因此全部改为运行时
    // GetProcAddress 动态解析：系统不支持时仅让「隐藏功能」模块降级为不可用。

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PQueryAllFeatureConfigurations(
        uint featureConfigurationType, ulong* changeStamp,
        RtlFeatureConfiguration* featureConfigurations, ulong* featureConfigurationCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate ulong PQueryFeatureConfigurationChangeStamp();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PSetFeatureConfigurations(
        ulong* previousChangeStamp, uint featureConfigurationType,
        RtlFeatureConfigurationUpdate* featureConfigurations, int featureConfigurationCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PSetSystemBootStatus(int bsdItemType, int* data, int dataLength, int* returnLength);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PGetSystemBootStatus(int bsdItemType, int* data, int dataLength, int* returnLength);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PCreateBootStatusDataFile(char* bootStatusPath);

    /// <summary>12 字节：FeatureId + CompactState 位域 + VariantPayload（布局必须与内核一致）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RtlFeatureConfiguration
    {
        public uint FeatureId;
        public uint CompactState;
        public uint VariantPayload;

        public int Priority => (int)(CompactState & 0xF);
        public int EnabledState => (int)((CompactState >> 4) & 0x3);
        public bool IsWexp => ((CompactState >> 6) & 1) == 1;
        public int Variant => (int)((CompactState >> 8) & 0x3F);
        public int VariantPayloadKind => (int)((CompactState >> 14) & 0x3);
    }

    /// <summary>32 字节（8 × uint），字段顺序对照 C# RTL_FEATURE_CONFIGURATION_UPDATE 声明。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RtlFeatureConfigurationUpdate
    {
        public uint FeatureId;
        public uint Priority;
        public uint EnabledState;
        public uint EnabledStateOptions;
        public uint Variant;
        public uint VariantPayloadKind;
        public uint VariantPayload;
        public uint Operation;

        public static RtlFeatureConfigurationUpdate NewReset(uint featureId, uint priority) => new()
        {
            FeatureId = featureId,
            Priority = priority,
            Operation = OperationResetState
        };
    }

    /// <summary>ntdll 功能配置 API 函数指针集合（全部经 GetProcAddress 动态解析，进程内只加载一次）。</summary>
    private sealed class NtdllApi
    {
        public required nint Lib;
        public required PQueryAllFeatureConfigurations QueryAll;
        public required PQueryFeatureConfigurationChangeStamp ChangeStamp;
        public required PSetFeatureConfigurations SetConfigurations;
        public required PSetSystemBootStatus SetBootStatus;
        public required PGetSystemBootStatus GetBootStatus;
        public required PCreateBootStatusDataFile CreateBootStatusFile;

        /// <summary>核心功能配置 API 是否齐备（决定「隐藏功能」模块是否可用）。</summary>
        public bool FeatureConfigSupported => true;
    }

    private static readonly Lazy<NtdllApi?> Api = new(ResolveApi);

    /// <summary>功能配置 API 在解析 ntdll 符号时是否可用（还需要系统版本 ≥ 18963）。</summary>
    public static bool ApiResolved => Api.Value is not null;

    private static NtdllApi? ResolveApi()
    {
        nint lib = LoadLibraryW("ntdll.dll");
        if (lib == 0)
            return null;

        var queryAll = GetProcAddress<PQueryAllFeatureConfigurations>(lib, "RtlQueryAllFeatureConfigurations");
        var changeStamp = GetProcAddress<PQueryFeatureConfigurationChangeStamp>(lib, "RtlQueryFeatureConfigurationChangeStamp");
        var setConfigs = GetProcAddress<PSetFeatureConfigurations>(lib, "RtlSetFeatureConfigurations");
        var setBoot = GetProcAddress<PSetSystemBootStatus>(lib, "RtlSetSystemBootStatus");
        var getBoot = GetProcAddress<PGetSystemBootStatus>(lib, "RtlGetSystemBootStatus");
        var createBoot = GetProcAddress<PCreateBootStatusDataFile>(lib, "RtlCreateBootStatusDataFile");
        if (queryAll is null || changeStamp is null || setConfigs is null)
            return null; // 核心 API 缺失（旧系统），放弃

        return new NtdllApi
        {
            Lib = lib,
            QueryAll = queryAll,
            ChangeStamp = changeStamp,
            SetConfigurations = setConfigs,
            SetBootStatus = setBoot,
            GetBootStatus = getBoot,
            CreateBootStatusFile = createBoot
        };
    }

    /// <summary>模块是否整体可用（系统版本达标 + 核心 API 齐备）。</summary>
    public static bool IsSupported()
    {
        if (Api.Value is null)
            return false;
        return GetOsBuild() >= MinSupportedBuild;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string lpFileName);

    private static T? GetProcAddress<T>(nint lib, string name) where T : Delegate
    {
        var addr = GetProcAddress(lib, name);
        return addr == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(addr);
    }

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    // ============ 核心操作（对照 ViVe FeatureManager.cs / nexbox feature_flags.rs） ============

    private static string ApiUnavailable() => "当前系统不支持功能配置 API（需要 Windows 10 1903 或更高版本）";

    /// <summary>
    /// 查询整个功能存储。关键点（实测 Windows 11 24H2 / build 26100）：ntdll 会把配置数量按
    /// 64 位写出，必须用 ulong 承接 count，否则越界写坏相邻栈变量。一次性大缓冲直取，
    /// 装不下时按内核返回的数量扩容重试。
    /// </summary>
    private static unsafe (RtlFeatureConfiguration[] Configs, ulong ChangeStamp) QueryAllConfigurations(uint cfgType)
    {
        var api = Api.Value ?? throw new InvalidOperationException(ApiUnavailable());
        var capacity = 8192;
        while (true)
        {
            var configs = new RtlFeatureConfiguration[capacity];
            var count = (ulong)capacity;
            var changeStamp = 0ul;
            int hr;
            fixed (RtlFeatureConfiguration* p = configs)
            {
                hr = api.QueryAll(cfgType, &changeStamp, p, &count);
            }
            if (hr != 0)
            {
                var status = unchecked((uint)hr);
                if (status is StatusBufferOverflow or StatusBufferTooSmall)
                {
                    var need = (long)Math.Max(count, (ulong)capacity * 2);
                    if (need > capacity)
                    {
                        capacity = (int)need;
                        continue;
                    }
                }
                throw new InvalidOperationException(NtStatusToMessage(hr));
            }
            var written = (int)count;
            if (written < capacity)
            {
                Array.Resize(ref configs, written);
                return (configs, changeStamp);
            }
            // 恰好装满可能被截断，扩容一倍重试
            capacity *= 2;
        }
    }

    /// <summary>校验优先级可写（不可变优先级抛错，与 FeatureManager.SetFeatureConfigurations 一致）。</summary>
    private static void ValidatePriority(uint priority)
    {
        if (ImmutablePriorities.Contains(priority))
            throw new InvalidOperationException($"优先级 {priority} 是系统不可变优先级，不允许写入");
    }

    /// <summary>写 Runtime 存储。previous_change_stamp 传 0 跳过并发检查（与 ViVeTool 默认行为一致）。</summary>
    private static unsafe void SetRuntimeConfigurations(RtlFeatureConfigurationUpdate[] updates)
    {
        var api = Api.Value ?? throw new InvalidOperationException(ApiUnavailable());
        var prevStamp = 0ul;
        int hr;
        fixed (RtlFeatureConfigurationUpdate* p = updates)
        {
            hr = api.SetConfigurations(&prevStamp, CfgTypeRuntime, p, updates.Length);
        }
        if (hr != 0)
            throw new InvalidOperationException(NtStatusToMessage(hr));
    }

    /// <summary>
    /// 写 Boot 存储。ntdll 的设置 API 只作用于 Runtime，持久化需按内核行为直接写
    /// FeatureManagement\Overrides 注册表（对照 FeatureManager.SetFeatureConfigurationsInRegistry，
    /// 不含 UserPolicy 分支——本模块固定 User 优先级）。
    /// </summary>
    private static void SetBootConfigurationsInRegistry(RtlFeatureConfigurationUpdate[] updates)
    {
        using var overrides = Registry.LocalMachine.CreateSubKey(
            @"SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides");
        if (overrides is null)
            throw new InvalidOperationException("无法打开 FeatureManagement 注册表分支（需要管理员权限）");

        foreach (var u in updates)
        {
            var obf = ObfuscateFeatureId(u.FeatureId);
            var subKeyPath = $@"{u.Priority}\{obf}";
            if ((u.Operation & OperationResetState) != 0)
            {
                // 删除该功能的覆盖键树，不存在则忽略
                overrides.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
                continue;
            }
            using var key = overrides.CreateSubKey(subKeyPath);
            if (key is null)
                throw new InvalidOperationException($"创建功能覆盖键失败（{subKeyPath}）");
            if ((u.Operation & OperationFeatureState) != 0)
            {
                key.SetValue("EnabledState", u.EnabledState, RegistryValueKind.DWord);
                key.SetValue("EnabledStateOptions", u.EnabledStateOptions, RegistryValueKind.DWord);
            }
            if ((u.Operation & OperationVariantState) != 0)
            {
                key.SetValue("Variant", u.Variant, RegistryValueKind.DWord);
                key.SetValue("VariantPayload", u.VariantPayload, RegistryValueKind.DWord);
                key.SetValue("VariantPayloadKind", u.VariantPayloadKind, RegistryValueKind.DWord);
            }
        }
    }

    /// <summary>
    /// Boot 存储写入后更新 LKG 状态为 BootPending（对照 ViVeTool UpdateLKGStatus）。
    /// 尽力而为：BSD 文件缺失时先创建；失败只记录日志，不让主操作报错。
    /// </summary>
    private static unsafe void UpdateLkgStatus()
    {
        var api = Api.Value;
        if (api?.SetBootStatus is null || api.GetBootStatus is null || api.CreateBootStatusFile is null)
            return;
        var current = BsdStateUninitialized;
        var result = api.GetBootStatus(BsdItemFeatureConfigurationState, &current, 4, null);
        if (result != 0)
        {
            if (unchecked((uint)result) == StatusObjectNameNotFound)
            {
                result = api.CreateBootStatusFile(null);
                if (result != 0)
                {
                    DebugWrite($"初始化 Boot 状态数据文件失败: 0x{unchecked((uint)result):X8}");
                    return;
                }
                current = BsdStateUninitialized;
            }
            else
            {
                DebugWrite($"查询 LKG 状态失败: 0x{unchecked((uint)result):X8}");
                return;
            }
        }
        if (current != BsdStateBootPending)
        {
            var newState = BsdStateBootPending;
            result = api.SetBootStatus(BsdItemFeatureConfigurationState, &newState, 4, null);
            if (result != 0)
                DebugWrite($"设置 LKG 状态失败: 0x{unchecked((uint)result):X8}");
        }
    }

    private static string NtStatusToMessage(int status) => unchecked((uint)status) switch
    {
        StatusAccessDenied => "拒绝访问：需要管理员权限",
        StatusUnsuccessful => "操作失败：功能存储已发生变化，请重试",
        StatusObjectNameNotFound => "操作失败：系统数据对象不存在",
        _ => $"操作失败 (0x{unchecked((uint)status):X8})"
    };

    private static void DebugWrite(string message) => System.Diagnostics.Debug.WriteLine($"[WindowsFeature] {message}");

    // ============ 功能 ID 混淆（对照 ViVe ObfuscationHelpers.cs） ============
    // 注册表键名使用混淆后的功能 ID。
    // 注意：C# RotateRight32(value, -1) 的移位数按 & 31 截断，等价于循环右移 31（即左移 1）。

    internal static uint ObfuscateFeatureId(uint id)
    {
        var v = BinaryPrimitives.ReverseEndianness(id ^ 0x7416_1A4E);
        v ^= 0x8FB2_3D4F;
        v = (v << 1) | (v >> 31); // rotate_left(1)
        return v ^ 0x833E_A8FF;
    }

    /// <summary>反混淆（单元测试用）。</summary>
    internal static uint DeobfuscateFeatureId(uint id)
    {
        var v = id ^ 0x833E_A8FF;
        v = (v >> 1) | (v << 31); // rotate_right(1)
        v ^= 0x8FB2_3D4F;
        return BinaryPrimitives.ReverseEndianness(v) ^ 0x7416_1A4E;
    }

    // ============ 系统信息 ============

    /// <summary>读取当前系统版本号（注册表 CurrentBuildNumber，如 26100）。</summary>
    public static int GetOsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var value = key?.GetValue("CurrentBuildNumber") as string;
            return int.TryParse(value?.Trim(), out var build) ? build : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ============ 功能字典 ============

    /// <summary>
    /// 加载功能字典（行格式「名字,ID」；重复 ID 取首个，与 ViVe 加载行为一致）。
    /// 优先读随包 Assets（精简版也可用），回退读 Tools\其他工具\ViveTool 下的官方字典。
    /// </summary>
    public static Dictionary<uint, string> LoadDictionary()
    {
        var path = FindDictionaryPath();
        return path is null ? new Dictionary<uint, string>() : LoadDictionaryFromFile(path);
    }

    /// <summary>从指定路径解析字典文件（供测试与自定义字典使用）。</summary>
    internal static Dictionary<uint, string> LoadDictionaryFromFile(string path)
    {
        var map = new Dictionary<uint, string>();
        foreach (var raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var comma = line.IndexOf(',');
            if (comma <= 0 || comma == line.Length - 1)
                continue;
            if (uint.TryParse(line[(comma + 1)..].Trim(), out var id))
                map.TryAdd(id, line[..comma].Trim());
        }
        return map;
    }

    internal static string? FindDictionaryPath()
    {
        // ① 随包 Assets（打包后位于 exe 旁 Assets\ 下）
        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "FeatureDictionary.pfs");
        if (File.Exists(bundled))
            return bundled;
        // ② Tools 内官方字典（ViveTool 同目录）
        var tools = Path.Combine(ToolCatalog.ToolsRoot, @"其他工具\ViveTool\FeatureDictionary.pfs");
        return File.Exists(tools) ? tools : null;
    }

    // ============ 对外操作（对照 ViveTool /query /enable /disable /reset） ============

    /// <summary>查询全部功能配置（Runtime 存储，毫秒级）。</summary>
    public static List<FeatureFlagEntry> QueryAll()
    {
        var (configs, _) = QueryAllConfigurations(CfgTypeRuntime);
        var result = new List<FeatureFlagEntry>(configs.Length);
        foreach (var c in configs)
        {
            result.Add(new FeatureFlagEntry(
                c.FeatureId,
                Name: null,
                Priority: c.Priority,
                State: (FeatureState)c.EnabledState,
                IsExperiment: c.IsWexp,
                HasConfig: true));
        }
        return result;
    }

    /// <summary>启用/禁用功能（ViVeTool /enable、/disable 同款：User 优先级 + Runtime/Boot 双存储）。</summary>
    public static string SetState(uint featureId, bool enabled)
    {
        ValidatePriority(PriorityUser);
        var update = new RtlFeatureConfigurationUpdate
        {
            FeatureId = featureId,
            Priority = PriorityUser,
            EnabledState = enabled ? 2u : 1u,
            EnabledStateOptions = 0,
            Operation = OperationFeatureState
        };
        SetRuntimeConfigurations([update]);
        SetBootConfigurationsInRegistry([update]);
        UpdateLkgStatus();
        return enabled ? "已启用（已写入 Runtime 与 Boot 存储，重启后保持生效）"
                       : "已禁用（已写入 Runtime 与 Boot 存储，重启后保持生效）";
    }

    /// <summary>重置功能的自定义配置（Runtime + Boot，priority 默认 User）。</summary>
    public static string Reset(uint featureId, uint priority = PriorityUser)
    {
        ValidatePriority(priority);
        var reset = RtlFeatureConfigurationUpdate.NewReset(featureId, priority);
        SetRuntimeConfigurations([reset]);
        SetBootConfigurationsInRegistry([reset]);
        UpdateLkgStatus();
        return $"功能 {featureId} 的自定义配置已重置（恢复系统默认）";
    }
}