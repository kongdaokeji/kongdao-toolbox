using System.Buffers.Binary;
using System.Text;

namespace TubaWinUi3.Services.DisplayTools;

internal readonly record struct Chromaticity(double X, double Y);
internal sealed record GamutResult(string Name, double Coverage, double AreaRatio);

/// <summary>EDID declarations only; these values are not instrument measurements.</summary>
internal sealed class DisplayEdid
{
    public string Manufacturer { get; private set; } = "未提供";
    public string Product { get; private set; } = "未提供";
    public string Serial { get; private set; } = "未提供";
    public string Model { get; private set; } = "未提供";
    public string Date { get; private set; } = "未提供";
    public string Version { get; private set; } = "未提供";
    public string Gamma { get; private set; } = "未提供";
    public string Range { get; private set; } = "未提供";
    public string AdaptiveSync { get; private set; } = "未声明（不能据此判断不支持）";
    public string ColorModes { get; private set; } = "未提供";
    public string WideColor { get; private set; } = "未声明";
    public string HdrFormats { get; private set; } = "未声明";
    public bool HdrPq { get; private set; }
    public bool Valid { get; private set; }
    public string Warning { get; private set; } = "未获得有效 EDID";

    public static DisplayEdid Parse(byte[] bytes)
    {
        var d = new DisplayEdid();
        if (bytes.Length < 128 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0,255,255,255,255,255,255,0 }) || !Checksum(bytes.AsSpan(0,128))) return d;
        d.Valid = true;
        d.Warning = "";
        var manufacturer = (bytes[8] << 8) | bytes[9];
        d.Manufacturer = new string(new[] { (char)(64 + ((manufacturer >> 10) & 31)), (char)(64 + ((manufacturer >> 5) & 31)), (char)(64 + (manufacturer & 31)) });
        d.Product = $"0x{BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)):X4}";
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        if (serial != 0) d.Serial = serial.ToString();
        d.Date = bytes[17] == 0 ? "未提供" : bytes[16] == 255 ? $"{1990 + bytes[17]} 年（型号年份）" : bytes[16] == 0 ? $"{1990 + bytes[17]} 年" : $"{1990 + bytes[17]} 年第 {bytes[16]} 周";
        d.Version = $"{bytes[18]}.{bytes[19]}";
        if (bytes[23] != 255) d.Gamma = ((bytes[23] + 100) / 100.0).ToString("F2");
        d.ColorModes = (bytes[20] & 128) != 0 ? "RGB 4:4:4" : "模拟信号";
        if (bytes[19] >= 4 && (bytes[20] & 128) != 0)
        {
            int mode = (bytes[24] >> 3) & 3;
            if ((mode & 1) != 0) d.ColorModes += " / YCbCr 4:4:4";
            if ((mode & 2) != 0) d.ColorModes += " / YCbCr 4:2:2";
        }
        for (int p = 54; p + 18 <= 126; p += 18)
        {
            if (bytes[p] != 0 || bytes[p + 1] != 0 || bytes[p + 2] != 0) continue;
            string text = Encoding.ASCII.GetString(bytes, p + 5, 13).Trim('\0',' ','\r','\n');
            switch (bytes[p + 3])
            {
                case 0xFC: if (text.Length > 0) d.Model = text; break;
                case 0xFF: if (text.Length > 0) d.Serial = text; break;
                case 0xFD:
                    int min = bytes[p+5], max = bytes[p+6];
                    if (bytes[19] >= 4) { if ((bytes[p+4] & 1) != 0) min += 255; if ((bytes[p+4] & 2) != 0) max += 255; }
                    if (min > 0 && max >= min) d.Range = $"{min}–{max} Hz（垂直扫描范围，非 VRR 保证）";
                    break;
            }
        }
        for (int block = 1; block <= bytes[126]; block++)
        {
            int offset = block * 128;
            if (offset + 128 > bytes.Length) { d.Warning = "EDID 扩展不完整，部分能力未知"; break; }
            var b = bytes.AsSpan(offset, 128);
            if (!Checksum(b)) { d.Warning = "部分 EDID 扩展校验失败，已忽略"; continue; }
            if (b[0] != 2) continue;
            if ((b[3] & 32) != 0 && !d.ColorModes.Contains("YCbCr 4:4:4")) d.ColorModes += " / YCbCr 4:4:4";
            if ((b[3] & 16) != 0 && !d.ColorModes.Contains("YCbCr 4:2:2")) d.ColorModes += " / YCbCr 4:2:2";
            int end = b[2] == 0 ? 4 : Math.Clamp((int)b[2], 4, 127);
            for (int p = 4; p < end;)
            {
                int tag = b[p] >> 5, length = b[p] & 31;
                p++;
                if (p + length > end) break;
                var data = b.Slice(p,length);
                if (tag == 7 && length >= 3 && data[0] == 6)
                {
                    var formats = new List<string>();
                    if ((data[1] & 4) != 0) { formats.Add("PQ（HDR10）"); d.HdrPq = true; }
                    if ((data[1] & 8) != 0) formats.Add("HLG");
                    if (formats.Count > 0) d.HdrFormats = string.Join(" / ", formats);
                }
                if (tag == 7 && length >= 3 && data[0] == 5)
                {
                    var modes = new List<string>();
                    if ((data[1] & 128) != 0) modes.Add("BT.2020 RGB");
                    if ((data[1] & 96) != 0) modes.Add("BT.2020 YCC");
                    if ((data[2] & 128) != 0) modes.Add("DCI-P3");
                    if (modes.Count > 0) d.WideColor = string.Join(" / ", modes);
                }
                if (tag == 3 && length >= 7 && data[0] == 0x1A && data[1] == 0 && data[2] == 0)
                    d.AdaptiveSync = $"AMD FreeSync 声明 · {data[5]}–{data[6]} Hz（当前启用状态未知）";
                p += length;
            }
        }
        return d;
    }

    private static bool Checksum(ReadOnlySpan<byte> b) { int sum = 0; foreach (byte n in b) sum += n; return (sum & 255) == 0; }
}

internal static class DisplayGamut
{
    public static string Describe(IReadOnlyList<GamutResult> gamuts)
    {
        var srgb=gamuts.FirstOrDefault(g=>g.Name=="sRGB");
        var p3=gamuts.FirstOrDefault(g=>g.Name=="DCI-P3");
        if(srgb is null || p3 is null || !double.IsFinite(srgb.Coverage) || !double.IsFinite(p3.Coverage))
            return "尚未获得有效色域数据，无法判断这块屏幕的色彩显示范围。";
        if(srgb.Coverage>=98 && p3.Coverage>=95)
            return $"sRGB 覆盖 {srgb.Coverage:F1}%、P3 覆盖 {p3.Coverage:F1}%，两者覆盖较完整，具有较广的色彩显示范围。";
        if(srgb.Coverage>=98)
            return $"sRGB 覆盖 {srgb.Coverage:F1}%，日常网页内容的色彩范围覆盖较完整，但 P3 覆盖 {p3.Coverage:F1}%，部分广色域颜色仍无法完整显示。";
        if(srgb.Coverage>=90)
            return $"sRGB 覆盖 {srgb.Coverage:F1}%、P3 覆盖 {p3.Coverage:F1}%，已覆盖大部分常见网页颜色，仍有部分颜色超出这块屏幕的显示范围。";
        return $"sRGB 覆盖 {srgb.Coverage:F1}%、P3 覆盖 {p3.Coverage:F1}%，色彩显示范围较有限，部分常见网页颜色也可能无法完整呈现。";
    }

    public static string Explain(string name) => name switch
    {
        "sRGB" => "网页、常见图片和普通 SDR 内容广泛采用的色域，适合用来判断日常内容的色彩范围是否覆盖完整。",
        "DCI-P3" => "源自数字电影的广色域标准，比 sRGB 覆盖更广，常用于比较广色域屏幕。这里比较的是 P3 三原色范围；不代表符合影院白点、Gamma，也不等同于完整的 Display P3 标准。",
        "Adobe RGB" => "面向摄影与印刷流程的广色域标准，相比 sRGB 扩展了绿色、青色范围。覆盖率高说明可覆盖更多该标准下的颜色，实际工作仍需要色彩管理与校准。",
        "NTSC (1953)" => "历史上的电视色域参考，常见于屏幕参数宣传。它与 sRGB 的三原色位置不同，因此 72% NTSC 不能直接换算成 100% sRGB；应分别看各自的覆盖率。",
        "Rec.2020" => "面向超高清电视的宽色域标准，范围比 sRGB 和 P3 更大。覆盖率高表示能显示更大范围的颜色，但不能据此判定支持 HDR 或具备 HDR 画质。",
        _ => "用于比较显示器色彩显示范围的参考色域。"
    };
    public static readonly (string Name, Chromaticity[] Points)[] Standards =
    [
        ("sRGB", [new(.64,.33), new(.30,.60), new(.15,.06)]),
        ("DCI-P3", [new(.68,.32), new(.265,.69), new(.15,.06)]),
        ("Adobe RGB", [new(.64,.33), new(.21,.71), new(.15,.06)]),
        ("NTSC (1953)", [new(.67,.33), new(.21,.71), new(.14,.08)]),
        ("Rec.2020", [new(.708,.292), new(.170,.797), new(.131,.046)])
    ];
    public static bool IsValid(Chromaticity[] p) => p.Length == 3 && p.All(v => double.IsFinite(v.X) && double.IsFinite(v.Y) && v.X > 0 && v.Y > 0 && v.X + v.Y <= 1.01) && Area(p) > .0001;
    private static double Cross(Chromaticity a, Chromaticity b, Chromaticity c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    public static double Area(IReadOnlyList<Chromaticity> p) { double a=0; for(int i=0;i<p.Count;i++) { var q=p[(i+1)%p.Count]; a+=p[i].X*q.Y-q.X*p[i].Y; } return Math.Abs(a)/2; }
    public static GamutResult[] Calculate(Chromaticity[] display)
    {
        if (!IsValid(display)) return [];
        return Standards.Select(s =>
        {
            var polygon = display.ToList();
            double direction = Math.Sign(Cross(s.Points[0],s.Points[1],s.Points[2]));
            for(int i=0;i<3 && polygon.Count>0;i++)
            {
                var a=s.Points[i]; var b=s.Points[(i+1)%3]; var input=polygon; polygon=[];
                var prev=input[^1]; double dp=direction*Cross(a,b,prev);
                foreach(var current in input)
                {
                    double dc=direction*Cross(a,b,current);
                    if ((dc>=0)!=(dp>=0)) { double t=dp/(dp-dc); polygon.Add(new(prev.X+t*(current.X-prev.X),prev.Y+t*(current.Y-prev.Y))); }
                    if(dc>=0) polygon.Add(current);
                    prev=current; dp=dc;
                }
            }
            double reference=Area(s.Points);
            return new GamutResult(s.Name,Math.Clamp(Area(polygon)/reference*100,0,100),Area(display)/reference*100);
        }).ToArray();
    }
}
