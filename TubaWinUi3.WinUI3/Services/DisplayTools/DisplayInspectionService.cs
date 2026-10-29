using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Devices.Display;
using Windows.Graphics;

namespace TubaWinUi3.Services.DisplayTools;

internal sealed class DisplayInspection
{
    public string DeviceName { get; init; } = "";
    public string InterfaceId { get; init; } = "";
    public string ColorDeviceKey { get; init; } = "";
    public bool? AdvancedColorActive { get; set; }
    public bool Mirrored { get; set; }
    public string Name { get; set; } = "显示器";
    public bool Primary { get; init; }
    public RectInt32 Bounds { get; init; }
    public string Mode { get; set; } = "未提供";
    public string Scale { get; init; } = "未提供";
    public DisplayEdid Edid { get; set; } = DisplayEdid.Parse([]);
    public Chromaticity[] Primaries { get; set; } = [];
    public Chromaticity? WhitePoint { get; set; }
    public GamutResult[] Gamuts { get; set; } = [];
    public string ProfilePath { get; set; } = "";
    public List<KeyValuePair<string,string>> Info { get; } = [];
    public List<KeyValuePair<string,string>> Hdr { get; } = [];
    public List<string> Warnings { get; } = [];
    public string Summary => $"{(Primary ? "主显示器 · " : "")}{Name} · {Mode} · 缩放 {Scale}";
    public void Add(string key, object? value) => Info.Add(new(key, value?.ToString() ?? "未提供"));
    public string Report() => $"屏幕工具检测报告\n时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n{Summary}\n设备：{DeviceName}\n\n" +
        string.Join("\n",Info.Select(p=>$"{p.Key}：{p.Value}")) + "\n\n色域（CIE 1931 xy）\n" +
        string.Join("\n",Gamuts.Select(g=>$"{g.Name}：覆盖率 {g.Coverage:F1}% / 面积比 {g.AreaRatio:F1}%")) + "\n\nHDR\n" +
        string.Join("\n",Hdr.Select(p=>$"{p.Key}：{p.Value}")) + $"\n\n当前 GDI 颜色配置文件：{(ProfilePath.Length>0?ProfilePath:"未提供")}\n" +
        string.Join("\n",Warnings) + "\n色度、亮度和能力来自 EDID / Windows 驱动声明，不是仪器实测。DisplayHDR 是认证，不能仅凭这些数值确认。";
}

internal static class DisplayInspectionService
{
    public static async Task<List<DisplayInspection>> InspectAsync()
    {
        // Run the entire inventory off the UI thread. Match the OS interface path, never model/name.
        var result = new List<DisplayInspection>();
        MonitorCallback callback = (nint monitor, nint dc, ref Rect rect, nint state) =>
        {
            var mi = new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>() };
            if(!GetMonitorInfoW(monitor,ref mi)) return true;
            var mode=new DevMode { Size=220 };
            bool hasMode=EnumDisplaySettingsW(mi.Device,-1,ref mode);
            string scale="未提供";
            if(GetDpiForMonitor(monitor,0,out uint dpi,out _)==0) scale=$"{dpi/96.0:P0}";
            for(uint i=0;;i++)
            {
                var device=new DisplayDevice { Size=Marshal.SizeOf<DisplayDevice>() };
                if(!EnumDisplayDevicesW(mi.Device,i,ref device,1)) break;
                if((device.Flags & 1)==0) continue;
                result.Add(new DisplayInspection { DeviceName=mi.Device,InterfaceId=device.Id,ColorDeviceKey=device.Key,Name=device.Description,
                    Primary=(mi.Flags&1)!=0, Bounds=new RectInt32(mi.Monitor.Left,mi.Monitor.Top,mi.Monitor.Right-mi.Monitor.Left,mi.Monitor.Bottom-mi.Monitor.Top),Scale=scale,
                    Mode=hasMode?$"{mode.Width} × {mode.Height} · {mode.Frequency} Hz":"未提供" });
            }
            if(!result.Any(d=>d.DeviceName==mi.Device)) result.Add(new DisplayInspection { DeviceName=mi.Device,Primary=(mi.Flags&1)!=0, Bounds=new RectInt32(mi.Monitor.Left,mi.Monitor.Top,mi.Monitor.Right-mi.Monitor.Left,mi.Monitor.Bottom-mi.Monitor.Top),Scale=scale,Mode=hasMode?$"{mode.Width} × {mode.Height} · {mode.Frequency} Hz":"未提供" });
            return true;
        };
        if(!EnumDisplayMonitors(0,0,callback,0)) throw new InvalidOperationException("Windows 无法枚举活动显示器。");
        foreach(var d in result)
        {
            d.Mirrored = result.Count(other => other.DeviceName == d.DeviceName) > 1;
            try
            {
                var m=await DisplayMonitor.FromInterfaceIdAsync(d.InterfaceId);
                if(m is null) throw new InvalidOperationException("系统未返回显示器描述");
                if(!string.IsNullOrWhiteSpace(m.DisplayName)) d.Name=m.DisplayName;
                d.Edid=DisplayEdid.Parse(m.GetDescriptor(DisplayMonitorDescriptorKind.Edid));
                d.Add("制造商 / 产品 ID",$"{d.Edid.Manufacturer} / {d.Edid.Product}");
                d.Add("型号",d.Edid.Model); d.Add("序列号",d.Edid.Serial); d.Add("制造日期",d.Edid.Date); d.Add("EDID 版本",d.Edid.Version);
                d.Add("原生分辨率",$"{m.NativeResolutionInRawPixels.Width} × {m.NativeResolutionInRawPixels.Height}");
                d.Add("连接方式",m.ConnectionKind==DisplayMonitorConnectionKind.Internal ? "内置面板" : $"{ConnectorName(m.PhysicalConnector.ToString())} · 外接显示器");
                if(m.PhysicalSizeInInches is { } size) d.Add("屏幕尺寸",$"{Math.Sqrt(size.Width*size.Width+size.Height*size.Height):F1} 英寸（{size.Width*2.54:F1} × {size.Height*2.54:F1} cm）");
                else d.Add("屏幕尺寸","未提供");
                d.Add("像素密度",m.RawDpiX>0?$"{m.RawDpiX:F1} × {m.RawDpiY:F1} PPI":"未提供");
                var points=new[] {m.RedPrimary,m.GreenPrimary,m.BluePrimary};
                d.Primaries=points.Select(p=>new Chromaticity(p.X,p.Y)).ToArray();
                d.WhitePoint=new(m.WhitePoint.X,m.WhitePoint.Y);
                for(int i=0;i<3;i++) d.Add(new[]{"Red","Green","Blue"}[i],$"x {points[i].X:F4} · y {points[i].Y:F4}");
                d.Add("White",$"x {m.WhitePoint.X:F4} · y {m.WhitePoint.Y:F4}");
                d.Gamuts=DisplayGamut.Calculate(d.Primaries);
                if(d.Gamuts.Length==0) d.Warnings.Add("驱动未提供有效三原色色度，无法计算色域。");
                d.Add("颜色模式（声明）",d.Edid.ColorModes); d.Add("Gamma（声明）",d.Edid.Gamma); d.Add("刷新范围",d.Edid.Range);
                d.Add("可变刷新率",d.Edid.AdaptiveSync); d.Add("广色域信号",d.Edid.WideColor);
                if(d.Edid.Warning.Length>0) d.Warnings.Add(d.Edid.Warning);
                ReadAdvancedColor(d,m);
                d.Hdr.Add(new("支持格式（EDID）",d.Edid.HdrFormats));
                d.Hdr.Add(new("峰值亮度",Nits(m.MaxLuminanceInNits)));
                d.Hdr.Add(new("全屏平均亮度",Nits(m.MaxAverageFullFrameLuminanceInNits)));
                d.Hdr.Add(new("最低亮度",Nits(m.MinLuminanceInNits)));
                d.Hdr.Add(new("DisplayHDR 参考档位",m.MaxLuminanceInNits>=400?$"峰值达到 {ReferenceTier(m.MaxLuminanceInNits)} nit 门槛；其他条件及认证未知":"无法估计；认证以厂商公布为准"));
            }
            catch(Exception ex) { d.Warnings.Add($"读取显示器描述失败：{ex.Message}"); }
            try { d.ProfilePath=GetProfile(d.DeviceName); } catch(Exception ex) { d.Warnings.Add($"颜色配置文件：{ex.Message}"); }
        }
        return result.OrderByDescending(d=>d.Primary).ToList();
    }

    internal static async Task RefreshAdvancedColorAsync(DisplayInspection display)
    {
        display.AdvancedColorActive = null;
        var monitor = await DisplayMonitor.FromInterfaceIdAsync(display.InterfaceId);
        if (monitor is null) throw new InvalidOperationException("显示器已断开，请刷新后重试。");
        var probe = new DisplayInspection();
        ReadAdvancedColor(probe, monitor);
        display.AdvancedColorActive = probe.AdvancedColorActive;
    }

    private static string Nits(float n) => float.IsFinite(n) && n>0?$"{n:0.####} cd/m²（驱动声明）":"未提供";
    private static string ConnectorName(string name) => name switch { "Unknown"=>"接口类型未提供", "Hdmi"=>"HDMI", "Dvi"=>"DVI", "Vga"=>"VGA", _=>name };
    private static int ReferenceTier(float n) => new[]{400,500,600,1000,1400,2000}.Last(x=>n>=x);
    private static void ReadAdvancedColor(DisplayInspection d,DisplayMonitor m)
    {
        var h=new Header { Type=15,Size=36,Low=m.DisplayAdapterId.LowPart,High=m.DisplayAdapterId.HighPart,Id=m.DisplayAdapterTargetId };
        var v2=new ColorInfo2 { Header=h };
        if(DisplayConfigGetDeviceInfo(ref v2)==0)
        {
            d.AdvancedColorActive=v2.ActiveMode!=0;
            d.Hdr.Add(new("HDR 状态",(v2.Flags&16)!=0? v2.ActiveMode==2?"支持 · 当前已开启":"支持 · 当前未开启":"系统未报告 HDR 支持"));
            d.Hdr.Add(new("输出信号",$"{EncodingName(v2.Encoding)} · {v2.Bits} bit/通道"));
            return;
        }
        h.Type=9; h.Size=32;
        var v1=new ColorInfo { Header=h };
        if(DisplayConfigGetDeviceInfo(ref v1)==0)
        {
            d.AdvancedColorActive=(v1.Flags&2)!=0;
            d.Hdr.Add(new("高级颜色状态",$"{((v1.Flags&1)!=0?"支持":"未报告支持")} · {((v1.Flags&2)!=0?"已开启":"未开启")}（旧系统不能区分 HDR / WCG）"));
            d.Hdr.Add(new("输出信号",$"{EncodingName(v1.Encoding)} · {v1.Bits} bit/通道"));
        }
        else d.Hdr.Add(new("HDR 状态","系统查询不可用"));
    }
    private static string EncodingName(uint n) => n switch {0=>"RGB",1=>"YCbCr 4:4:4",2=>"YCbCr 4:2:2",3=>"YCbCr 4:2:0",_=>"未知编码"};
    internal static string GetProfile(string device)
    {
        nint dc=CreateDCW("DISPLAY",device,null,0);
        if(dc==0) return "";
        try { uint size=0; GetICMProfileW(dc,ref size,null); if(size==0 || size>32768) return ""; var b=new StringBuilder((int)size); return GetICMProfileW(dc,ref size,b)?b.ToString():""; }
        finally { DeleteDC(dc); }
    }

    public static string ProfileDescription(string path)
    {
        if(string.IsNullOrEmpty(path)) return "未获得配置文件。Windows 可能使用系统默认色彩转换。";
        try
        {
            using var stream=File.OpenRead(path); var b=new byte[128]; stream.ReadExactly(b);
            if(!b.AsSpan(36,4).SequenceEqual("acsp"u8)) return "未识别的 ICC 文件头";
            return $"ICC {b[8]}.{b[9]>>4}.{b[9]&15} · {Encoding.ASCII.GetString(b,16,4).Trim()} → {Encoding.ASCII.GetString(b,20,4).Trim()} · {stream.Length:N0} 字节\n当前 GDI 默认配置；HDR / 高级颜色模式可能使用独立配置。ICC 描述色彩转换，不代表显示器实测色域。";
        }
        catch(Exception ex) { return $"无法读取 ICC 详情：{ex.Message}"; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key; }
    // DEVMODEW offsets are fixed on x86/x64/ARM64 (no pointer members).
    [StructLayout(LayoutKind.Explicit,Size=220)] private struct DevMode { [FieldOffset(68)] public ushort Size; [FieldOffset(172)] public uint Width; [FieldOffset(176)] public uint Height; [FieldOffset(184)] public uint Frequency; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type,Size,Low; public int High; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct ColorInfo { public Header Header; public uint Flags,Encoding,Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct ColorInfo2 { public Header Header; public uint Flags,Encoding,Bits,ActiveMode; }
    private delegate bool MonitorCallback(nint monitor,nint dc,ref Rect rect,nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool EnumDisplayDevicesW(string device,uint index,ref DisplayDevice info,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device,int index,ref DevMode mode);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ColorInfo info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref ColorInfo2 info);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern nint CreateDCW(string driver,string device,string? output,nint init);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern bool GetICMProfileW(nint dc,ref uint size,StringBuilder? path);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
