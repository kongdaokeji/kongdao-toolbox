using System.Buffers.Binary;
using System.Text;

namespace TubaWinUi3.Services.DisplayTools;

/// <summary>ICC v2 matrix/TRC display characterization from EDID; never a measured calibration.</summary>
internal static class DisplayIccProfile
{
    public static byte[] CreateNative(Chromaticity[] primaries, Chromaticity white, double gamma)
    {
        if (!DisplayGamut.IsValid(primaries) || white.X <= 0 || white.Y <= 0 || white.X + white.Y > 1 ||
            !double.IsFinite(white.X + white.Y) || !double.IsFinite(gamma) || gamma is < 1 or > 3.5)
            throw new ArgumentException("显示器色度或 Gamma 数据无效，无法生成原生配置。");
        double[] Xyz(Chromaticity p) => [p.X / p.Y, 1, (1 - p.X - p.Y) / p.Y];
        var columns = primaries.Select(Xyz).ToArray();
        double[,] m = new double[3, 3];
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) m[r, c] = columns[c][r];
        var scales = Multiply(Invert(m), Xyz(white));
        if (scales.Any(v => !double.IsFinite(v) || v <= 0)) throw new ArgumentException("显示器白点不在有效原色色域内。");
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) m[r, c] *= scales[c];
        double[,] bradford = { { .8951, .2664, -.1614 }, { -.7502, 1.7135, .0367 }, { .0389, -.0685, 1.0296 } };
        double[] d50 = [.9642, 1, .8249];
        var sourceCone = Multiply(bradford, Xyz(white)); var targetCone = Multiply(bradford, d50);
        var adaptation = new double[3, 3]; var inverse = Invert(bradford);
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            for (int k = 0; k < 3; k++) adaptation[r, c] += inverse[r, k] * targetCone[k] / sourceCone[k] * bradford[k, c];
        var adapted = new double[3][];
        for (int c = 0; c < 3; c++) adapted[c] = Multiply(adaptation, [m[0, c], m[1, c], m[2, c]]);

        byte[] XyzTag(double[] xyz)
        {
            var b = new byte[20]; Sig(b, 0, "XYZ ");
            for (int i = 0; i < 3; i++) Fixed(b, 8 + i * 4, xyz[i]);
            return b;
        }
        byte[] Curve()
        {
            var b = new byte[16]; Sig(b, 0, "curv"); U32(b, 8, 1);
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(12), (ushort)Math.Round(gamma * 256)); return b;
        }
        var text = Encoding.ASCII.GetBytes("Tuba Display Native (EDID estimate)\0");
        var desc = new byte[12 + text.Length + 78]; Sig(desc, 0, "desc"); U32(desc, 8, (uint)text.Length); text.CopyTo(desc, 12);
        var copyright = Encoding.ASCII.GetBytes("text\0\0\0\0Generated from monitor declarations; not instrument calibration.\0");
        var chad = new byte[44]; Sig(chad, 0, "sf32");
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) Fixed(chad, 8 + (r * 3 + c) * 4, adaptation[r, c]);
        (string Name, byte[] Data)[] tags = [("desc", desc), ("cprt", copyright), ("wtpt", XyzTag(d50)),
            ("rXYZ", XyzTag(adapted[0])), ("gXYZ", XyzTag(adapted[1])), ("bXYZ", XyzTag(adapted[2])),
            ("rTRC", Curve()), ("gTRC", Curve()), ("bTRC", Curve()), ("chad", chad)];
        int start = 132 + tags.Length * 12, size = start + tags.Sum(t => (t.Data.Length + 3) / 4 * 4);
        var profile = new byte[size]; U32(profile, 0, (uint)size); U32(profile, 8, 0x02400000);
        Sig(profile, 12, "mntr"); Sig(profile, 16, "RGB "); Sig(profile, 20, "XYZ ");
        var now = DateTime.UtcNow;
        int[] date = [now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second];
        for (int i = 0; i < date.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(24 + i * 2), (ushort)date[i]);
        Sig(profile, 36, "acsp"); Sig(profile, 40, "MSFT"); U32(profile, 64, 1);
        for (int i = 0; i < 3; i++) Fixed(profile, 68 + i * 4, d50[i]);
        Sig(profile, 80, "TUBA"); U32(profile, 128, (uint)tags.Length);
        for (int i = 0; i < tags.Length; i++)
        {
            int entry = 132 + i * 12; Sig(profile, entry, tags[i].Name);
            U32(profile, entry + 4, (uint)start); U32(profile, entry + 8, (uint)tags[i].Data.Length);
            tags[i].Data.CopyTo(profile, start); start += (tags[i].Data.Length + 3) / 4 * 4;
        }
        return profile;
    }
    private static void Sig(byte[] b, int offset, string s) => Encoding.ASCII.GetBytes(s).CopyTo(b, offset);
    private static void U32(byte[] b, int offset, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset), v);
    private static void Fixed(byte[] b, int offset, double v) => BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(offset), checked((int)Math.Round(v * 65536)));
    private static double[] Multiply(double[,] m, double[] v) => Enumerable.Range(0, 3).Select(r => Enumerable.Range(0, 3).Sum(c => m[r, c] * v[c])).ToArray();
    private static double[,] Invert(double[,] m)
    {
        double determinant = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-10) throw new ArgumentException("显示器原色色度无法构成有效矩阵。");
        var result = new double[3, 3];
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            result[c, r] = (m[(r + 1) % 3, (c + 1) % 3] * m[(r + 2) % 3, (c + 2) % 3] - m[(r + 1) % 3, (c + 2) % 3] * m[(r + 2) % 3, (c + 1) % 3]) / determinant;
        return result;
    }
}
