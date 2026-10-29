using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUI3.BackEnd;

/// <summary>解析 @dll,-id 格式的间接字符串（SHLoadIndirectString）。</summary>
public static class ShellResolve
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

    public static string ResolveIndirectString(string source)
    {
        try
        {
            var sb = new StringBuilder(512);
            if (SHLoadIndirectString(source, sb, (uint)sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                return sb.ToString();
        }
        catch { }
        return "";
    }
}
