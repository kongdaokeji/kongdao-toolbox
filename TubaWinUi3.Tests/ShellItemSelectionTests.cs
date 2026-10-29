using System.Runtime.InteropServices;
using TubaWinUi3.ShellExtension;

namespace TubaWinUi3.Tests;

public class ShellItemSelectionTests
{
    [Fact]
    public unsafe void ReadsPathFromRealShellItemArray()
    {
        var path = Path.Combine(Path.GetTempPath(), $"文件占用 ABI {Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "test");
        nint item = 0, array = 0;
        try
        {
            var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out item));
            var arrayIid = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromShellItem(item, ref arrayIid, out array));
            Assert.Equal(path, ShellItemSelection.TryGetFirstFilesystemPath(array));
        }
        finally
        {
            if (array != 0) Marshal.Release(array);
            if (item != 0) Marshal.Release(item);
            File.Delete(path);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out nint item);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(nint item, ref Guid iid, out nint array);
}
