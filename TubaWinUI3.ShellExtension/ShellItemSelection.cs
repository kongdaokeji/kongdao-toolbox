using System.Runtime.InteropServices;

namespace TubaWinUi3.ShellExtension;

/// <summary>
/// 读取选中项路径。这里手工走 IShellItemArray / IShellItem 的 vtable（不经 ComWrappers）：
/// 处理程序运行在 NativeAOT 下，只取「第一项文件系统路径」一个需求，手工调用最直接、依赖最少。
/// </summary>
internal static unsafe class ShellItemSelection
{
    private const uint SigdnFilesysPath = 0x80058000;

    internal static string? TryGetFirstFilesystemPath(nint shellItemArray)
    {
        if (shellItemArray == 0) return null;

        // vtable（IUnknown 3 槽之后）：
        //   3 BindToHandler / 4 GetPropertyStore / 5 GetPropertyDescriptionList /
        //   6 GetAttributes / 7 GetCount / 8 GetItemAt / 9 EnumItems
        var arrayVtable = *(nint**)shellItemArray;
        var getCount = (delegate* unmanaged[Stdcall]<nint, uint*, int>)arrayVtable[7];
        var getItemAt = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)arrayVtable[8];

        uint count = 0;
        if (getCount(shellItemArray, &count) < 0 || count == 0) return null;

        nint shellItem = 0;
        if (getItemAt(shellItemArray, 0, &shellItem) < 0 || shellItem == 0) return null;

        try
        {
            // IShellItem vtable：3 BindToHandler / 4 GetParent / 5 GetDisplayName / 6 GetAttributes / 7 Compare
            var itemVtable = *(nint**)shellItem;
            var getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)itemVtable[5];

            nint name = 0;
            if (getDisplayName(shellItem, SigdnFilesysPath, &name) < 0 || name == 0) return null;
            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)shellItem)[2];
            release(shellItem);
        }
    }
}
